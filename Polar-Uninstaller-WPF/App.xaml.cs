using System.Windows;
using PolarUninstallerWpf.Services;

namespace PolarUninstallerWpf;

public partial class App : Application
{
    static App() => DisplayFormat.ConfigureApplication();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException+=(_,args)=>CrashDiagnostics.Record("Unhandled UI exception",args.Exception);
        AppDomain.CurrentDomain.UnhandledException+=(_,args)=>
        {
            if(args.ExceptionObject is Exception error)CrashDiagnostics.Record("Unhandled process exception",error);
        };
        TaskScheduler.UnobservedTaskException+=(_,args)=>CrashDiagnostics.Record("Unobserved background task",args.Exception);

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

        var window = new MainWindow();
#if LAUNCHER_REMOVAL_TRIAL
        window.Title="Polar Uninstaller — Launcher Removal Trial (Steam unchanged)";
#elif EPIC_DIRECT_TRIAL
        window.Title="Polar Uninstaller — Epic Direct Removal Trial";
#endif
        MainWindow = window;
        window.Show();
        WindowsShellIconService.RefreshInstalledIcons();
    }
}
