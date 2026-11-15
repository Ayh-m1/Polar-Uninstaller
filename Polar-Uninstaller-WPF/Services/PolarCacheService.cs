using System.IO;

namespace PolarUninstallerWpf.Services;

public sealed record CacheEntry(string Path, long Bytes, int Files, int Skipped);

public sealed record CacheInventory(IReadOnlyList<CacheEntry> Entries, long TotalBytes, int Skipped);

public sealed record CacheClearResult(long FreedBytes, int RemovedEntries, int RemovedFiles, int Skipped);

/// <summary>
/// Handles only Polar-owned temporary locations: %TEMP%\PolarWpfUpdate-* and %TEMP%\PolarSetup-*.
/// Settings, logs, active downloads and unrelated caches are never touched.
/// </summary>
public static class PolarCacheService
{
    private static readonly string[] Prefixes = ["PolarWpfUpdate-", "PolarSetup-"];
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static Task<CacheInventory> InspectAsync(CancellationToken token = default) => InspectAsync(null, token);

    public static Task<CacheInventory> InspectAsync(string? root, CancellationToken token = default) => Task.Run(() =>
    {
        var entries = new List<CacheEntry>();
        int skipped = 0;
        foreach (string directory in FindDirectories(root))
        {
            token.ThrowIfCancellationRequested();
            if (UpdateDownloadService.IsActiveDirectory(directory)) continue;
            var (bytes, files, entrySkipped) = Measure(directory, token);
            skipped += entrySkipped;
            if (bytes > 0 || files > 0)
                entries.Add(new CacheEntry(directory, bytes, files, entrySkipped));
        }
        return new CacheInventory(entries, entries.Sum(e => e.Bytes), skipped);
    }, token);

    public static Task<CacheClearResult> ClearAsync(CancellationToken token = default) => ClearAsync(null, token);

    public static async Task<CacheClearResult> ClearAsync(string? root, CancellationToken token = default)
    {
        if (!await Gate.WaitAsync(0, token).ConfigureAwait(false))
            throw new InvalidOperationException("A cache cleanup is already running.");
        try
        {
            return await Task.Run(() => Clear(root, token), token).ConfigureAwait(false);
        }
        finally { Gate.Release(); }
    }

    private static CacheClearResult Clear(string? root, CancellationToken token)
    {
        long freed = 0;
        int removedEntries = 0, removedFiles = 0, skipped = 0;
        foreach (string directory in FindDirectories(root))
        {
            token.ThrowIfCancellationRequested();
            if (UpdateDownloadService.IsActiveDirectory(directory)) { skipped++; continue; }

            var directories = new List<string>();
            var stack = new Stack<string>();
            stack.Push(directory);
            while (stack.TryPop(out var current))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                    directories.Add(current);
                    foreach (var entry in new DirectoryInfo(current).GetFileSystemInfos("*"))
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                            if (entry is DirectoryInfo child) { stack.Push(child.FullName); continue; }
                            if (entry is not FileInfo file) continue;
                            long size = 0;
                            try { size = file.Length; } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                            try { File.SetAttributes(file.FullName, FileAttributes.Normal); } catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
                            file.Delete();
                            removedFiles++;
                            freed += size;
                        }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { skipped++; }
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or DirectoryNotFoundException) { skipped++; }
            }

            // Remove now-empty directories bottom-up; any locked directory is reported, never forced.
            foreach (string current in directories.OrderByDescending(p => p.Length))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (Directory.Exists(current))
                    {
                        Directory.Delete(current, false);
                        if (string.Equals(current, directory, StringComparison.OrdinalIgnoreCase)) removedEntries++;
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { skipped++; }
            }
        }
        return new CacheClearResult(freed, removedEntries, removedFiles, skipped);
    }

    private static (long Bytes, int Files, int Skipped) Measure(string root, CancellationToken token)
    {
        long bytes = 0;
        int files = 0, skipped = 0;
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.TryPop(out var current))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                foreach (var entry in new DirectoryInfo(current).GetFileSystemInfos("*"))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                        if (entry is DirectoryInfo directory) stack.Push(directory.FullName);
                        else if (entry is FileInfo file) { files++; bytes += file.Length; }
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or FileNotFoundException) { skipped++; }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or DirectoryNotFoundException) { skipped++; }
        }
        return (bytes, files, skipped);
    }

    public static IReadOnlyList<string> FindDirectories() => FindDirectories(null);

    private static IReadOnlyList<string> FindDirectories(string? isolatedRoot)
    {
        var result = new List<string>();
        string temp;
        try { temp = isolatedRoot ?? Path.GetTempPath(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return result; }
        if (string.IsNullOrWhiteSpace(temp) || !Directory.Exists(temp)) return result;
        foreach (string prefix in Prefixes)
        {
            try { result.AddRange(Directory.GetDirectories(temp, prefix + "*")); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
