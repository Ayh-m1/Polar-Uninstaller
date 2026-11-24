using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace PolarUninstallerWpf.Services;

/// <summary>How much is known about a row's size.</summary>
public enum BrowserSizeState
{
    /// <summary>The size has not been requested yet.</summary>
    Unknown,
    /// <summary>A background calculation is running; show a temporary state.</summary>
    Calculating,
    /// <summary>An exact size is available.</summary>
    Known,
    /// <summary>The folder was measured but some children were inaccessible.</summary>
    Partial,
    /// <summary>The folder itself could not be read.</summary>
    Unavailable
}

/// <summary>
/// A File Browser row. Size is mutable and raises change notifications so a
/// background folder measurement updates the grid and the active sort without
/// rebuilding the collection.
/// </summary>
public sealed class BrowserEntry : INotifyPropertyChanged
{
    private long? size;
    private BrowserSizeState sizeState;
    private bool arabic;

    public BrowserEntry(string name, string fullPath, bool isDirectory, long? size)
    {
        Name = name;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        this.size = size;
        sizeState = size.HasValue ? BrowserSizeState.Known : BrowserSizeState.Unknown;
    }

    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }
    public FileAttributes Attributes { get; init; }
    public bool IsDirectoryLink => IsDirectory && (Attributes & FileAttributes.ReparsePoint) != 0;
    public long? CountedSize => IsDirectoryLink ? null : Size;
    public bool IsHiddenOrSystem => (Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;
    public string DisplayName => IsHiddenOrSystem ? Name + "  ◇" : Name;

    public long? Size
    {
        get => size;
        set
        {
            if (size == value) return;
            size = value;
            Raise(nameof(Size));
            Raise(nameof(DisplaySize));
        }
    }

    public BrowserSizeState SizeState
    {
        get => sizeState;
        set
        {
            if (sizeState == value) return;
            sizeState = value;
            Raise(nameof(SizeState));
            Raise(nameof(DisplaySize));
            Raise(nameof(SizeToolTip));
        }
    }

    public DateTime Modified { get; init; }
    public DateTime Created { get; init; }

    public bool Arabic
    {
        get => arabic;
        set
        {
            if (arabic == value) return;
            arabic = value;
            Raise(nameof(Arabic));
            Raise(nameof(Type));
            Raise(nameof(SizeToolTip));
        }
    }

    public string Icon => IsDirectory ? "\u25B0" : "\u25A4";

    public string DisplaySize => SizeState switch
    {
        BrowserSizeState.Calculating => "…",
        BrowserSizeState.Unavailable => "—",
        BrowserSizeState.Partial when size.HasValue => "≥ " + FormatBytes(size.Value) + (IsDirectoryLink ? " ↗" : ""),
        _ when size.HasValue => FormatBytes(size.Value) + (IsDirectoryLink ? " ↗" : ""),
        _ => "—"
    };

    public string? SizeToolTip => IsDirectoryLink ? BrowserText.Get(
        SizeState == BrowserSizeState.Unavailable ? "LinkTargetSizeUnavailable" : "LinkTargetSize", Arabic) : null;

    public static string FormatBytes(long bytes) => DisplayFormat.BinarySize(bytes);

    public string Type => IsDirectory ? Arabic ? "مجلد" : "Folder" : Path.GetExtension(Name).ToUpperInvariant();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class FileBrowserService
{
    public Task<IReadOnlyList<BrowserEntry>> ListAsync(string path, CancellationToken cancellationToken = default) =>
        ListAsync(path, false, cancellationToken);

    public async Task<IReadOnlyList<BrowserEntry>> ListAsync(string path, bool showHidden, CancellationToken cancellationToken) =>
        (await OpenFolderAsync(path, showHidden, cancellationToken).ConfigureAwait(false)).Entries;

    public Task<(string Path, IReadOnlyList<BrowserEntry> Entries)> OpenFolderAsync(string path, bool showHidden, CancellationToken cancellationToken) =>
        Task.Run<(string, IReadOnlyList<BrowserEntry>)>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                path = ResolveLocalDirectoryLinkTarget(path) ?? path;

            var entries = new List<BrowserEntry>();
            // A denied root must fail navigation instead of appearing to be empty.
            var options = new EnumerationOptions { IgnoreInaccessible = false, RecurseSubdirectories = false, ReturnSpecialDirectories = false, AttributesToSkip = showHidden ? 0 : FileAttributes.Hidden | FileAttributes.System };
            foreach (var item in new DirectoryInfo(path).EnumerateFileSystemInfos("*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { entries.Add(new(item.Name, item.FullName, item is DirectoryInfo, item is FileInfo file && (item.Attributes & FileAttributes.ReparsePoint) == 0 ? file.Length : null) { Attributes = item.Attributes, Modified = item.LastWriteTime, Created = item.CreationTime }); }
                catch (Exception error) when (error is UnauthorizedAccessException or IOException) { }
            }
            return (path, entries.OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToArray());
        }, cancellationToken);

    internal static string? ResolveLocalDirectoryLinkTarget(string linkPath)
    {
        // Read the link metadata directly, even if Windows denies enumeration through the alias.
        var target = new DirectoryInfo(linkPath).ResolveLinkTarget(returnFinalTarget: false);
        if (target is null) return null;

        string path = target.FullName;
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) path = path[4..];
        if (path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\')
            throw new UnauthorizedAccessException(linkPath);

        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (new DriveInfo(Path.GetPathRoot(path)!).DriveType != DriveType.Fixed)
            throw new UnauthorizedAccessException(linkPath);

        var ancestors = new Stack<string>();
        for (string? ancestor = path; ancestor != null; ancestor = Path.GetDirectoryName(ancestor))
            ancestors.Push(ancestor);
        while (ancestors.TryPop(out var ancestor))
            if ((File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException(linkPath);
        return path;
    }
}
