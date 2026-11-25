using System.IO;
using PolarUninstallerWpf.Services;

internal static class ShortcutCleanupChecks
{
    internal static async Task<int> RunAsync()
    {
        string root = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "fixtures", "shortcuts-" + Guid.NewGuid().ToString("N"))).FullName;
        string install = Directory.CreateDirectory(Path.Combine(root, "install")).FullName;
        string programs = Directory.CreateDirectory(Path.Combine(root, "redirected", "Programs")).FullName;
        string publicPrograms = Directory.CreateDirectory(Path.Combine(root, "public", "Programs")).FullName;
        string desktop = Directory.CreateDirectory(Path.Combine(root, "redirected", "سطح المكتب")).FullName;
        string publicDesktop = Directory.CreateDirectory(Path.Combine(root, "public", "Desktop")).FullName;
        string name = "Polar Shortcut Fixture";
        string wrapper = Directory.CreateDirectory(Path.Combine(programs, "Vendor", name, "Nested")).FullName;
        string shared = Directory.CreateDirectory(Path.Combine(publicPrograms, name)).FullName;
        string appTarget = Path.Combine(install, "program.exe");
        string missingOther = Path.Combine(root, "other", "program.exe");
        File.WriteAllText(appTarget, "fixture");
        var program = new InstalledProgram(name, "Polar test fixture", "1", install);
        string ownedLnk = Path.Combine(wrapper, "اختصار.lnk");
        string ownedUrl = Path.Combine(shared, "arbitrary.url");
        string validOther = Path.Combine(shared, name + ".url");
        string invalid = Path.Combine(desktop, "invalid.lnk");
        string invalidUrl = Path.Combine(desktop, "invalid.url");
        string retargeted = Path.Combine(desktop, "retargeted.url");
        string publicUrl = Path.Combine(publicDesktop, "owned.url");
        string locked = Path.Combine(publicDesktop, "locked.url");
        string withArguments = Path.Combine(publicDesktop, "personal.lnk");
        string obsolete = Path.Combine(desktop, "post-uninstall.url");
        void WriteUrl(string path, string target) => File.WriteAllText(path, "[InternetShortcut]\r\nURL=" + new Uri(target).AbsoluteUri);
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            Console.WriteLine("PASS: " + label);
        }

        var write = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sta = new Thread(() =>
        {
            try
            {
                ShellShortcutService.Write(ownedLnk, appTarget);
                ShellShortcutService.Write(withArguments, appTarget, "my-document");
                write.SetResult();
            }
            catch (Exception error) { write.SetException(error); }
        });
        sta.SetApartmentState(ApartmentState.STA);
        sta.Start();
        await write.Task;
        foreach (string path in new[] { ownedUrl, retargeted, publicUrl, locked }) WriteUrl(path, appTarget);
        WriteUrl(validOther, missingOther);
        File.WriteAllText(invalid, "not a shell link");
        File.WriteAllText(invalidUrl, "[OtherSection]\r\nURL=" + new Uri(appTarget).AbsoluteUri);
        string[] locations = [programs, publicPrograms, desktop, publicDesktop];
        var captured = await ShortcutLeftoverService.ScanAsync(program, locations: locations);
        Check(captured.Select(x => x.Path).ToHashSet().SetEquals([ownedLnk, ownedUrl, retargeted, publicUrl, locked]),
            "Both Programs and Desktop fixture roots are scanned recursively, including a redirected Unicode path; name-only, malformed and personal argument links are excluded");
        Check(ShortcutLeftoverService.RemoveForCleanup(captured.Single(x => x.Path == ownedLnk), program)[0].StartsWith("Skipped") &&
            File.Exists(ownedLnk), "A shortcut with a live target cannot be deleted as a leftover");

        File.Delete(appTarget);
        WriteUrl(retargeted, missingOther);
        WriteUrl(obsolete, appTarget);
        var foundAfter = await ShortcutLeftoverService.ScanAsync(program, locations: locations);
        Check(foundAfter.Any(x => x.Path == obsolete) && foundAfter.All(x => x.Path != validOther && x.Path != retargeted),
            "A captured installation path still identifies stale shortcuts, while unrelated missing targets stay excluded");
        var combined = captured.Concat(foundAfter).DistinctBy(x => x.Path).Select(link => link with
        {
            StartMenuRoot = link.Path == ownedLnk ? programs : link.Path == ownedUrl ? publicPrograms : null
        }).ToArray();
        var snapshot = new UninstallSnapshot(program, [], combined, [], [appTarget]);
        var scanner = new SharedLeftoverScanner();
        var parentOwner = new InstalledProgram("Other installed program", "Other vendor", "1", root);
        var sharedCandidates = await scanner.ScanAsync(snapshot, [parentOwner], null, default);
        Check(sharedCandidates.All(candidate => candidate.Category != "Shortcut"), "An overlapping installed application blocks shortcut cleanup");
        using var lockedHandle = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.Read);
        var cleanup = await scanner.CleanupAsync(snapshot, [], null, default);
        Check(cleanup.ShortcutsRemoved == 4 && cleanup.FailedPaths.Contains(retargeted) && File.Exists(retargeted)
            && new[] { ownedLnk, ownedUrl, publicUrl, obsolete }.All(ShortcutLeftoverService.IsAbsent),
            "Existing cleanup verifies four deletions and reports a changed shortcut as still present");
        Check(cleanup.FailedPaths.Contains(locked) && File.Exists(locked), "A locked shortcut is preserved and reports incomplete cleanup");
        Check(!Directory.Exists(wrapper) && !Directory.Exists(Path.Combine(programs, "Vendor", name))
            && Directory.Exists(Path.Combine(programs, "Vendor")) && Directory.Exists(shared) && File.Exists(validOther),
            "Only empty application-specific Start Menu folders are pruned; unrelated folders and shortcuts remain");
        Check(File.Exists(invalid) && File.Exists(invalidUrl) && File.Exists(withArguments), "Invalid and personal shortcuts remain untouched");

        var code = Guid.NewGuid();
        var msi = program with { Location = "", Uninstall = "msiexec.exe /X " + code.ToString("B") };
        var advertised = new ShortcutLeftover("fixture.lnk", "", "") { MsiProduct = code };
        Check(ShortcutLeftoverService.OwnsShortcut(msi, advertised) &&
            !ShortcutLeftoverService.OwnsShortcut(msi, advertised with { MsiProduct = Guid.NewGuid() }),
            "Only the exact MSI product code supplies ownership for an advertised link without a raw target");
        Check(ShortcutLeftoverService.Owns(program with { Identifier = "steam:123", Source = "Steam" }, "steam://rungameid/123"),
            "Existing Steam shortcut identity is preserved");
        return 0;
    }
}
