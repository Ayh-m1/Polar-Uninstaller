using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using PolarUninstallerWpf.Services;

// Register WPF pack URIs in this headless console test host before resource localization.
_ = System.IO.Packaging.PackUriHelper.Create(new Uri("file:///PolarFixture"));
DashboardStorageService.UseCacheDirectory(Path.Combine(AppContext.BaseDirectory,"fixtures","storage-cache-"+Guid.NewGuid().ToString("N")));
if (args.Contains("--launcher139")) return Launcher139Checks.Run();
if (args.Contains("--smart139")) return Smart139Checks.Run();
if (args.Contains("--unified139")) return await Unified139Checks.RunAsync();
if (args.Contains("--deletion139")) return await Deletion139Checks.RunAsync();
if (args.Contains("--shortcut-cleanup")) return await ShortcutCleanupChecks.RunAsync();
if (args.Contains("--quick152")) return await Quick152Checks.RunAsync();
if (args.Contains("--large-games152")) return await LargeGame152Checks.RunAsync();
if (args.Length >= 1 && args[0] == "--steam153") return await Steam153Checks.RunAsync(args.Skip(1).ToArray());
if (args.Contains("--epic-direct")) return await EpicDirectChecks.RunAsync();
if(args.Length==2&&args[0]=="--launcher-fixture-uninstall")return LauncherExpansionChecks.RemoveFixture(args[1]);
if(args.Contains("--launchers"))return await LauncherExpansionChecks.RunAsync();
if(args.Contains("--launcher-coverage"))return await LauncherExpansionChecks.AuditAsync();
if(args.Length==2&&args[0]=="--release141")return LauncherExpansionChecks.VerifyRelease(args[1]);
if(args.Length==2&&args[0]=="--release142")return LauncherExpansionChecks.VerifyRelease(args[1],"1.0.142",4);

var failures = new List<string>();
void Check(bool condition, string name)
{
    if (!condition) failures.Add(name);
    Console.WriteLine((condition ? "PASS" : "FAIL") + ": " + name);
}

var root = Environment.GetEnvironmentVariable("POLAR_REGRESSION_ROOT")??Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var propsVersion = System.Text.RegularExpressions.Regex.Match(
    File.ReadAllText(Path.Combine(root, "Directory.Build.props")), "<Version>([^<]+)</Version>").Groups[1].Value;

// === Test 1: Invalid App IDs are rejected by the monitor ===
foreach (var bad in new[] { "0", "", "abc", "0x1", "-1" })
{
    var program = new InstalledProgram("Test", "Test", "", "", "", null, "", "Steam", "steam:" + bad);
    var probe = UninstallMonitorService.Probe(program, null);
    Check(!probe.Confirmed, "Invalid App ID '" + bad + "' cannot be confirmed as removed");
}

// === Test 2: Steam completion requires the manifest and folder to be gone ===
var tmp = Path.Combine(AppContext.BaseDirectory, "fixtures", "polar-monitor-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(Path.Combine(tmp, "steamapps", "common", "csgo"));
    File.WriteAllText(Path.Combine(tmp, "steamapps", "common", "csgo", "game.exe"), "data");
    var manifest = Path.Combine(tmp, "steamapps", "appmanifest_730.acf");
    File.WriteAllText(manifest, "\"appid\" \"730\"\n\"name\" \"Counter-Strike 2\"\n\"installdir\" \"csgo\"");

    var steamGame = new InstalledProgram("Counter-Strike 2", "Steam", "", Path.Combine(tmp, "steamapps", "common", "csgo"), "", null, "", "Steam", "steam:730");
    var withManifest = UninstallMonitorService.Probe(steamGame, null);
    Check(!withManifest.Confirmed && withManifest.LauncherRecordPresent, "Steam manifest present => not confirmed");
    Check(withManifest.InstallationFolderPresent, "Steam game folder detected while installed");

    File.Delete(manifest);
    var folderOnly = UninstallMonitorService.Probe(steamGame, null);
    Check(!folderOnly.Confirmed && !folderOnly.LauncherRecordPresent && folderOnly.InstallationFolderPresent,
        "Steam manifest gone but folder present => still not confirmed (no false success)");

    Directory.Delete(Path.Combine(tmp, "steamapps", "common", "csgo"), true);
    var removed = UninstallMonitorService.Probe(steamGame, null);
    Check(removed.Confirmed && !removed.LauncherRecordPresent && !removed.InstallationFolderPresent,
        "Steam manifest and folder gone => confirmed");
}
catch (Exception ex)
{
    Check(false, "Steam monitor fixture threw: " + ex.GetType().Name + " " + ex.Message);
}
finally
{
    try { Directory.Delete(tmp, true); } catch { }
}

// === Test 3: Generic completion requires registration, executable and folder to be gone ===
var genericTmp = Path.Combine(AppContext.BaseDirectory, "fixtures", "polar-generic-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(genericTmp);
    string exe = Path.Combine(genericTmp, "app.exe");
    string data = Path.Combine(genericTmp, "data.bin");
    File.WriteAllText(exe, "data");
    File.WriteAllText(data, "data");
    var program = new InstalledProgram("Generic App", "Publisher", "1.0", genericTmp, "", null, "", "Windows", "generic-1");
    var snapshot = new UninstallSnapshot(program, Array.Empty<RegistryLeftover>(), Array.Empty<ShortcutLeftover>(), Array.Empty<OwnedDataFolder>(), new[] { exe });

    var present = UninstallMonitorService.Probe(program, snapshot);
    Check(!present.Confirmed && present.InstallationFolderPresent, "Executable present => not confirmed");

    File.Delete(exe);
    var folderOnly = UninstallMonitorService.Probe(program, snapshot);
    Check(folderOnly.Confirmed && folderOnly.InstallationFolderPresent, "Primary executable and exact registration gone => confirmed with data leftovers");
    Check(folderOnly.Detail.Length > 0, "Probe provides a human-readable reason");

    Directory.Delete(genericTmp, true);
    var removed = UninstallMonitorService.Probe(program, snapshot);
    Check(removed.Confirmed, "No registration, executable or folder => confirmed");
}
catch (Exception ex)
{
    Check(false, "Generic monitor fixture threw: " + ex.GetType().Name + " " + ex.Message);
}
finally
{
    try { Directory.Delete(genericTmp, true); } catch { }
}

// === Test 4: Monitoring policy is short and adaptive (no fixed 10-minute wait) ===
Check(UninstallMonitorService.TotalWindow <= TimeSpan.FromMinutes(5), "Monitoring window is at most 5 minutes");
Check(UninstallMonitorService.IntervalFor(0) <= TimeSpan.FromSeconds(2), "First poll happens quickly");
Check(UninstallMonitorService.IntervalFor(100) <= TimeSpan.FromSeconds(10), "Later polls stay lightweight");
Check(!(UninstallMonitorService.TotalWindow == TimeSpan.FromMinutes(10)), "No mandatory 10-minute timeout");

// === Test 5: Steam manifest scanning still filters invalid App IDs ===
var scanTmp = Path.Combine(AppContext.BaseDirectory, "fixtures", "polar-scan-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(Path.Combine(scanTmp, "steamapps", "common", "csgo"));
    File.WriteAllText(Path.Combine(scanTmp, "steamapps", "libraryfolders.vdf"), "\"path\" \"" + scanTmp + "\"");
    File.WriteAllText(Path.Combine(scanTmp, "steamapps", "appmanifest_bad.acf"), "{ not valid json at all ");
    File.WriteAllText(Path.Combine(scanTmp, "steamapps", "appmanifest_zero.acf"), "\"appid\" \"0\"\n\"name\" \"Zero Game\"\n\"installdir\" \"zerogame\"");
    File.WriteAllText(Path.Combine(scanTmp, "steamapps", "appmanifest_missing.acf"), "\"installdir\" \"nomgame\"");
    File.WriteAllText(Path.Combine(scanTmp, "steamapps", "appmanifest_valid.acf"),
        "\"appid\" \"730\"\n\"name\" \"Counter-Strike 2\"\n\"installdir\" \"csgo\"\n\"SizeOnDisk\" \"10485760\"");

    var results = new List<InstalledProgram>();
    try { LauncherInventoryService.ScanSteam(scanTmp, results, CancellationToken.None); }
    catch (Exception ex) { Check(false, "ScanSteam threw: " + ex.GetType().Name + " " + ex.Message); results.Clear(); }

    Check(results.All(r => r.Identifier != "steam:0"), "App ID 0 is filtered out");
    Check(results.Any(r => r.Identifier == "steam:730" && r.Name == "Counter-Strike 2"), "Valid manifest 730 produces entry");
    Check(results.All(r => r.Uninstall == ""), "Steam uninstall command is empty (no fake -uninstall)");
    Check(results.Count == 1, "Only valid manifest produced an entry (count=" + results.Count + ")");
}
catch (Exception ex)
{
    Check(false, "Steam scan fixture threw: " + ex.GetType().Name + " " + ex.Message);
}
finally
{
    try { Directory.Delete(scanTmp, true); } catch { }
}

// === Test 6: Arabic text integrity across the project ===
var sourceFiles = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
    .Where(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
    .Where(p => !p.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase) && !p.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase))
    .ToList();
var corrupted = new List<string>();
foreach (var file in sourceFiles)
{
    var text = File.ReadAllText(file, Encoding.UTF8);
    if (text.Contains('\uFFFD')) corrupted.Add(Path.GetFileName(file));
}
Check(corrupted.Count == 0, "No U+FFFD replacement characters in source/JSON (corrupted: " + string.Join(", ", corrupted) + ")");

var deepPath = Path.Combine(root, "DeepUninstallerView.xaml.cs");
Check(File.Exists(deepPath), "DeepUninstallerView.xaml.cs found at " + deepPath);
if (File.Exists(deepPath))
{
    var deep = File.ReadAllText(deepPath, Encoding.UTF8);
    Check(deep.Contains("فتح Steam لإزالة"), "Steam uninstall confirmation is valid Arabic");
    Check(deep.Contains("تعذر فحص") == false, "No stale corrupted Arabic fragments");
    Check(!deep.Contains("verifyRemoval"), "Verify / Rescan button and its logic are removed");
    Check(!deep.Contains("VerifyPending"), "VerifyPending handler is removed");
    Check(deep.Contains("steam://uninstall/"), "Steam official uninstall protocol preserved");
    Check(deep.Contains("StartMonitoring") && deep.Contains("MonitorRemovalAsync"), "Automatic monitoring is wired into the uninstall flow");
    Check(deep.Contains("Unable to confirm removal"), "Non-blocking unconfirmed status exists");
    Check(!deep.Contains("TimeSpan.FromMinutes(10)"), "No 10-minute blocking wait remains");
}

var jsonPath = Path.Combine(root, "Assets", "ReleaseNotes.json");
Check(File.Exists(jsonPath), "ReleaseNotes.json found");
if (File.Exists(jsonPath))
{
    var json = File.ReadAllText(jsonPath, Encoding.UTF8);
    Check(System.Text.RegularExpressions.Regex.Matches(json, "[\u0600-\u06FF]+").Count > 0, "ReleaseNotes.json contains Arabic Unicode");
    Check(json.Contains("يُكتشف اكتمال الإزالة تلقائيًا"), "Arabic What's New mentions automatic detection");
    Check(json.Contains("Removed the Verify / Rescan button"), "English What's New mentions Verify / Rescan removal");
    Check(!json.Contains('\uFFFD'), "No replacement character in ReleaseNotes.json");
}

// === Test 7: Recursive storage scan is accurate and does not double count ===
var fixtureBase = Path.Combine(AppContext.BaseDirectory, "fixtures");
Directory.CreateDirectory(fixtureBase);
var storageRoot = Path.Combine(fixtureBase, "polar-storage-" + Guid.NewGuid().ToString("N"));
try
{
    string sub = Path.Combine(storageRoot, "sub");
    string nested = Path.Combine(sub, "nested");
    string empty = Path.Combine(storageRoot, "empty");
    Directory.CreateDirectory(nested);
    Directory.CreateDirectory(empty);
    File.WriteAllBytes(Path.Combine(storageRoot, "a.bin"), new byte[1000]);
    File.WriteAllBytes(Path.Combine(sub, "b.bin"), new byte[2000]);
    File.WriteAllBytes(Path.Combine(nested, "c.bin"), new byte[3000]);

    var service = new DashboardStorageService();
    var scan = await service.ScanAsync(storageRoot, CancellationToken.None);
    Check(scan.TotalBytes == 6000, "Storage scan totals only real bytes (got " + scan.TotalBytes + ")");
    Check(scan.FilesScanned == 3, "Storage scan counts 3 files (got " + scan.FilesScanned + ")");
    Check(scan.FoldersScanned == 4, "Storage scan counts 4 folders (got " + scan.FoldersScanned + ")");
    Check(!scan.Limited && scan.Skipped == 0, "Complete fixture scan is not marked incomplete");

    var subEntry = scan.LargestFolders.FirstOrDefault(f => string.Equals(f.Path, sub, StringComparison.OrdinalIgnoreCase));
    var nestedEntry = scan.LargestFolders.FirstOrDefault(f => string.Equals(f.Path, nested, StringComparison.OrdinalIgnoreCase));
    var emptyEntry = scan.LargestFolders.FirstOrDefault(f => string.Equals(f.Path, empty, StringComparison.OrdinalIgnoreCase));
    Check(subEntry is not null && subEntry.Bytes == 5000, "Immediate child folder size includes all descendants exactly once (got " + (subEntry?.Bytes.ToString() ?? "missing") + ")");
    Check(nestedEntry is null, "Nested folders are not listed alongside their parent folder");
    Check(emptyEntry is not null && emptyEntry.Bytes == 0, "Empty immediate child folders are listed with zero size");
    Check(scan.LargestFolders.Count == 2, "Only the two immediate child folders are listed (got " + scan.LargestFolders.Count + ")");
    string trimmedStorageRoot = Path.TrimEndingDirectorySeparator(storageRoot);
    Check(scan.LargestFolders.All(f => string.Equals(Path.GetDirectoryName(f.Path), trimmedStorageRoot, StringComparison.OrdinalIgnoreCase)),
        "Every listed folder is an immediate child of the scan root");
    Check(scan.LargestFolders.All(f => !string.Equals(f.Path, storageRoot, StringComparison.OrdinalIgnoreCase)), "Scan root is not listed as one of the largest folders");
    Check(scan.Largest.Count > 0 && scan.Largest[0].Name == "c.bin" && scan.Largest[0].Bytes == 3000, "Largest files are ranked by real size");
    Check(scan.Root == Path.TrimEndingDirectorySeparator(storageRoot) && !scan.IsDriveRoot, "Completed scan reports its exact folder scope");
    Check(DashboardStorageService.TryGetCached(storageRoot) is not null, "Completed scans are cached for page navigation");

    using (var cancelled = new CancellationTokenSource())
    {
        cancelled.Cancel();
        bool threw = false;
        try { await service.ScanAsync(storageRoot, cancelled.Token); }
        catch (OperationCanceledException) { threw = true; }
        Check(threw, "Cancelled storage scan stops promptly");
    }
}
catch (Exception ex)
{
    Check(false, "Storage scan fixture threw: " + ex.GetType().Name + " " + ex.Message);
}
finally
{
    DashboardStorageService.ClearCache();
    try { Directory.Delete(storageRoot, true); } catch { }
}

// === Test 8: Directory junctions are skipped and never scan outside the selected scope ===
var linkRoot = Path.Combine(fixtureBase, "polar-junction-" + Guid.NewGuid().ToString("N"));
try
{
    string inside = Path.Combine(linkRoot, "inside");
    string outside = Path.Combine(linkRoot, "outside");
    string junction = Path.Combine(inside, "junction");
    Directory.CreateDirectory(inside);
    Directory.CreateDirectory(outside);
    File.WriteAllBytes(Path.Combine(inside, "small.bin"), new byte[1000]);
    File.WriteAllBytes(Path.Combine(outside, "large.bin"), new byte[50000]);

    bool linked = false;
    try
    {
        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("/c"); psi.ArgumentList.Add("mklink"); psi.ArgumentList.Add("/J"); psi.ArgumentList.Add(junction); psi.ArgumentList.Add(outside);
        using var process = System.Diagnostics.Process.Start(psi);
        if (process != null) { process.WaitForExit(5000); linked = process.ExitCode == 0 && Directory.Exists(junction); }
    }
    catch (Exception) { linked = false; }

    if (linked)
    {
        var scan = await new DashboardStorageService().ScanAsync(inside, CancellationToken.None);
        Check(scan.TotalBytes == 1000, "Junction target outside the scope is not counted (got " + scan.TotalBytes + ")");
        Check(scan.Limited && scan.Skipped >= 1, "Skipped junction is reported as an incomplete scan");
        try { Directory.Delete(junction); } catch { }
    }
    else
    {
        Console.WriteLine("SKIP: directory junction could not be created on this machine");
    }
}
catch (Exception ex)
{
    Check(false, "Junction fixture threw: " + ex.GetType().Name + " " + ex.Message);
}
finally
{
    try
    {
        foreach (var dir in Directory.EnumerateDirectories(linkRoot, "*", SearchOption.AllDirectories).OrderByDescending(p => p.Length))
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) Directory.Delete(dir);
        Directory.Delete(linkRoot, true);
    }
    catch { }
}

// === Test 9: Size formatting uses KB / MB / GB / TB ===
Check(DriveSummary.Format(1536) == "1.5 KB", "Format(1536) => 1.5 KB (got " + DriveSummary.Format(1536) + ")");
Check(DriveSummary.Format(1048576) == "1.0 MB", "Format(1 MiB) => 1.0 MB (got " + DriveSummary.Format(1048576) + ")");
Check(DriveSummary.Format(1073741824) == "1.0 GB", "Format(1 GiB) => 1.0 GB (got " + DriveSummary.Format(1073741824) + ")");
Check(DriveSummary.Format(1099511627776) == "1.0 TB", "Format(1 TiB) => 1.0 TB (got " + DriveSummary.Format(1099511627776) + ")");
Check(new DriveSummary("C:\\", 1000, 250) { Label = "System" }.DisplayName.StartsWith("C:"), "Drive display name shows letter and label");
Check(Math.Abs(new DriveSummary("C:\\", 1000, 250).UsedPercent - 75) < 0.001, "Drive used percentage is accurate");

