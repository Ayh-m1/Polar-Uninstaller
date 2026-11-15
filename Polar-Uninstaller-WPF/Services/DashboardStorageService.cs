using System.IO;

namespace PolarUninstallerWpf.Services;

public sealed record DriveSummary(string Name, long Total, long Free)
{
    public string Label { get; init; } = "";
    public string Letter => Name.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? Letter : $"{Letter} ({Label})";
    public override string ToString()=>DisplayName;
    public long Used => Math.Max(0, Total - Free);
    public double UsedPercent => Total > 0 ? Math.Clamp((double)Used / Total * 100, 0, 100) : 0;
    public string Capacity => $"{Format(Used)} / {Format(Total)}";
    public string Available => Format(Free);
    public static string Format(long bytes)
    {
        const double kb = 1024d, mb = kb * 1024, gb = mb * 1024, tb = gb * 1024;
        double value = Math.Abs((double)bytes);
        if (value >= tb) return $"{bytes / tb:N1} TB";
        if (value >= gb) return $"{bytes / gb:N1} GB";
        if (value >= mb) return $"{bytes / mb:N1} MB";
        if (value >= kb) return $"{bytes / kb:N1} KB";
        return $"{bytes:N0} B";
    }
}

public sealed record LargeFile(string Name, string Path, long Bytes) { public string Size => DriveSummary.Format(Bytes); }

public sealed record StorageSnapshot(IReadOnlyList<DriveSummary> Drives, IReadOnlyList<LargeFile> Largest, int FilesScanned, bool Limited)
{
    /// <summary>Immediate child folders of the scan root, ranked by cumulative size.</summary>
    public IReadOnlyList<LargeFile> LargestFolders { get; init; } = [];
    public long TotalBytes { get; init; }
    public int FoldersScanned { get; init; }
    /// <summary>Every skipped entry: inaccessible items plus reparse points.</summary>
    public int Skipped { get; init; }
    /// <summary>Only entries skipped because Windows denied access or the path failed.</summary>
    public int Inaccessible { get; init; }
    public string Root { get; init; } = "";
    public bool IsDriveRoot { get; init; }
    public DateTimeOffset CompletedAt { get; init; } = DateTimeOffset.Now;
}

/// <summary>Describes exactly what a scan covers so partial results are never
/// presented as the size of an entire drive.</summary>
public sealed record StorageScope(string Root, bool IsDriveRoot)
{
    public string DriveRoot => Path.GetPathRoot(Root) ?? Root;
}

public sealed class DashboardStorageService
{
    // Files and folders are ranked independently. Folders are never truncated
    // because a drive may legitimately contain more than the historical limit.
    private const int LargestFileLimit = 500;
    private static readonly object CacheGate = new();
    private static readonly Dictionary<string, StorageSnapshot> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static long cacheVersion;

    /// <summary>The most recent completed scan, regardless of scope.</summary>
    public static StorageSnapshot? LastSnapshot { get; private set; }

    public static StorageScope ClassifyScope(string root)
    {
        string full = Path.GetFullPath(root);
        string rootPath = Path.TrimEndingDirectorySeparator(full);
        string? volume = Path.GetPathRoot(rootPath);
        bool isDrive = volume is not null &&
            string.Equals(Path.TrimEndingDirectorySeparator(volume), rootPath, StringComparison.OrdinalIgnoreCase);
        return new StorageScope(rootPath, isDrive);
    }

