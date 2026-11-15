using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace PolarUninstallerWpf.Services;

/// <summary>Game-specific native uninstallers, not a command that merely opens
/// a launcher. Never fabricates deletion flags or edits an opaque launcher DB.</summary>
public static class LauncherGameCommands
{
    internal static GameRemovalRoute? Resolve(InstalledProgram game)
    {
        if(!LauncherRemovalCoordinator.Supports(game)||game.Identifier.StartsWith("epic:",StringComparison.OrdinalIgnoreCase))return null;
        if(UninstallCommandService.TryParse(game.Uninstall,out var exe,out var args))return Route(exe,args);
        // Match the exact captured installation, not a display-name substring.
        foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})
        foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})
        {
            try
            {
                using var root=RegistryKey.OpenBaseKey(hive,view);
                using var uninstall=root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");if(uninstall==null)continue;
                foreach(string name in uninstall.GetSubKeyNames())
                {
                    using var key=uninstall.OpenSubKey(name);string location=Convert.ToString(key?.GetValue("InstallLocation"))??"";
                    string title=Convert.ToString(key?.GetValue("DisplayName"))??"";
                    string command=Convert.ToString(key?.GetValue("UninstallString"))??"";
                    if(!RegistrationMatches(game,name,title,location,command))continue;
                    if(UninstallCommandService.TryParse(command,out exe,out args))return Route(exe,args);
                }
            }
            catch(Exception error)when(LauncherMetadataDiscovery.ReadFailure(error)){}
        }
        return ResolveGogPayload(game);
    }
    // Missing ARP InstallLocation is common for Agent/Riot commands. Match their
    // exact product argument instead; a display name alone never grants authority.
    internal static bool RegistrationMatches(InstalledProgram game,string key,string title,string location,string command)
    {
        bool hasLocation=!string.IsNullOrWhiteSpace(location);
        bool sameLocation=Path.IsPathFullyQualified(location)&&LeftoverService.EqualPath(location,game.Location);
        if(hasLocation&&!sameLocation)return false;
        bool exactKey=game.Identifier.StartsWith("gog:",StringComparison.OrdinalIgnoreCase)&&key.Equals("GOG Game "+game.Identifier[4..],StringComparison.OrdinalIgnoreCase)||
            game.Identifier.StartsWith("ubisoft:",StringComparison.OrdinalIgnoreCase)&&key.Equals("Uplay Install "+game.Identifier[8..],StringComparison.OrdinalIgnoreCase);
        if(exactKey)return true;
        if(UninstallCommandService.TryParse(command,out var exe,out var args))
        {
            if(game.Identifier.StartsWith("battlenet:",StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(exe).Equals("Blizzard Uninstaller.exe",StringComparison.OrdinalIgnoreCase))
                return args.Count(a=>a.StartsWith("--uid=",StringComparison.OrdinalIgnoreCase))==1&&
                    args.Any(a=>a.Equals("--uid="+game.Identifier[10..],StringComparison.OrdinalIgnoreCase));
            if(game.Identifier.StartsWith("riot:",StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(exe).Equals("RiotClientServices.exe",StringComparison.OrdinalIgnoreCase))
                return args.Count(a=>a.StartsWith("--uninstall-product=",StringComparison.OrdinalIgnoreCase))==1&&
                    RiotGameService.ParseProductId(command)==RiotGameService.ProductIdOf(game);
        }
        return sameLocation&&title.Length>0&&InstallationPathResolver.NormalizeProduct(title)==InstallationPathResolver.NormalizeProduct(game.Name);
    }
    internal static GameRemovalRoute Route(string executable,IReadOnlyList<string> arguments)=>new(executable,arguments,false,
        "Polar closes the verified launcher, starts this game's own uninstaller, then verifies removal and cleans verified leftovers in the same operation. The official uninstaller may request confirmation.",
        "يغلق Polar اللانشر المتحقق منه ويشغّل مزيل اللعبة نفسه، ثم يتحقق من الإزالة وينظف البقايا المتحقق منها ضمن العملية نفسها. قد يطلب المزيل الرسمي تأكيدًا.",CloseLauncher:true,
        TrackProcess:!new[]{"Launcher.exe","RiotClientServices.exe","EADesktop.exe","Origin.exe","GalaxyClient.exe","Battle.net.exe","Battle.net Launcher.exe","UbisoftConnect.exe","upc.exe","Uplay.exe"}.Contains(Path.GetFileName(executable),StringComparer.OrdinalIgnoreCase));
    internal static GameRemovalRoute? ResolveGogPayload(InstalledProgram game)
    {
        if(!game.Identifier.StartsWith("gog:",StringComparison.OrdinalIgnoreCase)||!Directory.Exists(game.Location))return null;
        string id=game.Identifier[4..];if(id.Length==0||!id.All(char.IsAsciiDigit))return null;
        DirectRemovalService.ValidateRoot(game.Location);
        var info=Directory.GetFiles(game.Location,"goggame-*.info",SearchOption.TopDirectoryOnly);
        if(info.Length!=1||!Path.GetFileName(info[0]).Equals("goggame-"+id+".info",StringComparison.OrdinalIgnoreCase))return null;
        using var json=JsonDocument.Parse(File.ReadAllBytes(info[0]));var root=json.RootElement;
        if(!root.TryGetProperty("gameId",out var value)||value.ToString()!=id)return null;
        string executable=Path.Combine(game.Location,"unins000.exe");
        if(!File.Exists(executable)||!File.Exists(Path.Combine(game.Location,"unins000.dat")))return null;
        for(string? path=executable;path!=null;path=Path.GetDirectoryName(path))
            if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("A GOG uninstaller through a junction is blocked.");
        return Route(executable,Array.Empty<string>());
    }
}
