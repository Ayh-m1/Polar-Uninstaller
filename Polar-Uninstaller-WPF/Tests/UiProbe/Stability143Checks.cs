using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using PolarUninstallerWpf;
using PolarUninstallerWpf.Services;

internal static class Stability143Checks
{
    internal static int Run()
    {
        int failures=0,count=0;
        void Check(bool value,string label){count++;if(!value)failures++;Console.WriteLine((value?"PASS ":"FAIL ")+label);}
        var app=new App();app.InitializeComponent(); // No Run, Show, network or uninstall actions.
        var root=Path.Combine(AppContext.BaseDirectory,"fixtures","stability143-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string target=Path.Combine(root,"PolarUninstaller.exe"),otherTarget=Path.Combine(root,"Unrelated.exe");
        File.WriteAllText(target,"disposable shortcut target; never executed");
        File.WriteAllText(otherTarget,"unrelated disposable target; never executed");
        string icon=target; // The executable owns its icon; index 0 is the primary icon.
        string link=Path.Combine(root,"اختصار Polar.lnk"),otherLink=Path.Combine(root,"Unrelated.lnk");
        ShellShortcutService.Write(link,target,"--fixture argument",otherTarget);
        ShellShortcutService.Write(otherLink,otherTarget,"--keep",otherTarget);
        var otherBytes=File.ReadAllBytes(otherLink);
        Check(ShellShortcutService.UpdateIconForTarget(link,target,icon),"Matching Unicode shortcut is refreshed to the executable icon");
        var refreshed=ShellShortcutService.Read(link);
        Check(refreshed.Target==target&&refreshed.Arguments=="--fixture argument"&&refreshed.Icon==icon+",0","Refresh preserves target and arguments and points the icon at the EXE");
        Check(!ShellShortcutService.UpdateIconForTarget(link,target,icon),"Repeated icon refresh is idempotent");
        Check(!ShellShortcutService.UpdateIconForTarget(otherLink,target,icon)&&File.ReadAllBytes(otherLink).SequenceEqual(otherBytes),"Unrelated shortcut remains byte-for-byte unchanged");
        var metrics=new ActivityMetricsService(root);
        Parallel.For(0,500,_=>metrics.Record(false));
        Check(metrics.Snapshot().Clicks==500,"Concurrent click accounting retains all 500 clicks");
        metrics.ResetAsync().GetAwaiter().GetResult();
        Check(metrics.Snapshot().Clicks==0,"Reset remains serialized against background persistence");
        Check(File.ReadAllText(Path.Combine(root,"activity-metrics.json")).Contains("\"Clicks\":0"),"Reset persists zero rather than a stale click snapshot");
        var log=new ActivityLogService(app.Dispatcher);
        for(int i=0;i<1200;i++)log.Write("INFO","fixture "+i);
        Check(log.Entries.Count==0,"Logging is deferred out of the originating UI handler");
        int batches=0;log.BatchAdded+=_=>batches++;
        var drain=typeof(ActivityLogService).GetMethod("Drain",BindingFlags.NonPublic|BindingFlags.Instance)!;
        for(int i=0;i<12;i++)drain.Invoke(log,null);
        Check(log.Entries.Count==1000,"Displayed log stays bounded at 1000 entries");
        Check(batches==12,"1200 entries cause 12 batch notifications rather than 1200 scroll requests");
        var text=new TextBlock();var run=new Run("storage item");text.Inlines.Add(run);
        Check(ReferenceEquals(UiTree.Parent(run),text),"Inline Run resolves to its text parent without VisualTreeHelper exceptions");
        var button=new Button{Content=text};
        var hit=typeof(DashboardView).GetMethod("IsWithinButton",BindingFlags.Static|BindingFlags.NonPublic)!;
        Check((bool)hit.Invoke(null,new object[]{run})!,"Dashboard recognizes a button hit originating from inline text");
        var ctor=typeof(MainWindow).GetConstructor(BindingFlags.NonPublic|BindingFlags.Instance,null,new[]{typeof(SettingsService),typeof(ActivityMetricsService)},null)!;
        var window=(MainWindow)ctor.Invoke(new object[]{new SettingsService(root),metrics});
        var content=(ContentControl)window.FindName("PageContent");
        var navigate=typeof(MainWindow).GetMethod("NavigateTo",BindingFlags.NonPublic|BindingFlags.Instance)!;
        var toggle=typeof(MainWindow).GetMethod("ToggleSidebar",BindingFlags.NonPublic|BindingFlags.Instance)!;
        foreach(string page in new[]{"home","uninstall","cleaner","dashboard","files","whatsnew"})
        {
            navigate.Invoke(window,new object[]{page});var original=content.Content;
            for(int i=0;i<12;i++){navigate.Invoke(window,new object[]{page});toggle.Invoke(window,new object[]{window,new RoutedEventArgs()});}
            Check(ReferenceEquals(original,content.Content),page+": repeated navigation and sidebar toggles preserve page instance");
        }
        Check(AppInfo.Version=="1.0.144"&&ReleaseNotesService.LoadCurrent(false)?.Entries.Count==7&&ReleaseNotesService.LoadCurrent(true)?.Entries.Count==7,"v1.0.144 and concise bilingual notes embedded");
        Console.WriteLine($"{count} checks, {failures} failures. No personal application removal or live UI test performed.");
        return failures==0?0:1;
    }
}