// === Test 10: Polar cache inspection and cleanup stay inside Polar-owned locations ===
var cacheRoot = Path.Combine(fixtureBase, "polar-cache-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(cacheRoot);
    string updateDir = Path.Combine(cacheRoot, "PolarWpfUpdate-test");
    string setupDir = Path.Combine(cacheRoot, "PolarSetup-test");
    string unrelatedDir = Path.Combine(cacheRoot, "OtherApp-cache");
    Directory.CreateDirectory(updateDir);
    Directory.CreateDirectory(setupDir);
    Directory.CreateDirectory(unrelatedDir);
    File.WriteAllBytes(Path.Combine(updateDir, "update.exe"), new byte[3000]);
    File.WriteAllBytes(Path.Combine(updateDir, "extra.tmp"), new byte[1000]);
    string setupFile = Path.Combine(setupDir, "setup.exe");
    File.WriteAllBytes(setupFile, new byte[500]);
    File.WriteAllBytes(Path.Combine(unrelatedDir, "other.bin"), new byte[700]);

    var inventory = await PolarCacheService.InspectAsync(cacheRoot);
    Check(inventory.Entries.Count == 2, "Cache inspection finds only PolarWpfUpdate-* and PolarSetup-* (got " + inventory.Entries.Count + ")");
    Check(inventory.TotalBytes == 4500, "Cache inspection reports the real recoverable size (got " + inventory.TotalBytes + ")");

    UpdateDownloadService.RegisterActiveDirectory(updateDir);
    try
    {
        var activeInventory = await PolarCacheService.InspectAsync(cacheRoot);
        Check(activeInventory.Entries.Count == 1 && activeInventory.Entries[0].Path == setupDir, "Active update download directory is protected from cleanup");
    }
    finally { UpdateDownloadService.UnregisterActiveDirectory(updateDir); }

    using (var locked = new FileStream(setupFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
    {
        var partial = await PolarCacheService.ClearAsync(cacheRoot);
        Check(partial.FreedBytes == 4000, "Cleanup reports only bytes actually removed (got " + partial.FreedBytes + ")");
        Check(!Directory.Exists(updateDir), "Unlocked Polar update cache directory is removed");
        Check(File.Exists(setupFile), "Locked cache file survives and is not reported as freed");
        Check(partial.Skipped >= 1, "Locked cache file is reported as skipped");
    }

    var final = await PolarCacheService.ClearAsync(cacheRoot);
    Check(final.FreedBytes == 500, "Second cleanup removes the previously locked file (got " + final.FreedBytes + ")");
    Check(!Directory.Exists(updateDir) && !Directory.Exists(setupDir), "Both Polar cache directories are gone after cleanup");
    Check(File.Exists(Path.Combine(unrelatedDir, "other.bin")), "Unrelated cache directory is never touched");
}
catch (Exception ex)
{
    Check(false, "Polar cache fixture threw: " + ex.GetType().Name + " " + ex.Message);
}
finally
{
    try { Directory.Delete(cacheRoot, true); } catch { }
}

// === Test 11: Unicode file names survive File Browser listing ===
var unicodeRoot = Path.Combine(fixtureBase, "polar-unicode-" + Guid.NewGuid().ToString("N"));
try
{
    string unicodeDir = Path.Combine(unicodeRoot, "مجلد تجريبي");
    string unicodeFile = Path.Combine(unicodeDir, "ملف خاص.txt");
    Directory.CreateDirectory(unicodeDir);
    File.WriteAllText(unicodeFile, "test");
    var entries = await new FileBrowserService().ListAsync(unicodeDir);
    var entry = entries.FirstOrDefault(e => e.Name == "ملف خاص.txt");
    Check(entry is not null, "Unicode file name is listed exactly");
    Check(entry is not null && entry.FullPath == unicodeFile, "Unicode full path is preserved for File Browser selection");
}
catch (Exception ex)
{
    Check(false, "Unicode fixture threw: " + ex.GetType().Name + " " + ex.Message);
}
finally
{
    try { Directory.Delete(unicodeRoot, true); } catch { }
}

// === Test 12: Dashboard / File Browser wiring is present ===
var dashboardXaml = File.ReadAllText(Path.Combine(root, "DashboardView.xaml"), Encoding.UTF8);
Check(!dashboardXaml.Contains("Last 7 Days") && !dashboardXaml.Contains("Recent Activity"), "Dashboard chart and recent-activity panels are removed");
Check(dashboardXaml.Contains("StorageTitle") && dashboardXaml.Contains("DriveSelector") && dashboardXaml.Contains("UsageArc"), "Dashboard keeps the redesigned Storage Overview panel");
var mainCode = File.ReadAllText(Path.Combine(root, "MainWindow.xaml.cs"), Encoding.UTF8);
Check(mainCode.Contains("OpenStorageItem") && mainCode.Contains("new DashboardView(arabic, activity, OpenStorageItem)"), "Dashboard opens items through the built-in File Browser");
Check(mainCode.Contains("pendingFileReveal"), "MainWindow passes the exact file to reveal");
var browserCode = File.ReadAllText(Path.Combine(root, "FileBrowserView.xaml.cs"), Encoding.UTF8);
Check(browserCode.Contains("ApplyPendingReveal"), "File Browser selects the requested item after navigation");
var cacheService = File.ReadAllText(Path.Combine(root, "Services", "PolarCacheService.cs"), Encoding.UTF8);
Check(cacheService.Contains("IsActiveDirectory"), "Clear Cache protects active update directories");
var dashboardCode = File.ReadAllText(Path.Combine(root, "DashboardView.xaml.cs"), Encoding.UTF8);
Check(dashboardCode.Contains("ActivityMetricsService.Instance.ResetAsync") && dashboardCode.Contains("history.ClearAsync"), "Clear Cache resets persisted statistics and uninstall history");
Check(dashboardCode.Contains("BrowserOperationsService.ContentChanged"), "Dashboard refreshes after File Browser deletions");

// === Test 13: Quick Cleaner number and size formatting ===
Check(DisplayFormat.Number(4357) == "4,357", "File count uses stable grouping (got " + DisplayFormat.Number(4357) + ")");
Check(DisplayFormat.Number(13369.9, 1) == "13,369.9", "Decimal value uses stable separator (got " + DisplayFormat.Number(13369.9, 1) + ")");
Check(DisplayFormat.Size(1536, false) == "1.5 KB", "English size formats as 1.5 KB (got " + DisplayFormat.Size(1536, false) + ")");
long mbBytes = (long)Math.Round(13369.9 * 1048576.0);
Check(DisplayFormat.Size(mbBytes, false) == "13.06 GB", "Values above 1 GB are promoted to GB (got " + DisplayFormat.Size(mbBytes, false) + ")");
long groupedMbBytes = (long)Math.Round(1000.5 * 1048576.0);
Check(DisplayFormat.Size(groupedMbBytes, false) == "1,000.5 MB", "Large MB value keeps thousands grouping and one decimal (got " + DisplayFormat.Size(groupedMbBytes, false) + ")");
Check(DisplayFormat.Size(1524713390, false) == "1.42 GB", "GB value uses up to two decimals (got " + DisplayFormat.Size(1524713390, false) + ")");
string arabicSize = DisplayFormat.Size(1073741824, true);
Check(arabicSize == DisplayFormat.Isolate("1 GB"), "Arabic size isolates the number and the unit together");
Check(DisplayFormat.Size(0, true) == DisplayFormat.Isolate("0 B"), "Arabic zero bytes is isolated and readable");

string enSummary = DisplayFormat.CleanSummary(1524713390, 12, 3, false);
Check(enSummary.Contains("Cleanup completed") && enSummary.Contains("1.42 GB cleaned") && enSummary.Contains("3 items skipped"),
    "English cleanup summary reports actual cleaned size and skipped items (got " + enSummary + ")");
string arSummary = DisplayFormat.CleanSummary(1524713390, 12, 3, true);
Check(arSummary.Contains("اكتمل التنظيف بنجاح") && arSummary.Contains("1.42") && arSummary.Contains(DisplayFormat.Isolate("1.42 GB")) && arSummary.Contains("تُجاوز"),
    "Arabic cleanup summary reports actual cleaned size in correct wording");
Check(DisplayFormat.CleanSummary(0, 0, 4, false).Contains("No files were removed") && DisplayFormat.CleanSummary(0, 0, 4, false).Contains("4 items skipped"),
    "Nothing removed produces an accurate English message");
Check(DisplayFormat.CleanSummary(0, 0, 4, true).Contains("لم يتم حذف أي ملفات"), "Nothing removed produces an accurate Arabic message");
Check(DisplayFormat.CleanSummary(0, 3, 0, false).Contains("3 empty files removed"), "Zero-byte removals are reported without claiming freed space");
string enCategory = DisplayFormat.CategorySummary(4357, 0, mbBytes, 12, false, false);
Check(enCategory.Contains("4,357 items") && enCategory.Contains("13.06 GB") && enCategory.Contains("12 skipped"),
    "Category summary shows count, size and skipped in English (got " + enCategory + ")");
string arCategory = DisplayFormat.CategorySummary(4357, 0, mbBytes, 12, false, true);
Check(arCategory.Contains("4,357") && arCategory.Contains("13.06") && arCategory.Contains(DisplayFormat.Isolate("13.06 GB")) && arCategory.Contains("تُجاوز"),
    "Category summary shows count, size and skipped in Arabic");

// === Test 14: Quick Cleaner selection serialization ===
var defaultSelection = CleanerSelection.Parse(null);
Check(defaultSelection.SequenceEqual(new[]{TempCleanerService.Category.UserTemp,TempCleanerService.Category.SystemTemp}), "Missing selections default to User Temp and System Temp");
Check(CleanerSelection.Parse(Array.Empty<string>()).Length == 0, "An explicitly empty selection stays empty");
var parsedSelection = CleanerSelection.Parse(new[] { "RecycleBin", "Prefetch", "Bogus", "usertemp" });
Check(parsedSelection.SequenceEqual(new[] { TempCleanerService.Category.UserTemp, TempCleanerService.Category.Prefetch, TempCleanerService.Category.RecycleBin }),
    "Saved category names are parsed, deduplicated and ordered");
var serializedSelection = CleanerSelection.Serialize(new[] { TempCleanerService.Category.RecycleBin, TempCleanerService.Category.UserTemp, TempCleanerService.Category.UserTemp });
Check(serializedSelection.SequenceEqual(new[] { "UserTemp", "RecycleBin" }), "Selected categories serialize deterministically");

// === Test 15: Checkbox selections persist through settings ===
var settingsDir = Path.Combine(fixtureBase, "polar-settings-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(settingsDir);
    var store = new SettingsService(settingsDir);
    store.Save(new AppSettings("Blue", true, true, new[] { "SystemTemp", "RecycleBin" }));
    var reloaded = new SettingsService(settingsDir).Load();
    Check(reloaded.Theme == "Blue" && reloaded.Arabic && reloaded.SidebarCollapsed, "Existing settings still round-trip");
    Check(reloaded.CleanerCategories is not null && reloaded.CleanerCategories.SequenceEqual(new[] { "SystemTemp", "RecycleBin" }),
        "Cleaner selections survive a save/load cycle (got " + string.Join(",", reloaded.CleanerCategories ?? Array.Empty<string>()) + ")");

    var emptyStore = new SettingsService(Path.Combine(settingsDir, "empty"));
    emptyStore.Save(new AppSettings("Espresso", false, false, Array.Empty<string>()));
    var emptyReloaded = new SettingsService(Path.Combine(settingsDir, "empty")).Load();
    Check(emptyReloaded.CleanerCategories is { Length: 0 }, "Saving no selected categories is not replaced by the default");

    var legacyDir = Path.Combine(settingsDir, "legacy");
    Directory.CreateDirectory(legacyDir);
    File.WriteAllText(Path.Combine(legacyDir, "settings.json"), "{\"Theme\":\"Black\",\"Arabic\":false,\"SidebarCollapsed\":false}");
    var legacy = new SettingsService(legacyDir).Load();
    Check(legacy.CleanerCategories is null && CleanerSelection.Parse(legacy.CleanerCategories).Single() == TempCleanerService.Category.UserTemp,
        "Legacy settings without selections restore the historical default");
}
catch (Exception ex)
{
    Check(false, "Settings persistence fixture threw: " + ex.GetType().Name + " " + ex.Message);
}
finally
{
    try { Directory.Delete(settingsDir, true); } catch { }
}

// === Test 16: Cleanup result accounting uses actually removed data ===
var cleanerRoot = Path.Combine(fixtureBase, "polar-cleaner-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(cleanerRoot);
    File.WriteAllBytes(Path.Combine(cleanerRoot, "a.tmp"), new byte[1000]);
    File.WriteAllBytes(Path.Combine(cleanerRoot, "b.tmp"), new byte[2000]);
    File.WriteAllBytes(Path.Combine(cleanerRoot, "c.tmp"), new byte[3000]);
    var cleaner = new TempCleanerService();
    var scanResult = await cleaner.RunInDirectoryAsync(cleanerRoot, false, null, CancellationToken.None);
    Check(scanResult.Files == 3 && scanResult.Bytes == 6000, "Cleanup scan reports 3 files and 6,000 bytes (got " + scanResult.Files + " / " + scanResult.Bytes + ")");
    Check(File.Exists(Path.Combine(cleanerRoot, "a.tmp")), "Scanning does not delete files");

    var deleteResult = await cleaner.RunInDirectoryAsync(cleanerRoot, true, null, CancellationToken.None);
    Check(deleteResult.Files == 3 && deleteResult.Bytes == 6000, "Successful cleanup reports actually removed files and bytes");
    Check(!File.Exists(Path.Combine(cleanerRoot, "a.tmp")) && !File.Exists(Path.Combine(cleanerRoot, "b.tmp")) && !File.Exists(Path.Combine(cleanerRoot, "c.tmp")),
        "Successful cleanup removes the scanned temporary files");
}
catch (Exception ex)
{
    Check(false, "Cleanup accounting fixture threw: " + ex.GetType().Name + " " + ex.Message);
}
finally
{
    try { Directory.Delete(cleanerRoot, true); } catch { }
}

// === Test 17: Partial cleanup, locked files and read-only files ===
var partialRoot = Path.Combine(fixtureBase, "polar-cleaner-partial-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(partialRoot);
    string free = Path.Combine(partialRoot, "free.tmp");
    string locked = Path.Combine(partialRoot, "locked.tmp");
    File.WriteAllBytes(free, new byte[1000]);
    File.WriteAllBytes(locked, new byte[2000]);
    var cleaner = new TempCleanerService();
    using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
    {
        var partial = await cleaner.RunInDirectoryAsync(partialRoot, true, null, CancellationToken.None);
        Check(partial.Files == 1 && partial.Bytes == 1000, "Partial cleanup counts only the successfully removed file (got " + partial.Files + " / " + partial.Bytes + ")");
        Check(partial.Skipped >= 1, "Locked file is reported as skipped");
        Check(!File.Exists(free) && File.Exists(locked), "Locked file survives while unlocked file is removed");
    }

    File.SetAttributes(locked, FileAttributes.Normal);
    string readOnly = Path.Combine(partialRoot, "readonly.tmp");
    File.WriteAllBytes(readOnly, new byte[500]);
    File.SetAttributes(readOnly, FileAttributes.ReadOnly);
    var readOnlyResult = await cleaner.RunInDirectoryAsync(partialRoot, true, null, CancellationToken.None);
    Check(readOnlyResult.Bytes == 2500 && !File.Exists(locked) && !File.Exists(readOnly),
        "Previously locked and read-only temporary files are cleaned on retry (got " + readOnlyResult.Bytes + ")");

    File.WriteAllText(Path.Combine(partialRoot, "keep.txt"), "keep");
    File.WriteAllBytes(Path.Combine(partialRoot, "remove.pf"), new byte[700]);
    var prefetchResult = await cleaner.RunInDirectoryAsync(partialRoot, true, null, CancellationToken.None, prefetchOnly: true);
    Check(prefetchResult.Files == 1 && prefetchResult.Bytes == 700 && File.Exists(Path.Combine(partialRoot, "keep.txt")),
        "Prefetch mode removes only .pf files and preserves other files");
}
catch (Exception ex)
{
    Check(false, "Partial cleanup fixture threw: " + ex.GetType().Name + " " + ex.Message);
}
finally
{
    try
    {
        foreach (var file in Directory.EnumerateFiles(partialRoot)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(partialRoot, true);
    }
    catch { }
}

// === Test 18: Quick Cleaner wiring in the WPF page ===
var cleanerCode = File.ReadAllText(Path.Combine(root, "MainWindow.xaml.cs"), Encoding.UTF8);
Check(cleanerCode.Contains("CleanerCard(content)") && cleanerCode.Contains("BorderBrush = Brushes.Transparent"),
    "Category cards use borderless card styling");
Check(cleanerCode.Contains("CleanerSelection.Parse") && cleanerCode.Contains("CleanerSelection.Serialize"),
    "Quick Cleaner loads and saves the category selection");
Check(cleanerCode.Contains("PersistSelection") && cleanerCode.Contains("SaveSettings();"),
    "Changing a checkbox persists the selection");
Check(cleanerCode.Contains("DisplayFormat.CleanSummary") && cleanerCode.Contains("cleanedBytes += result.Bytes"),
    "Cleanup summary is based on actually removed bytes");
Check(cleanerCode.Contains("await Run(false, true)"), "Post-clean refresh keeps the success result visible");
Check(cleanerCode.Contains("outer.Loaded += async (_, _) => await Run(false)"), "Restoring the page only scans and never auto-cleans");
Check(cleanerCode.Contains("Background = Brushes.Transparent"), "Quick Cleaner card backgrounds blend with the page background");

// === Test 19: Sidebar vector icons use WPF geometries ===
var sidebarIconCode = File.ReadAllText(Path.Combine(root, "Services", "SidebarIcons.cs"), Encoding.UTF8);
Check(sidebarIconCode.Contains("Geometry.Parse") && sidebarIconCode.Contains("discord", StringComparison.OrdinalIgnoreCase),
    "Sidebar icons use WPF vector geometries including the Discord mark");

// === Test 20: Recent uninstall history persistence, recording and duplicates ===
var historyRoot = Path.Combine(fixtureBase, "polar-history-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(historyRoot);
    var historyService = new UninstallHistoryService(historyRoot);
    var appFolder = Path.Combine(historyRoot, "app");
    Directory.CreateDirectory(appFolder);
    File.WriteAllBytes(Path.Combine(appFolder, "a.bin"), new byte[2048]);
    var historyProgram = new InstalledProgram("History App", "Pub", "1.0", appFolder, "", null, "", "Steam", "steam:123");
    var historyCapture = await historyService.CaptureAsync(historyProgram, "op-1", false);
    Check(historyCapture.Name == "History App", "History capture records the application name");
    Check(historyCapture.SizeBytes == 2048, "History capture measures the folder size before removal");
    Check(historyService.StoragePath == Path.Combine(historyRoot, "history.json"), "History storage path is under the user data directory");
    Check(await historyService.RecordAsync(historyCapture), "First verified removal is recorded");
    Check(!(await historyService.RecordAsync(historyCapture)), "The same operation is not recorded twice");
    var historyReloaded = await new UninstallHistoryService(historyRoot).LoadAsync();
    Check(historyReloaded.Count == 1, "History is persisted and reloaded");
    var duplicateCapture = await historyService.CaptureAsync(historyProgram, "op-2", false);
    Check(!(await historyService.RecordAsync(duplicateCapture)), "An immediate duplicate identity is suppressed");
    Check(UninstallHistoryService.ShouldRecordDirect(new UninstallVerification(false, false, false)), "Direct success is recorded");
    Check(!UninstallHistoryService.ShouldRecordDirect(new UninstallVerification(true, false, false)), "A still-registered app is not recorded");
    Check(!UninstallHistoryService.ShouldRecordDirect(new UninstallVerification(false, true, false)), "A remaining folder prevents recording");
    Check(UninstallHistoryService.ShouldRecordMonitored(new RemovalProbe(true, false, false, "ok")), "Confirmed monitoring is recorded");
    Check(!UninstallHistoryService.ShouldRecordMonitored(new RemovalProbe(false, true, true, "no")), "Unconfirmed monitoring is not recorded");
    var unknownEntry = new UninstallHistoryEntry { Name = "Unknown App", RemovedAt = DateTimeOffset.Now };
    Check(UninstallHistoryFormat.Size(unknownEntry, false) == "Unknown", "Unknown size displays Unknown");
    Check(UninstallHistoryFormat.Size(unknownEntry, true) == "غير معروف", "Unknown size displays Arabic unknown");

    File.WriteAllText(Path.Combine(historyRoot, "history.json"), "{ not json");
    var corruptHistory = await new UninstallHistoryService(historyRoot).LoadAsync();
    Check(corruptHistory.Count == 0, "Corrupted history loads as an empty list");
}
catch (Exception ex)
{
    Check(false, "Recent history fixture threw: " + ex.GetType().Name + " " + ex.Message);
}
finally
{
    try { Directory.Delete(historyRoot, true); } catch { }
}

// === Test 21: File Browser size sorting ===
var browserEntries = new[]
{
    new BrowserEntry("zeta", "C:\\zeta", false, 100) { Arabic = false },
    new BrowserEntry("alpha", "C:\\alpha", false, null) { Arabic = false },
    new BrowserEntry("folder", "C:\\folder", true, 5000) { Arabic = false },
    new BrowserEntry("empty", "C:\\empty", true, null) { Arabic = false },
    new BrowserEntry("tiny", "C:\\tiny", false, 10) { Arabic = false }
};
var largestFirst = browserEntries.OrderBy(entry => entry, new BrowserEntryComparer(true)).ToArray();
Check(largestFirst[0].Name == "folder" && largestFirst[1].Name == "zeta" && largestFirst[2].Name == "tiny",
    "Largest-first sorts known sizes descending");
Check(largestFirst[3].Name == "empty" && largestFirst[4].Name == "alpha",
    "Unknown sizes sort last in largest-first order");
var smallestFirst = browserEntries.OrderBy(entry => entry, new BrowserEntryComparer(false)).ToArray();
Check(smallestFirst[0].Name == "tiny" && smallestFirst[1].Name == "zeta",
    "Smallest-first sorts known sizes ascending");
Check(smallestFirst[3].Name == "empty" && smallestFirst[4].Name == "alpha",
    "Unknown sizes sort last in smallest-first order");

// === Test 22: Release notes loading, grouping and history preservation ===
var releaseJson = File.ReadAllText(Path.Combine(root, "Assets", "ReleaseNotes.json"), Encoding.UTF8);
using (var releaseDoc = System.Text.Json.JsonDocument.Parse(releaseJson))
{
    var versions = releaseDoc.RootElement.EnumerateArray().Select(r => r.GetProperty("version").GetString()).ToArray();
    Check(versions.Contains("1.0.122"), "Release notes contain v1.0.122");
    Check(versions.Contains("1.0.121") && versions.Contains("1.0.120"), "Historical release entries are preserved");
    var latest = releaseDoc.RootElement.EnumerateArray().First();
    Check(latest.GetProperty("version").GetString() == propsVersion, "Latest release matches the source version (v" + propsVersion + ")");
    var latestEnglish = latest.GetProperty("en").EnumerateArray().ToArray();
    var latestArabic = latest.GetProperty("ar").EnumerateArray().ToArray();
    Check(latestEnglish.Length > 0 && latestEnglish.Length == latestArabic.Length &&
        latestEnglish.Concat(latestArabic).All(entry => !string.IsNullOrWhiteSpace(entry.GetString())),
        "The latest release has matching, non-empty English and Arabic changelog entries");
}
Check(ReleaseNotesService.IsFeature("What's New page redesigned with ..."), "Release-note feature classification works");
Check(ReleaseNotesService.IsImprovement("Dashboard, Quick Cleaner and File Browser surfaces now blend..."), "Release-note improvement classification works");
Check(ReleaseNotesService.IsFix("Fixed Steam game uninstallation: uses the official protocol."), "Release-note fix classification detects fixes");

// === Test 22b: What's New page focuses on the installed version ===
var selectionPool = new[]
{
    new ReleaseNote("1.0.135", new[] { "current entry" }),
    new ReleaseNote("1.0.134", new[] { "older entry" })
};
Check(ReleaseNotesService.FindCurrent(selectionPool, "1.0.135")?.Entries[0] == "current entry",
    "The installed version selects its own release note");
Check(ReleaseNotesService.FindCurrent(selectionPool, "v1.0.135")?.Version == "1.0.135",
    "Version prefixes are tolerated when selecting the current release note");
Check(ReleaseNotesService.FindCurrent(selectionPool, "1.0.136") == null,
    "A missing installed version never falls back to an older release note");
var curatedNote = new ReleaseNote("9.9.9", new[] { "Fixed a bug in update checks" },
    Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()) { HasHighlights = true };
Check(curatedNote.DisplayFixes.Count == 0,
    "Explicit highlights suppress fallback fix classification");
var notesViewCode = File.ReadAllText(Path.Combine(root, "ReleaseNotesView.cs"), Encoding.UTF8);
Check(!notesViewCode.Contains("Previous Releases", StringComparison.Ordinal) &&
      !notesViewCode.Contains("الإصدارات السابقة", StringComparison.Ordinal),
    "The What's New page no longer renders previous releases");
Check(!notesViewCode.Contains("releases[0]", StringComparison.Ordinal),
    "The What's New page never assumes the first entry is current");
Check(notesViewCode.Contains("ReleaseNotesService.FindCurrent", StringComparison.Ordinal) &&
      notesViewCode.Contains("AppInfo.Version", StringComparison.Ordinal),
    "The What's New page selects the installed version dynamically");
Check(notesViewCode.Contains("\"Show All Changes\"", StringComparison.Ordinal) &&
      notesViewCode.Contains("عرض جميع التغييرات", StringComparison.Ordinal) &&
      notesViewCode.Contains("IsExpanded = false", StringComparison.Ordinal),
    "Show All Changes remains with its expand/collapse behavior in both languages");

// === Test 24: Polar-owned installations are excluded from inventory ===
var polarProgram = new InstalledProgram("Polar Uninstaller", "Polar", "1.0.124", "C:\\Program Files\\Polar Uninstaller", "C:\\Program Files\\Polar Uninstaller\\PolarUninstaller.exe", null, "", "Windows", "", null, new[] { "C:\\Program Files\\Polar Uninstaller\\PolarUninstaller.exe" });
var polarWpfProgram = new InstalledProgram("Polar Uninstaller (WPF)", "Polar", "1.0.124", "C:\\Program Files\\Polar Uninstaller", "", null, "", "Windows", "");
var unrelatedProgram = new InstalledProgram("Google Chrome", "Google LLC", "1.0", "C:\\Program Files\\Google\\Chrome", "C:\\Program Files\\Google\\Chrome\\chrome.exe", null, "", "Windows", "");
Check(PolarApplicationIdentity.IsPolarOwned(polarProgram), "Polar Uninstaller is recognized as Polar-owned");
Check(PolarApplicationIdentity.IsPolarOwned(polarWpfProgram), "Polar Uninstaller (WPF) is recognized as Polar-owned");
Check(!PolarApplicationIdentity.IsPolarOwned(unrelatedProgram), "Unrelated applications are not excluded");
var mergedInventory = InventoryMerger.Merge(new[] { polarProgram, unrelatedProgram });
Check(mergedInventory.Count == 1 && mergedInventory[0].Name == "Google Chrome", "Polar-owned entries are removed during inventory processing");

// === Test 25: Recent uninstall and File Browser wiring ===
var dashboardXaml2 = File.ReadAllText(Path.Combine(root, "DashboardView.xaml"), Encoding.UTF8);
var deepCode2 = File.ReadAllText(Path.Combine(root, "DeepUninstallerView.xaml.cs"), Encoding.UTF8);
var browserCode2 = File.ReadAllText(Path.Combine(root, "FileBrowserView.xaml.cs"), Encoding.UTF8);
var dashboardCode2 = File.ReadAllText(Path.Combine(root, "DashboardView.xaml.cs"), Encoding.UTF8);
Check(dashboardCode2.Contains("No recent uninstalls yet.") && dashboardCode2.Contains("لا توجد عمليات إزالة مسجلة بعد."),
    "Exact empty-state text is used");
Check(deepCode2.Contains("UninstallHistoryService") && deepCode2.Contains("RecordAsync") && deepCode2.Contains("!verification.Incomplete&&!verification.Registered&&!executable"),
    "Deep uninstaller records only verified removals");
Check(!browserCode2.Contains("Edit address"), "Redundant Edit address command was removed");
Check(browserCode2.Contains("Sort by Size: Largest First") && browserCode2.Contains("Sort by Size: Smallest First"),
    "Both size-sort commands are present");
Check(browserCode2.Contains("BrowserEntryComparer"), "File Browser uses the shared size comparer");

// === Test 26: External removal detection rejects false positives ===
var externalRoot = Path.Combine(fixtureBase, "polar-external-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(externalRoot);
    string appDir = Path.Combine(externalRoot, "app");
    Directory.CreateDirectory(appDir);
    File.WriteAllBytes(Path.Combine(appDir, "app.bin"), new byte[128]);
    var installed = new InstalledProgram("Demo App", "Demo Publisher", "1.0", appDir, "", null, "", "Windows", "");
    Check(ExternalRemovalDetector.Evaluate(installed, new[] { installed }, 0) == ExternalRemovalDecision.None,
        "A present application is not a removal");
    Check(!ExternalRemovalDetector.IsTrustworthyScan(100, 20), "A large unexplained inventory drop is not trusted");
    Check(ExternalRemovalDetector.IsTrustworthyScan(100, 85), "A plausible inventory size is trusted");

    var update = new InstalledProgram("KB5031234", "Microsoft Corporation", "1.0", "", "", null, "", "Windows", "");
    Check(ExternalRemovalDetector.IsSystemUpdateOrService(update), "Windows KB updates are classified as system updates");
    Check(ExternalRemovalDetector.Evaluate(update, Array.Empty<InstalledProgram>(), 5) == ExternalRemovalDecision.None,
        "Windows updates are never recorded as removals");
    var service = new InstalledProgram("Xbox Service", "Microsoft Corporation", "1.0", "", "", null, "", "Windows", "");
    Check(ExternalRemovalDetector.IsSystemUpdateOrService(service), "Service components are excluded");
    var disconnected = new InstalledProgram("Offline App", "Pub", "1.0", @"\\polar-nonexistent-host\share\app", "", null, "", "Windows", "");
    Check(ExternalRemovalDetector.Evaluate(disconnected, Array.Empty<InstalledProgram>(), 5) == ExternalRemovalDecision.None,
        "An unavailable drive or network share is not treated as a removal");

    Directory.Delete(appDir, true);
    Check(ExternalRemovalDetector.Evaluate(installed, Array.Empty<InstalledProgram>(), 0) == ExternalRemovalDecision.Confirmed,
        "A missing installation folder confirms removal");

    var registryOnly = new InstalledProgram("Registry Only", "Pub", "1.0", "", "", null, "", "Windows", "");
    Check(ExternalRemovalDetector.Evaluate(registryOnly, Array.Empty<InstalledProgram>(), 0) == ExternalRemovalDecision.Pending,
        "A registry-only entry needs a second successful scan");
    Check(ExternalRemovalDetector.Evaluate(registryOnly, Array.Empty<InstalledProgram>(), 1) == ExternalRemovalDecision.Confirmed,
        "A second successful scan confirms a registry-only removal");

    string movedDir = Path.Combine(externalRoot, "moved");
    Directory.CreateDirectory(movedDir);
    var moved = installed with { Location = movedDir };
    Check(ExternalRemovalDetector.StillPresent(installed, moved),
        "A moved installation with the same identity is still present");
}
catch (Exception ex)
{
    Check(false, "External removal fixture threw: " + ex.GetType().Name + " " + ex.Message);
}
finally
{
    try { Directory.Delete(externalRoot, true); } catch { }
}

// === Test 27: Clear Cache resets persisted counters and uninstall history ===
var resetRoot = Path.Combine(fixtureBase, "polar-reset-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(resetRoot);
    var metrics = new ActivityMetricsService(resetRoot);
    metrics.Record(true);
    metrics.Record(false);
    Check(metrics.Snapshot().Opens == 1 && metrics.Snapshot().Clicks == 1, "Launch and click counters start from recorded values");
    await metrics.ResetAsync();
    Check(metrics.Snapshot().Opens == 0 && metrics.Snapshot().Clicks == 0, "Reset clears launch and click counters immediately");
    var reloadedMetrics = new ActivityMetricsService(resetRoot);
    Check(reloadedMetrics.Snapshot().Opens == 0 && reloadedMetrics.Snapshot().Clicks == 0,
        "Reset counters stay cleared after restarting");

    var clearHistoryRoot = Path.Combine(resetRoot, "history");
    Directory.CreateDirectory(clearHistoryRoot);
    var clearHistory = new UninstallHistoryService(clearHistoryRoot);
    string clearApp = Path.Combine(clearHistoryRoot, "app");
    Directory.CreateDirectory(clearApp);
    File.WriteAllBytes(Path.Combine(clearApp, "a.bin"), new byte[64]);
    var clearCapture = await clearHistory.CaptureAsync(
        new InstalledProgram("Clear App", "Pub", "1.0", clearApp, "", null, "", "Windows", ""), "clear-op", false);
    Check(await clearHistory.RecordAsync(clearCapture), "History entry exists before clearing");
    await clearHistory.ClearAsync();
    Check((await clearHistory.LoadAsync()).Count == 0, "Clear Cache removes all uninstall history");
    Check(!File.Exists(clearHistory.StoragePath), "Cleared history file does not return on reload");
}
catch (Exception ex)
{
    Check(false, "Reset fixture threw: " + ex.GetType().Name + " " + ex.Message);
}
finally
{
    try { Directory.Delete(resetRoot, true); } catch { }
}

// === Test 28: Snapshot baseline and pending removals are cleared together ===
var pendingRoot = Path.Combine(fixtureBase, "polar-pending-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(pendingRoot);
    string snapshotPath = Path.Combine(pendingRoot, "snapshot.json");
    string pendingPath = Path.Combine(pendingRoot, "pending.json");
    var pendingProgram = new InstalledProgram("Pending App", "Pub", "1.0", "", "", null, "", "Windows", "");
    InventorySnapshotService.Save(snapshotPath, new[] { pendingProgram });
    Check(InventorySnapshotService.Load(snapshotPath).Count == 1, "Inventory snapshot round-trips");
    InventorySnapshotService.SavePending(pendingPath, new[] { new PendingRemoval(pendingProgram, DateTimeOffset.Now, 1) });
    var pending = InventorySnapshotService.LoadPending(pendingPath);
    Check(pending.Count == 1 && pending[0].AbsenceCount == 1, "Pending removal state is persisted");
    InventorySnapshotService.Clear(snapshotPath);
    InventorySnapshotService.ClearPending(pendingPath);
    Check(InventorySnapshotService.Load(snapshotPath).Count == 0 && InventorySnapshotService.LoadPending(pendingPath).Count == 0,
        "Clearing history removes the old snapshot and pending removals");
}
catch (Exception ex)
{
    Check(false, "Pending fixture threw: " + ex.GetType().Name + " " + ex.Message);
}
finally
{
    try { Directory.Delete(pendingRoot, true); } catch { }
}

// === Test 29: Dashboard navigation, scan scope and reset wiring ===
Check(dashboardCode2.Contains("openInBrowser(row.Path,row.Path)") && dashboardCode2.Contains("openInBrowser(Path.GetDirectoryName(row.Path)"),
    "Dashboard results open in the built-in File Browser");
Check(!dashboardCode2.Contains("explorer.exe") && !dashboardCode2.Contains("Process.Start"),
    "Dashboard does not launch Windows Explorer for results");
Check(dashboardCode2.Contains("ActivityMetricsService.Instance.ResetAsync") &&
      dashboardCode2.Contains("history.ClearAsync") &&
      dashboardCode2.Contains("InventorySnapshotService.EstablishBaselineAsync"),
    "Clear Cache resets counters, history, and creates a new baseline");
Check(dashboardCode2.Contains("RunStorageScanAsync") && dashboardCode2.Contains("scanIncomplete") && dashboardCode2.Contains("ScanScopeText"),
    "Dashboard labels partial scans and their exact scope");
var folderScope = DashboardStorageService.ClassifyScope(storageRoot);
Check(!folderScope.IsDriveRoot && folderScope.Root == Path.TrimEndingDirectorySeparator(storageRoot),
    "Folder scans report folder scope");
string volumeRoot = Path.GetPathRoot(storageRoot)!;
var driveScope = DashboardStorageService.ClassifyScope(volumeRoot);
Check(driveScope.IsDriveRoot && driveScope.Root == Path.TrimEndingDirectorySeparator(volumeRoot),
    "Drive scans preserve the volume root instead of using a relative C: path");
Check(Path.TrimEndingDirectorySeparator(volumeRoot) == volumeRoot,
    "Drive roots keep their trailing separator so immediate-child folder matching stays correct");

// === Test 30: Dashboard only reports immediate child folders ===
var topLevelRoot = Path.Combine(fixtureBase, "polar-toplevel-" + Guid.NewGuid().ToString("N"));
try
{
    string first = Path.Combine(topLevelRoot, "first");
    string second = Path.Combine(topLevelRoot, "second");
    string firstChild = Path.Combine(first, "child");
    string firstGrandchild = Path.Combine(firstChild, "grandchild");
    Directory.CreateDirectory(firstGrandchild);
    Directory.CreateDirectory(second);
    File.WriteAllBytes(Path.Combine(firstGrandchild, "large.bin"), new byte[9000]);
    File.WriteAllBytes(Path.Combine(first, "small.bin"), new byte[1000]);
    File.WriteAllBytes(Path.Combine(second, "other.bin"), new byte[4000]);

    var topScan = await new DashboardStorageService().ScanAsync(topLevelRoot, CancellationToken.None);
    Check(topScan.LargestFolders.Count == 2,
        "A folder scan lists only its two immediate child folders (got " + topScan.LargestFolders.Count + ")");
    var firstEntry = topScan.LargestFolders.FirstOrDefault(f => string.Equals(f.Path, first, StringComparison.OrdinalIgnoreCase));
    Check(firstEntry is not null && firstEntry.Bytes == 10000,
        "The first immediate child reports its full cumulative size (got " + (firstEntry?.Bytes.ToString() ?? "missing") + ")");
    Check(topScan.LargestFolders.All(f => !string.Equals(f.Path, firstChild, StringComparison.OrdinalIgnoreCase) &&
                                          !string.Equals(f.Path, firstGrandchild, StringComparison.OrdinalIgnoreCase)),
        "Descendants of an already listed folder never appear as separate Dashboard results");
    Check(topScan.LargestFolders.First().Bytes >= topScan.LargestFolders.Last().Bytes,
        "Immediate child folders are ordered largest first");
}
catch (Exception ex)
{
    Check(false, "Top-level folder fixture threw: " + ex.GetType().Name + " " + ex.Message);
}
finally
{
    DashboardStorageService.ClearCache();
    try { Directory.Delete(topLevelRoot, true); } catch { }
}

// === Test 31: File Browser native multi-selection and batch operations ===
var batchRoot = Path.Combine(fixtureBase, "polar-batch-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(batchRoot);
    string folder = Path.Combine(batchRoot, "folder");
    string nested = Path.Combine(folder, "nested");
    Directory.CreateDirectory(nested);
    string fileA = Path.Combine(batchRoot, "a.txt");
    string fileB = Path.Combine(batchRoot, "b.txt");
    string fileC = Path.Combine(nested, "c.txt");
    File.WriteAllText(fileA, "a");
    File.WriteAllText(fileB, "b");
    File.WriteAllText(fileC, "c");

    var collapsed = BrowserOperationsService.CollapseNestedPaths(new[] { folder, nested, fileA, fileB });
    Check(collapsed.Count == 3 &&
          collapsed.Contains(folder, StringComparer.OrdinalIgnoreCase) &&
          collapsed.Contains(fileA, StringComparer.OrdinalIgnoreCase) &&
          collapsed.Contains(fileB, StringComparer.OrdinalIgnoreCase),
        "A folder and its selected descendant collapse into one operation");

    var rootCollapsed = BrowserOperationsService.CollapseNestedPaths(new[] { volumeRoot, Path.Combine(volumeRoot, "Windows") });
    Check(rootCollapsed.Count == 1 && string.Equals(rootCollapsed[0], volumeRoot, StringComparison.OrdinalIgnoreCase),
        "A drive root and its child collapse to the drive root selection");

    var ops = new BrowserOperationsService();
    var permanent = await ops.DeleteAsync(new[] { fileA, fileB }, true, CancellationToken.None);
    Check(permanent.Count == 2 && permanent.All(r => r.Success), "Batch permanent deletion removes every selected file");
    Check(!File.Exists(fileA) && !File.Exists(fileB), "Permanently deleted files no longer exist");

    var nestedResults = await ops.DeleteAsync(new[] { folder, nested }, true, CancellationToken.None);
    Check(nestedResults.Count == 1 && nestedResults[0].Success, "A folder and its descendant are processed exactly once");
    Check(!Directory.Exists(folder), "The selected folder is removed with all of its contents");

    // Real Shell recycling is opt-in through --deletion139, with a unique fixture and verified restore.

    string partialFile = Path.Combine(batchRoot, "partial.txt");
    File.WriteAllText(partialFile, "p");
    string protectedDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    var partial = await ops.DeleteAsync(new[] { partialFile, protectedDir }, true, CancellationToken.None);
    var protectedOutcome = partial.FirstOrDefault(r => string.Equals(r.Path, protectedDir, StringComparison.OrdinalIgnoreCase));
    var partialOutcome = partial.FirstOrDefault(r => string.Equals(r.Path, partialFile, StringComparison.OrdinalIgnoreCase));
    Check(partialOutcome is not null && partialOutcome.Success && protectedOutcome is not null && !protectedOutcome.Success,
        "A partially failed batch reports per-item success and failure");
    Check(!File.Exists(partialFile) && Directory.Exists(protectedDir), "A protected item is never deleted");

    string missing = Path.Combine(batchRoot, "missing.txt");
    var missingResults = await ops.DeleteAsync(new[] { missing }, true, CancellationToken.None);
    Check(missingResults.Count == 1 && !missingResults[0].Success, "A missing path fails gracefully instead of throwing");

    string cancelFile = Path.Combine(batchRoot, "cancel.txt");
    File.WriteAllText(cancelFile, "c");
    using (var cancelledBatch = new CancellationTokenSource())
    {
        cancelledBatch.Cancel();
        bool cancelled = false;
        try { await ops.DeleteAsync(new[] { cancelFile }, true, cancelledBatch.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled && File.Exists(cancelFile), "A cancelled batch stops before removing anything");
    }

    bool protectedThrew = false;
    try { BrowserOperationsService.ValidateDeletion(protectedDir); }
    catch (DeletionProtectionException) { protectedThrew = true; }
    Check(protectedThrew, "Protected system locations raise a dedicated protection error");
}
catch (Exception ex)
{
    Check(false, "Batch operation fixture threw: " + ex.GetType().Name + " " + ex.Message);
}
finally
{
    try { Directory.Delete(batchRoot, true); } catch { }
}

// === Test 32: Multi-selection wiring and Dashboard folder navigation ===
var browserXaml = File.ReadAllText(Path.Combine(root, "FileBrowserView.xaml"), Encoding.UTF8);
Check(browserXaml.Contains("SelectionMode=\"Extended\""), "File Browser uses native WPF extended selection");
Check(browserXaml.Contains("IsSelected") && browserXaml.Contains("✓"), "Every selected row shows a themed check indicator");
Check(browserCode.Contains("FilesPreviewKeyDown") && browserCode.Contains("Key.A") &&
      browserCode.Contains("ModifierKeys.Control") && browserCode.Contains("Files.SelectAll()"),
    "Ctrl+A selects all items in the current folder");
Check(browserCode.Contains("CollapseNestedPaths") && browserCode.Contains("DeletionProtectionException"),
    "Batch deletion deduplicates nested selections and keeps protection checks");
Check(browserCode.Contains("Permanently delete {count:N0}") && browserCode.Contains("Recycle Bin?") &&
      browserCode.Contains("MessageBoxButton.YesNo"),
    "Deletion confirmation distinguishes permanent deletion from the Recycle Bin and shows the count");
Check(browserCode.Contains("DetailsHeading.Text") && browserCode.Contains("Known size"),
    "The Details panel summarizes multiple selected files and folders");
Check(dashboardCode2.Contains("openInBrowser(row.Path,row.Path)"),
    "Clicking a Dashboard folder opens that exact folder in the File Browser");
Check(mainCode.Contains("currentDirectory = target") && mainCode.Contains("pendingFileReveal = reveal"),
    "Dashboard folder navigation passes the exact directory and reveal target without rescanning");
Check(!dashboardCode2.Contains("Process.Start") && !dashboardCode2.Contains("explorer.exe"),
    "Dashboard folder clicks never launch Windows Explorer");

// === Test 33: Largest folders and largest files are independent rankings ===
var rankingRoot = Path.Combine(fixtureBase, "polar-ranking-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(rankingRoot);
    for (int i = 0; i < 70; i++)
    {
        string folder = Path.Combine(rankingRoot, $"folder-{i:D2}");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "data.bin"), new byte[100 + i]);
    }
    for (int i = 0; i < 12; i++)
        File.WriteAllBytes(Path.Combine(rankingRoot, $"file-{i:D2}.bin"), new byte[50_000 + i]);

    var rankingScan = await new DashboardStorageService().ScanAsync(rankingRoot, CancellationToken.None);
    Check(rankingScan.LargestFolders.Count == 70,
        "Every accessible immediate folder is ranked even when large files exist (got " + rankingScan.LargestFolders.Count + ")");
    Check(rankingScan.LargestFolders.All(f => string.Equals(Path.GetDirectoryName(f.Path), Path.TrimEndingDirectorySeparator(rankingRoot), StringComparison.OrdinalIgnoreCase)),
        "Every ranked folder is an immediate child of the scan root");
    Check(rankingScan.LargestFolders.Zip(rankingScan.LargestFolders.Skip(1)).All(pair => pair.First.Bytes >= pair.Second.Bytes),
        "Folder ranking is ordered largest first");
    Check(rankingScan.Largest.Count >= 12 && rankingScan.Largest.Zip(rankingScan.Largest.Skip(1)).All(pair => pair.First.Bytes >= pair.Second.Bytes),
        "File ranking is independent and ordered largest first");
    Check(!rankingScan.Largest.Any(file => rankingScan.LargestFolders.Any(folder => string.Equals(folder.Path, file.Path, StringComparison.OrdinalIgnoreCase))),
        "Folders never consume slots in the largest-files ranking");
    Check(!rankingScan.Limited && rankingScan.Inaccessible == 0,
        "A fully accessible ranking scan reports no inaccessible items");
}
catch (Exception ex)
{
    Check(false, "Independent ranking fixture threw: " + ex.GetType().Name + " " + ex.Message);
}
finally
{
    DashboardStorageService.ClearCache();
    try { Directory.Delete(rankingRoot, true); } catch { }
}

// === Test 34: Clear Cache removes saved scan data but never user files ===
var clearFixture = Path.Combine(fixtureBase, "polar-clear-" + Guid.NewGuid().ToString("N"));
try
{
    string clearRoot = Path.Combine(clearFixture, "scan-root");
    string manualDir = Path.Combine(clearFixture, "manual");
    Directory.CreateDirectory(clearRoot);
    Directory.CreateDirectory(manualDir);
    string userFile = Path.Combine(clearRoot, "keep-me.txt");
    File.WriteAllText(userFile, "personal data");

    var clearScanService = new DashboardStorageService();
    await clearScanService.ScanAsync(clearRoot, CancellationToken.None);
    Check(DashboardStorageService.TryGetCached(clearRoot) is not null, "A completed scan is cached before Clear Cache");

    var manualStore = new ManualStorageService(manualDir);
    await manualStore.SaveAsync(new[] { new ManualStorageItem(userFile, 13, 1, 0, DateTime.Now) });
    Check(File.Exists(manualStore.CachePath), "Manually added scan items are saved to Polar's own metadata file");

    DashboardStorageService.ClearCache();
    await manualStore.ClearAsync();

    Check(DashboardStorageService.TryGetCached(clearRoot) is null && DashboardStorageService.LastSnapshot is null,
        "Clear Cache removes cached drive and folder scan results");
    Check(!File.Exists(manualStore.CachePath), "Clear Cache removes the saved Dashboard scan-item list");
    Check((await manualStore.LoadAsync()).Count == 0, "Cleared scan items do not come back on reload");
    Check(File.Exists(userFile) && File.ReadAllText(userFile) == "personal data",
        "Clear Cache never deletes the real user file that was saved as a scan item");
}
catch (Exception ex)
{
    Check(false, "Clear Cache fixture threw: " + ex.GetType().Name + " " + ex.Message);
}
finally
{
    DashboardStorageService.ClearCache();
    try { Directory.Delete(clearFixture, true); } catch { }
}

// === Test 35: File Browser sorting preference persists across restarts ===
var sortSettingsDir = Path.Combine(fixtureBase, "polar-sort-settings-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(sortSettingsDir);
    var sortStore = new SettingsService(sortSettingsDir);
    sortStore.Save(new AppSettings("Beige", true, false, new[] { "UserTemp" }, "sizeDesc", "Size", "Descending"));
    var sortReloaded = new SettingsService(sortSettingsDir).Load();
    Check(sortReloaded.FileBrowserSort == "Size" && sortReloaded.FileBrowserSortDirection == "Descending",
        "File Browser sorting field and direction survive a save/load cycle");
    Check(sortReloaded.DeepUninstallerSort == "sizeDesc" && sortReloaded.Theme == "Beige",
        "Saving the sort preference preserves unrelated settings");

    var legacySortDir = Path.Combine(sortSettingsDir, "legacy");
    Directory.CreateDirectory(legacySortDir);
    File.WriteAllText(Path.Combine(legacySortDir, "settings.json"), "{\"Theme\":\"Black\",\"Arabic\":false,\"SidebarCollapsed\":false}");
    var legacySort = new SettingsService(legacySortDir).Load();
    Check(legacySort.FileBrowserSort is null && legacySort.FileBrowserSortDirection is null,
        "Legacy settings without a sort preference load with safe defaults");
}
catch (Exception ex)
{
    Check(false, "Sorting settings fixture threw: " + ex.GetType().Name + " " + ex.Message);
}
finally
{
    try { Directory.Delete(sortSettingsDir, true); } catch { }
}

// === Test 36: Dashboard category, Clear Cache and persistent-sort wiring ===
Check(dashboardXaml.Contains("FoldersTab") && dashboardXaml.Contains("FilesTab") &&
      dashboardXaml.Contains("FileSizeFilter") && dashboardXaml.Contains("ShowMoreButton"),
    "Dashboard exposes separate folder/file tabs, a size filter, and Show more");
Check(dashboardCode2.Contains("automaticFolders") && dashboardCode2.Contains("automaticFiles") &&
      dashboardCode2.Contains("minimumFileBytes"),
    "Dashboard keeps folder and file results in independent collections");
Check(dashboardCode2.Contains("DashboardStorageService.ClearCache") &&
      dashboardCode2.Contains("manualService.ClearAsync") &&
      dashboardCode2.Contains("cacheGeneration"),
    "Clear Cache clears cached scans, saved items, and blocks stale background saves");
Check(browserCode.Contains("FileBrowserSort") && browserCode.Contains("FileBrowserSortDirection") &&
      browserCode.Contains("SaveSortPreference") && browserCode.Contains("UpdateSortIndicators"),
    "File Browser persists sorting and shows the active direction");
Check(mainCode.Contains("new FileBrowserView(arabic,currentDirectory,activity,reveal,settingsService,applicationInventory)") &&
      mainCode.Contains("existing.FileBrowserSort"),
    "MainWindow shares settings with the File Browser and preserves its sort preference");
Check(browserCode.Contains("ListSortDirection.Ascending") &&
      browserCode.Contains("nameof(BrowserEntry.Name),ListSortDirection.Ascending"),
    "Equal sort values use a stable secondary ordering");
Check(releaseJson.Contains("Largest Folders and Largest Files are now independent rankings") &&
      releaseJson.Contains("أكبر المجلدات وأكبر الملفات أصبحا تصنيفين مستقلين"),
    "Release notes document the independent rankings in English and Arabic");
Check(releaseJson.Contains("Clear Cache never deletes personal files") &&
      releaseJson.Contains("إزالة الكاش لا تحذف الملفات الشخصية أبدًا"),
    "Release notes document the Clear Cache safety guarantee in both languages");
Check(releaseJson.Contains("File Browser sorting is remembered") &&
      releaseJson.Contains("يُحفظ ترتيب متصفح الملفات"),
    "Release notes document persistent sorting in both languages");
Check(releaseJson.Contains("calculates folder sizes automatically") &&
      releaseJson.Contains("يحسب متصفح الملفات الآن أحجام المجلدات تلقائيًا"),
    "Release notes document automatic folder sizes in both languages");
Check(releaseJson.Contains("never follow junctions or symbolic links") &&
      releaseJson.Contains("نقاط إعادة التحليل أو الروابط الرمزية"),
    "Release notes document link-safe, cached background sizing in both languages");

// === Test 37: Automatic folder sizes, states, caching and link safety ===
var sizeRoot = Path.Combine(fixtureBase, "polar-foldersize-" + Guid.NewGuid().ToString("N"));
try
{
    string outer = Path.Combine(sizeRoot, "outer");
    string inner = Path.Combine(outer, "inner");
    string deep = Path.Combine(inner, "deep");
    Directory.CreateDirectory(deep);
    File.WriteAllBytes(Path.Combine(outer, "top.bin"), new byte[1000]);
    File.WriteAllBytes(Path.Combine(inner, "mid.bin"), new byte[2000]);
    File.WriteAllBytes(Path.Combine(deep, "low.bin"), new byte[4000]);

    FolderSizeService.ClearCache();
    var measured = await FolderSizeService.GetAsync(outer, CancellationToken.None);
    Check(measured.Bytes == 7000 && !measured.Partial && !measured.Unavailable,
        "A folder size sums files and nested subfolders exactly once (got " + measured.Bytes + ")");
    Check(FolderSizeService.TryGetCached(outer) is { Bytes: 7000 },
        "A completed folder measurement is cached");
    var reused = await FolderSizeService.GetAsync(outer, CancellationToken.None);
    Check(reused.Bytes == 7000, "A fresh cached measurement is reused instead of rescanning");

    var missing = await FolderSizeService.GetAsync(Path.Combine(sizeRoot, "missing"), CancellationToken.None);
    Check(missing.Unavailable && missing.Bytes is null,
        "A missing or inaccessible folder reports unavailable instead of a false zero");

    using (var cancelledSizes = new CancellationTokenSource())
    {
        cancelledSizes.Cancel();
        bool sizeCancelled = false;
        try { await FolderSizeService.GetAsync(inner, cancelledSizes.Token); }
        catch (OperationCanceledException) { sizeCancelled = true; }
        Check(sizeCancelled, "A cancelled folder measurement stops before scanning");
    }

    string junction = Path.Combine(sizeRoot, "outer-link");
    bool junctionCreated = false;
    try
    {
        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{outer}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.WaitForExit(5000);
        junctionCreated = Directory.Exists(junction) &&
            (File.GetAttributes(junction) & FileAttributes.ReparsePoint) != 0;
    }
    catch { }
    if (junctionCreated)
    {
        FolderSizeService.ClearCache();
        var linked = await FolderSizeService.GetAsync(sizeRoot, CancellationToken.None);
        Check(linked.Bytes == 7000, "A junction is not followed, preventing loops and double counting");
        Check(linked.Partial, "A skipped link marks the measurement as partial");
    }
    else
    {
        Console.WriteLine("NOTE: junction safety test skipped (link creation unavailable in this environment)");
    }

    var unknownEntry = new BrowserEntry("unknown", "C:\\unknown", true, null);
    Check(unknownEntry.DisplaySize == "\u2014", "An unknown folder shows a dash rather than a false size");
    var calculatingEntry = new BrowserEntry("calculating", "C:\\calculating", true, null)
    {
        SizeState = BrowserSizeState.Calculating
    };
    Check(calculatingEntry.DisplaySize == "\u2026", "A folder being measured shows a temporary loading state");
    var partialEntry = new BrowserEntry("partial", "C:\\partial", true, 2048)
    {
        SizeState = BrowserSizeState.Partial
    };
    Check(partialEntry.DisplaySize.StartsWith("\u2265"), "A partial measurement is marked as a lower bound");
    var knownEntry = new BrowserEntry("known", "C:\\known", true, 1048576);
    Check(knownEntry.DisplaySize == "1.0 MB", "A known folder size uses readable units");
    int sizeNotifications = 0;
    knownEntry.PropertyChanged += (_, e) =>
    {
        if (e.PropertyName == nameof(BrowserEntry.Size)) sizeNotifications++;
    };
    knownEntry.Size = 2097152;
    Check(sizeNotifications == 1 && knownEntry.DisplaySize == "2.0 MB",
        "A size update notifies the grid in place without rebuilding the row");

    Check(browserCode.Contains("AutoSizeFoldersAsync") && browserCode.Contains("FolderSizeService.GetAsync") &&
          browserCode.Contains("DispatcherPriority.Loaded"),
        "The File Browser starts folder measurements automatically after layout");
    Check(browserCode.Contains("autoSizeRequest?.Cancel()") && browserCode.Contains("generation!=navigationGeneration"),
        "Navigating away cancels stale folder-size work and rejects old results");
    Check(browserCode.Contains("ScheduleSortRefresh") && browserCode.Contains("RefreshSortPreservingSelection"),
        "Arriving sizes refresh the active size sort while preserving selection");
    Check(browserCode.Contains("FolderSizeService.TryGetCached") && browserCode.Contains("ApplyFolderSize(entry,cached)"),
        "Cached sizes are applied before the first sort on every navigation");
    Check(browserCode.Contains("ApplyFolderSize") && browserCode.Contains("Dispatcher.CheckAccess()"),
        "Size updates are always marshalled to the UI thread");
}
catch (Exception ex)
{
    Check(false, "Folder size fixture threw: " + ex.GetType().Name + " " + ex.Message);
}
finally
{
    FolderSizeService.ClearCache();
    try { Directory.Delete(sizeRoot, true); } catch { }
}

// === Test 38: Explicit multi-selection interaction wiring ===
Check(browserCode.Contains("FilesPreviewMouseLeftButtonDown") && browserCode.Contains("ModifierKeys.Shift") &&
      browserCode.Contains("SelectRange"),
    "Shift+Click selects a continuous range in the File Browser");
Check(browserCode.Contains("container.IsSelected=!container.IsSelected") && browserCode.Contains("SelectionCircleClick"),
    "Ctrl+Click and the selection circle toggle individual items");
Check(browserCode.Contains("Files.SelectionMode = SelectionMode.Extended"),
    "Extended selection is enforced in code, not only in XAML");
Check(browserCode.Contains("RefreshSortPreservingSelection") && browserCode.Contains("Files.SelectedItems.Add(entry)"),
    "Re-sorting after size updates restores the previous selection");
Check(browserXaml.Contains("x:Name=\"SelectionHitbox\"") && browserXaml.Contains("Width=\"42\" Height=\"42\"") &&
      browserXaml.Contains("Background=\"Transparent\"") &&
      browserXaml.Contains("MouseLeftButtonDown=\"SelectionCircleClick\""),
    "Selection circles expose a 42 DIP transparent hitbox");
Check(browserXaml.Contains("SelectionHitbox") &&
      browserXaml.IndexOf("SelectionHitbox", StringComparison.Ordinal) >
      browserXaml.IndexOf("ControlTemplate TargetType=\"ListViewItem\"", StringComparison.Ordinal),
    "The hitbox lives in the row template so clicks in the row padding are not clipped");
Check(browserXaml.Contains("Margin=\"-5,0,0,0\"") && browserXaml.Contains("HorizontalAlignment=\"Left\"") &&
      browserXaml.Contains("x:Name=\"SelectionIndicator\"") && browserXaml.Contains("FontSize=\"14\""),
    "The hitbox extends into the leading blank space without changing the visible indicator");

// === Test 39: One authoritative inventory drives both page counts ===
var sharedRecords = new List<InstalledProgram>
{
    new("Shared App", "Publisher", "1.0", @"C:\Program Files\SharedApp", "", null, "", "Windows", "shared-1"),
    new("Shared App", "Publisher", "1.0", @"C:\Program Files\SharedApp", "", null, "", "Windows", "shared-1"),
    new("Shared App", "Steam", "", @"C:\Program Files\SharedApp", "", null, "", "Steam", "steam:999"),
    new("Other App", "Publisher", "2.0", @"C:\Program Files\OtherApp", "", null, "", "Windows", "other-1"),
    new("Polar Uninstaller", "Polar", "1.0.125", @"C:\Program Files\Polar Uninstaller",
        @"C:\Program Files\Polar Uninstaller\PolarUninstaller.exe", null, "", "Windows", "")
};
var sharedService = new ApplicationInventoryService(_ => Task.FromResult<IReadOnlyList<InstalledProgram>>(sharedRecords.ToArray()));
int sharedEvents = 0;
sharedService.Changed += _ => sharedEvents++;
var sharedSnapshot = await sharedService.RefreshAsync();
var expectedShared = InventoryMerger.Merge(sharedRecords);
Check(sharedSnapshot.IsComplete, "The shared inventory reports a complete first scan");
Check(sharedSnapshot.Count == expectedShared.Count,
    "The shared inventory applies merge, deduplication and Polar exclusion exactly once");
Check(sharedSnapshot.Count == 2, "Duplicates and Polar-owned entries are not counted (count=" + sharedSnapshot.Count + ")");
Check(sharedService.Current?.Count == sharedSnapshot.Count,
    "Home reads the same published snapshot as Deep Uninstaller");
Check(sharedEvents == 1, "A successful scan publishes one shared change event");

sharedRecords.Add(new InstalledProgram("Third App", "Publisher", "3.0", @"C:\Program Files\ThirdApp", "", null, "", "Windows", "third-1"));
var refreshedSnapshot = await sharedService.RefreshAsync();
Check(refreshedSnapshot.Count == sharedSnapshot.Count + 1 && sharedService.Current?.Count == refreshedSnapshot.Count,
    "Refreshing the inventory updates the shared count for both pages");
Check(sharedEvents == 2, "Refreshing publishes the updated shared snapshot");

sharedService.NotifyRemoved(new InstalledProgram("Third App", "Publisher", "3.0", @"C:\Program Files\ThirdApp", "", null, "", "Windows", "third-1"));
Check(sharedService.Current?.Count == refreshedSnapshot.Count - 1,
    "A successful uninstall updates the shared count immediately");
Check(sharedEvents == 3, "A verified removal notifies both pages");

var homeCodeShared = File.ReadAllText(Path.Combine(root, "HomeView.xaml.cs"), Encoding.UTF8);
var mainCodeShared = File.ReadAllText(Path.Combine(root, "MainWindow.xaml.cs"), Encoding.UTF8);
var deepCodeShared = File.ReadAllText(Path.Combine(root, "DeepUninstallerView.xaml.cs"), Encoding.UTF8);
Check(homeCodeShared.Contains("ApplicationInventoryService") && homeCodeShared.Contains("snapshot.Count"),
    "Home displays the shared inventory count");
Check(deepCodeShared.Contains("ApplicationInventoryService") && deepCodeShared.Contains("snapshot.Count"),
    "Deep Uninstaller displays the shared inventory count");
Check(mainCodeShared.Contains("ApplicationInventoryService applicationInventory") &&
      mainCodeShared.Contains("new HomeView(arabic, applicationInventory)") &&
      mainCodeShared.Contains("new DeepUninstallerView(arabic, activity, applicationInventory)") &&
      !mainCodeShared.Contains("homePrograms"),
    "MainWindow passes one shared inventory service to both pages");
Check(deepCodeShared.Contains("view.Filter = FilterCard") && deepCodeShared.Contains("language.Set(CountText,TextBlock.TextProperty,lang=>snapshot.IsComplete"),
    "Search filters only the visible view and never changes the global inventory count");

// === Test 40: A stale asynchronous scan cannot replace a newer count ===
var slowScan = new TaskCompletionSource<IReadOnlyList<InstalledProgram>>(TaskCreationOptions.RunContinuationsAsynchronously);
var fastScan = new TaskCompletionSource<IReadOnlyList<InstalledProgram>>(TaskCreationOptions.RunContinuationsAsynchronously);
int scanCalls = 0;
var staleService = new ApplicationInventoryService(_ =>
{
    int call = Interlocked.Increment(ref scanCalls);
    return call == 1 ? slowScan.Task : fastScan.Task;
});
int staleEvents = 0;
staleService.Changed += _ => Interlocked.Increment(ref staleEvents);
var firstScan = staleService.RefreshAsync();
var secondScan = staleService.RefreshAsync();
fastScan.SetResult(new[]
{
    new InstalledProgram("New A", "P", "1", "", "", null, "", "Windows", "new-a"),
    new InstalledProgram("New B", "P", "1", "", "", null, "", "Windows", "new-b")
});
var newest = await secondScan;
slowScan.SetResult(new[] { new InstalledProgram("Old A", "P", "1", "", "", null, "", "Windows", "old-a") });
var oldest = await firstScan;
Check(newest.Count == 2 && staleService.Current?.Count == 2,
    "The newest scan remains the authoritative count");
Check(!staleService.IsCurrent(oldest) && staleService.IsCurrent(newest),
    "A stale scan is rejected by the generation guard");
Check(staleEvents == 1, "Only the newest scan raises a change event");

// === Test 41: Incomplete scans are not presented as confirmed counts ===
var manyPrograms = Enumerable.Range(0, 10)
    .Select(i => new InstalledProgram("Many " + i, "P", "1", "", "", null, "", "Windows", "many-" + i))
    .ToArray();
var partialSource = manyPrograms.ToList();
var partialService = new ApplicationInventoryService(_ => Task.FromResult<IReadOnlyList<InstalledProgram>>(partialSource.ToArray()));
var fullSnapshot = await partialService.RefreshAsync();
Check(fullSnapshot.IsComplete && fullSnapshot.Count == 10, "A complete baseline is published");
partialSource.Clear();
partialSource.Add(manyPrograms[0]);
var partialSnapshot = await partialService.RefreshAsync();
Check(!partialSnapshot.IsComplete, "A sudden incomplete scan is not confirmed");
Check(partialService.Current?.IsComplete == false, "The published snapshot exposes the incomplete state");
// === Test 43: Games Uninstall uses internal intelligent routing ===
var deepCode = File.ReadAllText(Path.Combine(root, "DeepUninstallerView.xaml.cs"), Encoding.UTF8);
Check(deepCode.Contains("TryParseUninstaller(card.Program.Uninstall") && deepCode.Contains("LauncherRouteService.Find(card.Program)"),
    "Games Uninstall checks an independent uninstaller before launcher routing");
Check(deepCode.Contains("steam://uninstall/{appId}") || deepCode.Contains("steam://uninstall/"),
    "Steam uninstall behavior is preserved");
Check(deepCode.Contains("Open the official launcher to uninstall") || deepCode.Contains("Open the registered uninstaller"),
    "Unsupported games fall back to the official launcher with a clear limitation");
Check(deepCode.Contains("CompleteRemoval") && deepCode.Contains("InspectAsync(plan,current") && !deepCode.Contains("CleanLeftoversAutomatically"),
    "Official completion detects leftovers without automatic cleanup");

Console.WriteLine();
// === Test 42: Feedback reports the authoritative application version ===
var feedbackCode = File.ReadAllText(Path.Combine(root, "FeedbackWindow.xaml.cs"), Encoding.UTF8);
Check(feedbackCode.Contains("app_version = AppInfo.Version"),
    "Feedback submissions use the authoritative application version");
Check(AppInfo.Version == propsVersion, "Application reports v" + propsVersion + " (actual=" + AppInfo.Version + ")");
Check(!feedbackCode.Contains("app_version = \"2.0.0\""),
    "The misleading hardcoded 2.0.0 feedback version is removed");

Console.WriteLine();
// === Test 43: EXE-only release detection and asset selection ===
Check(WpfUpdateService.IsCompatibleRelease("v1.0.128", "1.0.128", new[] { "PolarUninstaller.exe" }),
    "Generic tags with a standalone EXE are compatible");
Check(WpfUpdateService.IsCompatibleRelease("v1.0.128", "1.0.128", new[] { "PolarUninstaller-Setup-v1.0.128.exe" }),
    "Generic tags with a versioned Setup are compatible");
Check(!WpfUpdateService.IsCompatibleRelease("v1.0.104", "1.0.104", new[] { "Polar.Uninstaller.exe" }),
    "Legacy Python assets are not treated as WPF updates");
Check(WpfUpdateService.IsCompatibleRelease("wpf-v1.0.128", "1.0.128", Array.Empty<string>()),
    "wpf-v tags remain compatible");

var exeOnlyRelease = new WpfRelease("1.0.128", "", "", null,
    "https://github.com/Ayh-m1/Polar-Uninstaller/releases/tag/v1.0.128",
    new[] { "PolarUninstaller.exe" })
{
    Downloads = new[]
    {
        new WpfAsset("PolarUninstaller.exe",
            "https://github.com/Ayh-m1/Polar-Uninstaller/releases/download/v1.0.128/PolarUninstaller.exe",
            1024, "sha256:" + new string('a', 64))
    }
};
Check(UpdateDownloadService.Select(exeOnlyRelease)?.Name == "PolarUninstaller.exe",
    "The updater selects an EXE-only asset");

var setupRelease = new WpfRelease("1.0.128", "", "", null,
    "https://github.com/Ayh-m1/Polar-Uninstaller/releases/tag/v1.0.128",
    new[] { "PolarUninstaller-Setup-v1.0.128.exe" })
{
    Downloads = new[]
    {
        new WpfAsset("PolarUninstaller-Setup-v1.0.128.exe",
            "https://github.com/Ayh-m1/Polar-Uninstaller/releases/download/v1.0.128/PolarUninstaller-Setup-v1.0.128.exe",
            1024, "sha256:" + new string('a', 64))
    }
};
Check(UpdateDownloadService.Select(setupRelease)?.Name == "PolarUninstaller-Setup-v1.0.128.exe",
    "The updater still selects a versioned Setup asset");

var bothAssetsRelease = new WpfRelease("1.0.128", "", "", null,
    "https://github.com/Ayh-m1/Polar-Uninstaller/releases/tag/v1.0.128",
    new[] { "PolarUninstaller-Setup-v1.0.128.exe", "PolarUninstaller.exe" })
{
    Downloads = new[]
    {
        setupRelease.Downloads[0],
        exeOnlyRelease.Downloads[0]
    }
};
Check(UpdateDownloadService.Select(bothAssetsRelease)?.Name == "PolarUninstaller.exe",
    "The standalone EXE is preferred when both assets are published");
Check(SemanticVersion.Parse("1.0.128").CompareTo(SemanticVersion.Parse("1.0.127")) > 0,
    "Version comparison treats v1.0.128 as newer than v1.0.127");
Check(SemanticVersion.Parse("1.0.136").CompareTo(SemanticVersion.Parse("1.0.135")) > 0,
    "Version comparison treats v1.0.136 as newer than v1.0.135");
Check(SemanticVersion.Parse("1.0.137").CompareTo(SemanticVersion.Parse("1.0.136")) > 0,
    "Version comparison treats v1.0.137 as newer than v1.0.136");

string ReleaseJson(string tag, string assetName, long size, string digest) => JsonSerializer.Serialize(new[]
{
    new
    {
        tag_name = tag,
        name = "Polar uninstaller",
        body = "",
        draft = false,
        prerelease = false,
        published_at = "2026-11-09T05:16:49Z",
        html_url = $"https://github.com/Ayh-m1/Polar-Uninstaller/releases/tag/{tag}",
        assets = new[]
        {
            new
            {
                name = assetName,
                size,
                digest,
                browser_download_url = $"https://github.com/Ayh-m1/Polar-Uninstaller/releases/download/{tag}/{assetName}"
            }
        }
    }
});

string sampleDigest = "sha256:" + new string('a', 64);
using (var client = new HttpClient(new StaticJsonHandler(ReleaseJson("v1.0.128", "PolarUninstaller.exe", 1024, sampleDigest))))
{
    var service = new WpfUpdateService(client, WpfUpdateService.DefaultApiBase, "1.0.127");
    var result = service.CheckAsync(true).GetAwaiter().GetResult();
    Check(result.State == UpdateState.Available && result.Release?.Version == "1.0.128",
        "v1.0.127 detects an EXE-only v1.0.128 release");
    Check(result.Release != null && UpdateDownloadService.Select(result.Release)?.Name == "PolarUninstaller.exe",
        "The detected EXE-only release selects the standalone asset");
}
using (var client = new HttpClient(new StaticJsonHandler(ReleaseJson("v1.0.128", "PolarUninstaller-Setup-v1.0.128.exe", 1024, sampleDigest))))
{
    var service = new WpfUpdateService(client, WpfUpdateService.DefaultApiBase, "1.0.127");
    var result = service.CheckAsync(true).GetAwaiter().GetResult();
    Check(result.State == UpdateState.Available && result.Release != null &&
          UpdateDownloadService.Select(result.Release)?.Name == "PolarUninstaller-Setup-v1.0.128.exe",
        "Setup-only releases remain compatible");
}
using (var client = new HttpClient(new StaticJsonHandler(ReleaseJson("v1.0.128", "Polar.Uninstaller.exe", 1024, sampleDigest))))
{
    var service = new WpfUpdateService(client, WpfUpdateService.DefaultApiBase, "1.0.127");
    var result = service.CheckAsync(true).GetAwaiter().GetResult();
    Check(result.State == UpdateState.NoCompatibleRelease,
        "Legacy Python-only releases are ignored");
}
using (var client = new HttpClient(new StaticJsonHandler(ReleaseJson("v1.0.128", "PolarUninstaller.exe", 1024, sampleDigest))))
{
    var service = new WpfUpdateService(client, WpfUpdateService.DefaultApiBase, "1.0.128");
    var result = service.CheckAsync(true).GetAwaiter().GetResult();
    Check(result.State == UpdateState.UpToDate,
        "The same installed version is reported as up to date");
}

// === Test 42c: v1.0.136 detects the prepared v1.0.137 release (mocked GitHub response) ===
using (var client = new HttpClient(new StaticJsonHandler(ReleaseJson("v1.0.137", "PolarUninstaller.exe", 64 * 1024 * 1024, sampleDigest))))
{
    var service = new WpfUpdateService(client, WpfUpdateService.DefaultApiBase, "1.0.136");
    var result = service.CheckAsync(true).GetAwaiter().GetResult();
    Check(result.State == UpdateState.Available && result.Release?.Version == "1.0.137",
        "MOCKED: v1.0.136 detects a published v1.0.137 release as available");
    Check(UpdateDownloadService.Select(result.Release)?.Name == "PolarUninstaller.exe",
        "MOCKED: the v1.0.137 update selects the standalone PolarUninstaller.exe asset");
}

// === Test 42d: Update Center language synchronization helpers ===
var englishOnlyNotes = "## English\n### What's New\n- Faster update checks\n- Safer installation";
var arabicOnlyNotes = "## العربية\n- تحسين سرعة التحديث";
var bilingualUpdateNotes = englishOnlyNotes + "\n\n" + arabicOnlyNotes;

var enSelected = ReleaseNotesTextService.Select(bilingualUpdateNotes, false);
Check(enSelected.Text.Contains("Faster", StringComparison.Ordinal) && !enSelected.Text.Contains("أسرع", StringComparison.Ordinal),
    "English Update Center notes select the English section");
Check(!enSelected.LanguageFallback, "English notes are not marked as a fallback");

var arSelected = ReleaseNotesTextService.Select(bilingualUpdateNotes, true);
Check(arSelected.Text.Contains("تحسين", StringComparison.Ordinal) && !arSelected.Text.Contains("Faster", StringComparison.Ordinal),
    "Arabic Update Center notes select the Arabic section");
Check(!arSelected.LanguageFallback, "Arabic notes are not marked as a fallback");

var arFallback = ReleaseNotesTextService.Select(englishOnlyNotes, true);
Check(arFallback.LanguageFallback && !arFallback.FallbackIsArabic,
    "Arabic mode reports an English-only fallback");
var enFallback = ReleaseNotesTextService.Select(arabicOnlyNotes, false);
Check(enFallback.LanguageFallback && enFallback.FallbackIsArabic,
    "English mode reports an Arabic-only fallback");

var stripped = ReleaseNotesTextService.StripDuplicateHeading("### What's New\n- Faster update checks", false);
Check(!stripped.Contains("What's New", StringComparison.OrdinalIgnoreCase),
    "Duplicate What's New headings are removed from update notes");
Check(stripped.Contains("Faster update checks", StringComparison.Ordinal),
    "Stripping a duplicate heading keeps the note body");

// === Test 42e: partial removal classification keeps leftovers out of total failure ===
var advReportProgram = new InstalledProgram("Advanced Removal Test", "Polar", "1.0", @"C:\TestApp", Uninstall: "none");
var leftoverVerification = new UninstallVerification(false, false, false, false);
var leftoverReport = UninstallReportService.Evaluate(advReportProgram, leftoverVerification, null,
    new[] { @"C:\TestApp\leftover.exe" }, 38, 1, 7 * 1024 * 1024, "Direct removal", true);
Check(leftoverReport.Outcome == UninstallOutcome.UninstallFailed,
    "A remaining main executable prevents a false removed result");
Check(leftoverReport.Severity == "ERROR",
    "A still-present main executable is reported as an error");

// === Test 43b: release-note language selection and safe Markdown rendering ===
const string bilingualNotes = """
## English
### New
- **Faster** update checks
- Fixed the `download` progress display

## العربية
### جديد
- **تحديث** أسرع
- إصلاح عرض تقدم التنزيل
""";
var arabicNotes = ReleaseNotesTextService.Select(bilingualNotes, true);
Check(arabicNotes.Text.Contains("تحديث", StringComparison.Ordinal) &&
      arabicNotes.Text.Contains("أسرع", StringComparison.Ordinal) &&
      !arabicNotes.Text.Contains("Faster", StringComparison.Ordinal),
    "Arabic release notes select the Arabic section");
Check(!arabicNotes.LanguageFallback, "Published Arabic notes are not marked as a fallback");
var englishNotes = ReleaseNotesTextService.Select(bilingualNotes, false);
Check(englishNotes.Text.Contains("Faster", StringComparison.Ordinal) &&
      !englishNotes.Text.Contains("تحديث أسرع", StringComparison.Ordinal),
    "English release notes select the English section");
var englishOnly = ReleaseNotesTextService.Select("## What's New\n- A faster updater", true);
Check(englishOnly.LanguageFallback && englishOnly.Text.Contains("faster", StringComparison.OrdinalIgnoreCase),
    "English-only notes remain readable in Arabic mode with a fallback hint");
var emptyNotes = ReleaseNotesTextService.Select("", true);
Check(emptyNotes.Text.Length == 0, "Missing release notes are handled without throwing");
Check(MarkdownRenderer.ContainsArabic("تحديث أسرع") && !MarkdownRenderer.ContainsArabic("Faster update"),
    "Arabic script detection drives release-note direction");

var markdownBlocks = MarkdownRenderer.Parse("""
# Update 1.0.135
A paragraph with **bold** text.

## What's New
- First item
  - Nested item
1. Ordered item
> A quoted note

```csharp
var safe = true;
```

---
""");
Check(markdownBlocks.Any(b => b.Kind == MarkdownBlockKind.Heading && b.Level == 1 && b.Text.Contains("1.0.135")),
    "Markdown headings are parsed with their level");
Check(markdownBlocks.Any(b => b.Kind == MarkdownBlockKind.Bullet && b.Level == 1 && b.Text == "Nested item"),
    "Nested Markdown bullets are parsed with their indentation");
Check(markdownBlocks.Any(b => b.Kind == MarkdownBlockKind.OrderedItem && b.Number == 1),
    "Ordered Markdown lists are parsed");
Check(markdownBlocks.Any(b => b.Kind == MarkdownBlockKind.Quote && b.Text.Contains("quoted")),
    "Markdown block quotes are parsed");
Check(markdownBlocks.Any(b => b.Kind == MarkdownBlockKind.Code && b.Text.Contains("var safe")),
    "Fenced Markdown code blocks are parsed");
Check(markdownBlocks.Any(b => b.Kind == MarkdownBlockKind.Divider),
    "Markdown dividers are parsed");

var inlineSpans = MarkdownRenderer.ParseInline(
    "**bold** *italic* `code` [GitHub](https://github.com/Ayh-m1/Polar-Uninstaller)");
Check(inlineSpans.Any(s => s.Kind == MarkdownInlineKind.Bold && s.Text == "bold"),
    "Inline bold text is parsed");
Check(inlineSpans.Any(s => s.Kind == MarkdownInlineKind.Italic && s.Text == "italic"),
    "Inline italic text is parsed");
Check(inlineSpans.Any(s => s.Kind == MarkdownInlineKind.Code && s.Text == "code"),
    "Inline code is parsed");
Check(inlineSpans.Any(s => s.Kind == MarkdownInlineKind.Link && s.Url?.StartsWith("https://github.com/", StringComparison.Ordinal) == true),
    "Safe HTTPS links are parsed");
Check(!MarkdownRenderer.ParseInline("[click](javascript:alert(1))").Any(s => s.Kind == MarkdownInlineKind.Link),
    "Script-style links are never exposed as links");
var sanitized = MarkdownRenderer.Parse("<script>alert('x')</script>\n- Safe <img src=x onerror=alert(1)>");
string sanitizedText = string.Join(" ", sanitized.Select(b => b.Text));
Check(!sanitizedText.Contains("<script", StringComparison.OrdinalIgnoreCase) &&
      !sanitizedText.Contains("onerror", StringComparison.OrdinalIgnoreCase) &&
      sanitizedText.Contains("Safe", StringComparison.Ordinal),
    "HTML and script markup are stripped from release notes");

string updateXaml = File.ReadAllText(Path.Combine(root, "UpdateCenterWindow.xaml"), Encoding.UTF8);
Check(updateXaml.Contains("WindowStartupLocation=\"CenterOwner\"", StringComparison.Ordinal),
    "The update window opens centered over the main window");
Check(updateXaml.Contains("WindowStyle=\"SingleBorderWindow\"", StringComparison.Ordinal) &&
      updateXaml.Contains("ResizeMode=\"CanResize\"", StringComparison.Ordinal),
    "The update window keeps native window controls and free resizing");
Check(updateXaml.Contains("MinWidth=\"600\"", StringComparison.Ordinal) &&
      updateXaml.Contains("MinHeight=\"600\"", StringComparison.Ordinal),
    "The update window has a practical minimum size");
Check(updateXaml.Contains("FlowDocumentScrollViewer", StringComparison.Ordinal) &&
      updateXaml.Contains("VerticalScrollBarVisibility=\"Auto\"", StringComparison.Ordinal) &&
      updateXaml.Contains("HorizontalScrollBarVisibility=\"Disabled\"", StringComparison.Ordinal),
    "Release notes get an independent vertical scroller without horizontal overflow");
Check(updateXaml.Contains("x:Name=\"UpdateNowButton\"", StringComparison.Ordinal) &&
      updateXaml.Contains("x:Name=\"UpdateLaterButton\"", StringComparison.Ordinal),
    "The bottom area contains exactly the two decision buttons");
string updateCode = File.ReadAllText(Path.Combine(root, "UpdateCenterWindow.xaml.cs"), Encoding.UTF8);
Check(updateCode.Contains("\"Update Now\"", StringComparison.Ordinal) &&
      updateCode.Contains("تحديث الآن", StringComparison.Ordinal) &&
      updateCode.Contains("\"Update Later\"", StringComparison.Ordinal) &&
      updateCode.Contains("تحديث لاحقًا", StringComparison.Ordinal),
    "Both update decisions are localized in English and Arabic");
Check(updateCode.Contains("LaunchHelperAsync", StringComparison.Ordinal) &&
      updateCode.Contains("VerifyStandaloneExecutable", StringComparison.Ordinal),
    "Update Now verifies the download and reuses the safe external helper");
string updateMainCode = File.ReadAllText(Path.Combine(root, "MainWindow.xaml.cs"), Encoding.UTF8);
Check(updateMainCode.Contains("autoUpdatePromptShown", StringComparison.Ordinal) &&
      updateMainCode.Contains("StartupUpdateCheckAsync", StringComparison.Ordinal),
    "The automatic update prompt is guarded to appear once per session");

// === Test 44: Verification and replacement primitives ===
string releaseExe = Path.Combine(root, "bin", "Release", "net9.0-windows", "win-x64", "PolarUninstaller.exe");
if (File.Exists(releaseExe))
{
    var verified = UpdateVerificationService.VerifyStandaloneExecutable(releaseExe, propsVersion);
    Check(verified.IsValid, "The built x64 executable passes PE/version verification");
    Check(verified.Signature == AuthenticodeStatus.Unsigned,
        "The unsigned build is reported as unsigned, not as trusted");
    var wrongVersion = UpdateVerificationService.VerifyStandaloneExecutable(releaseExe, "1.0.129");
    Check(!wrongVersion.IsValid && wrongVersion.Error == "version_mismatch",
        "A mismatched embedded version is rejected");
}
else Console.WriteLine("NOTE: Release win-x64 executable was not found; PE verification was not exercised.");

string replaceDir = Path.Combine(AppContext.BaseDirectory, "fixtures", "polar-replace-" + Guid.NewGuid().ToString("N"));
try
{
    string replaceSourceDir = Path.Combine(replaceDir, "source");
    Directory.CreateDirectory(replaceSourceDir);
    string replaceTarget = Path.Combine(replaceDir, "PolarUninstaller.exe");
    string replaceSource = Path.Combine(replaceSourceDir, "PolarUninstaller.exe");
    File.WriteAllText(replaceTarget, "old-binary");
    File.WriteAllText(replaceSource, "new-binary");
    string replaceHash = UpdateVerificationService.ComputeSha256(replaceSource);
    Check(UpdateInstallerService.ReplaceWithBackup(replaceSource, replaceTarget, replaceHash),
        "Replacement succeeds with a matching hash");
    Check(File.ReadAllText(replaceTarget) == "new-binary",
        "The target contains the new executable");
    Check(File.ReadAllText(replaceTarget + ".bak") == "old-binary",
        "The backup contains the previous executable");
    Check(UpdateInstallerService.RestoreBackup(replaceTarget, replaceTarget + ".bak"),
        "Rollback restores the backup");
    Check(File.ReadAllText(replaceTarget) == "old-binary",
        "The target is restored to the previous executable");
    Check(!UpdateInstallerService.ReplaceWithBackup(replaceSource, replaceTarget, new string('0', 64)),
        "A mismatched hash is rejected");
    Check(File.ReadAllText(replaceTarget) == "old-binary",
        "A rejected replacement leaves the target unchanged");

    string nonPe = Path.Combine(replaceDir, "not-a-pe.bin");
    File.WriteAllText(nonPe, "not a portable executable");
    Check(!UpdateVerificationService.VerifyStandaloneExecutable(nonPe, "1.0.128").IsValid,
        "A non-PE file is rejected");
}
finally
{
    try { Directory.Delete(replaceDir, true); } catch { }
}

var parsedArguments = UpdateInstallerService.ParseArguments(new[]
{
    "--apply-update", "--target", @"C:\A B\PolarUninstaller.exe", "--pid", "123", "--no-restart"
});
Check(parsedArguments.TryGetValue("--target", out var parsedTarget) && parsedTarget == @"C:\A B\PolarUninstaller.exe",
    "Helper arguments preserve paths containing spaces");
Check(parsedArguments.TryGetValue("--pid", out var parsedPid) && parsedPid == "123" && parsedArguments.ContainsKey("--no-restart"),
    "Helper arguments parse values and flags");
Check(UpdateInstallerService.IsApplyUpdateMode(new[] { "--apply-update" }),
    "Apply-update mode is recognized");
Check(!UpdateInstallerService.IsApplyUpdateMode(new[] { "--other" }),
    "Unrelated arguments do not trigger apply-update mode");

// === Test 45: local release fixtures for the migration and EXE-only paths ===
// A loopback HTTP server exercises the real detection, download and
// verification code without publishing anything to GitHub.
var fixtureReleaseExe = Path.Combine(root, "Build", "Releases", "v1.0.130", "PolarUninstaller.exe");
if (!File.Exists(fixtureReleaseExe)) fixtureReleaseExe = Path.Combine(root, "Build", "SingleFile", "PolarUninstaller.exe");
if (File.Exists(fixtureReleaseExe) &&
    !string.Equals(System.Diagnostics.FileVersionInfo.GetVersionInfo(fixtureReleaseExe).FileVersion, "1.0.130.0", StringComparison.Ordinal))
{
    // Never present the current build as a v1.0.130 fixture: that would fail
    // version verification for reasons unrelated to the updater.
    Console.WriteLine("NOTE: No v1.0.130 fixture EXE is available; legacy migration download checks were skipped.");
    fixtureReleaseExe = "";
}
if (File.Exists(fixtureReleaseExe))
{
    byte[] releaseBytes = File.ReadAllBytes(fixtureReleaseExe);
    string releaseHash = UpdateVerificationService.ComputeSha256(fixtureReleaseExe);
    string fixtureStaging = Path.Combine(AppContext.BaseDirectory, "fixtures", "polar-fixture-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(fixtureStaging);

    using (var migrationServer = new FixtureServer())
    {
        migrationServer.AddRoute("/releases?per_page=100&page=1", "application/json", Encoding.UTF8.GetBytes(
            FixtureServer.ReleaseJson("1.0.129", "PolarUninstaller-Setup-v1.0.129.exe", 12345678, new string('a', 64), null)));
        using var client = new HttpClient();
        var service = new WpfUpdateService(client, "http://127.0.0.1:" + migrationServer.Port, "1.0.127");
        var result = service.CheckAsync(force: true).GetAwaiter().GetResult();
        Check(result.State == UpdateState.Available && result.Release?.Version == "1.0.129",
            "A v1.0.127 installation detects the v1.0.129 Setup migration release");
        Check(result.Release != null && UpdateDownloadService.Select(result.Release)?.Name == "PolarUninstaller-Setup-v1.0.129.exe",
            "The migration release selects the versioned Setup asset");
    }

    using (var exeServer = new FixtureServer())
    {
        string baseUrl = "http://127.0.0.1:" + exeServer.Port;
        string downloadBase = baseUrl + "/Ayh-m1/Polar-Uninstaller/releases/download";
        string exeUrl = downloadBase + "/v1.0.130/PolarUninstaller.exe";
        exeServer.AddRoute("/releases?per_page=100&page=1", "application/json", Encoding.UTF8.GetBytes(
            FixtureServer.ReleaseJson("1.0.130", "PolarUninstaller.exe", releaseBytes.Length, releaseHash, exeUrl)));
        exeServer.AddRoute("/Ayh-m1/Polar-Uninstaller/releases/download/v1.0.130/PolarUninstaller.exe", "application/octet-stream", releaseBytes);
        using var client = new HttpClient();
        var service = new WpfUpdateService(client, baseUrl, "1.0.129");
        var result = service.CheckAsync(force: true).GetAwaiter().GetResult();
        Check(result.State == UpdateState.Available && result.Release?.Version == "1.0.130",
            "A v1.0.129 installation detects the v1.0.130 EXE-only release");
        Check(result.Release != null && UpdateDownloadService.Select(result.Release)?.Name == "PolarUninstaller.exe",
            "The EXE-only release selects the standalone asset");
        if (result.Release != null)
        {
            string downloaded = UpdateDownloadService.DownloadAsync(
                result.Release, new Progress<DownloadProgress>(_ => { }), CancellationToken.None,
                "1.0.129", fixtureStaging, downloadBase).GetAwaiter().GetResult();
            Check(File.Exists(downloaded) && new FileInfo(downloaded).Length == releaseBytes.Length,
                "The v1.0.130 EXE downloads from the fixture at the expected size");
            Check(UpdateVerificationService.HashEquals(downloaded, releaseHash),
                "The v1.0.130 EXE matches the fixture SHA-256");
            Check(UpdateVerificationService.VerifyStandaloneExecutable(downloaded, "1.0.130", releaseHash, releaseBytes.Length).IsValid,
                "The v1.0.130 EXE passes PE, version and hash verification");
            string? directory = Path.GetDirectoryName(downloaded);
            try { if (directory != null) Directory.Delete(directory, true); } catch { }
        }
    }
    try { Directory.Delete(fixtureStaging, true); } catch { }
}
else
{
    Console.WriteLine("NOTE: The v1.0.130 release EXE is not available; legacy fixture checks were skipped.");
}

// === Test 46: real-binary EXE replacement and rollback ===
// Exercises the exact replacement/rollback primitives used by the update
// helper against the real v1.0.129 and v1.0.130 binaries, without publishing.
string realOldExe = Path.Combine(root, "obj", "releases", "v1.0.129", "PolarUninstaller.exe");
string realNewExe = Path.Combine(root, "Build", "Releases", "v1.0.130", "PolarUninstaller.exe");
if (File.Exists(realOldExe) && File.Exists(realNewExe))
{
    string oldHash = UpdateVerificationService.ComputeSha256(realOldExe);
    string newHash = UpdateVerificationService.ComputeSha256(realNewExe);
    string roundTripDir = Path.Combine(AppContext.BaseDirectory, "fixtures", "polar-roundtrip-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(roundTripDir);
    try
    {
        string target = Path.Combine(roundTripDir, "PolarUninstaller.exe");
        File.Copy(realOldExe, target, true);
        Check(UpdateInstallerService.ReplaceWithBackup(realNewExe, target, newHash),
            "The real v1.0.130 EXE replaces the real v1.0.129 EXE");
        Check(UpdateVerificationService.HashEquals(target, newHash) &&
              FileVersionInfo.GetVersionInfo(target).FileVersion == "1.0.130.0",
            "The replaced target reports v1.0.130");
        Check(UpdateVerificationService.HashEquals(target + ".bak", oldHash) &&
              FileVersionInfo.GetVersionInfo(target + ".bak").FileVersion == "1.0.129.0",
            "The backup preserves the real v1.0.129 EXE");
        Check(UpdateInstallerService.RestoreBackup(target, target + ".bak"),
            "Rollback restores the real v1.0.129 EXE");
        Check(UpdateVerificationService.HashEquals(target, oldHash) &&
              FileVersionInfo.GetVersionInfo(target).FileVersion == "1.0.129.0",
            "The rolled-back target reports v1.0.129");
        Check(!UpdateInstallerService.ReplaceWithBackup(realNewExe, target, new string('0', 64)),
            "A wrong hash is rejected for real binaries");
        Check(UpdateVerificationService.HashEquals(target, oldHash),
            "A rejected real-binary replacement leaves v1.0.129 unchanged");
    }
    finally { try { Directory.Delete(roundTripDir, true); } catch { } }
}
else Console.WriteLine("NOTE: Real v1.0.129/v1.0.130 binaries were not found; real-binary round-trip was not exercised.");

// === Test 47: real Steam library recognition (when Steam is installed) ===
string? realSteamRoot = null;
foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
{
    try
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
        using var steam = baseKey.OpenSubKey(@"SOFTWARE\Valve\Steam");
        string candidate = Convert.ToString(steam?.GetValue("SteamPath") ?? steam?.GetValue("InstallPath")) ?? "";
        if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate.Replace('/', '\\')))
        { realSteamRoot = candidate.Replace('/', '\\'); break; }
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException) { }
    if (realSteamRoot != null) break;
}
if (realSteamRoot != null)
{
    var steamResults = new List<InstalledProgram>();
    try { LauncherInventoryService.ScanSteam(realSteamRoot, steamResults, CancellationToken.None); }
    catch (Exception ex) { Check(false, "Real Steam scan threw: " + ex.GetType().Name + " " + ex.Message); }
    Console.WriteLine("Real Steam scan found " + steamResults.Count + " games:");
    foreach (var game in steamResults) Console.WriteLine("  " + game.Name + " | " + game.Identifier + " | " + game.Location);
    string[] expected = { "Stardew Valley", "Until Then", "Persona 3 Reload", "Persona 5 Royal" };
    foreach (string title in expected)
    {
        var match = steamResults.FirstOrDefault(g => string.Equals(g.Name, title, StringComparison.OrdinalIgnoreCase));
        Check(match != null, "Real Steam scan recognizes " + title);
        if (match != null)
        {
            Check(match.Identifier.StartsWith("steam:", StringComparison.OrdinalIgnoreCase) && match.Identifier.Length > "steam:".Length,
                title + " has a verified Steam AppID (" + match.Identifier + ")");
            Check(match.RequiresOfficialRemoval, title + " requires official removal (no unsafe direct delete)");
            string? db = SteamMetadataService.SteamDbUrl(match);
            Check(!string.IsNullOrWhiteSpace(db) && db!.StartsWith("https://steamdb.info/app/", StringComparison.Ordinal),
                title + " has a SteamDB reference URL");
        }
    }
    // Secondary-library / unusual-folder sanity: every recognized game must resolve to an existing folder.
    Check(steamResults.All(g => Directory.Exists(g.Location)), "Every recognized Steam game resolves to an existing installation folder");
    // No duplicate AppIDs.
    var ids = steamResults.Select(g => g.Identifier).ToList();
    Check(ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() == ids.Count, "Real Steam scan has no duplicate AppIDs");
}
else Console.WriteLine("NOTE: Steam is not installed on this machine; real Steam recognition was not exercised.");

// === Test 48: real Battle.net recognition (when Battle.net is installed) ===
string? realBattleRoot = null;
foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
{
    try
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
        using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
        if (uninstall != null)
        {
            foreach (var keyName in uninstall.GetSubKeyNames())
            {
                using var item = uninstall.OpenSubKey(keyName);
                string command = Convert.ToString(item?.GetValue("UninstallString")) ?? "";
                if (command.Contains("Blizzard Uninstaller.exe", StringComparison.OrdinalIgnoreCase)) { realBattleRoot = "registry"; break; }
            }
        }
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException) { }
    if (realBattleRoot != null) break;
}
if (realBattleRoot != null)
{
    var windows = (new WindowsInventoryService()).GetInstalledAsync().GetAwaiter().GetResult();
    var games = (new LauncherInventoryService()).GetInstalledAsync(CancellationToken.None).GetAwaiter().GetResult();
    var merged = InventoryMerger.Merge(windows.Concat(games));
    var battleGames = merged.Where(p => p.Identifier.StartsWith("battlenet:", StringComparison.OrdinalIgnoreCase)).ToArray();
    Console.WriteLine("Battle.net games found: " + battleGames.Length);
    foreach (var g in battleGames) Console.WriteLine("  " + g.Name + " | " + g.Identifier + " | " + g.Location + " | " + g.SizeKb);
    Check(battleGames.Length > 0, "Battle.net scan recognizes at least one Battle.net game");
    foreach (var g in battleGames)
    {
        Check(g.RequiresOfficialRemoval, g.Name + " requires official removal (no unsafe direct delete)");
        Check(Directory.Exists(g.Location), g.Name + " resolves to an existing installation folder");
        string? uninstall = g.Uninstall;
        Check(!string.IsNullOrWhiteSpace(uninstall) && uninstall.Contains("Blizzard Uninstaller.exe", StringComparison.OrdinalIgnoreCase),
            g.Name + " has an official Blizzard uninstall command");
    }
    // Overwatch specifically (if present)
    var overwatch = battleGames.FirstOrDefault(g => g.Name.Contains("Overwatch", StringComparison.OrdinalIgnoreCase));
    if (overwatch != null)
    {
        Check(overwatch.Identifier == "battlenet:prometheus", "Overwatch has the verified Battle.net UID prometheus (" + overwatch.Identifier + ")");
    }
}
else Console.WriteLine("NOTE: Battle.net is not installed; real Battle.net recognition was not exercised.");

