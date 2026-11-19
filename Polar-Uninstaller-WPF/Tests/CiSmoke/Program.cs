using System.IO;
using Microsoft.Win32;
using PolarUninstallerWpf.Services;

// All filesystem changes are confined to this disposable fixture.
string root = Path.Combine(Path.GetTempPath(), "Polar-CI-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int checks = 0;
void Check(bool value, string label)
{
    if (!value) throw new InvalidOperationException(label);
    checks++;
    Console.WriteLine("PASS: " + label);
}
try
{
    var settings = new SettingsService(Path.Combine(root, "settings"));
    settings.Save(new AppSettings(Arabic: true, SelectionTipDismissed: true));
    Check(settings.Load() is { Arabic: true, SelectionTipDismissed: true }, "Selection preference round trip");
    Check(DisplayFormat.Directional("1 / 2", false) == "1 / 2" &&
          DisplayFormat.Directional("1 / 2", true) == "\u20661 / 2\u2069", "RTL isolation");

    string data = Path.Combine(root, "data"), nested = Path.Combine(data, "child", "nested");
    Directory.CreateDirectory(nested);
    string first = Path.Combine(data, "first.bin"), second = Path.Combine(nested, "second.bin");
    File.WriteAllBytes(first, new byte[100]); File.WriteAllBytes(second, new byte[200]);
    DashboardStorageService.UseCacheDirectory(Path.Combine(root, "cache"));
    var scan = await new DashboardStorageService().ScanAsync(data, CancellationToken.None);
    Check(scan.TotalBytes == 300 && scan.FilesScanned == 2 && scan.IndexComplete && scan.FileIndex.Count == 2,
        "Storage scan captures a complete file index");
    Check(scan.FolderIndex.Single(f => f.Path == data) is { Bytes: 300, Files: 2, Folders: 3 },
        "Folder aggregates include nested files exactly once");
    var batch = new FileChangeBatch(Guid.NewGuid(), DateTimeOffset.UtcNow, FileChangeKind.Deleted,
        new[] { new FileChange(second, true, 200) });
    var updated = StorageCacheUpdates.Apply(scan, batch);
    Check(updated.TotalBytes == 100 && updated.FilesScanned == 1 && updated.FileIndex.Count == 1,
        "Deletion updates cached totals and indexes");
    Check(StorageCacheUpdates.Apply(updated, batch) == updated, "Repeated notifications are idempotent");
    await DashboardStorageService.FlushCacheAsync();
    DashboardStorageService.UseCacheDirectory(Path.Combine(root, "cache"));
    await DashboardStorageService.LoadCacheAsync();
    Check(DashboardStorageService.TryGetCached(data)?.FileIndex.Count == 2, "Disk cache preserves indexes");

    var registration = new ApplicationRegistration(RegistryHive.CurrentUser, RegistryView.Registry64, @"Software\PolarCiFixture");
    var program = new InstalledProgram("Fixture", "Polar CI", "1", data, first)
        { RegisteredIcon = first, Registration = registration };
    var merged = InventoryMerger.Merge(new[] { program }).Single();
    Check(merged.Registration == registration && merged.RegisteredIcon == first, "Inventory merge preserves icon ownership metadata");
    var inventory = new ApplicationInventoryService(_ => Task.FromResult<IReadOnlyList<InstalledProgram>>(new[] { program }));
    await inventory.RefreshAsync(); inventory.NotifyRemoved(program);
    Check(inventory.Current?.Count == 0, "Verified removal updates shared inventory");

    var duplicates = Path.Combine(root, "duplicates"); Directory.CreateDirectory(duplicates);
    File.WriteAllText(Path.Combine(duplicates, "a.txt"), "identical content");
    File.WriteAllText(Path.Combine(duplicates, "b.txt"), "identical content");
    var result = await new DuplicateScanService().ScanAsync(duplicates, null, CancellationToken.None);
    Check(result.Groups.Count == 1 && result.Groups[0].Files.Count == 2 && result.Groups[0].Hash.Length == 64,
        "Duplicate detection verifies file contents");
    Console.WriteLine($"All {checks} CI regression checks passed.");
    return 0;
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }
finally { Directory.Delete(root, true); }
