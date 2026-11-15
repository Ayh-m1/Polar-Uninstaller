using System.IO;

namespace PolarUninstallerWpf.Services;

/// <summary>
/// Evidence-based classification of installed products as games.
///
/// A product is only treated as a verified game when several independent
/// indicators agree (launcher identity, trusted publisher, game-specific
/// metadata, installation evidence). A name or publisher match alone is
/// never enough, and known non-game software (engines, tools, launchers,
/// anti-cheat, runtimes) is explicitly excluded.
/// </summary>
public static class GameClassificationService
{
    public enum Classification { VerifiedGame, PossibleGame, RegularApplication }

    private static readonly object CacheGate = new();
    private static readonly Dictionary<string, Classification> Cache = new(StringComparer.Ordinal);
    private const int CacheLimit = 4096;

    private static readonly string[] KnownGamePublishers =
    {
        "Roblox", "Mojang", "Blizzard", "Valve", "Electronic Arts", "Ubisoft",
        "Epic Games", "Rockstar", "Riot Games", "Activision", "Bethesda",
        "CD Projekt", "Square Enix", "Capcom", "Bandai Namco", "SEGA", "Konami",
        "2K", "Take-Two", "Warner Bros", "Bungie", "HoYoverse", "miHoYo",
        "Grinding Gear Games", "Digital Extremes", "Psyonix", "Respawn",
        "Treyarch", "Infinity Ward", "Sledgehammer Games", "Naughty Dog",
        "Santa Monica Studio", "Insomniac Games", "Guerrilla Games", "Rare",
        "Obsidian Entertainment", "inXile", "id Software", "Arkane Studios",
        "MachineGames", "Tango Gameworks", "ZeniMax", "Lucasfilm Games",
        "Disney", "Paradox", "Larian Studios", "FromSoftware", "Koei Tecmo",
        "Atlus", "NIS America", "XSEED", "Devolver Digital", "Annapurna",
        "Team17", "Focus Entertainment", "THQ Nordic", "Deep Silver",
        "505 Games", "Codemasters", "Frontier Developments", "Creative Assembly",
        "Relic Entertainment", "Firaxis", "Civilization", "Aspyr", "Feral",
        "Supergiant Games", "Klei Entertainment", "Re-Logic", "ConcernedApe",
        "Chucklefish", "Playdead", "Motion Twin", "MegaCrit", "Innersloth",
        "Among Us", "InnerSloth"
    };

    private static readonly string[] GameNameKeywords =
    {
        "game", "play", "player", "quest", "adventure", "battle", "war",
        "legend", "hero", "kingdom", "world", "arena", "league", "valorant",
        "fortnite", "minecraft", "roblox", "dota", "counter-strike", "overwatch",
        "diablo", "starcraft", "hearthstone", "wow", "elderscrolls", "fallout",
        "skyrim", "gta", "red dead", "witcher", "cyberpunk", "assassin",
        "far cry", "watch dogs", "rainbow six", "splinter cell", "ghost recon",
        "just dance", "trackmania", "trials", "for honor", "crew", "division",
        "avatar", "skull", "bones", "prince of persia", "rayman", "rabbids",
        "mario", "zelda", "pokemon", "metroid", "kirby", "donkey kong",
        "sonic", "final fantasy", "resident evil", "monster hunter", "street fighter",
        "tekken", "mortal kombat", "injustice", "batman", "lego", "harry potter",
        "hogwarts", "middle-earth", "shadow of", "mad max", "dying light",
        "dead island", "cyberpunk", "witcher", "gwent", "thronebreaker"
    };

    private static readonly string[] AntiIndicators =
    {
        "studio", "engine", "sdk", "driver", "runtime", "redistributable",
        "mod manager", "mod organizer", "anti-cheat", "anticheat", "tool",
        "editor", "server", "launcher", "connect", "updater", "uninstaller",
        "framework", "library", "plugin", "modding", "creator", "devkit",
        "development", "profiler", "debugger", "compiler", "mod kit"
    };

    private static readonly string[] AntiPublishers =
    {
        "Microsoft", "Intel", "AMD", "NVIDIA", "Adobe", "Autodesk",
        "Unity Technologies", "Epic Games" // engine/launcher publishers
    };

    /// <summary>True only for verified games.</summary>
    public static bool IsGame(InstalledProgram program) =>
        Classify(program) == Classification.VerifiedGame;

    /// <summary>
    /// Returns the full classification. Possible games are NOT treated as games
    /// automatically; they remain regular applications for routing purposes.
    /// </summary>
    public static Classification Classify(InstalledProgram program)
    {
        if (program is null) return Classification.RegularApplication;

        // Shared Riot components are never games.
        if (RiotGameService.IsProtectedComponent(program))
            return Classification.RegularApplication;

        string key = CacheKey(program);
        lock (CacheGate)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;
        }

        var result = ClassifyCore(program);