    /// <summary>Returns the last completed scan for a root without rescanning.</summary>
    public static StorageSnapshot? TryGetCached(string root)
    {
        try
        {
            var scope = ClassifyScope(root);
            lock (CacheGate) return Cache.TryGetValue(scope.Root, out var snapshot) ? snapshot : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    public static void ClearCache()
    {
        lock (CacheGate) { Cache.Clear(); LastSnapshot = null; cacheVersion++; }
    }

    public Task<IReadOnlyList<DriveSummary>> ReadDrivesAsync() => Task.Run<IReadOnlyList<DriveSummary>>(() =>
    {
        var result = new List<DriveSummary>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady) continue;
                if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
                if (drive.TotalSize <= 0) continue;
                string label = "";
                try { label = drive.VolumeLabel; } catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
                result.Add(new(drive.Name, drive.TotalSize, drive.AvailableFreeSpace) { Label = label });
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException) { }
        }
        return result.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    });

    public Task<StorageSnapshot> ScanAsync(string root, CancellationToken token, IProgress<int>? progress = null, IProgress<StorageSnapshot>? partial = null) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        long version;
        lock (CacheGate) version = cacheVersion;
        var scope = ClassifyScope(root);
        string rootPath = scope.Root;
        if (!Directory.Exists(rootPath)) throw new DirectoryNotFoundException(rootPath);

        var drives = new List<DriveSummary>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.IsReady && drive.DriveType == DriveType.Fixed && drive.TotalSize > 0)
                    drives.Add(new(drive.Name, drive.TotalSize, drive.AvailableFreeSpace) { Label = SafeLabel(drive) });
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }

        var largestFiles = new PriorityQueue<LargeFile, long>();
        var folderSizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        var snapshotClock = System.Diagnostics.Stopwatch.StartNew();

        pending.Push(rootPath);
        int files = 0, folders = 0, skipped = 0, inaccessible = 0;
        bool limited = false;
        long totalBytes = 0;

        var options = new EnumerationOptions
        {
            IgnoreInaccessible = false,
            RecurseSubdirectories = false,
            AttributesToSkip = 0,
            ReturnSpecialDirectories = false
        };

        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            string path = pending.Pop();
            try
            {
                var info = new DirectoryInfo(path);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0) { skipped++; limited = true; continue; }

                folderSizes[path] = 0;
                folders++;

                FileSystemInfo[] entries = info.GetFileSystemInfos("*", options);
                foreach (var entry in entries)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) { skipped++; limited = true; continue; }
                        if (entry is DirectoryInfo directory)
                        {
                            pending.Push(directory.FullName);
                        }
                        else if (entry is FileInfo file)
                        {
                            files++;
                            long length = file.Length;
                            folderSizes[path] += length;
                            totalBytes += length;
                            var item = new LargeFile(file.Name, file.FullName, length);
                            largestFiles.Enqueue(item, length);
                            if (largestFiles.Count > LargestFileLimit) largestFiles.Dequeue();
                        }
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or FileNotFoundException) { skipped++; inaccessible++; limited = true; }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or DirectoryNotFoundException) { skipped++; inaccessible++; limited = true; }

            if (snapshotClock.ElapsedMilliseconds >= 250)
            {
                snapshotClock.Restart();
                progress?.Report(files);
                partial?.Report(new StorageSnapshot(drives, Top(largestFiles), files, true)
                {
                    TotalBytes = totalBytes,
                    FoldersScanned = folderSizes.Count,
                    Skipped = skipped,
                    Inaccessible = inaccessible,
                    Root = rootPath,
                    IsDriveRoot = scope.IsDriveRoot,
                    CompletedAt = DateTimeOffset.Now
                });
            }
        }

        // Post-order accumulation: every directory already holds only its direct files,
        // so adding each child exactly once into its parent cannot double count.
        var ordered = folderSizes.Keys.OrderByDescending(p => p.Length).ToArray();
        var largestFolders = new List<LargeFile>();
        foreach (string path in ordered)
        {
            token.ThrowIfCancellationRequested();
            long bytes = folderSizes[path];
            string? parent = Path.GetDirectoryName(path);
            if (parent is not null && folderSizes.ContainsKey(parent))
                folderSizes[parent] += bytes;

            // Report only immediate children of the scan root. A parent's size
            // already includes every nested folder and file, so listing a parent
            // together with its descendants would overlap and mislead users.
            // Every accessible immediate child is kept: the folder ranking is
            // never truncated by the separate largest-files ranking.
            if (!string.Equals(path, rootPath, StringComparison.OrdinalIgnoreCase) &&
                parent is not null &&
                string.Equals(parent, rootPath, StringComparison.OrdinalIgnoreCase))
            {
                largestFolders.Add(new LargeFile(Path.GetFileName(path), path, bytes));
            }
        }

        progress?.Report(files);
        var snapshot = new StorageSnapshot(drives, Top(largestFiles), files, limited)
        {
            LargestFolders = largestFolders
                .OrderByDescending(folder => folder.Bytes)
                .ThenBy(folder => folder.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray(),
            TotalBytes = totalBytes,
            FoldersScanned = folders,
            Skipped = skipped,
            Inaccessible = inaccessible,
            Root = rootPath,
            IsDriveRoot = scope.IsDriveRoot,
            CompletedAt = DateTimeOffset.Now
        };
        // If Clear Cache ran while this scan was finishing, never repopulate the
        // cleared results from this now-stale snapshot.
        lock (CacheGate)
        {
            if (version == cacheVersion) { Cache[rootPath] = snapshot; LastSnapshot = snapshot; }
        }
        return snapshot;
    }, token);

    private static string SafeLabel(DriveInfo drive)
    {
        try { return drive.VolumeLabel; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return ""; }
    }

    private static IReadOnlyList<LargeFile> Top(PriorityQueue<LargeFile, long> queue) =>
        queue.UnorderedItems.Select(x => x.Element).OrderByDescending(x => x.Bytes).ToArray();
}
