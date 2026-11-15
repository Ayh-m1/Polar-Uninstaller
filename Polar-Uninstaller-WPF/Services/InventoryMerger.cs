using System.IO;

namespace PolarUninstallerWpf.Services;

public static class InventoryMerger
{
    public static IReadOnlyList<InstalledProgram> Merge(IEnumerable<InstalledProgram> records, CancellationToken token = default)
    {
        var groups = new List<List<InstalledProgram>>();
        foreach (var record in records)
        {
            token.ThrowIfCancellationRequested();
            string path = NormalizePath(record.Location), id = ValidIdentifier(record.Identifier);
            var matches = groups.Where(group =>
            {
                var knownPaths = group.Select(x => NormalizePath(x.Location)).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                bool samePath = path.Length > 0 && knownPaths.Contains(path, StringComparer.OrdinalIgnoreCase);
                bool sameId = id.Length > 0 && group.Any(x => string.Equals(ValidIdentifier(x.Identifier), id, StringComparison.OrdinalIgnoreCase));
                bool exactRegistration = group.Any(x => string.IsNullOrWhiteSpace(x.Location) && string.IsNullOrWhiteSpace(record.Location)
                    && !string.IsNullOrWhiteSpace(record.Uninstall)
                    && string.Equals(x.Name, record.Name, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.Publisher, record.Publisher, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.Version, record.Version, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.Uninstall.Trim(), record.Uninstall.Trim(), StringComparison.OrdinalIgnoreCase));
                // Launcher upgrades/moves leave stale Windows registration paths.
                // Merge equal IDs when one side no longer exists; preserve genuinely
                // separate live installations of the same title.
                bool stalePath = path.Length > 0 && !Directory.Exists(path) || knownPaths.All(p => !Directory.Exists(p));
                return exactRegistration || samePath || (sameId && (path.Length == 0 || knownPaths.Length == 0 || knownPaths.Contains(path, StringComparer.OrdinalIgnoreCase) || stalePath));
            }).ToList();
            // An id-only record is ambiguous when separate installations share an identifier.
            if (matches.Count == 1) matches[0].Add(record); else groups.Add(new() { record });
        }
        return groups.Select(Combine).Where(program => !PolarApplicationIdentity.IsPolarOwned(program)).OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
    public static string NormalizePath(string value)
    {
        try
        {
            string path = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"')).Replace('/', '\\');
            if (!Path.IsPathFullyQualified(path)) return "";
            path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            var shared = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Path.GetPathRoot(path) ?? "" };
            if (shared.Any(x => !string.IsNullOrWhiteSpace(x) && string.Equals(Path.TrimEndingDirectorySeparator(x), path, StringComparison.OrdinalIgnoreCase))) return "";
            if (path.EndsWith(@"\steamapps\common", StringComparison.OrdinalIgnoreCase)) return "";
            return path;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return ""; }
    }
    private static string ValidIdentifier(string id) => id.EndsWith(':') ? "" : id.Trim();
    private static InstalledProgram Combine(List<InstalledProgram> records)
    {
        var windows = records.FirstOrDefault(x => x.Source == "Windows");
        var launcher = records.FirstOrDefault(x => x.Source != "Windows");
        var first = windows ?? records[0];
        string Pick(Func<InstalledProgram, string> field) => records.Select(field).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";
        var ordered=records.OrderByDescending(x=>x.Source!="Windows").ToArray();
        var icons = ordered.SelectMany(x => x.IconCandidates ?? Array.Empty<string>()).Concat(ordered.Select(x => x.IconPath)).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string location = records.Select(x => x.Location).FirstOrDefault(Directory.Exists) ?? Pick(x => x.Location);
        return new InstalledProgram(first.Name, Pick(x => x.Publisher), Pick(x => x.Version), location, icons.FirstOrDefault() ?? "", records.Select(x => x.SizeKb).FirstOrDefault(x => x.HasValue), records.OrderByDescending(x => x.Source != "Windows").Select(x => x.Uninstall).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "", launcher?.Source ?? first.Source, Pick(x => ValidIdentifier(x.Identifier)), records.Select(x => x.Source).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), icons,records.Any(x=>x.RequiresOfficialRemoval)){LaunchUri=Pick(x=>x.LaunchUri)};
    }
}
