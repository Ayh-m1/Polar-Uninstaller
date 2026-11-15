using System.IO;
using Microsoft.Win32;

namespace PolarUninstallerWpf.Services;

public static class LauncherRouteService
{
    // Used only for games identified by launcher metadata. Opening the launcher
    // never implies that an uninstall completed or authorizes leftover deletion.
    public static string? Find(InstalledProgram game)
    {
        string id = game.Identifier;
        var candidates = new List<string>();
        if(LauncherRemovalCoordinator.Enabled&&id.StartsWith("ea:",StringComparison.OrdinalIgnoreCase))
        {
            foreach(var baseFolder in new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)})
            {
                candidates.Add(Path.Combine(baseFolder,"Electronic Arts","EA Desktop","EA Desktop","EADesktop.exe"));
                candidates.Add(Path.Combine(baseFolder,"Origin","Origin.exe"));
            }
            foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})
            foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})
                try
                {
                    using var root=RegistryKey.OpenBaseKey(hive,view);
                    foreach(string name in new[]{"EADesktop.exe","Origin.exe"})
                    {
                        using var key=root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\"+name);
                        string path=Environment.ExpandEnvironmentVariables(Convert.ToString(key?.GetValue(""))??"").Trim('"');
                        if(Path.IsPathFullyQualified(path))candidates.Insert(0,path);
                    }
                }
                catch(Exception error)when(LauncherMetadataDiscovery.ReadFailure(error)){}
            return candidates.FirstOrDefault(File.Exists);
        }
        if (id.StartsWith("epic:", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Epic Games", "Launcher", "Portal", "Binaries", "Win64", "EpicGamesLauncher.exe"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Epic Games", "Launcher", "Portal", "Binaries", "Win64", "EpicGamesLauncher.exe"));
        }
        else if (id.StartsWith("ubisoft:", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Ubisoft", "Ubisoft Game Launcher", "UbisoftConnect.exe"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ubisoft", "Ubisoft Game Launcher", "UbisoftConnect.exe"));
        }
        else if (id.StartsWith("rockstar:", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Rockstar Games", "Launcher", "Launcher.exe"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Rockstar Games", "Launcher", "Launcher.exe"));
        }
        else if (id.StartsWith("battlenet:", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Battle.net", "Battle.net Launcher.exe"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Battle.net", "Battle.net Launcher.exe"));
        }
        else if (id.StartsWith("riot:", StringComparison.OrdinalIgnoreCase))
        {
            // The verified Riot Client location wins over guessed paths.
            string? client = RiotGameService.FindClientExecutable();
            if (client != null) candidates.Add(client);
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Riot Games", "Riot Client", "RiotClientServices.exe"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Riot Games", "Riot Client", "RiotClientServices.exe"));
            string? systemDrive = Path.GetPathRoot(Environment.SystemDirectory);
            if (!string.IsNullOrWhiteSpace(systemDrive))
                candidates.Add(Path.Combine(systemDrive, "Riot Games", "Riot Client", "RiotClientServices.exe"));
        }
        else if (id.StartsWith("gog:", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "GOG Galaxy", "GalaxyClient.exe"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GOG Galaxy", "GalaxyClient.exe"));
        }
        else return null;
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                string? appPath = id.StartsWith("epic:", StringComparison.OrdinalIgnoreCase) ? "EpicGamesLauncher.exe" :
                    id.StartsWith("battlenet:", StringComparison.OrdinalIgnoreCase) ? "Battle.net.exe" :
                    id.StartsWith("gog:", StringComparison.OrdinalIgnoreCase) ? "GalaxyClient.exe" : null;
                if (appPath != null)
                {
                    using var registeredApp = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + appPath);
                    string executable = Environment.ExpandEnvironmentVariables(Convert.ToString(registeredApp?.GetValue("")) ?? "").Trim('"');
                    if (Path.IsPathFullyQualified(executable)) candidates.Insert(0, executable);
                }
                if (id.StartsWith("battlenet:", StringComparison.OrdinalIgnoreCase))
                {
                    using var battleNet = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Battle.net");
                    string install = Convert.ToString(battleNet?.GetValue("InstallLocation")) ?? "";
                    if (Path.IsPathFullyQualified(install)) candidates.Insert(0, Path.Combine(install, "Battle.net Launcher.exe"));
                }
                string keyName = id.StartsWith("ubisoft:", StringComparison.OrdinalIgnoreCase) ? @"SOFTWARE\Ubisoft\Launcher" :
                    id.StartsWith("rockstar:", StringComparison.OrdinalIgnoreCase) ? @"SOFTWARE\Rockstar Games\Launcher" :
                    id.StartsWith("epic:", StringComparison.OrdinalIgnoreCase) ? @"SOFTWARE\Epic Games\EpicGamesLauncher" :
                    id.StartsWith("gog:", StringComparison.OrdinalIgnoreCase) ? @"SOFTWARE\GOG.com\GalaxyClient" : "";
                using var key = keyName.Length == 0 ? null : root.OpenSubKey(keyName);
                using var galaxyPaths = id.StartsWith("gog:", StringComparison.OrdinalIgnoreCase) ? key?.OpenSubKey("paths") : null;
                string directory = Environment.ExpandEnvironmentVariables(Convert.ToString(key?.GetValue("InstallDir") ?? key?.GetValue("InstallFolder") ?? key?.GetValue("InstallationFolder") ?? key?.GetValue("path") ?? galaxyPaths?.GetValue("client")) ?? "").Trim('"');
                if (Path.IsPathFullyQualified(directory))
                {
                    string[] executables = id.StartsWith("ubisoft:", StringComparison.OrdinalIgnoreCase) ? new[] { "UbisoftConnect.exe", "upc.exe", "Uplay.exe" } :
                        id.StartsWith("epic:", StringComparison.OrdinalIgnoreCase) ? new[] { @"Portal\Binaries\Win64\EpicGamesLauncher.exe", @"Portal\Binaries\Win32\EpicGamesLauncher.exe" } :
                        id.StartsWith("gog:", StringComparison.OrdinalIgnoreCase) ? new[] { "GalaxyClient.exe" } : new[] { "Launcher.exe" };
                    foreach (var executableName in executables) candidates.Insert(0, Path.Combine(directory, executableName));
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }
        return candidates.FirstOrDefault(File.Exists);
    }
}