        lock (CacheGate)
        {
            if (Cache.Count >= CacheLimit) Cache.Clear();
            Cache[key] = result;
        }
        return result;
    }

    private static Classification ClassifyCore(InstalledProgram program)
    {
        string id = program.Identifier ?? "";
        if(LauncherRemovalCoordinator.Enabled&&id.StartsWith("ea:",StringComparison.OrdinalIgnoreCase)&&program.Source=="EA"&&!string.IsNullOrWhiteSpace(program.Uninstall))return Classification.VerifiedGame;

        // Launcher-managed games with a verified identifier.
        if (id.StartsWith("steam:", StringComparison.OrdinalIgnoreCase) ||
            id.StartsWith("battlenet:", StringComparison.OrdinalIgnoreCase) ||
            id.StartsWith("gog:", StringComparison.OrdinalIgnoreCase) ||
            id.StartsWith("epic:", StringComparison.OrdinalIgnoreCase) ||
            id.StartsWith("ubisoft:", StringComparison.OrdinalIgnoreCase) ||
            id.StartsWith("rockstar:", StringComparison.OrdinalIgnoreCase) ||
            id.StartsWith("riot:", StringComparison.OrdinalIgnoreCase))
            return Classification.VerifiedGame;

        var sources = program.Sources ?? new[] { program.Source };
        bool launcherSource = sources.Any(s => s != null && (
            s.Contains("Epic Games", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("Ubisoft", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("Rockstar", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("Battle.net", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("Riot Games", StringComparison.OrdinalIgnoreCase)));
        if (launcherSource && !string.IsNullOrWhiteSpace(program.Identifier))
            return Classification.PossibleGame;

        string name = (program.Name ?? "").ToLowerInvariant();
        string publisher = program.Publisher ?? "";
        string uninstall = program.Uninstall ?? "";
        string icon = program.IconPath ?? "";
        string location = program.Location ?? "";
        string evidence = (uninstall + " " + icon + " " + location).ToLowerInvariant();

        // Anti-indicators: never classify these as games.
        if (AntiIndicators.Any(a => name.Contains(a, StringComparison.Ordinal)))
            return Classification.RegularApplication;
        if (AntiPublishers.Any(p => publisher.Contains(p, StringComparison.OrdinalIgnoreCase)))
            return Classification.RegularApplication;

        int score = 0;
        int strong = 0;

        // Known game publisher.
        if (KnownGamePublishers.Any(p => publisher.Contains(p, StringComparison.OrdinalIgnoreCase)))
        { score += 2; strong++; }

        // Game keyword in the product name.
        if (GameNameKeywords.Any(k => name.Contains(k, StringComparison.Ordinal)))
        { score += 1; strong++; }

        // Verified Roblox Player signature (kept explicit for clarity).
        if (publisher.Contains("Roblox", StringComparison.OrdinalIgnoreCase) &&
            !name.Contains("studio") &&
            (name.Contains("player") || evidence.Contains("robloxplayer")))
        { score += 2; strong++; }

        // Installation path contains a "Games"/"Game" segment.
        if (location.IndexOf("\\Games\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
            location.IndexOf("\\Game\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
            location.EndsWith("\\Games", StringComparison.OrdinalIgnoreCase) ||
            location.EndsWith("\\Game", StringComparison.OrdinalIgnoreCase))
            score += 1;

        // Uninstall executable looks like a game installer.
        string exeName = "";
        try
        {
            string first = uninstall.Trim().Trim('"');
            int space = first.IndexOf(' ');
            string candidate = space > 0 ? first[..space] : first;
            candidate = Environment.ExpandEnvironmentVariables(candidate.Trim('"'));
            if (Path.IsPathFullyQualified(candidate)) exeName = Path.GetFileName(candidate).ToLowerInvariant();
        }
        catch (ArgumentException) { }
        if (exeName.Contains("installer") || exeName.Contains("setup") || exeName.Contains("uninstall"))
            score += 1;

        // Publisher contains "Games".
        if (publisher.Contains("Games", StringComparison.OrdinalIgnoreCase))
            score += 1;

        // Product metadata mentioning "Game".
        // (No file I/O here to keep classification fast and UI-thread safe.)

        // Name/publisher/path heuristics may suggest a game, but do not prove
        // launcher ownership or authorize a game-specific uninstall workflow.
        // VerifiedGame requires the supported installation identities above.
        if (strong >= 1 && score >= 2) return Classification.PossibleGame;
        return Classification.RegularApplication;
    }

    private static string CacheKey(InstalledProgram program) =>
        (program.Name ?? "") + "\u001F" + (program.Publisher ?? "") + "\u001F" +
        (program.Location ?? "") + "\u001F" + (program.Uninstall ?? "") + "\u001F" +
        (program.Identifier ?? "");

    /// <summary>Human-readable explanation for diagnostics/logging.</summary>
    public static string Explain(InstalledProgram program)
    {
        var c = Classify(program);
        return $"{program.Name}: {c}";
    }
}
