namespace PolarUninstallerWpf.Services;

/// <summary>
/// Size comparison for File Browser rows. Known sizes are ordered by direction;
/// entries with no measured size (for example folders that have not been sized
/// yet) always sort last so unknown values never masquerade as zero bytes.
/// </summary>
public sealed class BrowserEntryComparer : IComparer<BrowserEntry>, System.Collections.IComparer
{
    private readonly bool descending;
    public BrowserEntryComparer(bool descending) => this.descending = descending;

    int System.Collections.IComparer.Compare(object? x, object? y) => Compare(x as BrowserEntry, y as BrowserEntry);

    public int Compare(BrowserEntry? x, BrowserEntry? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return 1;
        if (y is null) return -1;

        bool xKnown = x.Size.HasValue;
        bool yKnown = y.Size.HasValue;
        if (!xKnown && !yKnown) return Fallback(x, y);
        if (!xKnown) return 1;
        if (!yKnown) return -1;

        int result = x.Size!.Value.CompareTo(y.Size!.Value);
        if (descending) result = -result;
        return result != 0 ? result : Fallback(x, y);
    }

    private static int Fallback(BrowserEntry x, BrowserEntry y)
    {
        if (x.IsDirectory != y.IsDirectory) return x.IsDirectory ? -1 : 1;
        return string.Compare(x.Name, y.Name, StringComparison.CurrentCultureIgnoreCase);
    }
}
