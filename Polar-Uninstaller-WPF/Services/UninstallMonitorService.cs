using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;
using Microsoft.Win32;

namespace PolarUninstallerWpf.Services;

/// <summary>
/// Result of one launcher-specific installation-state probe.
/// Confirmation requires launcher and payload evidence. Steam can confirm
/// removal with preserved auxiliary files. Launch or process exit alone is insufficient.
/// </summary>
public sealed record RemovalProbe(bool Confirmed, bool LauncherRecordPresent, bool InstallationFolderPresent, string Detail, bool MainExecutablePresent = false)
{
    public bool RetainedSteamAssets { get; init; }
    internal bool AwaitingSteamPayload { get; init; }
    internal bool SteamPayloadTimedOut { get; init; }
}

/// <summary>
/// Cheap, launcher-specific polling for official uninstall completion.
/// No files are ever deleted here and no UI thread work is performed.
/// </summary>
public static class UninstallMonitorService
{
    /// <summary>Default monitoring window for untracked application removal.</summary>
    public static TimeSpan TotalWindow => TimeSpan.FromMinutes(5);

    /// <summary>Adaptive polling: fast right after launch, slower later. No fixed blocking wait.</summary>
    public static TimeSpan IntervalFor(int attempt) => attempt switch
    {
        < 20 => TimeSpan.FromSeconds(1),
        < 50 => TimeSpan.FromSeconds(2),
        _ => TimeSpan.FromSeconds(4)
    };

    /// <summary>Waits for the official process, then retries live evidence while registration catches up.</summary>
    public static async Task<RemovalProbe> MonitorAsync(Func<RemovalProbe> probe,
        Task? officialExit, bool isGame, IProgress<RemovalProbe>? progress, CancellationToken token) =>
        await MonitorAsync(probe, officialExit, isGame, progress, token, TimeProvider.System).ConfigureAwait(false);

    internal static async Task<RemovalProbe> MonitorAsync(Func<RemovalProbe> probe,
        Task? officialExit, bool isGame, IProgress<RemovalProbe>? progress, CancellationToken token, TimeProvider clock)
    {
        if (officialExit != null)
        {
            // A bootstrapper or its tracked worker can legitimately take longer than the polling window.
            await officialExit.WaitAsync(token).ConfigureAwait(false);
        }

        long start = clock.GetTimestamp();
        long? steamPayloadStart = null;
        int attempt = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var result = await Task.Run(probe, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (result.Confirmed) return result;
            var (window, delay) = PollingPolicy(isGame, officialExit != null, result, attempt++);
            var remaining = window - clock.GetElapsedTime(start);
            if (result.AwaitingSteamPayload)
            {
                steamPayloadStart ??= clock.GetTimestamp();
                var settleRemaining = TimeSpan.FromSeconds(30) - clock.GetElapsedTime(steamPayloadStart.Value);
                if (settleRemaining < remaining) remaining = settleRemaining;
            }
            if (remaining <= TimeSpan.Zero)
                return result.AwaitingSteamPayload
                    ? result with
                    {
                        SteamPayloadTimedOut = true,
                        Detail = "Steam removed the installation record, but game files could not be verified as removed. Review the remaining folder or refresh after Steam finishes."
                    }
                    : result;
            progress?.Report(result);
            await Task.Delay(delay < remaining ? delay : remaining, token).ConfigureAwait(false);
        }
    }

    internal static (TimeSpan Window, TimeSpan Delay) PollingPolicy(bool isGame, bool tracked,
        RemovalProbe result, int attempt)
    {
        bool changed = !result.LauncherRecordPresent || !result.InstallationFolderPresent;
        var window = isGame && (!tracked || changed) ? TimeSpan.FromMinutes(15)
            : tracked ? TimeSpan.FromSeconds(isGame ? 20 : 8) : TotalWindow;
        return (window, isGame && changed ? TimeSpan.FromSeconds(1) : IntervalFor(attempt));
    }