// === Test 49: application size detection ===
{
    string sizeTestRoot = Path.Combine(AppContext.BaseDirectory, "fixtures", "polar-size-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(sizeTestRoot);
    try
    {
        Directory.CreateDirectory(Path.Combine(sizeTestRoot, "nested", "deep"));
        File.WriteAllBytes(Path.Combine(sizeTestRoot, "a.bin"), new byte[1000]);
        File.WriteAllBytes(Path.Combine(sizeTestRoot, "nested", "b.bin"), new byte[2000]);
        File.WriteAllBytes(Path.Combine(sizeTestRoot, "nested", "deep", "c.bin"), new byte[3000]);
        long? measured = ApplicationSizeService.MeasureAsync(sizeTestRoot, CancellationToken.None).GetAwaiter().GetResult();
        Check(measured.HasValue && measured.Value == 6000, "Recursive size calculation sums all nested files (got " + (measured.HasValue ? measured.Value : -1) + ")");
        // Cache reuse
        Check(ApplicationSizeService.TryGetCached(sizeTestRoot, out long? cached) && cached == 6000, "Measured size is cached");
        // Empty directory
        string emptyDir = Path.Combine(sizeTestRoot, "empty");
        Directory.CreateDirectory(emptyDir);
        long? emptyMeasured = ApplicationSizeService.MeasureAsync(emptyDir, CancellationToken.None).GetAwaiter().GetResult();
        Check(emptyMeasured.HasValue && emptyMeasured.Value == 0, "Empty directory measures as 0 bytes");
        // Missing path
        long? missing = ApplicationSizeService.MeasureAsync(Path.Combine(sizeTestRoot, "nope"), CancellationToken.None).GetAwaiter().GetResult();
        Check(!missing.HasValue, "Missing path returns null (unknown)");
        // Invalidation
        ApplicationSizeService.Invalidate(sizeTestRoot);
        Check(!ApplicationSizeService.TryGetCached(sizeTestRoot, out _), "Invalidate removes cached result");
    }
    finally { try { Directory.Delete(sizeTestRoot, true); } catch { } }
}

// === Optional Test 50: real published-asset download (network) ===
// Enable with POLAR_REAL_DOWNLOAD=1. This requires a published standalone
// PolarUninstaller.exe asset. As of this build the latest GitHub release is
// v1.0.127, which only contains a Setup installer, so this test stays skipped
// until v1.0.130 is published. Update the URL, digest and size at that point.
if (Environment.GetEnvironmentVariable("POLAR_REAL_DOWNLOAD") == "1")
{
    const string url = "https://github.com/Ayh-m1/Polar-Uninstaller/releases/download/v1.0.128/PolarUninstaller.exe";
    const string digest = "sha256:f1f543c256dc935723fafd5d00edab20709de1303986cc8cbafc387bfb2389af";
    const long size = 63997352;
    var realRelease = new WpfRelease("1.0.128", "Polar uninstaller", "", null,
        "https://github.com/Ayh-m1/Polar-Uninstaller/releases/tag/v1.0.128",
        new[] { "PolarUninstaller.exe" })
    {
        Downloads = new[] { new WpfAsset("PolarUninstaller.exe", url, size, digest) }
    };
    string? downloaded = null;
    try
    {
        downloaded = UpdateDownloadService.DownloadAsync(realRelease, new Progress<DownloadProgress>(_ => { }), CancellationToken.None, "1.0.127", AppContext.BaseDirectory).GetAwaiter().GetResult();
        Check(File.Exists(downloaded) && new FileInfo(downloaded).Length == size,
            "The real published EXE downloads at the expected size");
        Check(UpdateVerificationService.HashEquals(downloaded, digest[7..]),
            "The real published EXE matches the published SHA-256");
        Check(UpdateVerificationService.VerifyStandaloneExecutable(downloaded, "1.0.128", digest[7..], size).IsValid,
            "The real published EXE passes PE and version verification");
    }
    catch (Exception error)
    {
        Check(false, "Real published-asset download failed: " + error.GetType().Name + ": " + error.Message);
    }
    finally
    {
        if (downloaded != null)
        {
            string? directory = Path.GetDirectoryName(downloaded);
            try { if (directory != null) Directory.Delete(directory, true); } catch { }
        }
    }
}
else
{
    Console.WriteLine("SKIP: Real published-asset download (set POLAR_REAL_DOWNLOAD=1 to run)");
}

// === Test 51: Riot Games discovery, dedupe and shared-component protection ===
var riotFixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "polar-riot-" + Guid.NewGuid().ToString("N"));
try
{
    string riotRoot = Path.Combine(riotFixture, "Riot Games");
    string valorant = Path.Combine(riotRoot, "VALORANT");
    string valorantBin = Path.Combine(valorant, "live", "ShooterGame", "Binaries", "Win64");
    Directory.CreateDirectory(valorantBin);
    File.WriteAllText(Path.Combine(valorantBin, "VALORANT-Win64-Shipping.exe"), "x");
    string league = Path.Combine(riotRoot, "League of Legends");
    Directory.CreateDirectory(Path.Combine(league, "live"));
    File.WriteAllText(Path.Combine(league, "live", "LeagueClient.exe"), "x");
    string tft = Path.Combine(riotRoot, "teamfighttactics", "live");
    Directory.CreateDirectory(tft);
    File.WriteAllText(Path.Combine(tft, "TFT.exe"), "x");
    string client = Path.Combine(riotRoot, "Riot Client");
    Directory.CreateDirectory(client);
    File.WriteAllText(Path.Combine(client, "RiotClientServices.exe"), "x");
    string vanguard = Path.Combine(riotRoot, "Riot Vanguard");
    Directory.CreateDirectory(vanguard);
    File.WriteAllText(Path.Combine(vanguard, "vgc.exe"), "x");

    var discovered = RiotGameService.DiscoverDirectoryGames(new[] { riotRoot });
    Console.WriteLine("Riot fixture discovery found " + discovered.Count + " game(s)");
    foreach (var game in discovered) Console.WriteLine("  " + game.Name + " | " + game.Identifier + " | " + game.Location);
    Check(discovered.Count == 2, "Riot discovery lists exactly VALORANT and League of Legends (got " + discovered.Count + ")");
    var foundValorant = discovered.FirstOrDefault(g => g.Identifier == "riot:valorant");
    var foundLeague = discovered.FirstOrDefault(g => g.Identifier == "riot:leagueoflegends");
    Check(foundValorant is not null && foundValorant.Name == "VALORANT", "VALORANT is recognized with its verified product id");
    Check(foundLeague is not null && foundLeague.Name == "League of Legends", "League of Legends is recognized with its verified product id");
    Check(discovered.All(g => g.RequiresOfficialRemoval), "Every Riot game requires its official uninstaller");
    Check(discovered.All(g => g.Source == "Riot Games"), "Riot games report the Riot Games source");
    Check(discovered.All(g => RiotGameService.IsRiotGame(g)), "Every discovered Riot game is classified as a game");
    Check(!discovered.Any(g => g.Name.Contains("Riot Client", StringComparison.OrdinalIgnoreCase)), "Riot Client is never listed as a game");
    Check(!discovered.Any(g => g.Name.Contains("Vanguard", StringComparison.OrdinalIgnoreCase)), "Riot Vanguard is never listed as a game");
    Check(!discovered.Any(g => g.Identifier.Contains("teamfighttactics", StringComparison.OrdinalIgnoreCase)),
        "Teamfight Tactics is not duplicated as a separate game");

    var repeated = RiotGameService.DiscoverDirectoryGames(new[] { riotRoot, riotRoot, valorant });
    Check(repeated.Count == discovered.Count, "Repeated and overlapping Riot roots deduplicate to the same games");
    Check(repeated.Select(g => g.Identifier).Distinct(StringComparer.OrdinalIgnoreCase).Count() == repeated.Count,
        "Riot discovery never returns duplicate product ids");

    Check(RiotGameService.IsProtectedComponentName("Riot Client", client, "Riot Games"), "Riot Client is a protected shared component");
    Check(RiotGameService.IsProtectedComponentName("Riot Vanguard", vanguard, "Riot Games"), "Riot Vanguard is a protected shared component");
    Check(!RiotGameService.IsProtectedComponentName("VALORANT", valorant, "Riot Games"), "A real Riot game is not a protected component");
    Check(RiotGameService.IsRiotRoot(Path.Combine(Path.GetPathRoot(AppContext.BaseDirectory) ?? "C:\\", "Riot Games")),
        "A drive-root Riot Games container is shared");
    Check(RiotGameService.IsSharedPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Riot Games")),
        "The shared Riot Games program-data folder is protected");
    Check(!RiotGameService.IsRiotRoot(riotRoot), "A custom per-game container is not treated as a shared root");

    Check(RiotGameService.ParseProductId("\"C:\\Riot Games\\Riot Client\\RiotClientServices.exe\" --uninstall-product=valorant --uninstall-game=true") == "valorant",
        "The verified --uninstall-product value is parsed");
    Check(RiotGameService.ParseProductId("RiotClientServices.exe") == "", "A command without a product id yields no identifier");
    var parsedSettings = RiotGameService.ParseProductSettings(new[]
    {
        "product_install_full_path: \"C:/Riot Games/VALORANT\"", "# comment", "  nested: ignored", "product_install_root: C:/Riot Games"
    });
    Check(parsedSettings.GetValueOrDefault("product_install_full_path") == "C:/Riot Games/VALORANT" &&
          !parsedSettings.ContainsKey("nested"), "Product settings YAML parsing reads top-level keys only");

    string fakeClient = Path.Combine(client, "RiotClientServices.exe");
    var route = foundValorant is null ? null : RiotGameService.ResolveRoute(foundValorant, fakeClient);
    Check(route is not null && route.Kind == "client" && route.Arguments.Count == 0 && route.ProductId == "valorant",
        "The Riot route opens the client without inventing arguments");
    Check(foundValorant is not null && RiotGameService.ResolveRoute(foundValorant, null) is null,
        "No Riot route is produced without a verified client executable");
    var protectedProgram = new InstalledProgram("Riot Client", "Riot Games", "", client, "", null, "", "Riot Games", "riot:riotclient");
    Check(RiotGameService.IsProtectedComponent(protectedProgram), "A Riot Client program is recognized as protected");
    Check(RiotGameService.ResolveRoute(protectedProgram, fakeClient) is null, "Riot Client never gets a game removal route");
}
catch (Exception ex) { Check(false, "Riot fixture threw: " + ex.GetType().Name + " " + ex.Message); }
finally { try { Directory.Delete(riotFixture, true); } catch { } }

