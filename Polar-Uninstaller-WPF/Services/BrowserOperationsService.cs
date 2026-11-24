using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

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

    private static readonly HashSet<string> ProtectedVolumeEntries = new(StringComparer.OrdinalIgnoreCase)
    {
        "System Volume Information", "$Recycle.Bin", "Recovery", "EFI", "Boot",
        "bootmgr", "bootmgr.efi", "BOOTNXT", "boot.ini", "ntldr", "NTDETECT.COM",
        "pagefile.sys", "hiberfil.sys", "swapfile.sys", "DumpStack.log", "DumpStack.log.tmp",
        "WindowsApps", "WpSystem", "WUDownloadCache", "Config.Msi", "MSOCache",
        "Windows.old", "$WINDOWS.~BT", "$WINDOWS.~WS", "$WinREAgent", "$SysReset",
        "Documents and Settings"
    };

    private static string NormalizeDeletionPath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            path.StartsWith(@"\\.\", StringComparison.Ordinal))
            throw new DeletionProtectionException("Use a fully qualified filesystem path.");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (string.Equals(full, Path.GetPathRoot(full), StringComparison.OrdinalIgnoreCase))
            throw new DeletionProtectionException("Drive roots cannot be deleted.");
        if (full[(Path.GetPathRoot(full)?.Length ?? 0)..].Split(Path.DirectorySeparatorChar)
            .Any(part => part.EndsWith(' ') || part.EndsWith('.') || part.Contains(':')))
            throw new DeletionProtectionException("Ambiguous paths and alternate data streams are protected.");
        return full;
    }

    internal static void ValidateProtectedVolumeLocation(string path)
    {
        var root = Path.GetPathRoot(path);
        if (root != null && ProtectedVolumeEntries.Contains(path[root.Length..].Split(Path.DirectorySeparatorChar)[0]))
            throw new DeletionProtectionException("This volume item is protected.");
    }

    private static void ValidateProtectedLocation(string path)
    {
        ValidateProtectedVolumeLocation(path);
        foreach(var (target,subtree) in ProtectedLocations.Value)
            if(path.Equals(target,StringComparison.OrdinalIgnoreCase)||IsDescendantOf(target,path)||(subtree&&IsDescendantOf(path,target)))
                throw new DeletionProtectionException("This location is protected from deletion.");
    }

    private static readonly Lazy<(string Path,bool Subtree)[]> ProtectedLocations = new(() =>
    {
        var results=new List<(string,bool)>();
        void Add(string path,bool subtree)
        {
            if(Path.IsPathFullyQualified(path))results.Add((Path.TrimEndingDirectorySeparator(path),subtree));
        }

        foreach(var folder in new[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.System, Environment.SpecialFolder.SystemX86,
            Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.CommonApplicationData })
            Add(Environment.GetFolderPath(folder),true);
        foreach(var folder in new[] { Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.MyDocuments })
            Add(Environment.GetFolderPath(folder),false);

        var profile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if(profile.Length>0)Add(Path.Combine(profile,"AppData"),false);
        foreach(var folder in new[] { Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ApplicationData })
        {
            var data=Environment.GetFolderPath(folder);
            if(data.Length==0)continue;
            foreach(var name in new[] { "PolarUninstallerWPF-Development", "PolarUninstaller", "Polar Uninstaller" })
                Add(Path.Combine(data,name),true);
        }

        if(Environment.ProcessPath is string executable &&
            Path.GetFileName(executable).Equals("PolarUninstaller.exe",StringComparison.OrdinalIgnoreCase) &&
            Path.GetDirectoryName(executable) is string install)
            Add(install,true);

        return results.ToArray();
    });

    private static void ValidateRegisteredInstall(string path)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var hive=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,view);
                using var key=hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\PolarUninstallerWpfRust");
                if (key?.GetValue("InstallLocation") is not string install || !Path.IsPathFullyQualified(install)) continue;
                install=Path.TrimEndingDirectorySeparator(Path.GetFullPath(install));
                if (path.Equals(install,StringComparison.OrdinalIgnoreCase) || IsDescendantOf(path,install) || IsDescendantOf(install,path))
                    throw new DeletionProtectionException("The registered Polar Uninstaller installation is protected.");
            }
            catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException)
            {
                throw new DeletionProtectionException("The installed Polar Uninstaller path could not be checked.");
            }
        }
    }

    public static void ValidateDeletion(string path) => _ = DeletionAttributes(path);

    internal static FileAttributes DeletionAttributes(string path)
    {
        path = NormalizeDeletionPath(path);
        ValidateProtectedLocation(path);
        ValidateRegisteredInstall(path);
        // Check from the volume downward so an ancestor link is discovered
        // before querying attributes on a path inside its target.
        var parents = new Stack<string>();
        for (var parent = Path.GetDirectoryName(path); parent != null; parent = Path.GetDirectoryName(parent))
            parents.Push(parent);
        while (parents.TryPop(out var parent))
            if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                throw new DeletionProtectionException("Deletion through links is not supported.");

        var attributes=File.GetAttributes(path);
        string longPath = ExpandDeletionPath(path);
        if (!StringComparer.OrdinalIgnoreCase.Equals(path, longPath))
        {
            ValidateProtectedLocation(longPath);
            ValidateRegisteredInstall(longPath);
        }
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            FileSystemInfo link = (attributes & FileAttributes.Directory) != 0 ? new DirectoryInfo(path) : new FileInfo(path);
            if (link.LinkTarget is null)
                throw new DeletionProtectionException("This reparse point is not a supported link.");
        }

        return attributes;
    }

    internal static string ExpandDeletionPath(string path)
    {
        string extended = path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
        var buffer = new StringBuilder(512);
        uint length = GetLongPathName(extended, buffer, (uint)buffer.Capacity);
        if (length >= buffer.Capacity)
        {
            buffer.EnsureCapacity(checked((int)length));
            length = GetLongPathName(extended, buffer, (uint)buffer.Capacity);
        }
        if (length == 0 || length >= buffer.Capacity)
            throw new DeletionProtectionException("Windows could not verify the full deletion path.");

        string full = buffer.ToString();
        if (full.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + full[8..];
        return full.StartsWith(@"\\?\", StringComparison.Ordinal) ? full[4..] : full;
    }

    [DllImport("kernel32.dll", EntryPoint = "GetLongPathNameW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint GetLongPathName(string shortPath, StringBuilder longPath, uint bufferLength);

    public Task<IReadOnlyList<Outcome>> DeleteAsync(IReadOnlyList<string> paths, bool permanent, CancellationToken token) => Task.Run<IReadOnlyList<Outcome>>(async () =>
    {
        var results = new List<Outcome>();
        var changes=new List<FileChange>();
        try
        {
        foreach (var path in CollapseNestedPaths(paths))
        {
            token.ThrowIfCancellationRequested();
            var captured=new List<FileChange>();
            try
            {
                var attributes=DeletionAttributes(path);
                bool isDirectory=(attributes & FileAttributes.Directory)!=0;
                bool isLink=(attributes & FileAttributes.ReparsePoint)!=0;
                captured.Add(new(path,true,isDirectory||isLink?null:new FileInfo(path).Length,isDirectory));
                if (isDirectory && !isLink)
                {
                    var pending = new Stack<string>(); pending.Push(path);
                    while (pending.TryPop(out var directory))
                    {
                        token.ThrowIfCancellationRequested();
                        foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
                        {
                            ValidateProtectedLocation(entry.FullName);
                            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) throw new DeletionProtectionException("A selected folder contains a link; deletion was refused.");
                            captured.Add(new(entry.FullName,true,entry is FileInfo info?info.Length:null,entry is DirectoryInfo));
                            if (entry is DirectoryInfo child) pending.Push(child.FullName);
                        }
                    }
                }
                token.ThrowIfCancellationRequested();
                // Refuse a selected item replaced by a different kind during inspection.
                if (((DeletionAttributes(path) ^ attributes) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                    throw new DeletionProtectionException("The selected item changed during inspection.");
                if(permanent)
                {
                    // Explicitly bypass the Shell and Recycle Bin. Failure is
                    // reported, never retried using a different deletion mode.
                    if(isDirectory)Directory.Delete(path,recursive:!isLink);else File.Delete(path);
                }
                else await WindowsRecycleService.RecycleAsync(path,token);
                VerifyAbsent(path);
                results.Add(new(path, true, null));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or COMException or ArgumentException)
            { results.Add(new(path, false, (Directory.Exists(path)&&e is not DeletionProtectionException?"Folder operation failed and may be partial. ":"")+DeletionError(e))); }
            finally
            {
                foreach(var entry in captured)
                {bool absent=false;try{VerifyAbsent(entry.Path);absent=true;}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException){ }changes.Add(entry with{Success=absent});}
            }
        }
        }
        finally{await FileChangeHub.PublishAsync(changes,permanent?FileChangeKind.Deleted:FileChangeKind.Recycled).ConfigureAwait(false);}
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
