using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace PolarUninstallerWpf.Services;

public sealed record LauncherProcessProfile(string Prefix,string Name,IReadOnlyList<string> ProcessNames);

/// <summary>Shared close → continue orchestration. Steam is deliberately absent.
/// Only user-session launcher UI processes at verified image paths can be closed;
/// shared services, anti-cheat, games and name-only matches are never terminated.</summary>
public static class LauncherRemovalCoordinator
{
    public static bool Enabled=>true;
    internal static readonly LauncherProcessProfile[] Profiles=
    [
        new("gog:","GOG Galaxy",["GalaxyClient","GalaxyClient Helper","GalaxyCommunication"]),
        new("ubisoft:","Ubisoft Connect",["UbisoftConnect","upc","Uplay","UplayWebCore","UbisoftGameLauncher"]),
        new("rockstar:","Rockstar Games Launcher",["Launcher","LauncherPatcher","SocialClubHelper"]),
        new("battlenet:","Battle.net",["Battle.net","Battle.net Launcher","Battle.net Helper"]),
        new("riot:","Riot Client",["RiotClientServices","RiotClientUx","RiotClientUxRender"]),
        new("ea:","EA app / Origin",["EADesktop","EALauncher","Origin","OriginWebHelperService"])
    ];
    public static LauncherProcessProfile? Profile(InstalledProgram game)=>Profiles.FirstOrDefault(p=>game.Identifier.StartsWith(p.Prefix,StringComparison.OrdinalIgnoreCase));
    public static bool Supports(InstalledProgram game)=>Enabled&&Profile(game)!=null;
    internal static bool OwnsImage(LauncherProcessProfile profile,string image,string launcher)
    {
        if(!Path.IsPathFullyQualified(image)||!Path.IsPathFullyQualified(launcher))return false;
        string actual=Path.GetFullPath(image),expected=Path.GetFullPath(launcher);
        if(!profile.ProcessNames.Contains(Path.GetFileNameWithoutExtension(actual),StringComparer.OrdinalIgnoreCase))return false;
        string root=Path.GetDirectoryName(expected)!;
        // Riot UI executables may be versioned under UX, all within Riot Client.
        if(profile.Prefix=="riot:")
            for(var parent=new DirectoryInfo(root);parent!=null;parent=parent.Parent)
                if(parent.Name.Equals("Riot Client",StringComparison.OrdinalIgnoreCase)){root=parent.FullName;break;}
        return actual.Equals(expected,StringComparison.OrdinalIgnoreCase)||actual.StartsWith(root.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase);
    }
    private static List<Process> Candidates(LauncherProcessProfile profile,string? launcher=null)
    {
        using var self=Process.GetCurrentProcess();int session=self.SessionId;var result=new List<Process>();
        try
        {
            foreach(string name in profile.ProcessNames)
                foreach(var p in Process.GetProcessesByName(name))
                {
                    try
                    {
                        if(p.SessionId!=session||p.HasExited){p.Dispose();continue;}
                        if(launcher!=null&&!OwnsImage(profile,p.MainModule?.FileName??"",launcher)){p.Dispose();continue;}
                        result.Add(p);
                    }
                    catch(Exception e)when((e is InvalidOperationException or Win32Exception)&&Exited(p)){p.Dispose();}
                }
            return result;
        }
        catch{foreach(var p in result)p.Dispose();throw;}
    }
    private static bool Running(LauncherProcessProfile profile,string launcher){var processes=Candidates(profile,launcher);foreach(var p in processes)p.Dispose();return processes.Count>0;}
    private static bool Exited(Process process){try{return process.HasExited;}catch(InvalidOperationException){return true;}}
    public static async Task<bool> CloseAsync(InstalledProgram game,bool force,CancellationToken token)
    {
        var profile=Profile(game)??throw new IOException("This launcher is outside the supported close workflow. Steam is excluded.");
        if(DirectRemovalService.RunningProcesses(game.Location).Count>0)throw new IOException("Close this game before starting removal; Polar will not terminate the game or its save operation.");
        string? launcher=LauncherRouteService.Find(game);
        if(launcher==null)
        {
            var unknown=Candidates(profile);foreach(var p in unknown)p.Dispose();
            if(unknown.Count==0)return true;
            throw new IOException("Launcher processes are present but their installation path could not be verified; no process was closed.");
        }
        return await EpicLauncherCloseService.ContinueUntilClosedAsync(async()=>
        {
            var processes=Candidates(profile,launcher);
            try
            {
                if(processes.Count==0)return true;
                var verified=new List<(Process Process,string Image,DateTime Started)>();
                foreach(var p in processes)
                {
                    try
                    {
                        string image=p.MainModule?.FileName??"";
                        if(!OwnsImage(profile,image,launcher))throw new IOException("A same-named process is outside the verified launcher directory; it was preserved.");
                        verified.Add((p,image,p.StartTime));
                    }
                    catch(Exception e)when((e is InvalidOperationException or Win32Exception)&&Exited(p)){}
                }
                foreach(var item in verified)
                {
                    token.ThrowIfCancellationRequested();var p=item.Process;
                    try
                    {
                        if(p.HasExited)continue;
                        if(p.StartTime!=item.Started||!string.Equals(p.MainModule?.FileName,item.Image,StringComparison.OrdinalIgnoreCase))throw new IOException("Launcher identity changed before shutdown.");
                        if(force)p.Kill(false);else if(p.MainWindowHandle!=IntPtr.Zero)p.CloseMainWindow();
                    }
                    catch(Exception e)when((e is InvalidOperationException or Win32Exception)&&Exited(p)){}
                }
                using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(force?8:3));
                try{await Task.WhenAll(processes.Select(p=>p.WaitForExitAsync(deadline.Token)));return true;}
                catch(OperationCanceledException)when(!token.IsCancellationRequested){return false;}
            }
            finally{foreach(var p in processes)p.Dispose();}
        },()=>Running(profile,launcher),token,force?8:1);
    }
}
