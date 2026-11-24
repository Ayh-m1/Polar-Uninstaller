using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using PolarUninstallerWpf;
using PolarUninstallerWpf.Services;

internal static class Dashboard144Checks
{
    internal static int Run()
    {
        int count=0,failures=0;
        void Check(bool pass,string label){count++;if(!pass)failures++;Console.WriteLine((pass?"PASS ":"FAIL ")+label);}
        var app=new App();app.InitializeComponent(); // Offscreen only; never Run/Show.
        string project=AppContext.BaseDirectory;
        while(!File.Exists(Path.Combine(project,"PolarUninstallerWpf.csproj")))project=Directory.GetParent(project)?.FullName??throw new IOException("Missing project");
        string root=Path.Combine(project,"Build","Validation","Dashboard144-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var ctor=typeof(MainWindow).GetConstructor(BindingFlags.NonPublic|BindingFlags.Instance,null,new[]{typeof(SettingsService),typeof(ActivityMetricsService)},null)!;
        var window=(MainWindow)ctor.Invoke(new object[]{new SettingsService(root),new ActivityMetricsService(root)});
        Check(CleanerSelection.Parse(null).SequenceEqual(new[]{TempCleanerService.Category.UserTemp,TempCleanerService.Category.SystemTemp}),"Fresh cleaner defaults select only user/system Temp");
        Check(CleanerSelection.Parse(Array.Empty<string>()).Length==0&&CleanerSelection.Parse(new[]{"RecycleBin"}).Single()==TempCleanerService.Category.RecycleBin,"Explicit empty and existing category choices remain unchanged");
        Check(CleanerSelection.Parse(new[]{"999"}).Length==0,"Unknown numeric category is not treated as a cleanup target");
        var estimates=new Dictionary<TempCleanerService.Category,long>{{TempCleanerService.Category.UserTemp,100},{TempCleanerService.Category.SystemTemp,200}};
        Check(CleanerSelection.EstimatedBytes(CleanerSelection.Parse(null),estimates)==300&&CleanerSelection.EstimatedBytes(new[]{TempCleanerService.Category.SystemTemp},estimates)==200,"Selected-size estimate changes when categories are toggled");
        var totals=new SpaceFreedService(Path.Combine(root,"totals"));
        Task.WhenAll(Enumerable.Range(0,20).Select(_=>totals.AddAsync(1000,"same-cleanup"))).GetAwaiter().GetResult();
        Check(totals.TotalBytes==1000,"Repeated completion signals count one cleanup only");
        Check(!totals.AddAsync(0,"zero").GetAwaiter().GetResult()&&totals.TotalBytes==1000,"Unknown or zero reclaimed bytes are not invented");
        var second=new SpaceFreedService(Path.Combine(root,"totals"));
        Check(second.TotalBytes==1000,"Recorded total survives a new service instance");
        Task.WhenAll(totals.AddAsync(300,"operation-a"),second.AddAsync(200,"operation-b")).GetAwaiter().GetResult();
        Check(new SpaceFreedService(Path.Combine(root,"totals")).TotalBytes==1500,"Independent writers merge increments under the file lock");
        string historyRoot=Path.Combine(root,"history");var history=new UninstallHistoryService(historyRoot);
        var capture=new UninstallHistoryCapture("fixture-uninstall","Disposable fixture","Fixture","Fixture","fixture",Path.Combine(root,"not-installed"),4096,"");
        Check(history.RecordAsync(capture).GetAwaiter().GetResult(),"Synthetic verified-removal recording completes without uninstalling any software");
        Check(new SpaceFreedService(Path.Combine(historyRoot,"totals")).TotalBytes==4096,"Known verified uninstall size feeds the cumulative total");
        Check(!history.RecordAsync(capture).GetAwaiter().GetResult()&&new SpaceFreedService(Path.Combine(historyRoot,"totals")).TotalBytes==4096,"Duplicate uninstall does not increase the total twice");
        history.ClearAsync().GetAwaiter().GetResult();
        Check(new SpaceFreedService(Path.Combine(historyRoot,"totals")).TotalBytes==4096,"Clearing recent history preserves lifetime freed-space totals");
        int scans=0;
        var inventory=new ApplicationInventoryService(_=>{scans++;return Task.FromResult<IReadOnlyList<InstalledProgram>>(new[]{new InstalledProgram("Fixture One","Fixture","1",Path.Combine(root,"one")),new InstalledProgram("Fixture Two","Fixture","1",Path.Combine(root,"two"))});});
        var snapshot=inventory.RefreshAsync().GetAwaiter().GetResult();
        var log=new ActivityLogService(app.Dispatcher);
        foreach(bool arabic in new[]{false,true})
        {
            var dashboard=new DashboardView(arabic,log,inventory:inventory,freedSpace:new SpaceFreedService(Path.Combine(root,"totals")));
            SelectableText Text(string name)=>(SelectableText)dashboard.FindName(name);
            var show=typeof(DashboardView).GetMethod("ShowDrive",BindingFlags.NonPublic|BindingFlags.Instance)!;
            Check(Text("ProgramsValue").Text==DisplayFormat.Number(snapshot.Count)&&scans==1,"Dashboard reuses shared inventory without a second scan ("+arabic+")");
            Check(Text("FreedValue").Text==DisplayFormat.Size(1500,false),"Dashboard shows the persisted freed-space total ("+arabic+")");
            var drive=new DriveSummary(@"C:\",100,15){Label="Fixture Disk"};
            Check(drive.ToString()=="C: (Fixture Disk)","Drive displays a friendly label rather than a record dump ("+arabic+")");
            show.Invoke(dashboard,new object[]{drive});
            Check(((FrameworkElement)dashboard.FindName("StorageWarning")).Visibility==Visibility.Visible,"Storage warning is visible at exactly 85 percent ("+arabic+")");
            Check(ReferenceEquals(((System.Windows.Shapes.Path)dashboard.FindName("UsageArc")).Stroke,app.Resources["LogWarning"])
                &&ReferenceEquals(((ProgressBar)dashboard.FindName("UsageBar")).Foreground,app.Resources["LogWarning"])
                &&ReferenceEquals(Text("UsagePercent").Foreground,app.Resources["LogWarning"]),"Ring, bar and percentage all use the theme warning token ("+arabic+")");
            show.Invoke(dashboard,new object[]{drive with{Free=16}});
            Check(((FrameworkElement)dashboard.FindName("StorageWarning")).Visibility==Visibility.Collapsed,"Storage warning clears below 85 percent ("+arabic+")");
            string destination="";dashboard.NavigationRequested+=page=>destination=page;
            ((Button)dashboard.FindName("OpenCleanerButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(destination=="cleaner","Storage shortcut navigates to Quick Cleaner ("+arabic+")");
            ((Button)dashboard.FindName("OpenUninstallerButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(destination=="uninstall","Empty-state action navigates to Deep Uninstaller ("+arabic+")");
            foreach(var size in new[]{new Size(1100,760),new Size(1920,1080)}){dashboard.Measure(size);dashboard.Arrange(new Rect(size));}
            Check(dashboard.FlowDirection==(arabic?FlowDirection.RightToLeft:FlowDirection.LeftToRight),"Offscreen normal/maximized layouts preserve direction ("+arabic+")");
        }
        Check(ReleaseNotesService.DisplayEntry("Improvements: Clearer icons")=="Clearer icons"&&ReleaseNotesService.DisplayEntry("إصلاحات: أسماء الأقراص")=="أسماء الأقراص","Repeated category prefixes are stripped only when rendered in either language");
        Check(ReleaseNotesService.DisplayEntry(@"C:\Program Files")==@"C:\Program Files","Prefix trimming does not change paths or unrelated text");
        Console.WriteLine($"{count} checks, {failures} failures; no real cleanup, uninstall, registry edits or visible windows.");
        return failures==0?0:1;
    }
}
