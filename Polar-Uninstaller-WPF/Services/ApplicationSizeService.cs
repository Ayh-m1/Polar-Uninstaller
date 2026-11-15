using System.Collections.Concurrent;
using System.IO;

namespace PolarUninstallerWpf.Services;

/// <summary>
/// Centralized, cached, asynchronous application/game size detection.
/// Hierarchy (handled by callers): launcher metadata -> registry EstimatedSize
/// -> measured filesystem size. This service performs the filesystem measurement.
/// </summary>
public static class ApplicationSizeService
{
    private static readonly ConcurrentDictionary<string, long?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim Concurrency = new(4);

    public static string NormalizeKey(string location)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(location)) return "";
            string path = Environment.ExpandEnvironmentVariables(location.Trim().Trim('"')).Replace('/', '\\');
            if (!Path.IsPathFullyQualified(path)) return "";
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "";
        }
    }

    public static bool TryGetCached(string location, out long? size)
    {
        string key = NormalizeKey(location);
        if (key.Length == 0) { size = null; return false; }
        return Cache.TryGetValue(key, out size);
    }

    public static void Invalidate(string location)
    {
        string key = NormalizeKey(location);
        if (key.Length > 0) Cache.TryRemove(key, out _);
    }

    public static void InvalidateAll() => Cache.Clear();

    /// <summary>
    /// Measures the total size (in bytes) of all files under <paramref name="location"/>
    /// recursively. Returns null when the path is missing/invalid or no reliable
    /// measurement could be made. Never follows reparse points outside the tree.
    /// </summary>
    public static async Task<long?> MeasureAsync(string location, CancellationToken token = default)
    {
        string key = NormalizeKey(location);
        if (key.Length == 0 || !Directory.Exists(key)) return null;
        if (Cache.TryGetValue(key, out long? cached)) return cached;

        long total = 0;
        bool anyFile = false;
        bool incomplete = false;
        try
        {
            await Task.Run(() =>
            {
                var pending = new Stack<string>();
                pending.Push(key);
                while (pending.Count > 0)
                {
                    token.ThrowIfCancellationRequested();
                    string current = pending.Pop();
                    try
                    {
                        // Skip reparse points (junctions/symlinks) to avoid escaping the tree.
                        if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) continue;
                        foreach (string file in Directory.EnumerateFiles(current))
                        {
                            token.ThrowIfCancellationRequested();
                            try
                            {
                                var info = new FileInfo(file);
                                if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                                total += info.Length;
                                anyFile = true;
                            }
                            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or PathTooLongException)
                            {
                                incomplete = true;
                            }
                        }
                        foreach (string dir in Directory.EnumerateDirectories(current))
                        {
                            token.ThrowIfCancellationRequested();
                            try
                            {
                                if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) continue;
                                pending.Push(dir);
                            }
                            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or PathTooLongException)
                            {
                                incomplete = true;
                            }
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or PathTooLongException)
                    {
                        incomplete = true;
                    }
                }
            }, token);
        }
        catch (OperationCanceledException)
        {
            throw;
        }

        long? result = incomplete ? null : (anyFile ? total : 0);
        // Only cache complete measurements.
        if (!incomplete) Cache[key] = result;
        return result;
    }
}
