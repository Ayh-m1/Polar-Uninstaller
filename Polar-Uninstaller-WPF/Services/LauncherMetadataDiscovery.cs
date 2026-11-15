using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace PolarUninstallerWpf.Services;

/// <summary>Read-only launcher installation evidence, independent of ARP registrations.
/// Paths come from launcher records, never from a game-name list or a drive crawl.</summary>
internal static class LauncherMetadataDiscovery
{
    internal static bool ReadFailure(Exception e) => e is IOException or UnauthorizedAccessException
        or System.Security.SecurityException or ArgumentException or JsonException or InvalidOperationException or FormatException or OverflowException;
    internal static string Text(JsonElement root, string key) => root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    internal static IEnumerable<string> EpicDataRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EpicGamesLauncher", "Saved", "Data")
        };
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            try
            {
                using var registry = RegistryKey.OpenBaseKey(hive, view);
                using var key = registry.OpenSubKey(@"SOFTWARE\Epic Games\EpicGamesLauncher");
                var path = Convert.ToString(key?.GetValue("AppDataPath"));
                if (!string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path))
                {
                    roots.Add(path); roots.Add(Path.Combine(path, "Data"));
                }
            }
            catch (Exception e) when (ReadFailure(e)) { }
        return roots;
    }

    internal static IEnumerable<string> EpicManifestFiles()
    {
        foreach (var root in EpicDataRoots())
        {
            var directory = Path.Combine(root, "Manifests");
            if (!Directory.Exists(directory)) continue;
            // Materialize here so enumeration failures are caught by the caller.
            foreach (var file in Directory.GetFiles(directory, "*.item", new EnumerationOptions
                { RecurseSubdirectories=true, MaxRecursionDepth=8, AttributesToSkip=FileAttributes.ReparsePoint, IgnoreInaccessible=false })) yield return file;
        }
    }

    internal static IEnumerable<string> EpicIndexFiles() => new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "UnrealEngineLauncher", "LauncherInstalled.dat")
    }.Concat(EpicDataRoots().Select(x => Path.Combine(x, "LauncherInstalled.dat"))).Distinct(StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlyList<InstalledProgram> Epic(CancellationToken token, Action<string> diagnostic,
        IEnumerable<string>? manifests = null, IEnumerable<string>? indexes = null)
    {
        var results = new List<InstalledProgram>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Read(JsonElement entry, bool index)
        {
            token.ThrowIfCancellationRequested();
            var location = Text(entry, "InstallLocation");
            if (!Directory.Exists(location) || InventoryMerger.NormalizePath(location).Length == 0) return;
            if (entry.TryGetProperty("bIsIncompleteInstall", out var incomplete) && incomplete.ValueKind == JsonValueKind.True) return;
            if (entry.TryGetProperty("bIsInstallationComplete", out var complete) && complete.ValueKind == JsonValueKind.False) return;
            string app = Text(entry, "AppName"), catalog = Text(entry, "CatalogItemId");
            if (catalog.Length == 0) catalog = Text(entry, "ItemId");
            if (app.Length == 0 && catalog.Length == 0) return;
            string exe = Text(entry, "LaunchExecutable"), icon = "";
            if (exe.Length > 0)
            {
                icon = Path.GetFullPath(Path.Combine(location, exe));
                if (!icon.StartsWith(Path.GetFullPath(location).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) || !File.Exists(icon)) return;
            }
            else if (!HasPayload(location, token)) return;
            if (index)
            {
                // The launcher index alone can survive an interrupted download.
                // Require the game's own installed manifest to agree with its AppName.
                var metadata = Path.Combine(location, ".egstore");
                if (!Directory.Exists(metadata) || !Directory.GetFiles(metadata, "*.mancpn").Any(file =>
                {
                    try { using var doc = JsonDocument.Parse(File.ReadAllText(file)); return app.Length > 0 && Text(doc.RootElement, "AppName").Equals(app, StringComparison.OrdinalIgnoreCase); }
                    catch (Exception e) when (ReadFailure(e)) { return false; }
                })) return;
            }
            if (!seen.Add(Path.GetFullPath(location))) return;
            string name = Text(entry, "DisplayName");
            if (name.Length == 0) name = Path.GetFileName(location.TrimEnd('\\', '/'));
            long? size = null;
            if (entry.TryGetProperty("InstallSize", out var value) && long.TryParse(value.ToString(), System.Globalization.NumberStyles.Integer,
                DisplayFormat.NumericCulture, out var bytes) && bytes > 0) size = bytes / 1024;
            var game = new InstalledProgram(name, "Epic Games", Text(entry, "AppVersionString"), location, icon, size, "", "Epic Games", "epic:" + (catalog.Length > 0 ? catalog : app), RequiresOfficialRemoval: true);
            string ns = Text(entry, "CatalogNamespace"); if (ns.Length == 0) ns = Text(entry, "NamespaceId");
            if (app.Length > 0 && ns.Length > 0 && catalog.Length > 0)
                game = game with { LaunchUri = "com.epicgames.launcher://apps/" + Uri.EscapeDataString(ns + ":" + catalog + ":" + app) + "?action=launch&silent=true" };
            results.Add(game);
        }
        try
        {
            foreach (var file in manifests ?? EpicManifestFiles())
                try { using var doc = JsonDocument.Parse(File.ReadAllText(file)); Read(doc.RootElement, false); }
                catch (Exception e) when (ReadFailure(e)) { diagnostic("Epic Games: manifest could not be read (" + e.GetType().Name + ")."); }
        }
        catch (Exception e) when (ReadFailure(e)) { diagnostic("Epic Games: manifest directory unavailable (" + e.GetType().Name + ")."); }
        foreach (var file in (indexes ?? EpicIndexFiles()).Where(File.Exists))
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                if (doc.RootElement.TryGetProperty("InstallationList", out var entries) && entries.ValueKind == JsonValueKind.Array)
                    foreach (var entry in entries.EnumerateArray())
                        try { Read(entry, true); } catch (Exception e) when (ReadFailure(e)) { diagnostic("Epic Games: invalid installation-index record."); }
            }
            catch (Exception e) when (ReadFailure(e)) { diagnostic("Epic Games: installation index unreadable (" + e.GetType().Name + ")."); }
        return results;
    }

    // Battle.net ProductDb fields follow the Agent schema also used by Lutris:
    // https://github.com/lutris/lutris/blob/master/lutris/util/battlenet/product_db.py
    // Unknown protobuf fields are skipped; malformed/truncated records fail closed.
    internal static IReadOnlyList<InstalledProgram> BattleNet(CancellationToken token, bool requirePayload = true, string? database = null)
    {
        string file = database ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Battle.net", "Agent", "product.db");
        if (!File.Exists(file)) return Array.Empty<InstalledProgram>();
        if (new FileInfo(file).Length > 32 * 1024 * 1024) throw new InvalidDataException("Battle.net database exceeds read limit.");
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream();
        byte[] buffer = new byte[65536]; int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            token.ThrowIfCancellationRequested();
            if (memory.Length + read > 32 * 1024 * 1024) throw new InvalidDataException("Battle.net database exceeds read limit.");
            memory.Write(buffer, 0, read);
        }
        var games = new List<InstalledProgram>();
        foreach (var field in Fields(memory.ToArray()).Where(x => x.Number == 1 && x.Bytes != null))
        {
            token.ThrowIfCancellationRequested(); var product = Fields(field.Bytes!);
            string uid = StringField(product, 1), code = StringField(product, 2);
            if (uid.Length == 0 || code.Length == 0 || new[] { "agent", "bna", "battle.net" }.Contains(code, StringComparer.OrdinalIgnoreCase)) continue;
            var settings = BytesField(product, 3); var state = BytesField(product, 4);
            string location = StringField(Fields(settings), 1);
            var baseState = Fields(BytesField(Fields(state), 1));
            if (!baseState.Any(x => x.Number == 1 && x.Value == 1) || InventoryMerger.NormalizePath(location).Length == 0) continue;
            if (requirePayload && (!Directory.Exists(location) || !File.Exists(Path.Combine(location, ".build.info")) || !HasPayload(location, token))) continue;
            string name = Path.GetFileName(location.TrimEnd('\\', '/'));
            games.Add(new(name, "Blizzard Entertainment", StringField(baseState, 7), location, "", null, "", "Battle.net", "battlenet:" + uid, RequiresOfficialRemoval: true));
        }
        return games;
    }

    internal static IReadOnlyList<InstalledProgram> Gog(CancellationToken token, Action<string> diagnostic)
    {
        var results = new List<InstalledProgram>();
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            try
            {
                using var registry = RegistryKey.OpenBaseKey(hive, view);
                using var games = registry.OpenSubKey(@"SOFTWARE\GOG.com\Games");
                if (games == null) continue;
                foreach (var id in games.GetSubKeyNames())
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        using var key = games.OpenSubKey(id);
                        string location = Convert.ToString(key?.GetValue("path")) ?? "";
                        if (!HasPayload(location, token)) continue;
                        roots.Add(location);
                        string name = Convert.ToString(key?.GetValue("gameName")) ?? Path.GetFileName(location.TrimEnd('\\', '/'));
                        string icon = Convert.ToString(key?.GetValue("exe")) ?? "";
                        if (icon.Length > 0 && !Path.IsPathRooted(icon)) icon = Path.Combine(location, icon);
                        results.Add(new(name, "GOG.com", Convert.ToString(key?.GetValue("buildId")) ?? "", location, icon,
                            null, "", "GOG", "gog:" + id, RequiresOfficialRemoval: true));
                    }
                    catch (Exception e) when (ReadFailure(e)) { diagnostic("GOG: an installation registration is unreadable."); }
                }
            }
            catch (Exception e) when (ReadFailure(e)) { diagnostic("GOG: registry view unavailable (" + e.GetType().Name + ")."); }
        foreach (var programFiles in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
            foreach (var parent in new[] { Path.Combine(programFiles, "GOG Galaxy", "Games"), Path.Combine(programFiles, "GOG Games") })
                try { if (Directory.Exists(parent)) foreach (var dir in Directory.GetDirectories(parent)) roots.Add(dir); }
                catch (Exception e) when (ReadFailure(e)) { diagnostic("GOG: library directory unavailable."); }
        // A folder containing an EXE is not sufficient to invent a GOG game.
        // Actual goggame-<id>.info metadata supplies the persistent product identity.
        foreach (var root in roots)
            try
            {
                if (!HasPayload(root, token)) continue;
                foreach (var file in Directory.GetFiles(root, "goggame-*.info"))
                {
                    token.ThrowIfCancellationRequested();
                    using var json = JsonDocument.Parse(File.ReadAllText(file)); var entry = json.RootElement;
                    string id = Text(entry, "gameId");
                    if (id.Length == 0 && entry.TryGetProperty("gameId", out var numeric) && numeric.ValueKind == JsonValueKind.Number) id = numeric.GetRawText();
                    if (id.Length == 0 || !id.All(char.IsAsciiDigit)) continue;
                    string name = Text(entry, "name"); if (name.Length == 0) name = Path.GetFileName(root);
                    results.Add(new(name, "GOG.com", "", root, "", null, "", "GOG", "gog:" + id, RequiresOfficialRemoval: true));
                }
            }
            catch (Exception e) when (ReadFailure(e)) { diagnostic("GOG: installed game metadata unavailable (" + e.GetType().Name + ")."); }
        return results;
    }

    private sealed record Field(int Number, ulong Value, byte[]? Bytes);
    private static List<Field> Fields(byte[] data)
    {
        var fields = new List<Field>(); int offset = 0;
        ulong Varint()
        {
            ulong value = 0;
            for (int shift = 0; shift < 70; shift += 7)
            {
                if (offset >= data.Length) throw new InvalidDataException("Truncated protobuf varint.");
                byte b = data[offset++]; if (shift == 63 && b > 1) throw new InvalidDataException("Overflowing protobuf varint.");
                value |= (ulong)(b & 127) << shift; if ((b & 128) == 0) return value;
            }
            throw new InvalidDataException("Invalid protobuf varint.");
        }
        while (offset < data.Length)
        {
            ulong tag = Varint(); int number = checked((int)(tag >> 3));
            if (number <= 0) throw new InvalidDataException("Invalid protobuf field.");
            int wire = (int)(tag & 7);
            if (wire == 0) fields.Add(new(number, Varint(), null));
            else
            {
                int length = wire switch { 1 => 8, 5 => 4, 2 => checked((int)Varint()), _ => throw new InvalidDataException("Unsupported protobuf wire type.") };
                if (length < 0 || length > data.Length - offset) throw new InvalidDataException("Truncated protobuf field.");
                if (wire == 2) fields.Add(new(number, 0, data.AsSpan(offset, length).ToArray()));
                offset += length;
            }
        }
        return fields;
    }
    private static byte[] BytesField(List<Field> fields, int number) => fields.FirstOrDefault(x => x.Number == number && x.Bytes != null)?.Bytes ?? Array.Empty<byte>();
    private static string StringField(List<Field> fields, int number) => new UTF8Encoding(false, true).GetString(BytesField(fields, number));

    internal static bool HasPayload(string root, CancellationToken token)
    {
        if (InventoryMerger.NormalizePath(root).Length == 0) return false;
        int visited = 0;
        var queue = new Queue<(string Path, int Depth)>(); queue.Enqueue((root, 0));
        while (queue.Count > 0 && visited++ < 256)
        {
            token.ThrowIfCancellationRequested(); var current = queue.Dequeue();
            try
            {
                if ((File.GetAttributes(current.Path) & FileAttributes.ReparsePoint) != 0) continue;
                if (Directory.EnumerateFiles(current.Path, "*.exe").Any(p => !Path.GetFileName(p).StartsWith("unins", StringComparison.OrdinalIgnoreCase)
                    && !Path.GetFileName(p).Contains("crash", StringComparison.OrdinalIgnoreCase))) return true;
                if (current.Depth < 3) foreach (var dir in Directory.EnumerateDirectories(current.Path).Take(256))
                    if (!new[] { ".egstore", "__Installer", "_CommonRedist", "redist" }.Contains(Path.GetFileName(dir), StringComparer.OrdinalIgnoreCase)) queue.Enqueue((dir, current.Depth + 1));
            }
            catch (Exception e) when (ReadFailure(e)) { }
        }
        return false;
    }
}