    public static RemovalProbe Probe(InstalledProgram program, UninstallSnapshot? snapshot)
    {
        try
        {
            bool executable = snapshot!=null&&UninstallVerificationService.AnyExecutablePresent(snapshot.Executables);
            RemovalProbe WithCapturedEvidence(RemovalProbe result)
            {
                if (result.Confirmed && executable)
                    return result with { Confirmed = false, MainExecutablePresent = true,
                        Detail = "A captured game executable is still present." };
                // A removed main payload with absent authoritative launcher
                // records can leave logs/shortcuts behind. Such remnants are
                // not proof of a still-installed game. Unknown identity stays blocked.
                if(!result.Confirmed&&!result.LauncherRecordPresent&&!executable&&snapshot?.Executables.Count>0&&
                    DirectRemovalService.RunningProcesses(program.Location).Count==0)
                    return result with{Confirmed=true,Detail="Launcher registration and captured game executables are gone; retained resources require leftover inspection."};
                return result;
            }
            string id = program.Identifier ?? "";
            if (id.StartsWith("steam:", StringComparison.OrdinalIgnoreCase))
                return ProbeSteam(program, id["steam:".Length..], executable);
            if (id.StartsWith("epic:", StringComparison.OrdinalIgnoreCase))
                return WithCapturedEvidence(ProbeEpic(program, id["epic:".Length..], executable));
            if (id.StartsWith("ubisoft:", StringComparison.OrdinalIgnoreCase))
                return WithCapturedEvidence(ProbeUbisoft(program, id["ubisoft:".Length..], executable));
            if (id.StartsWith("rockstar:", StringComparison.OrdinalIgnoreCase))
                return WithCapturedEvidence(ProbeRockstar(program, id["rockstar:".Length..], executable));
            if (id.StartsWith("riot:", StringComparison.OrdinalIgnoreCase))
                return WithCapturedEvidence(ProbeRiot(program, snapshot, executable));
            if (id.StartsWith("battlenet:", StringComparison.OrdinalIgnoreCase))
            {
                bool record = LauncherMetadataDiscovery.BattleNet(CancellationToken.None, false).Any(x =>
                    x.Identifier.Equals(id, StringComparison.OrdinalIgnoreCase) || LeftoverService.EqualPath(x.Location, program.Location));
                if (record) return new(false, true, FolderHasFiles(program.Location), "Battle.net Agent still records this installed product.", executable);
            }
            if (id.StartsWith("gog:", StringComparison.OrdinalIgnoreCase) && RegistryKeyExists(@"SOFTWARE\GOG.com\Games\" + id[4..]))
                return new(false, true, FolderHasFiles(program.Location), "GOG still records this installation.", executable);
            return ProbeRegistered(program, snapshot, executable);
        }
        catch (Exception ex) when (LauncherMetadataDiscovery.ReadFailure(ex))
        {
            return new(false, true, true, "Probe error: " + ex.GetType().Name);
        }
    }

    private static RemovalProbe ProbeSteam(InstalledProgram program, string appId, bool executable)
    {
        if (appId.Length == 0 || !appId.All(char.IsAsciiDigit) || appId == "0")
            return new(false, true, true, "Invalid Steam App ID; removal cannot be monitored.", executable);
        string manifestName = $"appmanifest_{appId}.acf";
        int libraryBoundary = program.Location.IndexOf("\\steamapps\\", StringComparison.OrdinalIgnoreCase);
        // Usually the record is in the captured library. Check it before opening other Steam libraries.
        bool record = (libraryBoundary > 0 && ManifestPresent(Path.Combine(program.Location[..libraryBoundary], "steamapps", manifestName)))
            || SteamRoots(program.Location).Any(root => ManifestPresent(Path.Combine(root, "steamapps", manifestName)));
        bool folder = FolderHasFiles(program.Location);
        if (record) return new(false, true, folder, $"Steam still records App ID {appId}.", executable);
        if (!executable && folder && OnlyAuxiliarySteamFilesRemain(program.Location))
            return new(true, false, true, "Steam installation record and game payload are gone. The remaining auxiliary files were preserved.") { RetainedSteamAssets = true };
        if (folder) return new(false, false, true, "Steam manifest removed; the game folder still exists.", executable) { AwaitingSteamPayload = true };
        if (executable) return new(false, false, false, "A captured game executable is still present.", true);
        return new(true, false, false, "Steam manifest and installation folder are gone.", false);
    }

    private static bool ManifestPresent(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static bool OnlyAuxiliarySteamFilesRemain(string location)
    {
        if (!Path.IsPathFullyQualified(location)) return false;
        var root = new DirectoryInfo(location);
        if (root.Parent?.Name.Equals("common", StringComparison.OrdinalIgnoreCase) != true ||
            root.Parent.Parent?.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase) != true) return false;

        for (DirectoryInfo? path = root; path is not null; path = path.Parent)
            if ((path.Attributes & FileAttributes.ReparsePoint) != 0) return false;

        var pending = new Stack<DirectoryInfo>();
        pending.Push(root);
        int inspected = 0;
        long textBytes = 0;
        while (pending.TryPop(out var directory))
        {
            bool hasScript = false;
            bool hasOptions = false;
            foreach (var item in directory.EnumerateFileSystemInfos())
            {
                if (++inspected > 4096 || (item.Attributes & FileAttributes.ReparsePoint) != 0) return false;
                if (item is DirectoryInfo child) pending.Push(child);
                else if (item is FileInfo file)
                {
                    var extension = file.Extension.ToLowerInvariant();
                    if (extension is ".png" or ".jpg" or ".jpeg" or ".webp" or ".log") continue;
                    bool auxiliaryText = extension is ".ini" or ".cfg" or ".json" or ".txt" or ".xml";
                    bool nestedTool = directory != root && extension is (".bat" or ".cmd" or "");
                    bool options = nestedTool && file.Name.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase);
                    if (!(auxiliaryText || nestedTool) || !IsSmallSteamAuxiliaryFile(file, ref textBytes, options))
                        return false;
                    hasScript |= nestedTool && extension.Length > 0;
                    hasOptions |= options;
                }
                else return false;
            }
            // A small legacy OPTIONS blob next to a retained helper script
            // is auxiliary data. A standalone opaque file stays unverified.
            if (hasOptions && !hasScript) return false;
        }
        return inspected > 0 && DirectRemovalService.RunningProcesses(location).Count == 0;
    }

    private static bool IsSmallSteamAuxiliaryFile(FileInfo file, ref long textBytes, bool options)
    {
        const int maxFileBytes = 64 * 1024;
        const int maxTotalBytes = 256 * 1024;
        using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maxFileBytes || textBytes + stream.Length > maxTotalBytes) return false;
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        textBytes += bytes.Length;
        if (options)
            return bytes.Length <= 4096 && !bytes.AsSpan().StartsWith("MZ"u8) && !bytes.AsSpan().StartsWith("\u007fELF"u8);
        try
        {
            using var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false, true));
            return reader.ReadToEnd().All(c => !char.IsControl(c) || c is '\r' or '\n' or '\t');
        }
        catch (DecoderFallbackException) { return false; }
    }

    private static IEnumerable<string> SteamRoots(string location)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(location))
        {
            int index = location.IndexOf("\\steamapps\\", StringComparison.OrdinalIgnoreCase);
            if (index > 0) roots.Add(location[..index]);
        }
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var steam = baseKey.OpenSubKey(@"SOFTWARE\Valve\Steam");
                    string root = Convert.ToString(steam?.GetValue("SteamPath") ?? steam?.GetValue("InstallPath")) ?? "";
                    if (!string.IsNullOrWhiteSpace(root)) roots.Add(root);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
            }
        foreach (var root in roots.ToArray())
        {
            try
            {
                string folders = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(folders)) continue;
                var parsed = SteamKeyValueParser.Parse(File.ReadAllText(folders));
                var libraryFolders = SteamKeyValueParser.GetObject(parsed, "libraryfolders") ?? parsed;
                foreach (var library in libraryFolders)
                {
                    if (library.Value is Dictionary<string, object?> details)
                    {
                        string? path = SteamKeyValueParser.GetString(details, "path");
                        if (!string.IsNullOrWhiteSpace(path))
                            roots.Add(path!.Trim().Replace("\\\\", "\\").Replace('/', '\\'));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or InvalidOperationException) { }
        }
        return roots;
    }

    private static RemovalProbe ProbeEpic(InstalledProgram program, string catalogId, bool executable)
    {
        bool record = EpicEoshStore.HasInstallation(program);
            foreach (string file in LauncherMetadataDiscovery.EpicManifestFiles())
            {
                string raw = "";
                try
                {
                    raw = File.ReadAllText(file);
                    using var json = JsonDocument.Parse(raw);
                    var root = json.RootElement;
                    string Value(string key) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
                    if ((catalogId.Length > 0 && string.Equals(Value("CatalogItemId"), catalogId, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrWhiteSpace(program.Location) && LeftoverService.EqualPath(Value("InstallLocation"), program.Location)))
                    { record = true; break; }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
                {
                    // An unreadable manifest that still names this catalog item must
                    // never be treated as absent (no false success).
                    if (catalogId.Length > 0 && raw.Contains(catalogId, StringComparison.OrdinalIgnoreCase)) { record = true; break; }
                }
            }
        foreach (string file in LauncherMetadataDiscovery.EpicIndexFiles().Where(File.Exists))
        {
            using var index = JsonDocument.Parse(File.ReadAllText(file));
            if (index.RootElement.TryGetProperty("InstallationList", out var entries) && entries.ValueKind == JsonValueKind.Array)
                foreach (var entry in entries.EnumerateArray())
                    if (LeftoverService.EqualPath(LauncherMetadataDiscovery.Text(entry, "InstallLocation"), program.Location)) record = true;
        }
        bool folder = FolderHasFiles(program.Location);
        if (record) return new(false, true, folder, "Epic Games still records this installation.", executable);
        if (folder) return new(false, false, true, "Epic manifest removed; the game folder still exists.", executable);
        return new(true, false, false, "Epic manifest and installation folder are gone.", false);
    }

    private static RemovalProbe ProbeUbisoft(InstalledProgram program, string id, bool executable)
    {
        bool record = RegistryKeyExists(@"SOFTWARE\Ubisoft\Launcher\Installs\" + id);
        bool folder = FolderHasFiles(program.Location);
        if (record) return new(false, true, folder, "Ubisoft Connect still records this installation.", executable);
        if (folder) return new(false, false, true, "Ubisoft registration removed; the game folder still exists.", executable);
        return new(true, false, false, "Ubisoft registration and installation folder are gone.", false);
    }

    private static RemovalProbe ProbeRockstar(InstalledProgram program, string name, bool executable)
    {
        bool record = RegistryKeyExists(@"SOFTWARE\Rockstar Games\" + name);
        bool folder = FolderHasFiles(program.Location);
        if (record) return new(false, true, folder, "Rockstar Games Launcher still records this installation.", executable);
        if (folder) return new(false, false, true, "Rockstar registration removed; the game folder still exists.", executable);
        return new(true, false, false, "Rockstar registration and installation folder are gone.", false);
    }

    private static RemovalProbe ProbeRiot(InstalledProgram program, UninstallSnapshot? snapshot, bool executable)
    {
        bool record = RegistrationPresent(program, snapshot);
        bool metadata = RiotGameService.MetadataPresent(program);
        bool folder = FolderHasFiles(program.Location);
        if (record) return new(false, true, folder, "A Riot Games uninstall registration still exists.", executable);
        if (metadata) return new(false, true, folder, "Riot Client metadata still points to an existing installation.", executable);
        if (executable) return new(false, false, true, "The game executable still exists.", executable);
        if (folder) return new(false, false, true, "The Riot registration and metadata are gone; the game folder still exists.", false);
        return new(true, false, false, "Riot registration, metadata and installation folder are gone.", false);
    }

    private static RemovalProbe ProbeRegistered(InstalledProgram program, UninstallSnapshot? snapshot, bool executable)
    {
        bool record = RegistrationPresent(program, snapshot);
        bool folder = FolderHasFiles(program.Location);
        if(record&&!folder&&!executable&&string.IsNullOrWhiteSpace(program.Identifier)&&snapshot?.Executables.Count>0&&snapshot.Registry.Count>0&&
            snapshot.Registry.All(LeftoverService.RegistrationExists)&&DirectRemovalService.RunningProcesses(program.Location).Count==0)
            return new(true,true,false,"Captured application executable and installation payload are gone; the unchanged exact uninstall registration is an eligible leftover.",false);
        if (record) return new(false, true, folder, "The uninstall registration still exists.", executable);
        if (executable) return new(false, false, true, "The main executable still exists.", executable);
        if(snapshot?.Executables.Count>0&&DirectRemovalService.RunningProcesses(program.Location).Count==0)
            return new(true,false,folder,"Exact registration and captured application executable are gone; retained data requires a separate leftover inspection.",false);
        if (folder) return new(false, false, true, "The installation folder still contains files.", false);
        return new(true, false, false, "Registration, executable and installation folder are gone.", false);
    }

    private static bool RegistrationPresent(InstalledProgram program, UninstallSnapshot? snapshot)
    {
        // Once exact registrations have been captured, an unreadable unrelated
        // ARP key must not redefine this application's identity. A read failure
        // on a captured key itself still returns present (fail closed).
        if (snapshot?.Registry.Count>0) return snapshot.Registry.Any(LeftoverService.RegistrationExists);
        return LeftoverService.RegistrationExists(program);
    }

    private static bool RegistryKeyExists(string path)
    {
        Exception? unreadable=null;
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(path);
                if (key != null) return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { unreadable=ex; }
        }
        if(unreadable!=null)throw new IOException("A launcher registration view could not be read; installation absence cannot be confirmed.",unreadable);
        return false;
    }

    private static bool FolderHasFiles(string location)
    {
        if (string.IsNullOrWhiteSpace(location)) return false;
        try { return (File.GetAttributes(location)&FileAttributes.Directory)!=0 && Directory.EnumerateFileSystemEntries(location).Any(); }
        catch(FileNotFoundException){return false;}catch(DirectoryNotFoundException){return false;}
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return true; }
    }
}
