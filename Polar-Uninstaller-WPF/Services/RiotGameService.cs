using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PolarUninstallerWpf.Services;

/// <summary>A verified route for an official Riot Games removal.</summary>
public sealed record RiotUninstallRoute(
    string Kind,
    string Executable,
    IReadOnlyList<string> Arguments,
    string ProductId);

/// <summary>
/// Discovers installed Riot Games titles from verified local evidence:
/// Windows uninstall registrations, Riot Client install metadata, the Riot
/// Client install index, and validated game directories on any drive.
/// Riot Client and Riot Vanguard are shared components, never games.
/// </summary>
public static class RiotGameService
{
    private sealed record Candidate(
        string ProductId,
        string Name,
        string Location,
        string Root,
        string Publisher,
        string Version,
        string Uninstall,
        string Icon,
        long? SizeKb,
        int Priority);

    private static readonly Dictionary<string, string> ProductNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["valorant"] = "VALORANT",
        ["leagueoflegends"] = "League of Legends",
        ["bacon"] = "Legends of Runeterra",
        ["lion"] = "2XKO",
        ["teamfighttactics"] = "Teamfight Tactics"
    };

    // Teamfight Tactics is a game mode that ships inside the League of Legends
    // installation on PC. Listing it separately would duplicate the game.
    private static readonly HashSet<string> GameModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "teamfighttactics"
    };

    private static string CommonAppData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string ProgramFiles => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    private static string ProgramFilesX86 => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

    private static string MetadataDirectory => Path.Combine(CommonAppData, "Riot Games", "Metadata");

    public static IReadOnlyList<InstalledProgram> Discover(CancellationToken token = default)
    {
        var candidates = new List<Candidate>();
        candidates.AddRange(RegisteredGames(token));
        candidates.AddRange(MetadataGames(token));
        candidates.AddRange(AssociationGames(token));
        candidates.AddRange(DirectoryGames(KnownRoots(candidates), token));
        return Merge(candidates, token);
    }

    /// <summary>
    /// Parses the top-level scalar keys of a Riot product_settings.yaml file.
    /// Nested sections are intentionally ignored.
    /// </summary>
    public static Dictionary<string, string> ParseProductSettings(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in lines)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            if (raw.TrimStart().StartsWith('#')) continue;
            // Nested keys are indented; only top-level product settings matter.
            if (char.IsWhiteSpace(raw[0])) continue;
            int colon = raw.IndexOf(':');
            if (colon <= 0) continue;
            string key = raw[..colon].Trim();
            string value = raw[(colon + 1)..].Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') value = value[1..^1];
            if (key.Length > 0) values[key] = value;
        }
        return values;
    }

    /// <summary>Parses the verified --uninstall-product value from a registered command.</summary>
    public static string ParseProductId(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return "";
        var match = Regex.Match(command, "--uninstall-product=(?:\"([^\"]+)\"|([^\\s\"]+))", RegexOptions.IgnoreCase);
        if (!match.Success) return "";
        string value = match.Groups[1].Success && match.Groups[1].Value.Length > 0
            ? match.Groups[1].Value
            : match.Groups[2].Value;
        return NormalizeProduct(value.Trim().Trim('"'));
    }

    public static bool IsRiotGame(InstalledProgram program) =>
        program.Identifier.StartsWith("riot:", StringComparison.OrdinalIgnoreCase) ||
        (program.Sources ?? new[] { program.Source }).Any(source =>
            source.Contains("Riot Games", StringComparison.OrdinalIgnoreCase));

    public static string ProductIdOf(InstalledProgram program)
    {
        if (program.Identifier.StartsWith("riot:", StringComparison.OrdinalIgnoreCase))
            return NormalizeProduct(program.Identifier["riot:".Length..]);
        return NormalizeProduct(program.Name);
    }

    /// <summary>True for Riot Client, Riot Vanguard and other shared components.</summary>
    public static bool IsProtectedComponent(InstalledProgram program) =>
        IsProtectedComponentName(program.Name, program.Location, program.Publisher);

    public static bool IsProtectedComponentName(string name, string location, string publisher)
    {
        string normalized = NormalizeProduct(name);
        if (normalized is "riotclient" or "riotvanguard" or "vanguard") return true;
        string path = NormalizePath(location);
        if (path.Length == 0) return false;
        if (IsWithin(path, Path.Combine(CommonAppData, "Riot Games"))) return true;
        if (IsWithin(path, Path.Combine(LocalAppData, "Riot Games", "Riot Client"))) return true;
        if (IsWithin(path, Path.Combine(ProgramFiles, "Riot Vanguard"))) return true;
        // Only pay for the client lookup when the path actually looks Riot-owned.
        if (!path.Contains("Riot", StringComparison.OrdinalIgnoreCase)) return false;
        string? client = FindClientExecutable();
        if (client != null)
        {
            string? directory = Path.GetDirectoryName(client);
            if (directory != null && IsWithin(path, directory)) return true;
        }
        return false;
    }

    /// <summary>True for a shared Riot root or a shared client/vanguard/metadata path.</summary>
    public static bool IsSharedPath(string path)
    {
        string full = NormalizePath(path);
        if (full.Length == 0) return false;
        if (IsRiotRoot(full)) return true;
        if (IsWithin(full, Path.Combine(CommonAppData, "Riot Games"))) return true;
        if (IsWithin(full, Path.Combine(LocalAppData, "Riot Games"))) return true;
        if (IsWithin(full, Path.Combine(ProgramFiles, "Riot Vanguard"))) return true;
        if (!full.Contains("Riot", StringComparison.OrdinalIgnoreCase)) return false;
        string? client = FindClientExecutable();
        if (client != null)
        {
            string? directory = Path.GetDirectoryName(client);
            if (directory != null && IsWithin(full, directory)) return true;
        }
        return false;
    }

    /// <summary>
    /// True for a drive-root or well-known shared Riot Games container such as
    /// C:\Riot Games or %ProgramData%\Riot Games. Custom per-product folders
    /// installed elsewhere are not treated as shared.
    /// </summary>
    public static bool IsRiotRoot(string path)
    {
        string full = NormalizePath(path);
        if (full.Length == 0) return false;
        if (!Path.GetFileName(full).Equals("Riot Games", StringComparison.OrdinalIgnoreCase)) return false;
        string? parent = Path.GetDirectoryName(full);
        if (parent == null) return true;
        string? driveRoot = Path.GetPathRoot(full);
        if (driveRoot != null &&
            string.Equals(parent.TrimEnd('\\') + "\\", driveRoot, StringComparison.OrdinalIgnoreCase))
            return true;
        return new[] { ProgramFiles, ProgramFilesX86, LocalAppData, CommonAppData }
            .Any(root => root.Length > 0 && EqualPath(parent, root));
    }

    /// <summary>The game container that holds a "live" install folder.</summary>
    public static string GameRoot(string location)
    {
        string full = NormalizePath(location);
        if (full.Length == 0) return "";
        return Path.GetFileName(full).Equals("live", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(full) ?? full
            : full;
    }

    public static bool IsPlausibleInstall(string path, string productId)
    {
        string full = NormalizePath(path);
        if (full.Length == 0 || !Directory.Exists(full)) return false;
        if (IsProtectedComponentName("", full, "")) return false;
        if (IsRiotRoot(full)) return false;
        try
        {
            string[] preferred = NormalizeProduct(productId) switch
            {
                "valorant" => new[] { "VALORANT-Win64-Shipping.exe", "VALORANT.exe" },
                "leagueoflegends" => new[] { "LeagueClient.exe", "League of Legends.exe" },
                "bacon" => new[] { "LoR.exe", "Legends of Runeterra.exe" },
                _ => Array.Empty<string>()
            };
            if (preferred.Length > 0 && HasNamedExecutable(full, preferred, 5)) return true;
            return HasGameExecutable(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            System.Security.SecurityException or ArgumentException)
        {
            return false;
        }
    }

    public static string? FindClientExecutable()
    {
        foreach (string candidate in ClientCandidates())
            if (File.Exists(candidate)) return candidate;
        return null;
    }

    /// <summary>
    /// True when Riot Client metadata or the Riot install index still points to
    /// a live installation of this game. Stale metadata whose install folder is
    /// gone is not treated as a live record.
    /// </summary>
    public static bool MetadataPresent(InstalledProgram program)
    {
        string product = ProductIdOf(program);
        string location = NormalizePath(program.Location);
        try
        {
            if (Directory.Exists(MetadataDirectory))
            {
                foreach (string directory in Directory.EnumerateDirectories(MetadataDirectory))
                {
                    string folder = Path.GetFileName(directory);
                    if (NormalizeProduct(folder.Split('.')[0]) != product) continue;
                    string settings = Directory.EnumerateFiles(directory, "*.product_settings.yaml").FirstOrDefault() ?? "";
                    if (settings.Length == 0) continue;
                    var values = ParseProductSettings(File.ReadLines(settings));
                    string install = NormalizePath(values.GetValueOrDefault("product_install_full_path") ?? "");
                    if (install.Length == 0 || !Directory.Exists(install)) continue;
                    if (location.Length == 0) return true;
                    if (install.Equals(location, StringComparison.OrdinalIgnoreCase) ||
                        IsWithin(location, install) || IsWithin(install, location))
                        return true;
                }
            }

            string file = Path.Combine(CommonAppData, "Riot Games", "RiotClientInstalls.json");
            if (File.Exists(file))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file));
                if (document.RootElement.TryGetProperty("associated_client", out var associated) &&
                    associated.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in associated.EnumerateObject())
                    {
                        string path = NormalizePath(property.Name.Replace('/', '\\'));
                        if (path.Length == 0 || !Directory.Exists(path)) continue;
                        if (location.Length == 0) return true;
                        if (path.Equals(location, StringComparison.OrdinalIgnoreCase) ||
                            IsWithin(location, path) || IsWithin(path, location))
                            return true;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            System.Security.SecurityException or JsonException or ArgumentException)
        {
            // An unreadable record is not proof of removal.
            return true;
        }
        return false;
    }

    /// <summary>
    /// Resolves the official removal route for a game. Registered uninstallers
    /// are handled by the caller; this returns the verified Riot Client route
    /// when no game-specific registration exists. No command-line arguments are
    /// invented: the client is opened and the user confirms removal there.
    /// </summary>
    public static RiotUninstallRoute? ResolveRoute(InstalledProgram game, string? clientExecutable)
    {
        if (!IsRiotGame(game) || IsProtectedComponent(game)) return null;
        if (string.IsNullOrWhiteSpace(clientExecutable) || !File.Exists(clientExecutable)) return null;
        return new RiotUninstallRoute("client", clientExecutable, Array.Empty<string>(), ProductIdOf(game));
    }

    public static RiotUninstallRoute? ClientRoute(InstalledProgram game) =>
        ResolveRoute(game, FindClientExecutable());

    public static int InstalledGameCount() => Discover(CancellationToken.None).Count;

    /// <summary>Testable directory discovery used by the regression suite.</summary>
    public static IReadOnlyList<InstalledProgram> DiscoverDirectoryGames(
        IEnumerable<string> roots, CancellationToken token = default) =>
        Merge(DirectoryGames(roots, token), token);

    private static List<Candidate> RegisteredGames(CancellationToken token)
    {
        var results = new List<Candidate>();
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall == null) continue;
                foreach (string keyName in uninstall.GetSubKeyNames())
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        using var item = uninstall.OpenSubKey(keyName);
                        string name = Convert.ToString(item?.GetValue("DisplayName")) ?? "";
                        string publisher = Convert.ToString(item?.GetValue("Publisher")) ?? "";
                        string command = Convert.ToString(item?.GetValue("UninstallString")) ?? "";
                        string location = Convert.ToString(item?.GetValue("InstallLocation")) ?? "";
                        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(location)) continue;
                        bool riot = publisher.Contains("Riot Games", StringComparison.OrdinalIgnoreCase) ||
                                    name.Contains("Riot", StringComparison.OrdinalIgnoreCase) ||
                                    command.Contains("RiotClientServices.exe", StringComparison.OrdinalIgnoreCase);
                        if (!riot) continue;
                        if (IsProtectedComponentName(name, location, publisher)) continue;
                        string productId = ParseProductId(command);
                        if (productId.Length == 0) productId = NormalizeProduct(name);
                        if (productId.Length == 0 || IsGameMode(productId)) continue;
                        long? sizeKb = null;
                        if (long.TryParse(Convert.ToString(item?.GetValue("EstimatedSize")), out long parsed) && parsed > 0)
                            sizeKb = parsed;
                        string icon = Convert.ToString(item?.GetValue("DisplayIcon")) ?? "";
                        results.Add(new Candidate(
                            productId, name.Trim(), location.Replace('/', '\\'),
                            Path.GetDirectoryName(location.TrimEnd('\\')) ?? "",
                            string.IsNullOrWhiteSpace(publisher) ? "Riot Games" : publisher,
                            Convert.ToString(item?.GetValue("DisplayVersion")) ?? "",
                            command, icon, sizeKb, 3));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                        System.Security.SecurityException or ArgumentException) { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                System.Security.SecurityException or ArgumentException) { }
        }
        return results;
    }

    private static List<Candidate> MetadataGames(CancellationToken token)
    {
        var results = new List<Candidate>();
        try
        {
            if (!Directory.Exists(MetadataDirectory)) return results;
            foreach (string directory in Directory.EnumerateDirectories(MetadataDirectory))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    string folder = Path.GetFileName(directory);
                    string product = folder.Split('.')[0];
                    if (product.Length == 0) continue;
                    if (product.Contains("Riot Client", StringComparison.OrdinalIgnoreCase)) continue;
                    string productId = NormalizeProduct(product);
                    if (productId.Length == 0 || IsGameMode(productId) || IsProtectedComponentName(product, "", "")) continue;
                    string settings = Directory.EnumerateFiles(directory, "*.product_settings.yaml").FirstOrDefault() ?? "";
                    if (settings.Length == 0) continue;
                    var values = ParseProductSettings(File.ReadLines(settings));
                    string install = values.GetValueOrDefault("product_install_full_path") ?? "";
                    string root = values.GetValueOrDefault("product_install_root") ?? "";
                    if (install.Length == 0) continue;
                    results.Add(new Candidate(
                        productId, DisplayName(productId, install), install.Replace('/', '\\'),
                        root.Replace('/', '\\'), "Riot Games", "", "", "", null, 2));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                    System.Security.SecurityException or ArgumentException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            System.Security.SecurityException) { }
        return results;
    }

    private static List<Candidate> AssociationGames(CancellationToken token)
    {
        var results = new List<Candidate>();
        string file = Path.Combine(CommonAppData, "Riot Games", "RiotClientInstalls.json");
        if (!File.Exists(file)) return results;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            if (!document.RootElement.TryGetProperty("associated_client", out var associated) ||
                associated.ValueKind != JsonValueKind.Object)
                return results;
            foreach (var property in associated.EnumerateObject())
            {
                token.ThrowIfCancellationRequested();
                string path = property.Name.Replace('/', '\\').TrimEnd('\\');
                if (!Directory.Exists(path)) continue;
                string folder = Path.GetFileName(path);
                if (folder.Equals("live", StringComparison.OrdinalIgnoreCase))
                    folder = Path.GetFileName(Path.GetDirectoryName(path) ?? folder);
                string productId = NormalizeProduct(folder);
                if (productId.Length == 0 || IsGameMode(productId) ||
                    IsProtectedComponentName(folder, path, "Riot Games"))
                    continue;
                results.Add(new Candidate(
                    productId, DisplayName(productId, path), path,
                    Path.GetDirectoryName(path) ?? "", "Riot Games", "", "", "", null, 1));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            JsonException or ArgumentException) { }
        return results;
    }

    private static List<Candidate> DirectoryGames(IEnumerable<string> roots, CancellationToken token)
    {
        var results = new List<Candidate>();
        foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            if (!Directory.Exists(root)) continue;
            IEnumerable<string> directories;
            try { directories = Directory.EnumerateDirectories(root); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                System.Security.SecurityException) { continue; }
            foreach (string directory in directories)
            {
                token.ThrowIfCancellationRequested();
                string folder = Path.GetFileName(directory);
                if (IsProtectedComponentName(folder, directory, "Riot Games")) continue;
                string productId = NormalizeProduct(folder);
                if (productId.Length == 0 || IsGameMode(productId)) continue;
                if (!IsPlausibleInstall(directory, productId)) continue;
                results.Add(new Candidate(
                    productId, DisplayName(productId, directory), directory, root,
                    "Riot Games", "", "", "", null, 0));
            }
        }
        return results;
    }

    private static IReadOnlyList<string> KnownRoots(IEnumerable<Candidate> candidates)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
            if (candidate.Root.Length > 0) roots.Add(candidate.Root);
        foreach (string drive in SafeFixedDrives())
            roots.Add(Path.Combine(drive, "Riot Games"));
        foreach (string folder in new[] { ProgramFiles, ProgramFilesX86, LocalAppData, CommonAppData })
            if (folder.Length > 0) roots.Add(Path.Combine(folder, "Riot Games"));
        string? client = FindClientExecutable();
        if (client != null)
        {
            string? directory = Path.GetDirectoryName(client);
            string? parent = directory == null ? null : Path.GetDirectoryName(directory);
            if (parent != null) roots.Add(parent);
        }
        return roots.ToArray();
    }

    private static IReadOnlyList<InstalledProgram> Merge(List<Candidate> candidates, CancellationToken token)
    {
        var groups = new List<List<Candidate>>();
        foreach (var candidate in candidates.OrderByDescending(item => item.Priority))
        {
            token.ThrowIfCancellationRequested();
            var group = groups.FirstOrDefault(existing => IsSameGame(existing[0], candidate));
            if (group != null) group.Add(candidate);
            else groups.Add(new List<Candidate> { candidate });
        }

        var result = new List<InstalledProgram>();
        foreach (var group in groups)
        {
            token.ThrowIfCancellationRequested();
            var best = group.OrderByDescending(item => item.Priority).First();
            var deepest = group.Where(item => Directory.Exists(item.Location))
                .OrderByDescending(item => item.Location.Length)
                .FirstOrDefault();
            string location = deepest?.Location ?? best.Location;
            if (!IsPlausibleInstall(location, best.ProductId)) continue;

            string name = group.Where(item => item.Priority >= 3)
                .Select(item => item.Name).FirstOrDefault(value => value.Length > 0) ?? "";
            if (name.Length == 0)
                name = group.Select(item => item.Name).FirstOrDefault(value => value.Length > 0) ??
                       DisplayName(best.ProductId, location);
            string publisher = group.Select(item => item.Publisher)
                .FirstOrDefault(value => value.Length > 0) ?? "Riot Games";
            string version = group.Select(item => item.Version)
                .FirstOrDefault(value => value.Length > 0) ?? "";
            string uninstall = group.Select(item => item.Uninstall)
                .FirstOrDefault(value => value.Length > 0) ?? "";
            string icon = group.Select(item => item.Icon)
                .FirstOrDefault(value => value.Length > 0) ?? "";
            long? size = group.Select(item => item.SizeKb).FirstOrDefault(value => value.HasValue);
            string productId = best.ProductId;

            var icons = new List<string>();
            if (icon.Length > 0) icons.Add(icon);
            string executable = FindPrimaryExecutable(location, productId);
            if (executable.Length > 0) icons.Add(executable);

            result.Add(new InstalledProgram(
                name.Trim(), publisher, version, location, icons.FirstOrDefault() ?? "",
                size, uninstall, "Riot Games", "riot:" + productId, null, icons, true));
        }
        return result;
    }

    private static bool IsSameGame(Candidate a, Candidate b)
    {
        if (a.ProductId.Length > 0 && b.ProductId.Length > 0 &&
            a.ProductId.Equals(b.ProductId, StringComparison.OrdinalIgnoreCase))
            return true;
        string first = NormalizePath(a.Location), second = NormalizePath(b.Location);
        if (first.Length == 0 || second.Length == 0) return false;
        if (first.Equals(second, StringComparison.OrdinalIgnoreCase)) return true;
        return IsWithin(first, second) || IsWithin(second, first);
    }

    private static bool IsGameMode(string productId) =>
        GameModes.Contains(NormalizeProduct(productId));

    private static string DisplayName(string productId, string path)
    {
        if (ProductNames.TryGetValue(NormalizeProduct(productId), out string? name)) return name;
        string folder = Path.GetFileName(NormalizePath(path));
        if (folder.Equals("live", StringComparison.OrdinalIgnoreCase))
            folder = Path.GetFileName(Path.GetDirectoryName(NormalizePath(path)) ?? folder);
        return string.IsNullOrWhiteSpace(folder) ? productId : folder;
    }

    private static string FindPrimaryExecutable(string location, string productId)
    {
        string[] preferred = NormalizeProduct(productId) switch
        {
            "valorant" => new[] { "VALORANT-Win64-Shipping.exe", "VALORANT.exe" },
            "leagueoflegends" => new[] { "LeagueClient.exe", "League of Legends.exe" },
            "bacon" => new[] { "LoR.exe", "Legends of Runeterra.exe" },
            _ => Array.Empty<string>()
        };
        foreach (string name in preferred)
        {
            string? found = FindNamedExecutable(location, name, 5);
            if (found != null) return found;
        }
        return FindFirstGameExecutable(location) ?? "";
    }

    private static bool HasNamedExecutable(string root, IReadOnlyList<string> names, int maxDepth) =>
        names.Any(name => FindNamedExecutable(root, name, maxDepth) != null);

    private static string? FindNamedExecutable(string root, string name, int maxDepth)
    {
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        int examined = 0;
        while (pending.Count > 0 && examined < 1200)
        {
            var (directory, depth) = pending.Pop();
            try
            {
                foreach (string file in Directory.EnumerateFiles(directory, "*.exe").Take(64))
                {
                    examined++;
                    if (Path.GetFileName(file).Equals(name, StringComparison.OrdinalIgnoreCase)) return file;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                System.Security.SecurityException) { }
            if (depth >= maxDepth) continue;
            try
            {
                foreach (string child in Directory.EnumerateDirectories(directory).Take(32))
                    pending.Push((child, depth + 1));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                System.Security.SecurityException) { }
        }
        return null;
    }

    private static bool HasGameExecutable(string root) => FindFirstGameExecutable(root) != null;

    private static string? FindFirstGameExecutable(string root)
    {
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        int examined = 0;
        while (pending.Count > 0 && examined < 600)
        {
            var (directory, depth) = pending.Pop();
            try
            {
                foreach (string file in Directory.EnumerateFiles(directory, "*.exe").Take(64))
                {
                    examined++;
                    string name = Path.GetFileName(file);
                    if (name.Contains("unins", StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.Equals("RiotClientServices.exe", StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.Equals("update.exe", StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.Equals("RiotClientCrashHandler.exe", StringComparison.OrdinalIgnoreCase)) continue;
                    return file;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                System.Security.SecurityException) { }
            if (depth >= 3) continue;
            try
            {
                foreach (string child in Directory.EnumerateDirectories(directory).Take(32))
                    pending.Push((child, depth + 1));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                System.Security.SecurityException) { }
        }
        return null;
    }

    private static IEnumerable<string> ClientCandidates()
    {
        var candidates = new List<string>();
        string file = Path.Combine(CommonAppData, "Riot Games", "RiotClientInstalls.json");
        if (File.Exists(file))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file));
                foreach (string key in new[] { "rc_default", "rc_live" })
                {
                    if (!document.RootElement.TryGetProperty(key, out var value) ||
                        value.ValueKind != JsonValueKind.String) continue;
                    string path = (value.GetString() ?? "").Replace('/', '\\');
                    if (path.Length > 0) candidates.Add(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                JsonException or ArgumentException) { }
        }
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall == null) continue;
                foreach (string keyName in uninstall.GetSubKeyNames())
                {
                    using var item = uninstall.OpenSubKey(keyName);
                    string name = Convert.ToString(item?.GetValue("DisplayName")) ?? "";
                    if (!name.Contains("Riot Client", StringComparison.OrdinalIgnoreCase)) continue;
                    string location = Convert.ToString(item?.GetValue("InstallLocation")) ?? "";
                    if (location.Length > 0)
                        candidates.Add(Path.Combine(location.Replace('/', '\\').TrimEnd('\\'),
                            "RiotClientServices.exe"));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                System.Security.SecurityException or ArgumentException) { }
        }
        candidates.Add(Path.Combine(LocalAppData, "Riot Games", "Riot Client", "RiotClientServices.exe"));
        candidates.Add(Path.Combine(ProgramFilesX86, "Riot Games", "Riot Client", "RiotClientServices.exe"));
        candidates.Add(Path.Combine(ProgramFiles, "Riot Games", "Riot Client", "RiotClientServices.exe"));
        string? drive = Path.GetPathRoot(Environment.SystemDirectory);
        if (!string.IsNullOrWhiteSpace(drive))
            candidates.Add(Path.Combine(drive, "Riot Games", "Riot Client", "RiotClientServices.exe"));
        return candidates.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> SafeFixedDrives()
    {
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
        return drives.Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady)
            .Select(drive => drive.RootDirectory.FullName)
            .ToArray();
    }

    private static string NormalizeProduct(string value) =>
        InstallationPathResolver.NormalizeProduct(value ?? "");

    private static string NormalizePath(string value)
    {
        try
        {
            string path = Environment.ExpandEnvironmentVariables((value ?? "").Trim().Trim('"')).Replace('/', '\\');
            if (!Path.IsPathFullyQualified(path)) return "";
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "";
        }
    }

    private static bool EqualPath(string a, string b)
    {
        string first = NormalizePath(a), second = NormalizePath(b);
        return first.Length > 0 && first.Equals(second, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWithin(string path, string root)
    {
        string full = NormalizePath(path), parent = NormalizePath(root);
        if (full.Length == 0 || parent.Length == 0) return false;
        return full.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
               full.StartsWith(parent + "\\", StringComparison.OrdinalIgnoreCase);
    }
}
