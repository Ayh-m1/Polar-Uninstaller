using PolarUninstallerWpf.Services;

/// <summary>
/// Read-only duplicate audit across Windows registrations and every launcher.
/// It prints raw groups that share an install path or launcher id (before) and the
/// merged groups the product would show (after). Nothing is uninstalled, launched
/// or deleted.
/// </summary>
internal static class DuplicateAudit146
{
    private static string NameKey(InstalledProgram p) => InstallationPathResolver.NormalizeProduct(p.Name);
    private static string PathKey(InstalledProgram p) => InventoryMerger.NormalizePath(p.Location);
    private static string IdKey(InstalledProgram p) => p.Identifier.EndsWith(':') ? "" : p.Identifier.Trim().ToLowerInvariant();
    private static string SourceKey(InstalledProgram p) => string.IsNullOrWhiteSpace(p.Source) ? "Windows" : p.Source;

    internal static int Run()
    {
        var token = CancellationToken.None;
        var windows = new WindowsInventoryService().GetInstalledAsync(token).GetAwaiter().GetResult();
        var launchers = new LauncherInventoryService(_ => { }).GetInstalledAsync(token).GetAwaiter().GetResult();
        var raw = windows.Concat(launchers).ToArray();
        Console.WriteLine($"DUP raw windows={windows.Count} launchers={launchers.Count} total={raw.Length}");

        var registryGroups = windows.Where(p => p.Registration is not null)
            .GroupBy(p => (p.Registration!.Hive, Path: p.Registration.Path.ToLowerInvariant(), Name: p.Name, Version: p.Version, Command: p.Uninstall))
            .Where(g => g.Count() > 1).ToArray();
        Console.WriteLine($"DUP identicalRegistryGroups={registryGroups.Length} rows={registryGroups.Sum(g => g.Count())}");
        foreach (var group in registryGroups)
        {
            Console.WriteLine($"  REGDUP n={group.Count()} {group.Key.Name} | v={group.Key.Version} | key={group.Key.Path}");
            foreach (var row in group) PrintRaw(row);
        }

        int clusters = 0;
        Console.WriteLine("BEFORE groups sharing a normalized install path:");
        foreach (var group in raw.Where(p => PathKey(p).Length > 0)
                     .GroupBy(p => (NameKey(p), PathKey(p))).Where(g => g.Count() > 1)
                     .OrderByDescending(g => g.Count()))
        {
            clusters++;
            Console.WriteLine($"GROUP path n={group.Count()} name={group.First().Name} path={group.Key.Item2}");
            foreach (var row in group) PrintRaw(row);
        }
        Console.WriteLine("BEFORE groups sharing a launcher identifier:");
        foreach (var group in raw.Where(p => IdKey(p).Length > 0)
                     .GroupBy(IdKey).Where(g => g.Count() > 1)
                     .OrderByDescending(g => g.Count()))
        {
            clusters++;
            Console.WriteLine($"GROUP id n={group.Count()} id={group.Key}");
            foreach (var row in group) PrintRaw(row);
        }
        Console.WriteLine("BEFORE per-launcher duplicate groups:");
        foreach (var source in raw.Select(SourceKey).Where(s => s != "Windows").Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s))
        {
            var rows = raw.Where(p => SourceKey(p).Equals(source, StringComparison.OrdinalIgnoreCase)).ToArray();
            int pathGroups = rows.Where(p => PathKey(p).Length > 0).GroupBy(p => (NameKey(p), PathKey(p))).Count(g => g.Count() > 1);
            int idGroups = rows.Where(p => IdKey(p).Length > 0).GroupBy(IdKey).Count(g => g.Count() > 1);
            Console.WriteLine($"  LAUNCHER {source} raw={rows.Length} pathGroups={pathGroups} idGroups={idGroups}");
        }
        foreach (var launcher in new[] { "Riot", "EA", "Ubisoft", "Xbox", "Rockstar" })
            if (!raw.Any(p => SourceKey(p).Equals(launcher, StringComparison.OrdinalIgnoreCase)))
                Console.WriteLine($"  LAUNCHER {launcher} raw=0 pathGroups=0 idGroups=0 (not installed)");
        Console.WriteLine("DUP clusters=" + clusters);

        var merged = InventoryMerger.Merge(raw, token);
        Console.WriteLine($"DUP merged={merged.Count} (raw={raw.Length})");

        int afterPath = 0, afterId = 0;
        Console.WriteLine("AFTER merged rows sharing a normalized install path:");
        foreach (var group in merged.Where(p => PathKey(p).Length > 0)
                     .GroupBy(p => (NameKey(p), PathKey(p))).Where(g => g.Count() > 1))
        {
            afterPath++;
            Console.WriteLine($"AFTER GROUP path n={group.Count()} path={group.Key.Item2}");
            foreach (var row in group) PrintMerged(row);
        }
        if (afterPath == 0) Console.WriteLine("  (none)");
        Console.WriteLine("AFTER merged rows sharing a launcher identifier:");
        foreach (var group in merged.Where(p => IdKey(p).Length > 0).GroupBy(IdKey).Where(g => g.Count() > 1))
        {
            afterId++;
            Console.WriteLine($"AFTER GROUP id n={group.Count()} id={group.Key}");
            foreach (var row in group) PrintMerged(row);
        }
        if (afterId == 0) Console.WriteLine("  (none)");
        Console.WriteLine($"DUP afterPathGroups={afterPath} afterIdGroups={afterId}");

        Console.WriteLine("MERGED player-visible rows:");
        foreach (var name in raw.GroupBy(NameKey).Where(g => g.Count() > 1).Select(g => g.Key))
            foreach (var row in merged.Where(p => NameKey(p) == name))
                PrintMerged(row);

        Console.WriteLine("MERGED system database row:");
        foreach (var row in merged.Where(p => p.Name.Contains("Application Compatibility", StringComparison.OrdinalIgnoreCase)
                                            || p.Name.Contains("OpenAL", StringComparison.OrdinalIgnoreCase)))
            PrintMerged(row);

        Console.WriteLine("ALLMERGED names:");
        foreach (var row in merged.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            Console.WriteLine($"  ALLMERGED | {row.Name} | {SourceKey(row)} | v={(string.IsNullOrWhiteSpace(row.Version) ? "-" : row.Version)} | {row.Location}");

        Console.WriteLine("DUP done. Read-only audit; no uninstaller launched and no file changed.");
        return 0;
    }

    private static void PrintRaw(InstalledProgram p) =>
        Console.WriteLine($"  RAW | {p.Name} | pub={p.Publisher} | v={p.Version} | size={(p.SizeKb?.ToString() ?? "-")} | id={p.Identifier} | src={SourceKey(p)} | loc={p.Location} | uninstall={p.Uninstall} | reg={p.Registration?.Path}");
    private static void PrintMerged(InstalledProgram p) =>
        Console.WriteLine($"  MERGED | {p.Name} | pub={p.Publisher} | v={(string.IsNullOrWhiteSpace(p.Version) ? "-" : p.Version)} | size={(p.SizeKb?.ToString() ?? "-")} | id={p.Identifier} | src={SourceKey(p)} | sources={(p.Sources is null ? "" : string.Join("+", p.Sources))} | loc={p.Location} | uninstall={p.Uninstall} | reg={p.Registration?.Path}");
}
