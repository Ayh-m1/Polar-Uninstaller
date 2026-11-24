using System.Windows;
using PolarUninstallerWpf.Services;

namespace PolarUninstallerWpf;

public partial class App : Application
{
    static App() => DisplayFormat.ConfigureApplication();

    protected override void OnStartup(StartupEventArgs e)
    {
        AppSafety.Install(this,e.Args.Contains("--polar-icon-worker")||UpdateInstallerService.IsApplyUpdateMode(e.Args));
        using var startupTiming=PerformanceTrace.Measure("startup");
        // Must remain the very first startup branch: the isolated icon worker runs
        // headless and no window, inventory scan or update helper may be created.
        if(e.Args.Contains("--polar-icon-worker"))
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            try { IconWorkerPool.RunConsoleWorker(); }
            catch (Exception error) { CrashDiagnostics.Record("Icon worker console",error);try{Console.Error.WriteLine(error.GetType().Name+": "+error.Message);}catch{}Shutdown(1);return;}
            Shutdown();
            return;
        }
        base.OnStartup(e);
        Exit+=(_,_)=>IconWorkerPool.StopAll();

        if (UpdateInstallerService.IsApplyUpdateMode(e.Args))
        {
            // The helper runs without a window. There is no StartupUri, so the
            // main window is never created and the process stays alive only
            // until the replacement work finishes.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            int exitCode;
            try { exitCode = UpdateInstallerService.RunApplyUpdate(e.Args); }
            catch (Exception error)
            {
                WpfUpdateService.Log("Unhandled helper error: " + error);
                exitCode = 1;
            }
            Shutdown(exitCode);
            return;
        }

        try { UpdateInstallerService.CleanupAfterSuccessfulStart(); }
        catch (Exception error) { WpfUpdateService.Log("Startup cleanup failed: " + error.Message); }

        try { UpdateInstallerService.SynchronizeInstalledMetadata(); }
        catch (Exception error) { WpfUpdateService.Log("Metadata synchronization failed: " + error.Message); }

        try { UpdateDownloadService.CleanupStaleDownloads(); }
        catch (Exception error) { WpfUpdateService.Log("Stale download cleanup failed: " + error.Message); }

        if(!SingleInstance.Acquire()){SingleInstance.ActivateExisting();Shutdown();return;}
        Exit+=(_,_)=>SingleInstance.Release();
        var window = new MainWindow();
#if LAUNCHER_REMOVAL_TRIAL
        window.Title="Polar Uninstaller — Launcher Removal Trial (Steam unchanged)";
#elif EPIC_DIRECT_TRIAL
        window.Title="Polar Uninstaller — Epic Direct Removal Trial";
#endif
        MainWindow = window;
        window.Show();
        _=SafeTasks.Run(()=>Task.Run(WindowsShellIconService.RefreshInstalledIcons),"Refresh Windows icons");
    }
}
