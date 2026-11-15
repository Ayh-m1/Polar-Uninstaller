using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PolarUninstallerWpf.Services;

public sealed class LauncherInventoryService(Action<string>? diagnostic = null)
{
    public Task<IReadOnlyList<InstalledProgram>> GetInstalledAsync(CancellationToken token) => Task.Run<IReadOnlyList<InstalledProgram>>(() =>
    {
        var results = new List<InstalledProgram>();
        void Add(string name, string publisher, string location, string version = "", string icon = "", long? size = null, string identifier = "")
        {
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(location) && Directory.Exists(location))
                results.Add(new(name.Trim(), publisher, version, location, icon, size, "", publisher, identifier, RequiresOfficialRemoval: true));
        }
        void Report(string message) => diagnostic?.Invoke("[GAMES] " + message);
        results.AddRange(LauncherMetadataDiscovery.Epic(token, Report));
        if(EpicGameRemovalService.Enabled)results.AddRange(new EpicGameRemovalService().RecoverableGames());
        if(LauncherRemovalCoordinator.Enabled)results.AddRange(EaGameDiscovery.Discover(token));
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var steam = baseKey.OpenSubKey(@"SOFTWARE\Valve\Steam");
                var steamRoot = Convert.ToString(steam?.GetValue("SteamPath") ?? steam?.GetValue("InstallPath"));
                if (!string.IsNullOrWhiteSpace(steamRoot)) ScanSteam(steamRoot, results, token);
                // Both per-user and machine registrations can own Ubisoft installs.
                {
                    using var ubisoft = baseKey.OpenSubKey(@"SOFTWARE\Ubisoft\Launcher\Installs");
                    if (ubisoft != null) foreach (var id in ubisoft.GetSubKeyNames())
                    {
                        token.ThrowIfCancellationRequested(); using var game = ubisoft.OpenSubKey(id);
                        string loc = Convert.ToString(game?.GetValue("InstallDir") ?? game?.GetValue("InstallPath") ?? game?.GetValue("InstallLocation")) ?? "";
                        if (LauncherMetadataDiscovery.HasPayload(loc, token)) Add(Convert.ToString(game?.GetValue("DisplayName")) ?? Path.GetFileName(loc.TrimEnd('\\', '/')), "Ubisoft", loc, icon: Convert.ToString(game?.GetValue("DisplayIcon")) ?? "", identifier: "ubisoft:" + id);
                    }
                }
                using var rockstar = baseKey.OpenSubKey(@"SOFTWARE\Rockstar Games");
                if (rockstar != null) foreach (var name in rockstar.GetSubKeyNames())
                {
                    token.ThrowIfCancellationRequested(); if (name.Contains("launcher", StringComparison.OrdinalIgnoreCase) || name.Contains("social club", StringComparison.OrdinalIgnoreCase)) continue;
                    using var game = rockstar.OpenSubKey(name);
                    string location=Convert.ToString(game?.GetValue("InstallFolder") ?? game?.GetValue("Install Folder"))??"";
                    string icon=Convert.ToString(game?.GetValue("DisplayIcon") ?? game?.GetValue("ExePath") ?? game?.GetValue("Executable"))??"";
                    if(icon.Length>0&&!Path.IsPathRooted(icon)&&Path.IsPathFullyQualified(location))icon=Path.Combine(location,icon);
                    if (LauncherMetadataDiscovery.HasPayload(location, token)) Add(name, "Rockstar Games", location, icon: icon, identifier: "rockstar:" + name);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException) { }
        }
        // Riot Games: discover VALORANT, League of Legends, Legends of Runeterra
        // and other verified titles from registrations, Riot Client metadata,
        // the client install index and validated game directories on any drive.
        // Riot Client and Riot Vanguard are shared components, never games.
        try { results.AddRange(RiotGameService.Discover(token)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException) { }

        results.AddRange(LauncherMetadataDiscovery.Gog(token, Report));
        try { results.AddRange(LauncherMetadataDiscovery.BattleNet(token)); }
        catch (Exception ex) when (LauncherMetadataDiscovery.ReadFailure(ex)) { Report("Battle.net: Agent database unreadable (" + ex.GetType().Name + ")."); }

        // Battle.net: discover installed Blizzard games through their Windows uninstall
        // registration, which uses Blizzard Uninstaller.exe with a --uid= product id.
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;
                foreach (var keyName in uninstall.GetSubKeyNames())
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        using var item = uninstall.OpenSubKey(keyName);
                        string name = Convert.ToString(item?.GetValue("DisplayName")) ?? "";
                        string publisher = Convert.ToString(item?.GetValue("Publisher")) ?? "";
                        string command = Convert.ToString(item?.GetValue("UninstallString")) ?? "";
                        string location = Convert.ToString(item?.GetValue("InstallLocation")) ?? "";
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        if (!command.Contains("Blizzard Uninstaller.exe", StringComparison.OrdinalIgnoreCase)) continue;
                        var uidMatch = Regex.Match(command, @"--uid=(?:""([^""]+)""|([^\s""]+))", RegexOptions.IgnoreCase);
                        string uid = uidMatch.Success ? (uidMatch.Groups[1].Success && uidMatch.Groups[1].Value.Length > 0 ? uidMatch.Groups[1].Value : uidMatch.Groups[2].Value) : "";
                        uid = uid.Trim().Trim('"');
                        if (string.IsNullOrWhiteSpace(uid) || uid.Equals("battle.net", StringComparison.OrdinalIgnoreCase)) continue;
                        // Optional ARP InstallLocation can be absent. Recover only from
                        // a matching Agent UID, never from a guessed game name/path.
                        if (string.IsNullOrWhiteSpace(location)) location = results.FirstOrDefault(x => x.Identifier.Equals("battlenet:" + uid, StringComparison.OrdinalIgnoreCase))?.Location ?? "";
                        if (string.IsNullOrWhiteSpace(location) || !LauncherMetadataDiscovery.HasPayload(location, token)) continue;
                        long? sizeKb = null;
                        var size = item?.GetValue("EstimatedSize");
                        if (long.TryParse(Convert.ToString(size), out long parsed) && parsed > 0) sizeKb = parsed;
                        string icon = Convert.ToString(item?.GetValue("DisplayIcon")) ?? "";
                        var icons = new List<string>();
                        if (icon.Length > 0) icons.Add(icon);
                        results.Add(new(name.Trim(), string.IsNullOrWhiteSpace(publisher) ? "Blizzard Entertainment" : publisher, Convert.ToString(item?.GetValue("DisplayVersion")) ?? "", location, icon, sizeKb, command, "Battle.net", "battlenet:" + uid, null, icons, true));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException) { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException) { }
        }

        foreach (var launcher in new[] { "Steam", "Epic Games", "Battle.net", "Ubisoft", "Rockstar Games", "GOG", "Riot Games" })
        {
            int count = results.Count(p => p.Source.Equals(launcher, StringComparison.OrdinalIgnoreCase));
            if (count > 0) Report(launcher + ": " + DisplayFormat.Number(count) + " verified installation record(s), before deduplication.");
            else if (LauncherDetected(launcher)) Report(launcher + ": launcher metadata exists, but no installed games passed validation. Check library availability, manifest access and installation completion.");
        }
        return results.Distinct().ToArray();
    }, token);

    internal static void ScanSteam(string root, List<InstalledProgram> results, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(root)) return;
        root = root.Trim().Replace('/', '\\');
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
        // Parse libraryfolders.vdf with a real Valve KeyValues parser so secondary
        // libraries on other drives are discovered reliably.
        string foldersPath = Path.Combine(root, "steamapps", "libraryfolders.vdf");
        if (File.Exists(foldersPath))
        {
            try
            {
                var parsed = SteamKeyValueParser.Parse(File.ReadAllText(foldersPath));
                var libraryFolders = SteamKeyValueParser.GetObject(parsed, "libraryfolders") ?? parsed;
                foreach (var library in libraryFolders)
                {
                    token.ThrowIfCancellationRequested();
                    if (library.Value is Dictionary<string, object?> details)
                    {
                        string? path = SteamKeyValueParser.GetString(details, "path");
                        if (!string.IsNullOrWhiteSpace(path))
                            libraries.Add(path!.Trim().Replace("\\\\", "\\").Replace('/', '\\'));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or InvalidOperationException) { }
        }

        var seenAppIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (string library in libraries)
        {
            token.ThrowIfCancellationRequested();
            string steamApps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamApps)) continue;
            IEnumerable<string> manifests;
            try { manifests = Directory.EnumerateFiles(steamApps, "appmanifest_*.acf"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { continue; }

            foreach (string manifestPath in manifests)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var data = SteamKeyValueParser.Parse(File.ReadAllText(manifestPath));
                    var appState = SteamKeyValueParser.GetObject(data, "AppState") ?? data;

                    string appId = SteamKeyValueParser.GetString(appState, "appid") ?? "";
                    if (string.IsNullOrWhiteSpace(appId) || !appId.All(char.IsAsciiDigit) || appId == "0") continue;
                    if (seenAppIds.Contains(appId)) continue; // validate before reserving the AppID

                    string name = SteamKeyValueParser.GetString(appState, "name") ?? "";
                    string installDir = SteamKeyValueParser.GetString(appState, "installdir") ?? "";
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(installDir)) continue;
                    if (installDir.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || installDir == "." || installDir == "..") continue;

                    string location = Path.Combine(steamApps, "common", installDir);
                    if (!Directory.Exists(location)) continue;

                    string stateFlags = SteamKeyValueParser.GetString(appState, "StateFlags") ?? "";
                    if (!string.IsNullOrWhiteSpace(stateFlags) && int.TryParse(stateFlags, out int flags) && (flags & 4) == 0) continue;

                    long? size = null;
                    string sizeText = SteamKeyValueParser.GetString(appState, "SizeOnDisk") ?? "";
                    if (long.TryParse(sizeText, out long bytes) && bytes > 0) size = bytes / 1024;

                    var iconPaths = new List<string>();
                    foreach (var registryView in new[] { RegistryView.Registry32, RegistryView.Registry64 })
                    {
                        try
                        {
                            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView);
                            using var gameKey = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App " + appId);
                            string registeredIcon = Convert.ToString(gameKey?.GetValue("DisplayIcon")) ?? "";
                            if (registeredIcon.Length > 0)
                            {
                                string clean = registeredIcon.Trim().Trim('"');
                                int comma = clean.LastIndexOf(','); if (comma > 0 && int.TryParse(clean[(comma + 1)..], out _)) clean = clean[..comma].Trim().Trim('"');
                                string relocated = Path.Combine(root, "steam", "games", Path.GetFileName(clean));
                                if (File.Exists(relocated)) iconPaths.Add(relocated);
                            }
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
                    }
                    foreach (string cache in new[] { Path.Combine(root, "appcache", "librarycache"), Path.Combine(library, "appcache", "librarycache"), Path.Combine(steamApps, "librarycache") })
                    {
                        foreach (string iconName in new[] { $"{appId}_icon.jpg", $"{appId}_logo.png", $"{appId}_header.jpg", $"{appId}_library_600x900.jpg", $"{appId}_hero.jpg", Path.Combine(appId, "icon.jpg"), Path.Combine(appId, "icon.png") })
                        {
                            string path = Path.Combine(cache, iconName);
                            if (File.Exists(path)) iconPaths.Add(path);
                        }
                    }
                    string perGameCache = Path.Combine(root, "appcache", "librarycache", appId);
                    if (Directory.Exists(perGameCache))
                    {
                        try
                        {
                            iconPaths.AddRange(Directory.EnumerateFiles(perGameCache)
                                .Where(p => new[] { ".png", ".jpg", ".ico" }.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
                                .OrderByDescending(p => Path.GetFileName(p).Contains("icon", StringComparison.OrdinalIgnoreCase))
                                .Take(32));
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
                    }

                    seenAppIds.Add(appId);
                    results.Add(new(name, "Steam", "", location, "", size, "", "Steam", "steam:" + appId, null, iconPaths, true));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException) { }
            }
        }
    }

    private static bool LauncherDetected(string name)
    {
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string? folder = name switch
        {
            "Battle.net" => Path.Combine(programData, "Battle.net"),
            "Epic Games" => Path.Combine(programData, "Epic"),
            "Riot Games" => Path.Combine(programData, "Riot Games"),
            _ => null
        };
        if (folder != null && Directory.Exists(folder)) return true;
        string keyPath = name switch
        {
            "Steam" => @"SOFTWARE\Valve\Steam", "Ubisoft" => @"SOFTWARE\Ubisoft\Launcher",
            "Rockstar Games" => @"SOFTWARE\Rockstar Games\Launcher", "GOG" => @"SOFTWARE\GOG.com\GalaxyClient",
            "Epic Games" => @"SOFTWARE\Epic Games\EpicGamesLauncher", _ => ""
        };
        if (keyPath.Length == 0) return false;
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            try { using var registry = RegistryKey.OpenBaseKey(hive, view); using var key = registry.OpenSubKey(keyPath); if (key != null) return true; }
            catch (Exception ex) when (LauncherMetadataDiscovery.ReadFailure(ex)) { }
        return false;
    }
}
