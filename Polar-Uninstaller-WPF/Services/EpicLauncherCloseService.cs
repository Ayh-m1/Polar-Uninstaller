using System.Diagnostics;
using System.IO;
using System.ComponentModel;

namespace PolarUninstallerWpf.Services;

/// <summary>Only exact Epic images in the current session are eligible. A name
/// is used for discovery, never as authority to terminate a process.</summary>
public static class EpicLauncherCloseService
{
    public static bool AnyRunning()
    {
        bool running=false;
        foreach(string name in new[]{"EpicGamesLauncher","EpicWebHelper"})
            foreach(var process in Process.GetProcessesByName(name))using(process)
                if(!Exited(process))running=true;
        return running;
    }
    internal static bool OwnsImage(string image,string launcher)
    {
        if(!Path.IsPathFullyQualified(image)||!Path.IsPathFullyQualified(launcher))return false;
        string expected=Path.GetFullPath(launcher),actual=Path.GetFullPath(image);
        if(actual.Equals(expected,StringComparison.OrdinalIgnoreCase))return true;
        string folder=Path.GetDirectoryName(expected)!;
        for(var parent=new DirectoryInfo(folder);parent!=null;parent=parent.Parent)
            if(parent.Name.Equals("Launcher",StringComparison.OrdinalIgnoreCase)){folder=parent.FullName;break;}
        return Path.GetFileName(actual).Equals("EpicWebHelper.exe",StringComparison.OrdinalIgnoreCase)&&actual.StartsWith(folder.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase);
    }
    public static async Task<bool> CloseAsync(InstalledProgram game,bool force,CancellationToken token)
        =>await ContinueUntilClosedAsync(()=>ClosePassAsync(game,force,token),AnyRunning,token,force?8:1);

    // The removal confirmation explicitly authorizes both stages. Escalation is
    // limited to the same path-verified launcher processes, never games/services.
    internal static async Task<bool> CloseAuthorizedAsync(Func<bool,Task<bool>> close,Action escalating,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(await close(false))return true;
        token.ThrowIfCancellationRequested();escalating();
        return await close(true);
    }

    // A launcher may spawn/replace a WebHelper while its main window exits.
    // Keep the user's current authorization and wait for the newly verified
    // process set instead of treating the first completed snapshot as failure.
    internal static async Task<bool> ContinueUntilClosedAsync(Func<Task<bool>> closePass,Func<bool> running,CancellationToken token,int maxPasses=8)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        // A timed-out snapshot does not prove the launcher failed to close: its
        // parent may have exited while a replacement helper was starting.
        // Re-enumerate and verify that replacement under the same authorization.
        try
        {
            for(int pass=0;pass<maxPasses;pass++)
            {
                deadline.Token.ThrowIfCancellationRequested();if(!running())return true;
                await closePass(); // Always finish/dispose the process handles before returning.
            }
        }
        catch(OperationCanceledException)when(!token.IsCancellationRequested){return !running();}
        token.ThrowIfCancellationRequested();return !running();
    }
    private static bool Exited(Process process)
    {try{return process.HasExited;}catch(InvalidOperationException){return true;}}

    private static async Task<bool> ClosePassAsync(InstalledProgram game,bool force,CancellationToken token)
    {
        if(!AnyRunning())return true;
        string executable=LauncherRouteService.Find(game)??throw new IOException("The Epic launcher executable could not be verified; automatic closing was blocked.");
        var processes=new List<(Process Process,DateTime Started,string Image)>();
        try
        {
            using var self=Process.GetCurrentProcess();int session=self.SessionId;
            foreach(string name in new[]{"EpicGamesLauncher","EpicWebHelper"})
                foreach(var p in Process.GetProcessesByName(name))
                {
                    try
                    {
                        if(p.HasExited){p.Dispose();continue;}
                        string image=p.MainModule?.FileName??"";
                        if(p.SessionId!=session||!OwnsImage(image,executable))throw new IOException("An Epic-named process could not be verified; it was not closed.");
                        processes.Add((p,p.StartTime,image));
                    }
                    catch(Exception error)when((error is Win32Exception or InvalidOperationException)&&Exited(p)){p.Dispose();}
                    catch{p.Dispose();throw;}
                }
            // Validate the entire set before requesting shutdown of any process.
            foreach(var entry in processes)
            {
                token.ThrowIfCancellationRequested();var p=entry.Process;
                try
                {
                    if(p.HasExited)continue;
                    if(p.StartTime!=entry.Started||!string.Equals(p.MainModule?.FileName,entry.Image,StringComparison.OrdinalIgnoreCase))throw new IOException("The launcher process identity changed.");
                    if(force)p.Kill(false);else if(p.MainWindowHandle!=IntPtr.Zero)p.CloseMainWindow();
                }
                catch(Exception error)when((error is Win32Exception or InvalidOperationException)&&Exited(p)){}
            }
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(force?8:3));
            try{await Task.WhenAll(processes.Select(x=>x.Process.WaitForExitAsync(timeout.Token)));}
            catch(OperationCanceledException)when(!token.IsCancellationRequested){return false;}
            return true; // The coordinator checks for replacement processes next.
        }
        finally{foreach(var entry in processes)entry.Process.Dispose();}
    }
}
