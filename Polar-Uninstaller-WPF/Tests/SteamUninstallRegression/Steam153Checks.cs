using PolarUninstallerWpf.Services;

internal static class Steam153Checks
{
    internal static async Task<int> RunAsync(string[] audit)
    {
        using var fixture = new TemporaryFixture();
        int passed = 0;
        void Check(bool condition, string text)
        {
            if (!condition) throw new InvalidOperationException(text);
            Console.WriteLine("PASS: " + text);
            passed++;
        }

        string steamApps = Directory.CreateDirectory(Path.Combine(fixture.Path, "steamapps")).FullName;
        var first = Game("Remnants", "921534101");
        var second = Game("Ambiguous", "921534102");
        var third = Game("Next game", "921534103");
        var mod = Directory.CreateDirectory(Path.Combine(first.Location, "Extras")).FullName;
        File.WriteAllText(Path.Combine(first.Location, "settings.ini"), "[Graphics]");
        File.WriteAllText(Path.Combine(first.Location, "config.json"), "{}");
        File.WriteAllText(Path.Combine(mod, "run.bat"), "@echo fixture only");
        File.WriteAllBytes(Path.Combine(mod, "OPTIONS"), [0x16, 0, 0, 0, 0xac, 0xdb]);
        File.WriteAllText(Path.Combine(second.Location, "game.pak"), "Uncertain payload");
        string liveExecutable = Path.Combine(first.Location, "Game.exe");
        string manifest = Path.Combine(steamApps, "appmanifest_921534101.acf");
        File.WriteAllText(manifest, "fixture");
        Check(UninstallMonitorService.Probe(first, null).LauncherRecordPresent, "A manifest still keeps the game installed");
        File.Delete(manifest);
        var probe = UninstallMonitorService.Probe(first, null);
        Check(probe is { Confirmed: true, RetainedSteamAssets: true }, "Configuration and small nested tools are preserved as leftovers");
        var report = UninstallReportService.FromProbe(first, probe, null, probe.Detail);
        Check(report.Outcome == UninstallOutcome.RemovedWithLeftovers && report.RemainingPaths.Contains(first.Location),
            "Verified removal reports the remaining folder");

        File.WriteAllText(liveExecutable, "Never run this fixture");
        Check(!UninstallMonitorService.Probe(first, null).Confirmed, "An executable blocks confirmation even without a manifest");
        File.Delete(liveExecutable);
        var disguised = Path.Combine(mod, "opaque");
        File.WriteAllBytes(disguised, [77, 90, 0, 0]);
        Check(!UninstallMonitorService.Probe(first, null).Confirmed, "A binary file without an extension blocks confirmation");
        File.Delete(disguised);
        File.WriteAllBytes(Path.Combine(mod, "OPTIONS"), [77, 90, 0, 0]);
        Check(!UninstallMonitorService.Probe(first, null).Confirmed, "An executable signature disguised as a helper's OPTIONS file blocks confirmation");
        File.WriteAllBytes(Path.Combine(mod, "OPTIONS"), [0x16, 0, 0, 0, 0xac, 0xdb]);
        var inventory = new ApplicationInventoryService(_ => Task.FromResult<IReadOnlyList<InstalledProgram>>([first, second]));
        var refreshed = await inventory.RefreshAsync();
        Check(refreshed.Programs.Count == 1 && refreshed.Programs[0].Identifier == second.Identifier,
            "Refresh suppresses only the verified stale Steam entry");

        var order = new List<string>();
        var summary = await UninstallBatchService.RunAsync(new[] { first, second, third }, async (game, token) =>
        {
            order.Add(game.Name);
            var result = await UninstallMonitorService.MonitorAsync(
                () => UninstallMonitorService.Probe(game, null), null, true, null, token, new AdvancingClock());
            var outcome = UninstallReportService.FromProbe(game, result, null, result.Detail);
            if (game == second)
                Check(result.SteamPayloadTimedOut && outcome.Outcome == UninstallOutcome.VerificationIncomplete,
                    "Uncertain payload times out and needs manual follow-up");
            return result.Confirmed ? BatchItemOutcome.Succeeded : BatchItemOutcome.Manual;
        }, default);
        Check(order.SequenceEqual([first.Name, second.Name, third.Name]) &&
            summary is { Succeeded: 2, Manual: 1, Failed: 0, Skipped: 0 },
            "The queue runs in order and continues after a manual result");
        Check(Directory.GetFiles(first.Location, "*", SearchOption.AllDirectories).Length == 4 &&
            File.Exists(Path.Combine(second.Location, "game.pak")), "Both confirmed leftovers and unverified payload remain on disk");

        if (audit.Length == 2)
        {
            var audited = new InstalledProgram("Steam read-only audit", "", "", Path.GetFullPath(audit[1]), Identifier: "steam:" + audit[0]);
            var actual = UninstallMonitorService.Probe(audited, null);
            Console.WriteLine("Read-only Steam result: " + actual.Detail);
            Check(actual is { Confirmed: true, RetainedSteamAssets: true }, "Read-only audit confirms the reported game's remaining files are auxiliary");
        }
        Console.WriteLine($"Steam153: {passed} checks passed.");
        return 0;

        InstalledProgram Game(string name, string id) =>
            new(name, "Fixture", "1", Directory.CreateDirectory(Path.Combine(steamApps, "common", name)).FullName,
                Identifier: "steam:" + id, RequiresOfficialRemoval: true);
    }

    private sealed class AdvancingClock : TimeProvider
    {
        private long seconds;
        public override long TimestampFrequency => 1;
        public override long GetTimestamp() => Interlocked.Add(ref seconds, 31);
    }

    private sealed class TemporaryFixture : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("polar-steam153-").FullName;
        public void Dispose() => Directory.Delete(Path, true);
    }
}
