using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Xml.Linq;
using PolarUninstallerWpf;
using PolarUninstallerWpf.Services;

internal static partial class Parts146Checks
{
    private static int checks,failures;
    private static string project="",root="";
    private static Application app=null!;
    private static MainWindow main=null!;
    private static void Check(bool ok,string name){checks++;if(!ok)failures++;Console.WriteLine((ok?"PASS ":"FAIL ")+name);}
    private static object? Field(object o,string name)=>o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(o);
    private static void Set(object o,string name,object? value)=>o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(o,value);
    private static object? Invoke(object o,string name,params object?[] args)=>o.GetType().GetMethods(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(o,args);
    private static void Pump(){var frame=new DispatcherFrame();app.Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}
    private static void Wait(Task task){var watch=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted&&watch.Elapsed<TimeSpan.FromSeconds(30))Pump();if(!task.IsCompleted)throw new TimeoutException("Fixture operation timed out");task.GetAwaiter().GetResult();}
    private static void Setup(string part)
    {
        app=new Application();SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        project=AppContext.BaseDirectory;while(!File.Exists(Path.Combine(project,"PolarUninstallerWpf.csproj")))project=Directory.GetParent(project)!.FullName;
        root=Path.Combine(project,"Build","Validation","Parts146-"+part+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var xml=XDocument.Load(Path.Combine(project,"App.xaml"));XNamespace ns="http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        app.Resources=(ResourceDictionary)System.Windows.Markup.XamlReader.Parse(new XElement(ns+"ResourceDictionary",new XAttribute(XNamespace.Xmlns+"x","http://schemas.microsoft.com/winfx/2006/xaml"),xml.Root!.Element(ns+"Application.Resources")!.Elements()).ToString().Replace("clr-namespace:PolarUninstallerWpf\"","clr-namespace:PolarUninstallerWpf;assembly=PolarUninstaller\""));
        var ctor=typeof(MainWindow).GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(SettingsService),typeof(ActivityMetricsService)},null)!;
        main=(MainWindow)ctor.Invoke(new object[]{new SettingsService(Path.Combine(root,"settings")),new ActivityMetricsService(root)});
        DashboardStorageService.UseCacheDirectory(Path.Combine(root,"cache"));
    }
    private static int Finish(string part){Console.WriteLine($"Part {part}: {checks} checks, {failures} failures. Only owned fixtures/mocks. Artifacts: {root}");return failures==0?0:1;}
    private sealed class InlineProgress(Action<int> action):IProgress<int>{public void Report(int value)=>action(value);}
    internal static int RunA()
    {
        Setup("A");var service=new DashboardStorageService();string scope=Path.Combine(root,"scope"),a=Path.Combine(scope,"A"),inner=Path.Combine(a,"inner");Directory.CreateDirectory(inner);
        File.WriteAllBytes(Path.Combine(a,"one.bin"),new byte[10]);File.WriteAllBytes(Path.Combine(inner,"two.bin"),new byte[20]);File.WriteAllBytes(Path.Combine(scope,"keep.bin"),new byte[5]);
        var scan=service.ScanAsync(scope,default);Wait(scan);var before=scan.Result;
        Check(before.TotalBytes==35&&before.FilesScanned==3&&before.FoldersScanned==3,"A scan indexes exact file bytes and directory counts");
        Check(before.FolderIndex.Single(f=>f.Path==a) is{Bytes:30,Files:2,Folders:2},"Nested folder aggregate includes descendants once");
        File.WriteAllBytes(Path.Combine(scope,"new.bin"),new byte[7]);var fresh=service.ScanAsync(scope,default);Wait(fresh);
        Check(fresh.Result.TotalBytes==42&&fresh.Result.FilesScanned==4&&fresh.Result.CompletedAt>before.CompletedAt,"Explicit refresh scans current files and changes timestamp, bypassing previous cache");
        before=fresh.Result;var batch=new FileChangeBatch(Guid.NewGuid(),DateTimeOffset.UtcNow,FileChangeKind.Deleted,new[]{new FileChange(a,true),new FileChange(Path.Combine(inner,"two.bin"),true),new FileChange(Path.Combine(scope,"keep.bin"),false)});
        var reduced=StorageCacheUpdates.Apply(before,batch);
        Check(reduced.TotalBytes==12&&reduced.FilesScanned==2&&reduced.FoldersScanned==1,"Nested parent/child deletion subtracts bytes and counts exactly once; failures ignored");
        Check(reduced.LargestFolders.Count==0&&reduced.Largest.All(f=>!StorageCacheUpdates.Within(f.Path,a)),"Deleted folder and all descendants leave rankings");
        Check(reduced.FolderIndex.Single().Bytes==12&&reduced.FolderIndex.Single().Files==2,"Parent/root totals update without rescanning");
        Check(StorageCacheUpdates.Apply(reduced,batch)==reduced&&StorageCacheUpdates.Apply(reduced,batch with{Id=Guid.NewGuid()}).TotalBytes==12,"Repeated notifications are idempotent, including duplicate producer IDs");
        Check(StorageCacheUpdates.Within(Path.Combine(scope,"x"),Path.GetPathRoot(scope)!)&&!StorageCacheUpdates.Within(a+"ardvark",a),"Drive-root and prefix-boundary containment are correct");
        var failed=StorageCacheUpdates.Apply(before,batch with{Id=Guid.NewGuid(),Changes=new[]{new FileChange(a,false)}});Check(ReferenceEquals(failed,before),"Failed deletion leaves cached totals untouched");
        var notifications=new List<FileChangeBatch>();void Observe(FileChangeBatch b)=>notifications.Add(b);FileChangeHub.Changed+=Observe;
        try{Wait(new BrowserOperationsService().DeleteAsync(new[]{a},true,default));}finally{FileChangeHub.Changed-=Observe;}
        Check(!Directory.Exists(a)&&DashboardStorageService.TryGetCached(scope)?.TotalBytes==12,"Real deletion of owned fixture updates cache through the shared publisher");
        Check(notifications.Any(n=>n.Changes.Any(c=>c.Path==a&&c.Success)),"Shared notification carries verified successful paths");
        DashboardStorageService.UseCacheDirectory(Path.Combine(root,"cache"));Wait(DashboardStorageService.LoadCacheAsync());var restored=DashboardStorageService.TryGetCached(scope)!;
        Check(restored.TotalBytes==12&&restored.FilesScanned==2&&restored.Largest.All(f=>File.Exists(f.Path)),"Disk reload cannot resurrect deleted fixture entries");
        Wait(FileChangeHub.PublishAsync(new[]{new FileChange(Path.Combine(scope,"keep.bin"),true,5)},FileChangeKind.HiddenFromDashboard));
        Check(File.Exists(Path.Combine(scope,"keep.bin"))&&DashboardStorageService.TryGetCached(scope)!.TotalBytes==12,"Dashboard remove-from-list preserves files and actual scope totals");
        string many=Path.Combine(root,"many");Directory.CreateDirectory(many);for(int i=1;i<=510;i++)File.WriteAllBytes(Path.Combine(many,i+".bin"),new byte[i]);var manyScan=service.ScanAsync(many,default);Wait(manyScan);
        var refill=StorageCacheUpdates.Apply(manyScan.Result,new(Guid.NewGuid(),DateTimeOffset.UtcNow,FileChangeKind.Deleted,new[]{new FileChange(Path.Combine(many,"510.bin"),true)}));
        Check(refill.Largest.Count==500&&refill.Largest[0].Bytes==509&&refill.Largest.Any(f=>f.Bytes==10),"Ranking refills from cached entries beyond the visible/top-file list");
        var sparse=before with{IndexComplete=false,FileIndex=before.FileIndex.Take(1).ToArray()};var sparseRemoved=StorageCacheUpdates.Apply(sparse,batch);
        Check(sparseRemoved.TotalBytes==12&&sparseRemoved.FilesScanned==2,"Bounded file index still uses exact nested-folder aggregates");
        string race=Path.Combine(root,"race");Directory.CreateDirectory(race);string racingFile=Path.Combine(race,"removed.bin");File.WriteAllBytes(racingFile,new byte[10]);File.WriteAllBytes(Path.Combine(race,"keep.bin"),new byte[5]);
        bool published=false;var racingScan=service.ScanAsync(race,default,new InlineProgress(_=>{if(published)return;published=true;File.Delete(racingFile);FileChangeHub.PublishAsync(new[]{new FileChange(racingFile,true,10)}).GetAwaiter().GetResult();}));Wait(racingScan);
        Check(racingScan.Result.TotalBytes==5&&racingScan.Result.FilesScanned==1&&!racingScan.Result.Largest.Any(f=>f.Path==racingFile),"Deletion during scan is reconciled before both UI result and disk cache publish");
        var clearingScan=service.ScanAsync(race,default,new InlineProgress(_=>DashboardStorageService.ClearCache()));Wait(clearingScan);
        Check(DashboardStorageService.TryGetCached(race)==null,"Clear Cache during scan cannot be repopulated by an old scan");
        foreach(bool ar in new[]{false,true})
        {
            var log=new ActivityLogService(app.Dispatcher);var inv=new ApplicationInventoryService(_=>Task.FromResult<IReadOnlyList<InstalledProgram>>(new[]{new InstalledProgram("Fixture","Fixture","1",scope)}));
            var dashboard=new DashboardView(ar,log,null,inv,new SpaceFreedService(Path.Combine(root,"freed")),new UninstallHistoryService(Path.Combine(root,"history")),new ManualStorageService(Path.Combine(root,"manual")));
            Invoke(dashboard,"ApplySnapshot",restored,true);File.WriteAllBytes(Path.Combine(scope,"latest.bin"),new byte[9]);var refresh=(Task)Invoke(dashboard,"RefreshAllAsync")!;
            Check(!((Button)dashboard.FindName("RefreshButton")).IsEnabled,"Header refresh exposes busy state AR="+ar);Wait(refresh);Pump();
            var applied=(StorageSnapshot)Field(dashboard,"displayedSnapshot")!;
            Check(applied.TotalBytes==21&&applied.FilesScanned==3&&applied.CompletedAt>restored.CompletedAt,"Header refresh replaces current scope scan and timestamp AR="+ar);
            Check(((SelectableText)dashboard.FindName("ProgramsValue")).Text=="1"&&((Button)dashboard.FindName("RefreshButton")).IsEnabled,"Overview count refreshes and button recovers AR="+ar);
            Check(log.Entries.Any(e=>e.Severity=="INFO"&&e.Message.Contains(ar?"حُدّثت":"Dashboard refreshed")),"Successful refresh records INFO in current language AR="+ar);
            Set(dashboard,"scanRoot",Path.Combine(root,"missing"));Wait((Task)Invoke(dashboard,"RefreshAllAsync")!);Pump();
            Check(ReferenceEquals(Field(dashboard,"displayedSnapshot"),applied)&&log.Entries.Any(e=>e.Severity=="ERROR"),"Refresh failure preserves old scan and reports ERROR AR="+ar);
            var slowInv=new ApplicationInventoryService(async token=>{await Task.Delay(Timeout.Infinite,token);return Array.Empty<InstalledProgram>();});
            var slow=new DashboardView(ar,log,null,slowInv,new SpaceFreedService(Path.Combine(root,"slow")),new UninstallHistoryService(Path.Combine(root,"history")),new ManualStorageService(Path.Combine(root,"manual")));Invoke(slow,"ApplySnapshot",applied,true);
            var pending=(Task)Invoke(slow,"RefreshAllAsync")!;slow.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));Pump();
            Check(!pending.IsCompleted,"Unloading the view does not cancel an in-flight refresh AR="+ar);
            slow.CancelWork();Wait(pending);
            Check(((Button)slow.FindName("RefreshButton")).IsEnabled,"Explicit cancellation recovers the refresh button AR="+ar);
        }
        Check(AppInfo.Version=="1.0.150","Version stays v1.0.150");return Finish("A");
    }
}
