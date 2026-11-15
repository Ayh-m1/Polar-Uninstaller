using System.Collections.Concurrent;
using System.IO;

namespace PolarUninstallerWpf.Services;

/// <summary>The measured size of a folder.</summary>
public readonly record struct FolderSizeResult(long? Bytes, bool Partial, bool Unavailable);

/// <summary>
/// Measures folder sizes on background threads with a global concurrency limit,
/// a short-lived cache and a per-path gate so the same folder is never scanned
/// twice at the same time. Reparse points are never followed, which prevents
/// recursive loops and double counting.
/// </summary>
public static class FolderSizeService
{
    private static readonly ConcurrentDictionary<string, CacheItem> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> PathGates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim Slots = new(4, 4);
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);

    private readonly record struct CacheItem(FolderSizeResult Result, DateTimeOffset MeasuredAt);

    /// <summary>Returns a cached result when it is still fresh.</summary>
    public static FolderSizeResult? TryGetCached(string path)
    {
        if (!Cache.TryGetValue(path, out var item)) return null;
        if (DateTimeOffset.Now - item.MeasuredAt <= CacheLifetime) return item.Result;
        Cache.TryRemove(path, out _);
        return null;
    }

    public static void Invalidate(string path)
    {
        Cache.TryRemove(path, out _);
    }

    /// <summary>Clears every cached measurement. Used by tests and Clear Cache.</summary>
    public static void ClearCache() => Cache.Clear();

    /// <summary>
    /// Measures a folder, reusing a fresh cached value when possible. At most four
    /// folders are scanned concurrently across the whole application.
    /// </summary>
    public static async Task<FolderSizeResult> GetAsync(string path, CancellationToken token)
    {
        if (TryGetCached(path) is { } cached) return cached;

        var gate = PathGates.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (TryGetCached(path) is { } again) return again;

            await Slots.WaitAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                var measured = await Task.Run(() => Measure(path, token), token).ConfigureAwait(false);
                var result = measured.Accessible
                    ? new FolderSizeResult(measured.Bytes, measured.Skipped > 0, false)
                    : new FolderSizeResult(null, false, true);
                Cache[path] = new CacheItem(result, DateTimeOffset.Now);
                return result;
            }
            finally { Slots.Release(); }
        }
        finally { gate.Release(); }
    }

    /// <summary>Forces a fresh measurement for an explicit user recalculation.</summary>
    public static Task<FolderSizeResult> RecalculateAsync(string path, CancellationToken token)
    {
        Invalidate(path);
        return GetAsync(path, token);
    }

    /// <summary>
    /// Recursively sums file lengths below <paramref name="root"/>. Directory sizes
    /// are never added to their parent (files are counted once), links and
    /// junctions are skipped, and inaccessible children only mark the result as
    /// partial instead of discarding an otherwise measurable folder.
    /// </summary>
    public static (long Bytes, int Skipped, bool Accessible) Measure(string root, CancellationToken token)
    {
        long bytes = 0;
        int skipped = 0;
        try
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return (0, 0, false);
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return (0, 0, false);
            // Probe the root first so an inaccessible folder is reported as
            // unavailable rather than as a misleading exact zero.
            using (var probe = Directory.EnumerateFileSystemEntries(root).GetEnumerator()) _ = probe.MoveNext();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException) { return (0, 0, false); }

        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var path))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                foreach (var item in new DirectoryInfo(path).EnumerateFileSystemInfos())
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        if ((item.Attributes & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                        if (item is DirectoryInfo directory) pending.Push(directory.FullName);
                        else if (item is FileInfo file) bytes += file.Length;
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { skipped++; }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { skipped++; }
        }
        return (bytes, skipped, true);
    }
}
