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
        }
    }

    public string Icon => IsDirectory ? "\u25B0" : "\u25A4";

    public string DisplaySize => SizeState switch
    {
        BrowserSizeState.Calculating => "…",
        BrowserSizeState.Unavailable => "—",
        BrowserSizeState.Partial when size.HasValue => "≥ " + FormatBytes(size.Value),
        _ when size.HasValue => FormatBytes(size.Value),
        _ => "—"
    };

    public static string FormatBytes(long bytes) => bytes >= 1073741824 ? $"{bytes / 1073741824.0:N1} GB" : bytes >= 1048576 ? $"{bytes / 1048576.0:N1} MB" : bytes >= 1024 ? $"{bytes / 1024.0:N1} KB" : $"{bytes:N0} B";

    public string Type => IsDirectory ? Arabic ? "مجلد" : "Folder" : Path.GetExtension(Name).ToUpperInvariant();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class FileBrowserService
{
    public Task<IReadOnlyList<BrowserEntry>> ListAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<BrowserEntry>>(() =>
        {
            var entries = new List<BrowserEntry>();
            var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false, ReturnSpecialDirectories = false };
            foreach (var item in new DirectoryInfo(path).EnumerateFileSystemInfos("*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { entries.Add(new(item.Name, item.FullName, item is DirectoryInfo, item is FileInfo file ? file.Length : null) { Modified = item.LastWriteTime, Created = item.CreationTime }); }
                catch (Exception error) when (error is UnauthorizedAccessException or IOException) { }
            }
            return entries.OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }, cancellationToken);
}