// === Test 52: Verified Registry cleanup, backup and shared-key protection ===
Check(RegistryCleanupService.IsProtectedPath(@"Software\Microsoft\Windows\CurrentVersion\Run"), "Windows Registry roots are protected");
Check(RegistryCleanupService.IsProtectedPath(@"Software\Classes\PolarFixture"), "Registry Classes are protected");
Check(RegistryCleanupService.IsProtectedPath(@"Software\Riot Games\Riot Client"), "Riot Client Registry keys are protected");
Check(RegistryCleanupService.IsProtectedPath(@"Software\Riot Games\Riot Vanguard"), "Riot Vanguard Registry keys are protected");
Check(!RegistryCleanupService.IsProtectedPath(@"Software\PolarFixtureVendor\PolarFixtureApp"), "An application-owned settings key is not protected");

var registryFixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "polar-registry-" + Guid.NewGuid().ToString("N"));
string registryVendor = "PolarFixtureVendor" + Guid.NewGuid().ToString("N")[..8];
string settingsKeyPath = "Software\\" + registryVendor + "\\PolarFixtureApp";
try
{
    Directory.CreateDirectory(registryFixture);
    File.WriteAllText(Path.Combine(registryFixture, "PolarFixtureApp.exe"), "x");
    using (var key = Registry.CurrentUser.CreateSubKey(settingsKeyPath))
    {
        key!.SetValue("InstallPath", registryFixture);
        key.SetValue("Product", "PolarFixtureApp");
    }
    var fixtureProgram = new InstalledProgram("PolarFixtureApp", "PolarFixtureVendor", "1.0", registryFixture,
        Path.Combine(registryFixture, "PolarFixtureApp.exe"), null, "", "Windows", "");
    Check(RegistryCleanupService.CaptureSettings(fixtureProgram with { Location = "" }).Count == 0,
        "Settings keys are never captured without a verified installation path");
    Check(RegistryCleanupService.CaptureSettings(fixtureProgram,
            new[] { fixtureProgram, fixtureProgram with { Location = @"C:\Other\PolarFixtureApp" } }).Count == 0,
        "An ambiguous product name blocks settings-key ownership");

    var settingsTargets = RegistryCleanupService.CaptureSettings(fixtureProgram);
    var settingsTarget = settingsTargets.FirstOrDefault(t => t.Path.Equals(settingsKeyPath, StringComparison.OrdinalIgnoreCase));
    Check(settingsTarget is not null, "A settings key whose values reference the verified installation is captured");
    Check(settingsTarget is not null && settingsTarget.Kind == "Settings" && settingsTarget.Evidence.Length > 0,
        "Captured keys carry a kind and ownership evidence");
    Check(settingsTarget is not null && RegistryCleanupService.Exists(settingsTarget), "The captured key exists before removal");

    string registryBackup = Path.Combine(registryFixture, "backup");
    var registryRemoval = RegistryCleanupService.RemoveAsync(new[] { settingsTarget! }, registryBackup).GetAwaiter().GetResult();
    Check(registryRemoval.Removed == 1 && registryRemoval.Failed == 0, "The verified settings key is removed");
    Check(!RegistryCleanupService.Exists(settingsTarget!), "The removed settings key is verified as gone");
    string[] backupFiles = Directory.Exists(registryBackup) ? Directory.GetFiles(registryBackup, "*.reg") : Array.Empty<string>();
    Check(backupFiles.Length == 1 && new FileInfo(backupFiles[0]).Length > 0,
        "A Registry backup file is written before deletion");

    var sharedTarget = new RegistryCleanupTarget(RegistryHive.CurrentUser, RegistryView.Registry64,
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall", "Settings", "test");
    var sharedRemoval = RegistryCleanupService.RemoveAsync(new[] { sharedTarget }, registryBackup).GetAwaiter().GetResult();
    Check(sharedRemoval.Skipped == 1 && sharedRemoval.Removed == 0, "A shared Windows key is skipped, never deleted");
    Check(RegistryCleanupService.Exists(sharedTarget), "The shared Windows key still exists after a cleanup attempt");
}
catch (Exception ex) { Check(false, "Registry fixture threw: " + ex.GetType().Name + " " + ex.Message); }
finally
{
    try { Registry.CurrentUser.DeleteSubKeyTree(settingsKeyPath, false); } catch { }
    try { Registry.CurrentUser.DeleteSubKeyTree("Software\\" + registryVendor, false); } catch { }
    try { Directory.Delete(registryFixture, true); } catch { }
}

// === Test 53: Four post-uninstall outcomes and bilingual reports ===
var reportProgram = new InstalledProgram("Report App", "Report Publisher", "1.0", @"C:\Program Files\Report App", "", null, "", "Windows", "");
UninstallReport EvaluateReport(UninstallVerification verification, bool executablePresent, string incompleteReason = "") =>
    UninstallReportService.Evaluate(reportProgram, verification, null, Array.Empty<string>(), 10, 0, 2048, "test", executablePresent, incompleteReason);

var reportSuccess = EvaluateReport(new UninstallVerification(false, false, false), false);
Check(reportSuccess.Outcome == UninstallOutcome.SuccessfullyRemoved && reportSuccess.Title(false) == "Successfully Removed" &&
      reportSuccess.Title(true) == "أُزيل بنجاح", "A fully verified removal reports Successfully Removed in both languages");
Check(reportSuccess.Severity == "SUCCESS", "A successful removal uses the SUCCESS severity");

var reportStaleRegistration = EvaluateReport(new UninstallVerification(true, false, false), false);
Check(reportStaleRegistration.Outcome == UninstallOutcome.RemovedWithLeftovers,
    "A stale uninstall registration is reported as Removed with Leftovers");
var reportFolderLeft = EvaluateReport(new UninstallVerification(false, true, true), false);
Check(reportFolderLeft.Outcome == UninstallOutcome.RemovedWithLeftovers,
    "A remaining installation folder is reported as Removed with Leftovers");
var reportStillInstalled = EvaluateReport(new UninstallVerification(true, true, true), false);
Check(reportStillInstalled.Outcome == UninstallOutcome.UninstallFailed && reportStillInstalled.Title(false) == "Uninstall Failed",
    "A registration and folder that still exist report Uninstall Failed");
Check(EvaluateReport(new UninstallVerification(false, false, true), true).Outcome == UninstallOutcome.UninstallFailed,
    "A stray main executable with no registration or folder is reported as Removed with Leftovers");
var reportIncomplete = EvaluateReport(new UninstallVerification(false, false, false, true), false);
Check(reportIncomplete.Outcome == UninstallOutcome.VerificationIncomplete && reportIncomplete.Title(false) == "Verification Incomplete",
    "An unreadable verification reports Verification Incomplete");
Check(EvaluateReport(new UninstallVerification(false, false, false), false, "No verified installation path was captured.").Outcome ==
      UninstallOutcome.VerificationIncomplete, "A missing verification path reports Verification Incomplete");
Check(reportStillInstalled.Severity == "ERROR" && reportStaleRegistration.Severity == "WARNING",
    "Outcome severities are mapped for the Activity Log");

Check(UninstallReportService.FromProbe(reportProgram, new RemovalProbe(true, false, false, "gone"), null, "official").Outcome ==
      UninstallOutcome.SuccessfullyRemoved, "A confirmed launcher probe reports Successfully Removed");
Check(UninstallReportService.FromProbe(reportProgram, new RemovalProbe(false, false, true, "folder"), null, "official").Outcome ==
      UninstallOutcome.RemovedWithLeftovers, "A removed launcher record with a remaining folder reports Removed with Leftovers");
Check(UninstallReportService.FromProbe(reportProgram, new RemovalProbe(false, true, true, "still installed"), null, "official").Outcome ==
      UninstallOutcome.UninstallFailed, "A still-registered game with its folder reports Uninstall Failed");
Check(UninstallReportService.FromProbe(reportProgram, new RemovalProbe(false, false, false, "exe", true), null, "official").Outcome ==
      UninstallOutcome.UninstallFailed, "A remaining main executable reports Uninstall Failed for a launcher probe");
Check(UninstallReportService.FromProbe(reportProgram, new RemovalProbe(false, false, false, "unknown"), null, "official").Outcome ==
      UninstallOutcome.VerificationIncomplete, "An unconfirmed probe without leftovers reports Verification Incomplete");
var reportLeftoverCleanup = new LeftoverCleanupResult(1, 0, 0, 1, 0, 0, 0, 0, 0, 1024,
    new[] { @"C:\ProgramData\Report App" }, Array.Empty<string>(), Array.Empty<string>(),
    new[] { @"C:\ProgramData\Report App" }, Array.Empty<string>());
Check(UninstallReportService.FromProbe(reportProgram, new RemovalProbe(true, false, false, "gone"),
        reportLeftoverCleanup, "official").Outcome == UninstallOutcome.RemovedWithLeftovers,
    "A confirmed launcher removal with verified leftovers reports Removed with Leftovers");
Check(UninstallReportService.FromProbe(reportProgram, new RemovalProbe(true, false, false, "gone"),
        reportLeftoverCleanup with { Failed = 1, RemainingPaths = Array.Empty<string>() }, "official").Outcome ==
      UninstallOutcome.RemovedWithLeftovers, "A confirmed launcher removal with a failed cleanup reports Removed with Leftovers");
Check(UninstallReportService.FromProbe(reportProgram, new RemovalProbe(true, false, false, "gone"),
        reportLeftoverCleanup with { RemainingPaths = Array.Empty<string>() }, "official").Outcome ==
      UninstallOutcome.SuccessfullyRemoved, "A confirmed launcher removal with a fully verified cleanup reports Successfully Removed");
Check(UninstallReportService.FromProbe(reportProgram, new RemovalProbe(true, false, false, "gone"), null,
        "official", cleanupUnverified: true).Outcome == UninstallOutcome.VerificationIncomplete,
    "A confirmed launcher removal without leftover verification reports Verification Incomplete");

var reportEnglish = reportSuccess.LogLines(false);
var reportArabic = reportSuccess.LogLines(true);
Check(reportEnglish.Count > 0 && reportEnglish.All(line => !line.StartsWith("\u200F", StringComparison.Ordinal)),
    "English report lines are left-to-right");
Check(reportArabic.Count == reportEnglish.Count && reportArabic.All(line => line.StartsWith("\u200F", StringComparison.Ordinal)),
    "Arabic report lines are right-to-left marked");
Check(reportEnglish.Any(line => line.Contains("Result: Successfully Removed")) && reportArabic.Any(line => line.Contains("النتيجة")),
    "The report states the outcome in both languages");
Check(reportEnglish.Any(line => line.Contains("Removed — files:")) && reportEnglish.Any(line => line.Contains("Reclaimed:")),
    "The report lists removed counts and the reclaimed size");
Check(reportEnglish.Any(line => line.Contains("State — registration:")) && reportArabic.Any(line => line.Contains("الحالة")),
    "The report states the verification evidence in both languages");

var reportWithLeftovers = UninstallReportService.Evaluate(reportProgram, new UninstallVerification(true, false, false), null,
    new[] { @"C:\ProgramData\Report App", @"C:\Users\Public\Report App.lnk" }, 4, 1, 512, "test");
Check(reportWithLeftovers.RemainingPaths.Count == 2 &&
      reportWithLeftovers.LogLines(false).Any(line => line.Contains("Remaining leftovers (2)")),
    "Remaining leftovers are listed in the report");
Check(reportWithLeftovers.Summary(false).Contains("Removed with Leftovers") && reportWithLeftovers.Summary(true).Contains("بقايا"),
    "The status summary names leftovers in both languages");

// === Test 54: Exact uninstall-registration identity, including Registry-only entries ===
string registrationName = "PolarFixture Registration " + Guid.NewGuid().ToString("N")[..8];
string registrationKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\" + registrationName;
string registrationBackup = Path.Combine(AppContext.BaseDirectory, "fixtures", "polar-regbackup-" + Guid.NewGuid().ToString("N"));
try
{
    using (var key = Registry.CurrentUser.CreateSubKey(registrationKey))
    {
        key!.SetValue("DisplayName", registrationName);
        key.SetValue("UninstallString", "\"C:\\Program Files\\PolarFixture\\unins000.exe\"");
    }
    var registrationService = new LeftoverService(registrationBackup);
    var registryOnlyProgram = new InstalledProgram(registrationName, "PolarFixture", "", "", "", null, "\"C:\\Program Files\\PolarFixture\\unins000.exe\"", "Windows", "");
    var capturedRegistrations = registrationService.CaptureAsync(registryOnlyProgram).GetAwaiter().GetResult();
    Check(capturedRegistrations.Count == 1 && capturedRegistrations[0].Name == registrationName,
        "A Registry-only registration is captured by its exact identity without an install location");
    Check(registrationService.CaptureAsync(new InstalledProgram("PolarFixture Registration Other", "PolarFixture", "", "", "", null, "", "Windows", ""))
        .GetAwaiter().GetResult().Count == 0, "A different display name never matches an existing registration");
    Check(registrationService.CaptureAsync(registryOnlyProgram with { Location = @"C:\Other\PolarFixture" })
        .GetAwaiter().GetResult().Count == 0, "A mismatched installation path rejects an otherwise equal display name");
    Check(LeftoverService.RegistrationExists(registryOnlyProgram), "The registration is detected while it exists");
    Check(LeftoverService.RegistrationExists(capturedRegistrations[0]), "The captured registration is verified as present");

    var registrationRemoval = registrationService.RemoveAsync(new LeftoverReport(Array.Empty<string>(), capturedRegistrations))
        .GetAwaiter().GetResult();
    Check(registrationRemoval.Any(message => message.StartsWith("Removed exact registration", StringComparison.Ordinal)),
        "The exact registration is removed with a backup");
    Check(!LeftoverService.RegistrationExists(capturedRegistrations[0]) && !LeftoverService.RegistrationExists(registryOnlyProgram),
        "The removed registration is verified as gone");
}
catch (Exception ex) { Check(false, "Registration fixture threw: " + ex.GetType().Name + " " + ex.Message); }
finally
{
    try { Registry.CurrentUser.DeleteSubKeyTree(registrationKey, false); } catch { }
    try { Directory.Delete(registrationBackup, true); } catch { }
}

// === Test 55: Deep Uninstaller wiring for Riot, verification and reporting ===
var deepCodeFinal = File.ReadAllText(Path.Combine(root, "DeepUninstallerView.xaml.cs"), Encoding.UTF8);
var windowsCodeFinal = File.ReadAllText(Path.Combine(root, "Services", "WindowsInventoryService.cs"), Encoding.UTF8);
var launcherCodeFinal = File.ReadAllText(Path.Combine(root, "Services", "LauncherInventoryService.cs"), Encoding.UTF8);
var routeCodeFinal = File.ReadAllText(Path.Combine(root, "Services", "LauncherRouteService.cs"), Encoding.UTF8);
var reportCodeFinal = File.ReadAllText(Path.Combine(root, "Services", "UninstallReportService.cs"), Encoding.UTF8);
Check(windowsCodeFinal.Contains("RiotGameService.ParseProductId") && windowsCodeFinal.Contains("RiotGameService.IsProtectedComponentName"),
    "Windows inventory assigns Riot product identifiers and protects shared components");
Check(launcherCodeFinal.Contains("RiotGameService.Discover"), "Launcher inventory uses verified Riot discovery");
Check(routeCodeFinal.Contains("RiotGameService.FindClientExecutable"), "The launcher route resolves the verified Riot Client");
Check(deepCodeFinal.Contains("RiotGameService.IsRiotGame") && deepCodeFinal.Contains("RiotGameService.InstalledGameCount"),
    "Deep Uninstaller recognizes Riot games and warns before removing shared components");
Check(deepCodeFinal.Contains("UninstallReportService.Evaluate") && deepCodeFinal.Contains("UninstallReportService.FromProbe") &&
      deepCodeFinal.Contains("report.LogLines"), "Both removal paths write the detailed bilingual report to the Activity Log");
Check(deepCodeFinal.Contains("SetStatus(report.Summary)"), "The status bar shows the verified outcome summary");
Check(reportCodeFinal.Contains("Successfully Removed") && reportCodeFinal.Contains("Removed with Leftovers") &&
      reportCodeFinal.Contains("Verification Incomplete") && reportCodeFinal.Contains("Uninstall Failed"),
    "All four outcome names exist in the report service");

// === Test 55: Roblox Player is classified as a game, Roblox Studio is not ===
var robloxPlayer = new InstalledProgram("Roblox Player for qmc", "Roblox Corporation", "1.0",
    @"C:\Users\qmc\AppData\Local\Roblox\Versions\version-4ea8e85481e34f46",
    @"C:\Users\qmc\AppData\Local\Roblox\Versions\version-4ea8e85481e34f46\RobloxPlayerInstaller.exe,0", null,
    "\"C:\\Users\\qmc\\AppData\\Local\\Roblox\\Versions\\version-4ea8e85481e34f46\\RobloxPlayerInstaller.exe\" -uninstall",
    "Windows", "");
var robloxStudio = new InstalledProgram("Roblox Studio for qmc", "Roblox Corporation", "1.0",
    @"C:\Users\qmc\AppData\Local\Roblox\Versions\version-ffd2994ae3bd41ab",
    @"C:\Users\qmc\AppData\Local\Roblox\Versions\version-ffd2994ae3bd41ab\RobloxStudioInstaller.exe,0", null,
    "\"C:\\Users\\qmc\\AppData\\Local\\Roblox\\Versions\\version-ffd2994ae3bd41ab\\RobloxStudioInstaller.exe\" -uninstall",
    "Windows", "");
Check(GameClassificationService.IsGame(robloxPlayer), "Roblox Player is classified as a game");
Check(!GameClassificationService.IsGame(robloxStudio), "Roblox Studio is not classified as a game");
Check(GameClassificationService.IsGame(robloxPlayer) && !string.IsNullOrWhiteSpace(robloxPlayer.Uninstall),
    "Roblox Player has a verified official uninstall command");
Check(!GameClassificationService.IsGame(new InstalledProgram("Notepad", "Microsoft", "1.0", @"C:\Windows", "", null, "", "Windows", "")),
    "An ordinary Windows application is not classified as a game");

// Verify the Deep Uninstaller wires the classification through GameClassificationService.
string deepCodeV134 = File.ReadAllText(Path.Combine(root, "DeepUninstallerView.xaml.cs"));
Check(deepCodeV134.Contains("GameClassificationService.IsGame(program)"),
    "Deep Uninstaller delegates game classification to GameClassificationService");
Check(deepCodeV134.Contains("IsGame(game.Program)"),
    "Games Uninstall is enabled through the shared game classification");

// === Test 56: Epic Games Store discovery (read-only, real environment) ===
try
{
    var epicGames = new LauncherInventoryService().GetInstalledAsync(System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    var rocket = epicGames.FirstOrDefault(p => p.Name.Contains("Rocket", StringComparison.OrdinalIgnoreCase));
    if (rocket == null)
    {
        Console.WriteLine("SKIP: Rocket League not installed in this environment (read-only discovery)");
    }
    else
    {
        Check(true, "Epic discovery finds Rocket League (real environment)");
        Check(rocket.Source == "Epic Games", "Rocket League is sourced from Epic Games");
        Check(rocket.Identifier.StartsWith("epic:", StringComparison.OrdinalIgnoreCase), "Rocket League has an Epic identifier");
        Check(GameClassificationService.IsGame(rocket), "Rocket League is classified as a game");
        Check(!string.IsNullOrWhiteSpace(rocket.Location) && System.IO.Directory.Exists(rocket.Location),
            "Rocket League has a verified installation directory");
    }
}
catch (Exception ex) { Check(false, "Epic discovery threw: " + ex.GetType().Name); }

Console.WriteLine();

// === v1.0.138 removal engine: disposable files/Registry only ===
string engineFixture=Path.Combine(AppContext.BaseDirectory,"fixtures","polar-engine-"+Guid.NewGuid().ToString("N"));
string engineApp=Path.Combine(engineFixture,"Application");
string engineKey="Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\PolarEngineFixture"+Guid.NewGuid().ToString("N");
try
{
    Directory.CreateDirectory(engineApp);
    string primary=Path.Combine(engineApp,"PolarEngineFixture.exe");
    string locked=Path.Combine(engineApp,"locked.bin");
    File.WriteAllText(primary,"fixture executable");
    File.WriteAllText(locked,"fixture lock");
    File.WriteAllText(Path.Combine(engineApp,"ordinary.tmp"),"ordinary");
    var program=new InstalledProgram("PolarEngineFixture","PolarTest","1.0",engineApp,primary,
        Uninstall:"\""+Path.Combine(engineApp,"uninstall.exe")+"\"");
    using(var key=Registry.CurrentUser.CreateSubKey(engineKey))
    {key.SetValue("DisplayName",program.Name);key.SetValue("InstallLocation",engineApp);key.SetValue("UninstallString",program.Uninstall);}
    var scanner=new SharedLeftoverScanner(new LeftoverService(Path.Combine(engineFixture,"RegistryBackups")));
    var engine=new RemovalEngine(scanner);
    var before=await scanner.CaptureAsync(program,CancellationToken.None);
    Check(before.Registry.Count>0&&before.Executables.Contains(primary),"Removal identity is captured before the first deletion");
    var plan=await engine.PlanAsync(before,new[]{program},true,CancellationToken.None);
    Check(plan.Files.Count==3&&File.Exists(primary),"Building a removal plan is read-only");
    using(var hold=new FileStream(locked,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
    {
        var first=await engine.ExecuteAsync(plan,new[]{program},null,CancellationToken.None);
        Check(!File.Exists(primary)&&!File.Exists(Path.Combine(engineApp,"ordinary.tmp"))&&File.Exists(locked),
            "One locked file does not stop deletion of the other planned files");
        Check(first.Cleanup.Failed>0&&first.Remaining.Any(c=>c.Path==locked),
            "Locked owned leftovers remain eligible for a second-stage retry");
        Check(first.Cleanup.Messages.Any(m=>m.Contains("locked by a process",StringComparison.Ordinal)),
            "The locked-file reason is retained per item");
    }
    var second=await engine.ExecuteAsync(plan,new[]{program},null,CancellationToken.None);
    Check(!Directory.Exists(engineApp)&&!LeftoverService.RegistrationExists(program),
        "Advanced retry uses the original plan to remove remaining files and exact registration");
    Check(second.Remaining.Count==0&&second.Cleanup.FilesRemoved==1,"Retry counts only files actually deleted in this pass");
    Check(Directory.GetFiles(Path.Combine(engineFixture,"RegistryBackups"),"*.reg").Length>0,
        "Direct registry removal produces a real backup before deletion");
    var third=await engine.ExecuteAsync(plan,Array.Empty<InstalledProgram>(),null,CancellationToken.None);
    Check(third.Cleanup.FilesRemoved==0&&third.Remaining.Count==0,"Already deleted targets are safe and idempotent");

    Directory.CreateDirectory(engineApp);File.WriteAllText(primary,"official fixture");
    File.WriteAllText(Path.Combine(engineApp,"retained.log"),"owned log");
    var officialBefore=await scanner.CaptureAsync(program,CancellationToken.None);
    var officialPlan=await engine.PlanAsync(officialBefore,new[]{program},false,CancellationToken.None);
    File.Delete(primary); // Controlled fixture simulates the third-party uninstaller.
    var probe=UninstallMonitorService.Probe(program,officialBefore);
    Check(probe.Confirmed&&probe.InstallationFolderPresent,"Official verification distinguishes retained data from the removed primary executable");
    var detected=await engine.InspectAsync(officialPlan,Array.Empty<InstalledProgram>(),CancellationToken.None);
    Check(detected.Count>0&&File.Exists(Path.Combine(engineApp,"retained.log")),
        "Official completion inspection detects leftovers WITHOUT deleting them");
    var officialRetry=await engine.ExecuteAsync(officialPlan,Array.Empty<InstalledProgram>(),null,CancellationToken.None);
    Check(!Directory.Exists(engineApp)&&officialRetry.Remaining.Count==0,"Explicit advanced cleanup works after official fixture removal");

    Directory.CreateDirectory(engineApp);File.WriteAllText(primary,"initial");
    var changedBefore=await scanner.CaptureAsync(program,CancellationToken.None);
    var changedPlan=await engine.PlanAsync(changedBefore,new[]{program},true,CancellationToken.None);
    File.WriteAllText(primary,"replacement belonging to a changed installation");
    var changedResult=await engine.ExecuteAsync(changedPlan,new[]{program},null,CancellationToken.None);
    Check(File.Exists(primary)&&changedResult.Cleanup.Skipped>0,"A file replaced since planning is retained");
    bool blocked=false;
    try{await engine.ExecuteAsync(changedPlan,new[]{program,new InstalledProgram("Other product","Other","1",Path.Combine(engineApp,"Shared"))},null,CancellationToken.None);}
    catch(IOException){blocked=true;}
    Check(blocked&&File.Exists(primary),"Revalidation blocks an installation newly shared by another product");
    bool cancelled=false;using(var cancel=new CancellationTokenSource()){cancel.Cancel();try{await engine.ExecuteAsync(changedPlan,new[]{program},null,cancel.Token);}catch(OperationCanceledException){cancelled=true;}}
    Check(cancelled&&File.Exists(primary),"Cancellation never starts a pending deletion");

    File.WriteAllText(primary,"readonly");
    var readonlyPlan=await engine.PlanAsync(await scanner.CaptureAsync(program,CancellationToken.None),new[]{program},true,CancellationToken.None);
    File.SetAttributes(primary,FileAttributes.ReadOnly);
    try
    {
        var permissionResult=await engine.ExecuteAsync(readonlyPlan,new[]{program},null,CancellationToken.None);
        Check(File.Exists(primary)&&permissionResult.Cleanup.Messages.Any(m=>m.Contains("access denied",StringComparison.Ordinal)),
            "Permission failure is reported without changing ACLs or taking ownership");
    }
    finally{File.SetAttributes(primary,FileAttributes.Normal);}
    foreach(string unsafeRoot in new[]{Path.GetPathRoot(engineApp)!,Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles)})
    {
        bool protectedRoot=false;try{DirectRemovalService.ValidateRoot(unsafeRoot);}catch(IOException){protectedRoot=true;}
        Check(protectedRoot,"Protected root is blocked: "+unsafeRoot);
    }
    var managed=await engine.PlanAsync(await scanner.CaptureAsync(program with{RequiresOfficialRemoval=true},CancellationToken.None),
        new[]{program},false,CancellationToken.None);
    Check(managed.Root==null&&managed.Files.Count==0,"Advanced cleanup does not bypass Windows-managed direct-removal protections");

    using(var key=Registry.CurrentUser.CreateSubKey(engineKey))
    {key.SetValue("DisplayName",program.Name);key.SetValue("InstallLocation",engineApp);key.SetValue("UninstallString",program.Uninstall);key.SetValue("DisplayVersion","1.0");}
    var capturedForBackup=await scanner.CaptureAsync(program,CancellationToken.None);
    string blockedBackup=Path.Combine(engineFixture,"NotADirectory");File.WriteAllText(blockedBackup,"fixture");
    var backupFailure=await new LeftoverService(blockedBackup).RemoveAsync(new LeftoverReport(Array.Empty<string>(),capturedForBackup.Registry));
    Check(LeftoverService.RegistrationExists(program)&&backupFailure.Any(m=>m.StartsWith("Skipped registration",StringComparison.Ordinal)),
        "A failed Registry backup prevents deletion of the registration");
    using(var key=Registry.CurrentUser.OpenSubKey(engineKey,true)){key!.SetValue("DisplayVersion","2.0");}
    await new LeftoverService(Path.Combine(engineFixture,"UpgradeBackups")).RemoveAsync(new LeftoverReport(Array.Empty<string>(),capturedForBackup.Registry));
    Check(LeftoverService.RegistrationExists(program),"A newly upgraded registration is not deleted using an older capture");
    Check((await scanner.CaptureAsync(program with{Location="",Uninstall=""},CancellationToken.None)).Registry.Count==0,
        "An application name alone never authorizes Registry removal");

    // A changed settings key must not be removed based on stale ownership.
    string settingsPath="Software\\PolarEngineSettings"+Guid.NewGuid().ToString("N");
    try
    {
        using(var key=Registry.CurrentUser.CreateSubKey(settingsPath)){key.SetValue("InstallPath",engineApp);}
        var settingsProgram=program with{Name=settingsPath["Software\\".Length..]};
        var target=RegistryCleanupService.CaptureSettings(settingsProgram).First(t=>t.Path.Equals(settingsPath,StringComparison.OrdinalIgnoreCase));
        using(var key=Registry.CurrentUser.OpenSubKey(settingsPath,true)){key!.SetValue("InstallPath",@"C:\UnrelatedApplication");}
        var rejected=await RegistryCleanupService.RemoveAsync(new[]{target},Path.Combine(engineFixture,"SettingsBackup"));
        Check(rejected.Removed==0&&RegistryCleanupService.Exists(target),"Changed Registry ownership is revalidated and retained");
    }
    finally{Registry.CurrentUser.DeleteSubKeyTree(settingsPath,false);}
}
catch(Exception ex){Check(false,"Removal engine fixture: "+ex);}
finally
{
    Registry.CurrentUser.DeleteSubKeyTree(engineKey,false);
    if(Directory.Exists(engineFixture))Directory.Delete(engineFixture,true);
}



// === Integrated duplicate scanner and safe per-group cleanup ===
string duplicatesFixture=Path.Combine(root,"Build","TestData","duplicates-"+Guid.NewGuid().ToString("N"));
try
{
    string scope=Path.Combine(duplicatesFixture,"scan");
    Directory.CreateDirectory(Path.Combine(scope,"nested"));
    string original=Path.Combine(scope,"original.txt");
    string copy=Path.Combine(scope,"nested","different-name.txt");
    File.WriteAllText(original,"identical content");File.Copy(original,copy);
    File.WriteAllText(Path.Combine(scope,"same.txt"),"AAAA");File.WriteAllText(Path.Combine(scope,"nested","same.txt"),"BBBB");
    File.WriteAllText(Path.Combine(scope,"empty-a.txt"),"");File.WriteAllText(Path.Combine(scope,"nested","empty-b.txt"),"");
    var scanner=new DuplicateScanService();
    var scan=await scanner.ScanAsync(scope,null,CancellationToken.None);
    Check(scan.Root==scope&&scan.Scanned==6,"Duplicate scan is scoped recursively to the selected physical folder");
    Check(scan.Groups.Count==2,"Identical content and empty files are grouped; equal names with different contents are not");
    var contentGroup=scan.Groups.Single(g=>g.Size>0);
    Check(contentGroup.Files.Select(f=>Path.GetFileName(f.Path)).Distinct().Count()==2,"Different file names do not prevent content-based duplicate detection");
    Check(contentGroup.Hash.Length==64,"Duplicate membership is verified by SHA-256");
    Check(scan.Groups.All(g=>!g.Files.Any(f=>Path.GetFileName(f.Path)=="same.txt")),"Same-name different-content files never enter the same duplicate group");

    bool wasCancelled=false;using(var cts=new CancellationTokenSource())
    {cts.Cancel();try{await scanner.ScanAsync(scope,null,cts.Token);}catch(OperationCanceledException){wasCancelled=true;}}
    Check(wasCancelled,"Duplicate scanning honors cancellation");

    var cleanup=new DuplicateCleanupService();
    var selected=contentGroup.Files.Last();
    DateTime timestamp=File.GetLastWriteTimeUtc(selected.Path);
    File.WriteAllText(selected.Path,"changed!! content");File.SetLastWriteTimeUtc(selected.Path,timestamp);
    var changed=await cleanup.CleanAsync(scan,new[]{selected.Path},true,Array.Empty<string>(),null,CancellationToken.None);
    Check(File.Exists(selected.Path)&&changed.Items.All(i=>!i.Removed),"Changed content is retained even if size/timestamp are reused");
    File.WriteAllText(selected.Path,"identical content");File.SetLastWriteTimeUtc(selected.Path,timestamp);

    using(var hold=new FileStream(selected.Path,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
    {
        var lockedScan=await scanner.ScanAsync(scope,null,CancellationToken.None);
        Check(lockedScan.Skipped>0&&lockedScan.Groups.All(g=>g.Files.All(f=>f.Path!=selected.Path)),"Scanning skips locked files without invalid duplicate matches");
        var locked=await cleanup.CleanAsync(scan,new[]{selected.Path},true,Array.Empty<string>(),null,CancellationToken.None);
        Check(locked.Items.All(i=>!i.Removed)&&File.Exists(selected.Path),"A locked duplicate is skipped without crashing");
    }
    string outside=Path.Combine(duplicatesFixture,"outside.txt");File.WriteAllText(outside,"identical content");
    var unknown=await cleanup.CleanAsync(scan,new[]{outside},true,Array.Empty<string>(),null,CancellationToken.None);
    Check(File.Exists(outside)&&unknown.Items.Single().Code=="not_scanned","Unscanned selected paths cannot be deleted");
    var outsider=new DuplicateFile(outside,new FileInfo(outside).Length,File.GetLastWriteTimeUtc(outside));
    var injected=scan with{Groups=new[]{contentGroup with{Files=contentGroup.Files.Append(outsider).ToArray()}}};
    var scoped=await cleanup.CleanAsync(injected,new[]{outside},true,Array.Empty<string>(),null,CancellationToken.None);
    Check(File.Exists(outside)&&scoped.Items.Single().Code=="protected","Even an injected group member outside the scan scope is protected");

    var protectedCopy=await cleanup.CleanAsync(scan,new[]{copy},true,new[]{Path.GetDirectoryName(copy)!},null,CancellationToken.None);
    Check(File.Exists(copy)&&protectedCopy.Items.Single().Code=="protected","Custom application installation roots are protected");
    var allSelected=await cleanup.CleanAsync(scan,scan.Groups.SelectMany(g=>g.Files).Select(f=>f.Path),true,Array.Empty<string>(),null,CancellationToken.None);
    Check(allSelected.Items.Count(i=>i.Removed)==2&&allSelected.Items.Count(i=>i.Code=="last_copy")==2,
        "Selecting every duplicate preserves one verified copy per group");
    Check(contentGroup.Files.Count(f=>File.Exists(f.Path))==1,"A real duplicate deletion retains an original copy");
    Check(allSelected.Reclaimed==contentGroup.Size,"Cleanup reports the logical bytes actually removed");
    Check(allSelected.Groups.Count==0,"Groups with only one remaining copy leave the results");

    string surviving=contentGroup.Files.Single(f=>File.Exists(f.Path)).Path;
    var stale=await cleanup.CleanAsync(scan,new[]{surviving},true,Array.Empty<string>(),null,CancellationToken.None);
    Check(File.Exists(surviving)&&stale.Items.Any(i=>i.Code=="last_copy"),"Missing/stale sibling copies never authorize deleting the last valid copy");

    string binary=Path.Combine(scope,"owned.exe");File.Copy(surviving,binary);
    var binaryScan=await scanner.ScanAsync(scope,null,CancellationToken.None);
    var protectedBinary=await cleanup.CleanAsync(binaryScan,new[]{binary},true,Array.Empty<string>(),null,CancellationToken.None);
    Check(File.Exists(binary)&&protectedBinary.Items.Single().Code=="protected","Application binaries are not duplicate-cleanup targets");
    string largeA=Path.Combine(scope,"large-a.dat"),largeB=Path.Combine(scope,"nested","large-b.dat");
    await File.WriteAllBytesAsync(largeA,new byte[1572864]);File.Copy(largeA,largeB);
    var largeScan=await scanner.ScanAsync(scope,null,CancellationToken.None);
    Check(largeScan.Groups.Any(g=>g.Size==1572864&&g.Files.Count==2),"Files larger than the streaming buffer are compared correctly");
    bool missingScopeRejected=false;
    try{await cleanup.CleanAsync(scan with{Root=""},new[]{surviving},true,Array.Empty<string>(),null,CancellationToken.None);}
    catch(ArgumentException){missingScopeRejected=true;}
    Check(missingScopeRejected&&File.Exists(surviving),"Cleanup requires the original physical scan scope");
}
catch(Exception ex){Check(false,"Duplicate fixture: "+ex);}
finally
{
    string allowed=Path.GetFullPath(Path.Combine(root,"Build","TestData"))+Path.DirectorySeparatorChar;
    if(!Path.GetFullPath(duplicatesFixture).StartsWith(allowed,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Unsafe fixture cleanup path");
    if(Directory.Exists(duplicatesFixture))Directory.Delete(duplicatesFixture,true);
}


if (failures.Count == 0)
{
    Console.WriteLine("ALL REGRESSION TESTS PASSED");
    return 0;
}
Console.WriteLine(failures.Count + " TEST(S) FAILED:");
foreach (var f in failures) Console.WriteLine("  - " + f);
return 1;

internal sealed class StaticJsonHandler : HttpMessageHandler
{
    private readonly string json;
    public StaticJsonHandler(string json) => this.json = json;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
}

/// <summary>
/// Minimal loopback HTTP server used by the release-fixture tests. It avoids
/// HttpListener URL reservations so the regression suite can run unelevated.
/// </summary>
internal sealed class FixtureServer : IDisposable
{
    private readonly TcpListener listener;
    private readonly Dictionary<string, (string ContentType, byte[] Body)> routes = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource stop = new();
    private readonly Task loop;

    public int Port { get; }

    public FixtureServer()
    {
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        loop = Task.Run(AcceptAsync);
    }

    public void AddRoute(string path, string contentType, byte[] body) => routes[path] = (contentType, body);

    private async Task AcceptAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(); }
            catch { break; }
            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        try
        {
            using (client)
            using (var stream = client.GetStream())
            {
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, leaveOpen: true);
                string? requestLine = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(requestLine)) return;
                string[] parts = requestLine.Split(' ');
                string path = parts.Length > 1 ? parts[1] : "/";
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
                if (!routes.TryGetValue(path, out var route))
                {
                    await WriteResponseAsync(stream, "text/plain", Encoding.UTF8.GetBytes("not found"));
                    return;
                }
                await WriteResponseAsync(stream, route.ContentType, route.Body);
            }
        }
        catch { }
    }

    private static async Task WriteResponseAsync(Stream stream, string contentType, byte[] body)
    {
        byte[] header = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: " + contentType + "\r\n" +
            "Content-Length: " + body.Length + "\r\n" +
            "Connection: close\r\n\r\n");
        await stream.WriteAsync(header, 0, header.Length);
        await stream.WriteAsync(body, 0, body.Length);
        await stream.FlushAsync();
    }

    public void Dispose()
    {
        stop.Cancel();
        try { listener.Stop(); } catch { }
        try { loop.Wait(2000); } catch { }
        stop.Dispose();
    }

    public static string ReleaseJson(string version, string assetName, long size, string hash, string? url)
    {
        string download = url ?? $"https://github.com/Ayh-m1/Polar-Uninstaller/releases/download/v{version}/{assetName}";
        return "[{\"tag_name\":\"v" + version + "\",\"name\":\"Polar Uninstaller " + version + "\",\"body\":\"fixture\",\"draft\":false,\"prerelease\":false,\"published_at\":\"2026-11-09T00:00:00Z\",\"html_url\":\"https://github.com/Ayh-m1/Polar-Uninstaller/releases/tag/v" + version + "\",\"assets\":[{\"name\":\"" + assetName + "\",\"browser_download_url\":\"" + download + "\",\"size\":" + size + ",\"digest\":\"sha256:" + hash + "\"}]}]";
    }
}
