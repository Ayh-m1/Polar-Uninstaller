using System.IO;
using System.Runtime.InteropServices;

namespace PolarUninstallerWpf.Services;

/// <summary>Raised when a path is a drive root, a protected system location,
/// or reached through a reparse point. These paths are never deleted.</summary>
public sealed class DeletionProtectionException : IOException
{
    public DeletionProtectionException(string message) : base(message) { }
}

public sealed class BrowserOperationsService
{
    /// <summary>Raised after at least one filesystem item was successfully removed.</summary>
    public static event Action? ContentChanged;
    public static void NotifyContentChanged() => ContentChanged?.Invoke();

    public sealed record Outcome(string Path, bool Success, string? Error);

    /// <summary>
    /// Collapses a selection so a folder and one of its descendants are not
    /// processed twice. The parent is kept and the descendant is dropped.
    /// </summary>
    public static IReadOnlyList<string> CollapseNestedPaths(IEnumerable<string> paths)
    {
        var ordered = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path=>Path.IsPathFullyQualified(path)?Path.GetFullPath(path):throw new ArgumentException("Only explicit absolute deletion paths are accepted."))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path.Length)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var kept = new List<string>();
        foreach (string path in ordered)
        {
            string trimmed = Path.TrimEndingDirectorySeparator(path);
            if (kept.Any(parent => IsDescendantOf(trimmed, parent))) continue;
            kept.Add(path);
        }
        return kept;
    }

    private static bool IsDescendantOf(string path, string parent)
    {
        string root = Path.TrimEndingDirectorySeparator(parent);
        // A volume root already ends with a separator; adding another one would
        // turn "C:\" into "C:\\" and fail to match its direct children.
        string prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return path.Length > prefix.Length &&
               path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public static void ValidateDeletion(string path)
    {
        path = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        if (path.Length <= 3) throw new DeletionProtectionException("Drive roots cannot be deleted.");
        var protectedFolders = new[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.MyDocuments };
        foreach (var folder in protectedFolders)
        {
            var target = Environment.GetFolderPath(folder).TrimEnd(Path.DirectorySeparatorChar);
            if (target.Length == 0) continue;
            bool system = folder is Environment.SpecialFolder.Windows or Environment.SpecialFolder.ProgramFiles or Environment.SpecialFolder.ProgramFilesX86 or Environment.SpecialFolder.CommonApplicationData;
            if (string.Equals(path, target, StringComparison.OrdinalIgnoreCase) || system && path.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new DeletionProtectionException("This location is protected from deletion.");
        }
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new DeletionProtectionException("Deletion through links is not supported.");
    }

    public Task<IReadOnlyList<Outcome>> DeleteAsync(IReadOnlyList<string> paths, bool permanent, CancellationToken token) => Task.Run<IReadOnlyList<Outcome>>(async () =>
    {
        var results = new List<Outcome>();
        foreach (var path in CollapseNestedPaths(paths))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                ValidateDeletion(path);
                if (Directory.Exists(path))
                {
                    var pending = new Stack<string>(); pending.Push(path);
                    while (pending.TryPop(out var directory))
                    {
                        token.ThrowIfCancellationRequested();
                        foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
                        {
                            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) throw new DeletionProtectionException("A selected folder contains a link; deletion was refused.");
                            if (entry is DirectoryInfo child) pending.Push(child.FullName);
                        }
                    }
                }
                token.ThrowIfCancellationRequested();
                if(permanent)
                {
                    // Explicitly bypass the Shell and Recycle Bin. Failure is
                    // reported, never retried using a different deletion mode.
                    if(Directory.Exists(path))Directory.Delete(path,true);else File.Delete(path);
                }
                else await WindowsRecycleService.RecycleAsync(path,token);
                VerifyAbsent(path);
                results.Add(new(path, true, null));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or COMException or ArgumentException)
            { results.Add(new(path, false, (Directory.Exists(path)&&e is not DeletionProtectionException?"Folder operation failed and may be partial. ":"")+DeletionError(e))); }
        }
        if (results.Any(r => r.Success)) NotifyContentChanged();
        return results;
    }, token);

    internal static void VerifyAbsent(string path)
    {
        try{File.GetAttributes(path);}
        catch(FileNotFoundException){return;}catch(DirectoryNotFoundException){return;}
        throw new IOException("Deletion verification incomplete: the selected item still exists.");
    }
    private static string DeletionError(Exception error)=>error switch
    {
        DeletionProtectionException=>"This target is protected or contains a symbolic link/junction.",
        UnauthorizedAccessException or System.Security.SecurityException=>"Access denied; no permissions were changed.",
        FileNotFoundException or DirectoryNotFoundException=>"The selected item was not found.",
        IOException when (error.HResult&0xFFFF) is 32 or 33=>"The selected item is locked by another process.",
        IOException when error.Message.StartsWith("Windows did not",StringComparison.Ordinal)||error.Message.StartsWith("Recycle Bin support",StringComparison.Ordinal)||error.Message.StartsWith("Deletion verification incomplete:",StringComparison.Ordinal)=>error.Message,
        _=>"Deletion or its verification failed (0x"+error.HResult.ToString("X8")+"); no alternative deletion mode was used."
    };

    public Task<(long Bytes, int Skipped)> SizeAsync(string root, CancellationToken token) => Task.Run(() =>
    {
        var result = FolderSizeService.Measure(root, token);
        return (result.Bytes, result.Skipped);
    }, token);

    /// <summary>
    /// Returns the recursive size of a folder, or null when the folder itself is
    /// missing, inaccessible or a reparse point. Inaccessible children are
    /// skipped and do not turn an otherwise measurable folder into "unknown".
    /// </summary>
    public Task<long?> TrySizeAsync(string root, CancellationToken token) => Task.Run<long?>(() =>
    {
        var result = FolderSizeService.Measure(root, token);
        return result.Accessible ? result.Bytes : null;
    }, token);
}
