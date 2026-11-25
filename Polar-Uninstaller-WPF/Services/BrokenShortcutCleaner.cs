using System.IO;

namespace PolarUninstallerWpf.Services;

internal static class BrokenShortcutCleaner
{
    internal sealed record Candidate(ShortcutLeftover Link, InstalledProgram Owner, long Bytes);

    internal static async Task<IReadOnlyList<Candidate>> ScanAsync(IReadOnlyList<UninstallHistoryEntry> history,
        ApplicationInventory inventory, CancellationToken token, IEnumerable<string>? locations = null)
    {
        if (!inventory.IsComplete) throw new IOException("Installed programs could not be verified.");
        var owners = await Task.Run(() => history.Where(entry => string.IsNullOrWhiteSpace(entry.Identifier))
            .Select(entry => new InstalledProgram(entry.Name, entry.Publisher, "", entry.Location,
                Uninstall: entry.MsiProduct is Guid product ? $"msiexec.exe /X {product:B}" : ""))
            .DistinctBy(owner => (owner.Location.ToUpperInvariant(), owner.Uninstall))
            .Where(owner => IsFormerInstallation(owner, inventory.Programs)).ToArray(), token);
        if (owners.Length == 0) return [];

        var matches = new Dictionary<string, InstalledProgram>(StringComparer.OrdinalIgnoreCase);
        var links = await ShortcutLeftoverService.ScanHistoryAsync(link =>
        {
            if (!ShortcutLeftoverService.IsStaleApplicationShortcut(link)) return false;
            var matchesForLink = owners.Where(owner => ShortcutLeftoverService.OwnsShortcut(owner, link)).Take(2).ToArray();
            if (matchesForLink.Length != 1) return false;
            matches[link.Path] = matchesForLink[0];
            return true;
        }, token, locations);
        return await Task.Run(() =>
        {
            var candidates = new List<Candidate>();
            foreach (var link in links)
            {
                token.ThrowIfCancellationRequested();
                try { candidates.Add(new(link, matches[link.Path], new FileInfo(link.Path).Length)); }
                catch (Exception error) when (RemovalEngine.IsItemError(error)) { }
            }
            return (IReadOnlyList<Candidate>)candidates;
        }, token);
    }

    private static bool IsFormerInstallation(InstalledProgram owner, IReadOnlyList<InstalledProgram> installed)
    {
        string root = InventoryMerger.NormalizePath(owner.Location);
        Guid? msi = ShortcutLeftoverService.MsiProductCode(owner);
        bool SamePathOrChild(string path, string parent) => path.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        try
        {
            if (!ShortcutLeftoverService.UsesApplicationChecks(owner)) return false;
            if (root.Length > 0)
            {
                if (!ShortcutLeftoverService.IsSafeInstallationRoot(root)) return false;
                var drive = new DriveInfo(Path.GetPathRoot(root)!);
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed ||
                    !Directory.Exists(Path.GetDirectoryName(root))) return false;
                if (msi == null && !ShortcutLeftoverService.IsAbsent(root)) return false;
            }
            else if (msi == null) return false;
            return !installed.Any(program =>
            {
                string other = InventoryMerger.NormalizePath(program.Location);
                return (root.Length > 0 && other.Length > 0 && (SamePathOrChild(root, other) || SamePathOrChild(other, root))) ||
                    (msi.HasValue && ShortcutLeftoverService.MsiProductCode(program) == msi);
            });
        }
        catch (Exception error) when (RemovalEngine.IsItemError(error)) { return false; }
    }

    internal static Task<TempCleanerService.Result> CleanAsync(IEnumerable<Candidate> reviewed, ApplicationInventory inventory,
        IProgress<string> log, CancellationToken token) => Task.Run(() =>
    {
        if (!inventory.IsComplete) throw new IOException("Installed programs could not be verified.");
        int removed = 0, skipped = 0;
        long bytes = 0;
        foreach (var candidate in reviewed)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (ShortcutLeftoverService.IsAbsent(candidate.Link.Path) ||
                    !IsFormerInstallation(candidate.Owner, inventory.Programs))
                {
                    skipped++;
                    continue;
                }
                var messages = ShortcutLeftoverService.RemoveForCleanup(candidate.Link, candidate.Owner);
                foreach (string message in messages) log.Report(message);
                if (!messages[0].StartsWith("Removed shortcut permanently:", StringComparison.Ordinal) ||
                    !ShortcutLeftoverService.IsAbsent(candidate.Link.Path))
                {
                    skipped++;
                    continue;
                }
                bytes += candidate.Bytes;
                removed++;
            }
            catch (Exception error) when (RemovalEngine.IsItemError(error))
            {
                skipped++;
                log.Report("Skipped shortcut: " + candidate.Link.Path + " (" + error.GetType().Name + ")");
            }
        }
        return new TempCleanerService.Result(removed, bytes, skipped);
    }, token);
}
