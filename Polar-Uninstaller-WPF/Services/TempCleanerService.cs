using System.IO;
using System.Runtime.InteropServices;

namespace PolarUninstallerWpf.Services;

// Only enumerates the predefined cleanup locations, skipping links and inaccessible entries.
public sealed class TempCleanerService
{
    public enum Category { UserTemp, SystemTemp, Prefetch, RecycleBin }
    public sealed record Result(int Files, long Bytes, int Skipped, int Directories = 0);

    public Task<Result> RunAsync(bool delete, IProgress<int>? progress, CancellationToken token) =>
        RunInDirectoryAsync(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), delete, progress, token);

    public Task<Result> RunCategoryAsync(Category category, bool delete, IProgress<int>? progress, CancellationToken token)
    {
        if (category == Category.RecycleBin) return RecycleBinCleaner.RunAsync(delete,token);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var root = category switch
        {
            Category.UserTemp => Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar),
            Category.SystemTemp => Path.Combine(windows, "Temp"),
            Category.Prefetch => Path.Combine(windows, "Prefetch"),
            _ => throw new ArgumentOutOfRangeException(nameof(category))
        };
        return RunInDirectoryAsync(root, delete, progress, token, category == Category.Prefetch);
    }

    internal Task<Result> RunInDirectoryAsync(string root, bool delete, IProgress<int>? progress, CancellationToken token, bool prefetchOnly = false) => Task.Run(() =>
    {
        var rootInfo = new DirectoryInfo(root);
        var protectedDirectories=CleanerRuntimeProtection.Directories();
        if(CleanerRuntimeProtection.IsProtected(root,protectedDirectories))
            throw new IOException("This directory contains application runtime or update files and cannot be cleaned.");
        if (!rootInfo.Exists || (rootInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The temporary directory is unavailable or is a link.");

        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            AttributesToSkip = 0,
            ReturnSpecialDirectories = false
        };

        // First discover all eligible files and directories without deleting
        // anything. Directories are processed deepest-first afterwards.
        var files = new List<(string Path, long Size)>();
        var directories = new List<string>();
        var stack = new Stack<string>();
        stack.Push(root);
        int protectedSkipped=0;

        while (stack.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            string current = stack.Pop();
            if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase)) { }
            try
            {
                foreach (string entry in Directory.EnumerateFileSystemEntries(current, "*", options))
                {
                    token.ThrowIfCancellationRequested();
                    if(CleanerRuntimeProtection.IsProtected(entry,protectedDirectories)){protectedSkipped++;continue;}
                    try
                    {
                        var attributes = File.GetAttributes(entry);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            directories.Add(entry);
                            stack.Push(entry);
                        }
                        else
                        {
                            var file = new FileInfo(entry);
                            if (prefetchOnly && !file.Extension.Equals(".pf", StringComparison.OrdinalIgnoreCase)) continue;
                            files.Add((entry, file.Length));
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                    {
                        // Skip the unreadable entry but keep scanning siblings.
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // A directory that cannot be enumerated is skipped entirely.
            }
        }

        int deletedFiles = 0, skipped = protectedSkipped, deletedDirectories = 0;
        long bytes = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();

        // Delete files first, deepest independent operations first.
        foreach (var (path, size) in files)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var file = new FileInfo(path);
                if(CleanerRuntimeProtection.IsProtected(path,protectedDirectories)||(file.Attributes&FileAttributes.ReparsePoint)!=0){skipped++;continue;}
                if (delete)
                {
                    if ((file.Attributes & FileAttributes.ReadOnly) != 0) file.Attributes &= ~FileAttributes.ReadOnly;
                    file.Delete();
                }
                deletedFiles++;
                bytes += size;
                if (clock.ElapsedMilliseconds >= 150) { progress?.Report(deletedFiles); clock.Restart(); }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                skipped++;
            }
        }

        // Remove directories from deepest level upward. A directory is removed
        // only if it has become empty; any remaining file or subdirectory
        // (including skipped/protected ones) prevents deletion.
        foreach (string directory in directories.OrderByDescending(d => d.Length))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (!Directory.Exists(directory)) continue;
                if (Directory.EnumerateFileSystemEntries(directory).Any()) continue;
                if (delete) Directory.Delete(directory, false);
                deletedDirectories++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                skipped++;
            }
        }

        return new Result(deletedFiles, bytes, skipped, deletedDirectories);
    }, token);
}
