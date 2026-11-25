using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using PolarUninstallerWpf.Services;

internal static class LargeGame152Checks
{
    internal static async Task<int> RunAsync()
    {
        string fixture = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "fixtures", "large-games-" + Guid.NewGuid().ToString("N"))).FullName;
        var prefixes = new[] { "steam", "epic", "ubisoft", "battlenet", "gog", "rockstar", "riot", "ea" };
        int checks = 0;
        void Check(bool pass, string name)
        {
            if (!pass) throw new InvalidOperationException(name);
            checks++;
            Console.WriteLine("PASS: " + name);
        }
        foreach (string prefix in prefixes)
        {
            string gameId = prefix is "steam" or "ubisoft" or "gog" ? "1998123456" : "polar-fixture-" + Guid.NewGuid().ToString("N");
            string folder = Directory.CreateDirectory(Path.Combine(fixture, prefix, "steamapps", "common", "Disposable Game")).FullName;
            string executable = Path.Combine(folder, "Game.exe");
            string payload = Path.Combine(folder, "content.pak");
            File.WriteAllText(executable, "Never run this fixture.");
            File.WriteAllText(payload, "Small file standing in for a large payload.");
            var program = new InstalledProgram("Polar Large Game Fixture " + prefix, "Fixture", "1", folder,
                SizeKb: 100L * 1024 * 1024, Identifier: prefix + ":" + gameId, RequiresOfficialRemoval: true);
            var registration = new RegistryLeftover(RegistryHive.CurrentUser, RegistryView.Registry64,
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\PolarTest-" + Guid.NewGuid().ToString("N"), program.Name, folder);
            var snapshot = new UninstallSnapshot(program, [registration], [], [], [executable]);
            var pending = UninstallMonitorService.Probe(program, snapshot);
            Check(!pending.Confirmed && pending.MainExecutablePresent,
                $"{prefix}: a captured executable prevents success while game files remain");
            var (window, delay) = UninstallMonitorService.PollingPolicy(true, false, pending, 100);
            Check(window == TimeSpan.FromMinutes(15) && delay <= TimeSpan.FromSeconds(4),
                $"{prefix}: launcher-managed removal may take longer without a fixed wait after completion");
            var registrationGone = new RemovalProbe(false, false, true, "Game files remain", true);
            Check(UninstallMonitorService.PollingPolicy(true, true, registrationGone, 100) ==
                (TimeSpan.FromMinutes(15), TimeSpan.FromSeconds(1)), $"{prefix}: fast polling resumes after the record disappears");
            File.Delete(executable);
            File.Delete(payload);
            var watch = Stopwatch.StartNew();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var confirmed = await UninstallMonitorService.MonitorAsync(() => UninstallMonitorService.Probe(program, snapshot),
                Task.CompletedTask, true, null, timeout.Token);
            Check(confirmed.Confirmed && !confirmed.MainExecutablePresent,
                $"{prefix}: first post-exit probe verifies the removed payload ({watch.ElapsedMilliseconds} ms on this fixture)");
        }
        Check(UninstallMonitorService.PollingPolicy(false, true, new(false, false, true, "App files"), 0).Window == TimeSpan.FromSeconds(8),
            "The tracked application timing remains unchanged");
        Console.WriteLine($"LargeGames152: {checks} checks passed.");
        return 0;
    }
}
