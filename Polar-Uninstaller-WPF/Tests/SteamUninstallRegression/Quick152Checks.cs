using System.IO;
using PolarUninstallerWpf.Services;

internal static class Quick152Checks
{
    internal static async Task<int> RunAsync()
    {
        string fixtureParent = Directory.GetParent(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory))!.FullName;
        string root = Directory.CreateDirectory(Path.Combine(fixtureParent, "quick152-" + Guid.NewGuid().ToString("N"))).FullName;
        int checks = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            checks++;
            Console.WriteLine("PASS: " + label);
        }
        var defaults = CleanerSelection.Parse(null);
        Check(defaults.SequenceEqual([TempCleanerService.Category.UserTemp, TempCleanerService.Category.SystemTemp]) &&
            Enum.GetValues<TempCleanerService.Category>().Length == 6, "Exactly six categories, with only the previous two selected by default");
        Check(CleanerSelection.Parse(["ShaderCache", "BrokenShortcuts", "RamCache"]).SequenceEqual(
            [TempCleanerService.Category.BrokenShortcuts, TempCleanerService.Category.RamCache]),
            "Saved shader choices are ignored while supported selections are restored");
        long standby = await StandbyMemoryCleaner.ScanAsync(default);
        Check(standby >= 0 && standby % Environment.SystemPageSize == 0, "Windows reports aligned standby memory without purging it");
        Check(new StandbyMemoryCleaner.Result(1000, 700).Released == 300 && new StandbyMemoryCleaner.Result(1000, 1200).Released == 0,
            "RAM delta never becomes negative if Windows refills the cache");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            var attempted = StandbyMemoryCleaner.ReleaseAsync(cancelled.Token);
            try { await attempted; } catch (OperationCanceledException) { }
            Check(attempted.IsCanceled, "Cancelled standby release never enters the native purge operation");
        }

        string desktop = Directory.CreateDirectory(Path.Combine(root, "سطح المكتب")).FullName;
        string installation = Path.Combine(root, "Former Application");
        string target = Path.Combine(installation, "app.exe");
        string owned = Path.Combine(desktop, "owned.url");
        string unrelated = Path.Combine(desktop, "orphan.url");
        string steam = Path.Combine(desktop, "game.url");
        File.WriteAllText(owned, "[InternetShortcut]\r\nURL=" + new Uri(target).AbsoluteUri);
        File.WriteAllText(unrelated, "[InternetShortcut]\r\nURL=" + new Uri(Path.Combine(root, "unrecorded", "app.exe")).AbsoluteUri);
        File.WriteAllText(steam, "[InternetShortcut]\r\nURL=steam://rungameid/123");
        UninstallHistoryEntry[] history = [new() { Name = "Former Application", Publisher = "Fixture", Location = installation }];
        var inventory = new ApplicationInventory([], true, 1, DateTimeOffset.Now);
        var found = await BrokenShortcutCleaner.ScanAsync(history, inventory, default, [desktop]);
        Check(found.Count == 1 && found[0].Link.Path == owned, "Shortcut discovery requires a verified historical owner and ignores a missing unrelated target and Steam");
        var conflictingInventory = inventory with { Programs = [new("Reinstalled application", "Fixture", "2", installation)] };
        var progress = new Progress<string>(_ => { });
        var blocked = await BrokenShortcutCleaner.CleanAsync(found, conflictingInventory, progress, default);
        Check(blocked is { Files: 0, Skipped: 1 } && File.Exists(owned), "An installation that reappears after review blocks cleanup");
        var cleaned = await BrokenShortcutCleaner.CleanAsync(found, inventory, progress, default);
        Check(cleaned.Files == 1 && !File.Exists(owned) && File.Exists(unrelated) && File.Exists(steam), "Only reviewed and revalidated history matches are removed");
        Check((await BrokenShortcutCleaner.ScanAsync(history, inventory, default, [desktop])).Count == 0, "Shortcut results refresh after removal");
        Console.WriteLine($"Quick152: {checks} checks passed.");
        return 0;
    }
}
