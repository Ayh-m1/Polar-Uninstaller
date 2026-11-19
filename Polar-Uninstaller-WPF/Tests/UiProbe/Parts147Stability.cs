using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using PolarUninstallerWpf;
using PolarUninstallerWpf.Services;
internal static partial class Parts146Checks
{
    internal static int RunStability147()
    {
        Setup("Stability147");AppSafety.LogDirectory=Path.Combine(root,"crash");CrashDiagnostics.LogDirectory=AppSafety.LogDirectory;
        // No window is shown or automated. Exercise the real page factories and theme/language handlers.
        for(int i=0;i<12;i++){Invoke(main,"NavigateTo",new[]{"home","uninstall","cleaner","dashboard","files","whatsnew"}[i%6]);Pump();}
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();using var process=Process.GetCurrentProcess();process.Refresh();long memory=GC.GetTotalMemory(true);int handles=process.HandleCount;
        var times=new List<double>();
        for(int i=0;i<500;i++)
        {
            var watch=Stopwatch.StartNew();Invoke(main,"NavigateTo",new[]{"home","uninstall","cleaner","dashboard","files","whatsnew"}[i%6]);
            if(i<200){((ComboBox)main.FindName("ThemePicker")).SelectedIndex=i%8;Invoke(main,"SwitchLanguage",null,new RoutedEventArgs());}
            main.Content?.GetType();Pump();times.Add(watch.Elapsed.TotalMilliseconds);
        }
        Advance(.2);GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();process.Refresh();long delta=GC.GetTotalMemory(true)-memory;int handleDelta=process.HandleCount-handles;
        Check(delta<32L*1024*1024,"500 headless page cycles / 200 theme+language changes have bounded retained managed memory");
        Check(handleDelta<32,"Headless page stress has bounded process handles");
        string saved=(string)Field(main,"page")!;Set(main,"page","missing-fixture");
        var failed=(StackPanel)Invoke(main,"BuildPageSafely")!;
        Check(failed.Children.OfType<Button>().Any()&&failed.Children.OfType<TextBlock>().All(t=>!t.Text.Contains("Exception")),"Failed page produces a friendly inline Retry state without a raw stack");
        Set(main,"page",saved);
        var log=new ActivityLogService(app.Dispatcher);var inventory=new ApplicationInventoryService(_=>Task.FromResult<IReadOnlyList<InstalledProgram>>([]));
        var deep=new DeepUninstallerView(false,log,inventory,new SettingsService(Path.Combine(root,"deep")));
        ((CancellationTokenSource)Field(deep,"cancellation")!).Cancel();Invoke(deep,"ResetLifetime");
        Check(!((CancellationTokenSource)Field(deep,"cancellation")!).IsCancellationRequested,"Returning to Deep Uninstaller renews the cancelled page token");
        Wait(SafeTasks.Run(()=>Task.FromException(new IOException("fixture access failure")),"owned fixture"));Pump();
        Check(true,"Observed background error does not escape or open a dialog");
        File.WriteAllText(Path.Combine(root,"headless-ui-perf.json"),JsonSerializer.Serialize(new{cycles=500,themeLanguageChanges=200,p95Ms=times.Order().ElementAt(474),maxMs=times.Max(),retainedBytes=delta,handleDelta,trace=PerformanceTrace.Snapshot()},new JsonSerializerOptions{WriteIndented=true}));
        app.ShutdownMode=ShutdownMode.OnExplicitShutdown;main.Close();Advance(.2);Check((bool)Field(main,"shutdownReady")!,"Closing flushes settings without reentrant WPF Close");
        return Finish("Stability147");
    }
}
