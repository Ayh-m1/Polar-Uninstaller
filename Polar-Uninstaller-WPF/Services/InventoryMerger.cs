using System.IO;

namespace PolarUninstallerWpf.Services;

public static class InventoryMerger
{
    public static IReadOnlyList<InstalledProgram> Merge(IEnumerable<InstalledProgram> records, CancellationToken token = default)
    {
        var groups = new List<List<InstalledProgram>>();
        foreach (var record in Deduplicated(records))
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(record.Name)) continue;
            string path = NormalizePath(record.Location), id = ValidIdentifier(record.Identifier);
            var matches = groups.Where(group => Matches(group, record, path, id)).ToList();
            if (matches.Count == 0)
            {
                groups.Add(new() { record });
                continue;
            }
            if (matches.Count == 1)
            {
                matches[0].Add(record);
                continue;
            }
            // Several groups match. Only an exact live path identifies one
            // unambiguously; a bare id shared by two installations must stay
            // ambiguous instead of collapsing into one row.
            var exact = path.Length == 0
                ? new List<List<InstalledProgram>>()
                : matches.Where(group => group.Any(x => string.Equals(NormalizePath(x.Location), path, StringComparison.OrdinalIgnoreCase))).ToList();
            if (exact.Count == 1) exact[0].Add(record);
            else groups.Add(new() { record });
        }
        return groups.Select(Combine)
            .Where(program => !PolarApplicationIdentity.IsPolarOwned(program))
            .Where(program => !IsPureSystemDatabase(program))
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static bool Matches(List<InstalledProgram> group, InstalledProgram record, string path, string id)
    {
        string name = InstallationPathResolver.NormalizeProduct(record.Name);
        bool sameName = group.Any(x => InstallationPathResolver.NormalizeProduct(x.Name) == name);
        bool sameRegistry = record.Registration is { } registration && group.Any(x => x.Registration is { } other &&
            registration.Hive == other.Hive &&
            registration.Path.Equals(other.Path, StringComparison.OrdinalIgnoreCase) &&
            registration.Name == other.Name && registration.Version == other.Version && registration.Command == other.Command);
        bool exactRegistration = group.Any(x => string.IsNullOrWhiteSpace(x.Location) && string.IsNullOrWhiteSpace(record.Location)
            && !string.IsNullOrWhiteSpace(record.Uninstall)
            && string.Equals(x.Name, record.Name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Publisher, record.Publisher, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Version, record.Version, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Uninstall.Trim(), record.Uninstall.Trim(), StringComparison.OrdinalIgnoreCase));
        // The same product in the same folder is one installation. A launcher
        // product id is not a version, so it never blocks this match.
        // Same product in the same folder is one installation. Publishers are
        // metadata that launchers frequently rewrite, so they never block a
        // name-and-path match.
        bool samePath = sameName && path.Length > 0 && group.Any(x =>
            string.Equals(NormalizePath(x.Location), path, StringComparison.OrdinalIgnoreCase) &&
            VersionsCompatible(x, record));
        // Same launcher + same normalized path is always one installation.
        bool sameLauncherPath = path.Length > 0 && sameName && group.Any(x =>
            x.Source != "Windows" && record.Source != "Windows" &&
            string.Equals(x.Source, record.Source, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(NormalizePath(x.Location), path, StringComparison.OrdinalIgnoreCase));
        bool sameId = id.Length > 0 && group.Any(x => string.Equals(ValidIdentifier(x.Identifier), id, StringComparison.OrdinalIgnoreCase));
        var knownPaths = group.Select(x => NormalizePath(x.Location)).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        // Launcher upgrades/moves leave stale Windows registration paths.
        bool stalePath = path.Length > 0 && !Directory.Exists(path) || knownPaths.All(p => !Directory.Exists(p));
        return sameRegistry || exactRegistration || samePath || sameLauncherPath ||
            (sameId && (path.Length == 0 || knownPaths.Length == 0 || knownPaths.Contains(path, StringComparer.OrdinalIgnoreCase) || stalePath));
    }

    private static bool VersionsCompatible(InstalledProgram first, InstalledProgram second)
    {
        string a = KnownVersion(first), b = KnownVersion(second);
        return a.Length == 0 || b.Length == 0 || string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static string KnownVersion(InstalledProgram program) =>
        ProductIdLike(program.Version, program.Identifier) ? "" : (program.Version ?? "").Trim();

    /// <summary>A launcher product/build id is metadata, never a display version.</summary>
    private static bool ProductIdLike(string version, string identifier)
    {
        string value = (version ?? "").Trim();
        if (value.Length == 0) return false;
        if (value.Length >= 10 && value.All(char.IsAsciiDigit)) return true;
        string id = ValidIdentifier(identifier ?? "");
        int separator = id.IndexOf(':');
        string idValue = separator >= 0 ? id[(separator + 1)..] : "";
        return idValue.Length >= 6 && value.Equals(idValue, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The same ARP key can be visible through both registry views. Its identity is
    /// hive + key path + values, not the view, so identical rows collapse first.
    /// </summary>
    private static IEnumerable<InstalledProgram> Deduplicated(IEnumerable<InstalledProgram> records)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            string key = IdentityKey(record);
            if (key.Length > 0 && !seen.Add(key)) continue;
            yield return record;
        }
    }

    private static string IdentityKey(InstalledProgram program)
    {
        if (program.Registration is { } registration)
            return string.Join("|", "reg", registration.Hive, registration.Path, registration.Name, registration.Version, registration.Command);
        if (program.Source != "Windows" || ValidIdentifier(program.Identifier).Length > 0)
            return string.Join("|", "launcher", program.Source, ValidIdentifier(program.Identifier), NormalizePath(program.Location), program.Name.Trim(), program.Version.Trim(), program.Uninstall.Trim());
        return "";
    }

    /// <summary>
    /// A leftover database row with no uninstaller, publisher, version or location
    /// cannot be acted on and is not a program. Keep rows that still have any one of
    /// those values (the Application Compatibility sdb row keeps its uninstaller).
    /// </summary>
    private static bool IsPureSystemDatabase(InstalledProgram program) =>
        program.Source == "Windows" && program.Registration is not null &&
        string.IsNullOrWhiteSpace(program.Uninstall) &&
        string.IsNullOrWhiteSpace(program.Publisher) &&
        string.IsNullOrWhiteSpace(program.Version) &&
        string.IsNullOrWhiteSpace(program.Location);

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
        string rawLocation = records.Select(x => x.Location).FirstOrDefault(Directory.Exists) ?? Pick(x => x.Location);
        string normalized = NormalizePath(rawLocation);
        string location = normalized.Length > 0 ? normalized : rawLocation;
        // A merged launcher row never keeps the registry EstimatedSize: the list
        // measures the real folder. Launcher-reported sizes are still acceptable.
        long? size = launcher is null
            ? records.Select(x => x.SizeKb).FirstOrDefault(x => x is >0&&x<=long.MaxValue/1024)
            : records.Where(x => x.Source != "Windows").Select(x => x.SizeKb).Where(x => x is >0&&x<=long.MaxValue/1024).Max();
        string uninstall = records.OrderByDescending(x => x.Source != "Windows").Select(x => x.Uninstall).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";
        return new InstalledProgram(first.Name, Pick(x => x.Publisher), RealVersion(records), location, icons.FirstOrDefault() ?? "", size, uninstall, launcher?.Source ?? first.Source, Pick(x => ValidIdentifier(x.Identifier)), records.Select(x => x.Source).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), icons,records.Any(x=>x.RequiresOfficialRemoval)){RegisteredIcon=Pick(x=>x.RegisteredIcon),LaunchUri=Pick(x=>x.LaunchUri),Registration=records.Select(x=>x.Registration).FirstOrDefault(x=>x!=null)};
    }

    /// <summary>Prefer a real version. Long all-digit launcher product ids never win.</summary>
    private static string RealVersion(List<InstalledProgram> records)
    {
        foreach (var record in records.OrderByDescending(x => x.Source == "Windows"))
        {
            if (string.IsNullOrWhiteSpace(record.Version)) continue;
            if (ProductIdLike(record.Version, record.Identifier)) continue;
            return record.Version.Trim();
        }
        return "";
    }
}
