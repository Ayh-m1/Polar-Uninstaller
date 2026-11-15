using System.Globalization;
using System.Text;
using System.Text.Json;
using PolarUninstallerWpf.Services;

internal static class Launcher139Checks
{
    // Narrow, read-only parser regression coverage. No launcher/ARP registrations
    // are changed; synthetic EXE payloads are text and are never executed.
    internal static int Run()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "fixtures", "launcher139-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); int passed = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS " + label); passed++; }
        try
        {
            string game = Path.Combine(root, "Game ١٢٣"); Directory.CreateDirectory(game);
            File.WriteAllText(Path.Combine(game, "game.exe"), "synthetic fixture, never executed");
            string manifest = Path.Combine(root, "game.item"), index = Path.Combine(root, "LauncherInstalled.dat");
            void Manifest(object entry) => File.WriteAllText(manifest, JsonSerializer.Serialize(entry));
            IReadOnlyList<InstalledProgram> Epic() => LauncherMetadataDiscovery.Epic(default, _ => { }, new[] { manifest }, Array.Empty<string>());
            Manifest(new { AppName = "synthetic-app", CatalogItemId = "synthetic-id", InstallLocation = game, LaunchExecutable = "game.exe", InstallSize = "3145728" });
            var epic = Epic().Single();
            Check(epic.SizeKb == 3072, "Epic string-valued InstallSize accepted");
            Check(epic.Name == "Game ١٢٣" && epic.Location == game, "Optional name fallback preserves actual Unicode filename/path");
            Check(epic.RequiresOfficialRemoval && GameClassificationService.IsGame(epic), "Epic classified and official-removal guard retained");
            Manifest(new { AppName = "synthetic-app", InstallLocation = game, LaunchExecutable = "game.exe", InstallSize = new { unexpected = true } });
            Check(Epic().Count == 1, "Invalid optional size does not discard installed Epic game");
            Manifest(new { AppName = "synthetic-app", InstallLocation = game, LaunchExecutable = "game.exe", bIsIncompleteInstall = true });
            Check(Epic().Count == 0, "Epic incomplete flag rejected");
            Manifest(new { AppName = "synthetic-app", InstallLocation = game, LaunchExecutable = "missing.exe" });
            Check(Epic().Count == 0, "Epic missing declared executable rejected");
            Manifest(new { AppName = "synthetic-app", InstallLocation = game, LaunchExecutable = "../outside.exe" });
            Check(Epic().Count == 0, "Epic executable path traversal rejected");
            File.WriteAllText(index, JsonSerializer.Serialize(new { InstallationList = new[] { new { AppName = "synthetic-app", InstallLocation = game, ItemId = "synthetic-id" } } }));
            IReadOnlyList<InstalledProgram> Index() => LauncherMetadataDiscovery.Epic(default, _ => { }, Array.Empty<string>(), new[] { index });
            Check(Index().Count == 0, "Epic stale index without installed game manifest rejected");
            Directory.CreateDirectory(Path.Combine(game, ".egstore"));
            File.WriteAllText(Path.Combine(game, ".egstore", "game.mancpn"), "{\"AppName\":\"synthetic-app\"}");
            Check(Index().Count == 1, "Epic installed index plus matching local manifest accepted");

            byte[] String(int field, string value) => Bytes(field, Encoding.UTF8.GetBytes(value));
            byte[] Varint(ulong value) { var data = new List<byte>(); do { byte b = (byte)(value & 127); value >>= 7; data.Add((byte)(b | (value > 0 ? 128 : 0))); } while (value > 0); return data.ToArray(); }
            byte[] Bytes(int field, byte[] data) => Varint((ulong)((field << 3) | 2)).Concat(Varint((ulong)data.Length)).Concat(data).ToArray();
            string db = Path.Combine(root, "product.db");
            void Database(bool installed, string code = "synthetic-code")
            {
                var state = new byte[] { 8, installed ? (byte)1 : (byte)0 }.Concat(String(7, "1.2.3")).ToArray();
                var product = String(1, "synthetic-uid").Concat(String(2, code)).Concat(Bytes(3, String(1, game))).Concat(Bytes(4, Bytes(1, state))).ToArray();
                File.WriteAllBytes(db, Bytes(1, product));
            }
            File.WriteAllText(Path.Combine(game, ".build.info"), "synthetic build evidence"); Database(true);
            var battle = LauncherMetadataDiscovery.BattleNet(default, database: db).Single();
            Check(battle.Identifier == "battlenet:synthetic-uid" && battle.Location == game, "Battle.net protobuf identity and custom path parsed");
            Check(battle.RequiresOfficialRemoval && GameClassificationService.IsGame(battle), "Battle.net official routing protection retained");
            Database(false); Check(LauncherMetadataDiscovery.BattleNet(default, database: db).Count == 0, "Battle.net uninstalled state rejected");
            Database(true, "agent"); Check(LauncherMetadataDiscovery.BattleNet(default, database: db).Count == 0, "Battle.net shared Agent excluded");
            Database(true); File.Delete(Path.Combine(game, "game.exe"));
            Check(LauncherMetadataDiscovery.BattleNet(default, database: db).Count == 0, "Battle.net stale payload rejected");
            Check(LauncherMetadataDiscovery.BattleNet(default, false, db).Count == 1, "Uninstall monitor still sees Agent record without payload");
            File.WriteAllBytes(db, new byte[] { 10, 127 }); bool rejected = false;
            try { LauncherMetadataDiscovery.BattleNet(default, database: db); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Truncated protobuf fails closed");
            var windows = epic with { Source = "Windows", Identifier = "", Uninstall = "fixture-uninstaller" };
            var merged = InventoryMerger.Merge(new[] { windows, epic });
            Check(merged.Count == 1 && merged[0].Identifier == epic.Identifier && merged[0].RequiresOfficialRemoval, "Registry and launcher evidence deduplicate without losing guards");
            string steam = Path.Combine(root, "Steam"), other = Path.Combine(root, "OtherLibrary");
            foreach (var library in new[] { steam, other }) Directory.CreateDirectory(Path.Combine(library, "steamapps", "common", "Synthetic Game"));
            File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"), "\"libraryfolders\" { \"1\" { \"path\" \"" + other.Replace("\\", "\\\\") + "\" } }");
            string SteamManifest(int flags) => "\"AppState\" { \"appid\" \"987654321\" \"name\" \"Synthetic Game\" \"installdir\" \"Synthetic Game\" \"StateFlags\" \"" + flags + "\" }";
            File.WriteAllText(Path.Combine(steam, "steamapps", "appmanifest_987654321.acf"), SteamManifest(0));
            File.WriteAllText(Path.Combine(other, "steamapps", "appmanifest_987654321.acf"), SteamManifest(4));
            var steamRecords = new List<InstalledProgram>(); LauncherInventoryService.ScanSteam(steam, steamRecords, default);
            Check(steamRecords.Count == 1 && steamRecords[0].Location.StartsWith(other), "Stale Steam library record cannot hide installed secondary-library copy");
            File.WriteAllText(Path.Combine(other, "steamapps", "appmanifest_987654321.acf"), SteamManifest(2));
            steamRecords.Clear(); LauncherInventoryService.ScanSteam(steam, steamRecords, default);
            Check(steamRecords.Count == 0, "Steam record without installed state bit rejected");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            Check(DisplayFormat.Number(12345) == "12,345", "Arabic culture uses Western formatted counts");
            Check(DisplayFormat.Date(new DateTime(2026, 11, 11), "yyyy-MM-dd") == "2026-11-11", "Arabic culture uses stable Gregorian date formatting");
            Console.WriteLine($"{passed} focused launcher/numeric checks passed; no personal applications modified."); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine("FAIL " + e); return 1; }
        finally { Directory.Delete(root, true); }
    }
}
