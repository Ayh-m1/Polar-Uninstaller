using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Xml.Linq;
using Microsoft.Win32;
using PolarUninstallerWpf;
using PolarUninstallerWpf.Services;

internal static class Audit146Checks
{
    internal static int Run()
    {
        int checks=0,failures=0;void Check(bool ok,string name){checks++;if(!ok)failures++;Console.WriteLine((ok?"PASS ":"FAIL ")+name);}
        var app=new Application();SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        string project=AppContext.BaseDirectory;while(!File.Exists(Path.Combine(project,"PolarUninstallerWpf.csproj")))project=Directory.GetParent(project)!.FullName;
        var xml=XDocument.Load(Path.Combine(project,"App.xaml"));XNamespace ns="http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        app.Resources=(ResourceDictionary)System.Windows.Markup.XamlReader.Parse(new XElement(ns+"ResourceDictionary",new XAttribute(XNamespace.Xmlns+"x","http://schemas.microsoft.com/winfx/2006/xaml"),xml.Root!.Element(ns+"Application.Resources")!.Elements()).ToString().Replace("clr-namespace:PolarUninstallerWpf\"","clr-namespace:PolarUninstallerWpf;assembly=PolarUninstaller\""));
        string root=Path.Combine(project,"Build","Validation","Audit146-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        object? Invoke(object o,string name,params object?[] args)=>o.GetType().GetMethods(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(o,args);
        object? Field(object o,string name)=>o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(o);
        void Pump(){var f=new DispatcherFrame();app.Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>f.Continue=false));Dispatcher.PushFrame(f);}
        void Wait(Task task){var watch=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted&&watch.Elapsed<TimeSpan.FromSeconds(10))Pump();task.GetAwaiter().GetResult();}
        string folder=Path.Combine(root,"Unicode مسار with spaces");Directory.CreateDirectory(folder);string exe=Path.Combine(folder,"uninstall.exe");File.WriteAllText(exe,"Not an executable. Parsing fixture only.");
        Check(UninstallCommandService.TryParse('"'+exe+'"'+" /S --name \"two words\"",out var parsed,out var arguments)&&parsed==exe&&arguments.SequenceEqual(new[]{"/S","--name","two words"}),"Quoted executable and quoted arguments preserve boundaries");
        Check(UninstallCommandService.TryParse(exe+" /quiet",out parsed,out arguments)&&parsed==exe&&arguments.SequenceEqual(new[]{"/quiet"}),"Unquoted existing executable path with Unicode/spaces is parsed safely");
        Check(!UninstallCommandService.TryParse(Path.Combine(folder,"missing.exe")+" /S",out _,out _),"Missing uninstaller cannot execute");
        Check(!UninstallCommandService.TryParse("cmd.exe /c echo dangerous",out _,out _)&&!UninstallCommandService.TryParse('"'+exe+'"'+"\n/S",out _,out _),"Shell and multiline command input are blocked");
        string guid="{11111111-2222-3333-4444-555555555555}";
        Check(UninstallCommandService.TryParse("msiexec.exe /I"+guid+" /qn",out _,out arguments)&&arguments.SequenceEqual(new[]{"/X"+guid,"/qn"}),"Attached MSI install action becomes uninstall; quiet flags survive");
        Check(UninstallCommandService.TryParse("msiexec /i "+guid+" /INSTALLDIR=C:\\Fixture",out _,out arguments)&&arguments[0]=="/X"&&arguments[2]=="/INSTALLDIR=C:\\Fixture","MSI /INSTALLDIR option is not incorrectly rewritten");
        Check(!UninstallCommandService.TryParse("msiexec /INSTALLDIR=C:\\Fixture",out _,out _),"MSI without a removal target is not accepted");
        Check(!UninstallCommandService.TryParse("msiexec /X",out _,out _)&&!UninstallCommandService.TryParse("msiexec /X not-a-product",out _,out _),"Incomplete or invalid MSI targets cannot execute");
        Check(UninstallCommandService.IsQuiet(new[]{"/VERYSILENT"})&&!UninstallCommandService.IsQuiet(new[]{"--normal"}),"Quiet-switch detection is explicit");
        Check(UninstallCommandService.ResolveRegistered("",'"'+exe+'"'+" /S","","",false).EndsWith(" /S"),"QuietUninstallString is used only as a missing-command fallback");
        Check(UninstallCommandService.ResolveRegistered("","",'"'+exe+'"'+" /modify","",false)=="","Arbitrary EXE ModifyPath is not treated as uninstall");
        Check(UninstallCommandService.ResolveRegistered("","","msiexec /I "+guid,"",true).Contains("/X"),"MSI ModifyPath has a verified removal action");
        Check(UninstallCommandService.ResolveRegistered("","","",guid,true)=="msiexec.exe /X "+guid,"WindowsInstaller product GUID supplies the official MSI route");
        Check(BidiText.Localize(true,"","ملفات Prefetch") == "ملفات "+DisplayFormat.Isolate("Prefetch"),"Arabic Prefetch label isolates the technical term without reordering it");
        Check(BidiText.IsolateLatin("لـ Bloons TD 6")=="لـ "+DisplayFormat.Isolate("Bloons TD 6"),"Mixed game names and digits stay in one LTR isolate");
        Check(BidiText.IsolateLatin(DisplayFormat.Isolate("C:\\Folder 12"))==DisplayFormat.Isolate("C:\\Folder 12"),"Existing display isolates are not nested or changed");
        Check(BrowserText.Get("ItemCount",true,12).Contains(DisplayFormat.Isolate("12")),"Resource substitution occurs before numeric isolation");
        var first=new InstalledProgram("Fixture Alpha","Fixture Publisher","1",folder,SizeKb:10);
        var other=first with{Name="Fixture Beta"};
        Check(InventoryMerger.Merge(new[]{first,first}).Count==1,"Duplicate identity rows merge");
        Check(InventoryMerger.Merge(new[]{first,other}).Count==2,"Different products sharing a folder remain separate");
        Check(InventoryMerger.Merge(new[]{first,first with{Version="2"}}).Count==2,"Distinct installed versions sharing a folder are not collapsed");
        Check(!UninstallVerificationService.Matches(first,other)&&!UninstallVerificationService.Matches(first,first with{Publisher="Different publisher"}),"Verification cannot adopt a different product or publisher sharing a folder");
        Check(InventoryMerger.Merge(new[]{first with{Name=" "},first}).Count==1,"Whitespace-only names are excluded");
        string gogGame=Path.Combine(root,"GOG Games","Yakuza 0");Directory.CreateDirectory(gogGame);
        string gogUninstall='"'+Path.Combine(gogGame,"unins000.exe")+'"';
        var gogRegistration=new RegistryLeftover(RegistryHive.LocalMachine,RegistryView.Registry32,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\1103602225_is1","Yakuza 0",gogGame+"\\",gogUninstall,"1.015a");
        var yakuzaWindows=new InstalledProgram("Yakuza 0","GOG.com","1.015a",gogGame+"\\","",2153,gogUninstall,"Windows","",null,null){Registration=gogRegistration};
        var yakuzaGog=new InstalledProgram("Yakuza 0","GOG.com","56599486495894936",gogGame,"",null,"","GOG","gog:1103602225",null,null,true);
        var yakuza=InventoryMerger.Merge(new[]{yakuzaWindows,yakuzaGog,yakuzaGog with{Version=""}});
        Check(yakuza.Count==1,"Yakuza 0 registry and repeated GOG rows merge into one installation");
        Check(yakuza[0].Version=="1.015a","Merged row keeps the real version instead of the GOG product id");
        Check(yakuza[0].SizeKb is null,"Merged row drops the registry EstimatedSize so the real folder size is measured");
        Check(yakuza[0].Location==gogGame,"Merged row uses the normalized install path without a trailing separator");
        Check(string.Equals(yakuza[0].Uninstall,gogUninstall,StringComparison.OrdinalIgnoreCase),"Merged row keeps the single registry uninstall command");
        Check(yakuza[0].Identifier=="gog:1103602225"&&yakuza[0].Sources!.Contains("Windows")&&yakuza[0].Sources!.Contains("GOG"),"Merged row retains launcher identity and metadata");
        Check(InventoryMerger.Merge(new[]{yakuzaGog with{Version="1.0"},yakuzaGog with{Version="2.0"}}).Count==1,"Same launcher and path merge even when build versions differ");
        string sdbKey=@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{22221111-1111-1111-1111-111111111111}.sdb";
        string sdbCommand=Path.Combine(Environment.SystemDirectory,"sdbinst.exe")+" -u \"C:\\Windows\\AppPatch\\CustomSDB\\fixture.sdb\"";
        var sdb64=new InstalledProgram("Microsoft Windows Application Compatibility Fix Database","","","","",null,sdbCommand,"Windows","",null,null){Registration=new(RegistryHive.LocalMachine,RegistryView.Registry64,sdbKey,"Microsoft Windows Application Compatibility Fix Database","",sdbCommand,"")};
        var sdb=InventoryMerger.Merge(new[]{sdb64,sdb64 with{Registration=sdb64.Registration! with{View=RegistryView.Registry32}}});
        Check(sdb.Count==1,"Identical registry rows seen through both registry views deduplicate");
        Check(sdb[0].Uninstall.Length>0,"A system database row that has a real uninstaller is retained");
        Check(InventoryMerger.Merge(new[]{sdb64 with{Uninstall="",Registration=sdb64.Registration! with{Command=""}}}).Count==0,"A pure system database row with no uninstaller, publisher or version is hidden");
        string moved=Path.Combine(root,"other install");Directory.CreateDirectory(moved);
        var a=first with{Identifier="steam:4567",Source="Steam"};var b=a with{Location=moved};
        var inventory=new ApplicationInventoryService(_=>Task.FromResult<IReadOnlyList<InstalledProgram>>(new[]{a,b}));Wait(inventory.RefreshAsync());inventory.NotifyRemoved(a);
        Check(inventory.Current!.Count==1&&inventory.Current.Programs[0].Location==moved,"Removing one game identity does not erase a second live installation");
        string payload=Path.Combine(folder,"app.exe");var stale=new ApplicationInventoryService(_=>Task.FromResult<IReadOnlyList<InstalledProgram>>(new[]{first}));Wait(stale.RefreshAsync());stale.NotifyRemoved(first,new[]{payload});Wait(stale.RefreshAsync());
        Check(stale.Current!.Count==0&&stale.Current.IsComplete,"An unchanged leftover registration cannot re-add verified missing payload in this session");
        File.WriteAllText(payload,"Synthetic reinstall marker");Wait(stale.RefreshAsync());Check(stale.Current!.Count==1,"A reappearing captured executable restores the reinstalled app to inventory");File.Delete(payload);
        bool blocked=false;try{DirectRemovalService.Validate(first with{Location=Environment.GetFolderPath(Environment.SpecialFolder.Windows)},new[]{first});}catch(IOException){blocked=true;}Check(blocked,"Windows folders are blocked without attempting deletion");
        blocked=false;try{DirectRemovalService.Validate(first,new[]{first,other});}catch(IOException){blocked=true;}Check(blocked,"Shared product directory is blocked without attempting deletion");
        var launch=DateTime.UtcNow;var processes=new[]{new UninstallProcessTracker.ProcessIdentity(12,11,launch.AddMilliseconds(10)),new UninstallProcessTracker.ProcessIdentity(13,12,launch.AddMilliseconds(20)),new UninstallProcessTracker.ProcessIdentity(14,11,launch.AddSeconds(-5)),new UninstallProcessTracker.ProcessIdentity(15,999,launch.AddMilliseconds(30))};
        var descendants=UninstallProcessTracker.Descendants(processes,new Dictionary<int,DateTime>{{11,launch}},launch);
        Check(descendants.Select(p=>p.Id).SequenceEqual(new[]{12,13}),"Child/grandchild discovery excludes unrelated and pre-launch processes");
        var processStart=new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};processStart.ArgumentList.Add(typeof(Audit146Checks).Assembly.Location);processStart.ArgumentList.Add("--audit-process-parent");
        var launched=DateTime.UtcNow;var fixtureProcess=System.Diagnostics.Process.Start(processStart)!;var timer=System.Diagnostics.Stopwatch.StartNew();Wait(Task.Run(()=>UninstallProcessTracker.WaitAsync(fixtureProcess,launched)));
        Check(timer.Elapsed>=TimeSpan.FromMilliseconds(2500),"Real disposable bootstrap/child processes are tracked through completion and grace window");
        string registryPath=@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PolarAudit146-"+Guid.NewGuid().ToString("N");
        try
        {
            using(var key=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(registryPath))
            {key.SetValue("DisplayName","Polar Audit146 Disposable");key.SetValue("InstallLocation",folder);key.SetValue("DisplayVersion","1");}
            var identity=new RegistryLeftover(Microsoft.Win32.RegistryHive.CurrentUser,Microsoft.Win32.RegistryView.Registry64,registryPath,"Polar Audit146 Disposable",folder,"","1");
            var registered=first with{Name=identity.Name,Uninstall="msiexec.exe /X "+guid,RequiresOfficialRemoval=true,Registration=identity};
            var scanner=new SharedLeftoverScanner(new LeftoverService(Path.Combine(root,"registry-backups")));var captured=scanner.CaptureAsync(registered,CancellationToken.None);Wait(captured);
            Check(captured.Result.Registry.Any(r=>r.Path==registryPath),"Generated MSI/quiet route still captures exact original empty-command registration");
            Check(LeftoverService.RegistrationExists(registered),"Exact HKCU registration identity is checked before monitoring");
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(registryPath);
            Check(!LeftoverService.RegistrationExists(registered),"Only the disposable exact registration disappearance is detected");
        }
        finally{Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(registryPath,false);}
        int active=0,maxActive=0;var order=new List<int>();UninstallBatchResult? batch=null;
        Wait(Task.Run(async()=>batch=await UninstallBatchService.RunAsync(new[]{1,2,3},async(i,_)=>{active++;maxActive=Math.Max(maxActive,active);try{order.Add(i);await Task.Yield();if(i==2)throw new IOException("Fixture failure");return i==1?BatchItemOutcome.Succeeded:BatchItemOutcome.Skipped;}finally{active--; }},CancellationToken.None)));
        Check(maxActive==1&&order.SequenceEqual(new[]{1,2,3})&&batch==new UninstallBatchResult(1,1,1,false),"Batch is sequential, isolates a failure and reports success/failure/skipped");
        using(var cancel=new CancellationTokenSource())
        {
            Wait(Task.Run(async()=>batch=await UninstallBatchService.RunAsync(new[]{1,2,3},(_,_)=>{cancel.Cancel();return Task.FromResult(BatchItemOutcome.Succeeded);},cancel.Token)));
            Check(batch==new UninstallBatchResult(1,0,2,true),"Cancellation prevents later items from starting");
        }
        var ctor=typeof(MainWindow).GetConstructor(BindingFlags.NonPublic|BindingFlags.Instance,null,new[]{typeof(SettingsService),typeof(ActivityMetricsService)},null)!;
        var main=(MainWindow)ctor.Invoke(new object[]{new SettingsService(Path.Combine(root,"main")),new ActivityMetricsService(root)});
        typeof(MainWindow).GetField("currentDirectory",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(main,root);
        var sidebarKeys=new[]{"home","uninstall","cleaner","dashboard","files","whatsnew"};
        foreach(bool ar in new[]{false,true})
        {
            if((bool)Field(main,"arabic")! !=ar)Invoke(main,"SwitchLanguage",main,new RoutedEventArgs());
            for(int i=0;i<sidebarKeys.Length;i++)
            {
                var button=((StackPanel)main.FindName("Nav")).Children.OfType<Button>().ElementAt(i);button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check((string)Field(main,"page")! ==sidebarKeys[i],"Actual sidebar click routes to "+sidebarKeys[i]+" AR="+ar);
            }
            Invoke(main,"ToggleLog",main,new RoutedEventArgs());Check(((ListBox)main.FindName("Log")).Visibility==Visibility.Collapsed,"Activity Log collapse works AR="+ar);Invoke(main,"ToggleLog",main,new RoutedEventArgs());
            var picker=(ComboBox)main.FindName("ThemePicker");foreach(var theme in ThemeCatalog.All.Keys){picker.SelectedItem=theme;Check(new SettingsService(Path.Combine(root,"main")).Load().Theme==theme,"Theme applies and persists: "+theme+" AR="+ar);}
        }
        var log=new ActivityLogService(app.Dispatcher);
        foreach(bool ar in new[]{false,true})
        {
            var settings=new SettingsService(Path.Combine(root,ar?"ar":"en"));var deep=new DeepUninstallerView(ar,log,new ApplicationInventoryService(_=>Task.FromResult<IReadOnlyList<InstalledProgram>>(new[]{first,other})),settings);
            var home=new HomeView(ar,new ApplicationInventoryService());var routes=new List<string>();home.NavigationRequested+=routes.Add;
            IEnumerable<Button> Buttons(DependencyObject element)
            {if(element is Button button)yield return button;foreach(var child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>())foreach(var found in Buttons(child))yield return found;}
            foreach(var card in Buttons(home).Where(b=>b.Tag is string).ToArray()){string expected=(string)card.Tag;card.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(routes.Last()==expected,"Home card emits its actual navigation command: "+expected+" AR="+ar);}
            var list=(ListBox)deep.FindName("List");var search=(TextBox)deep.FindName("Search");
            var cards=new List<ProgramCard>{new(first,ar),new(other,ar),new(a with{Location=moved},ar)};
            Invoke(deep,"ApplyDeepSort",cards);list.SelectedItems.Add(cards.Single(c=>c.Name==first.Name&&c.Program.Identifier.Length==0));list.SelectedItems.Add(cards.Single(c=>c.Name==other.Name));
            Check(list.SelectionMode==SelectionMode.Extended&&list.SelectedItems.Count==2,"Deep list supports multiple selections AR="+ar);
            ((ComboBox)deep.FindName("SortSelector")).SelectedIndex=1;Check(list.SelectedItems.Count==2,"Sorting preserves selected identities AR="+ar);
            search.Text="Beta";Check(list.Items.Count==1&&list.SelectedItems.Count==1,"Search deliberately deselects hidden items AR="+ar);
            search.Clear();((ComboBox)Field(deep,"kindSelector")!).SelectedIndex=2;Check(list.Items.Count==1&&list.Items.Cast<ProgramCard>().All(c=>GameClassificationService.IsGame(c.Program)),"Games filter shows only classified games AR="+ar);
            ((ComboBox)Field(deep,"kindSelector")!).SelectedIndex=1;Check(list.Items.Count==2,"Programs filter excludes games AR="+ar);
            var bad=new ProgramCard(first with{SizeKb=long.MaxValue,Publisher="",Version="",Location=""},ar);Check(bad.Size=="—"&&bad.Publisher=="—"&&bad.Version=="—"&&bad.Location=="—","Bad/unknown metadata has clean non-overflowing placeholders AR="+ar);
            cards[0].UpdateSize(999);Check(cards[0].MeasuredSizeKb==999,"Measured size is exposed to sorting AR="+ar);
            typeof(DeepUninstallerView).GetField("busy",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(deep,true);Wait((Task)Invoke(deep,"UninstallSelectionAsync",true)!);Check((bool)Field(deep,"busy")!,"Second start is ignored while busy AR="+ar);
            using var client=new HttpClient(new OfflineHandler());var feedback=new FeedbackWindow(ar,log,client);
            Invoke(feedback,"SendClick",feedback,new RoutedEventArgs());Check(!string.IsNullOrWhiteSpace(((TextBlock)feedback.FindName("Status")).Text),"Feedback validates an empty category without sending AR="+ar);
            ((RadioButton)feedback.FindName("Bug")).IsChecked=true;((TextBox)feedback.FindName("MessageInput")).Text="Disposable offline feedback fixture";
            Invoke(feedback,"SendClick",feedback,new RoutedEventArgs());while((bool)Field(feedback,"sending")!)Pump();
            Check(((TextBox)feedback.FindName("MessageInput")).Text.Contains("fixture")&&((Button)feedback.FindName("SendButton")).IsEnabled,"Offline feedback retains text and re-enables controls AR="+ar);
            using var successClient=new HttpClient(new SuccessHandler());var successful=new FeedbackWindow(ar,log,successClient);((RadioButton)successful.FindName("Bug")).IsChecked=true;
            ((TextBox)successful.FindName("MessageInput")).Text="ab";Invoke(successful,"SendClick",successful,new RoutedEventArgs());Check(((TextBox)successful.FindName("MessageInput")).Text=="ab"&&!(bool)Field(successful,"sending")!,"Short feedback is rejected before any request AR="+ar);
            ((TextBox)successful.FindName("MessageInput")).Text="Disposable successful feedback fixture";Invoke(successful,"SendClick",successful,new RoutedEventArgs());while((bool)Field(successful,"sending")!)Pump();
            Check(((TextBox)successful.FindName("MessageInput")).Text.Length==0&&((Button)successful.FindName("SendButton")).IsEnabled,"Mock successful feedback clears input and restores controls AR="+ar);
            typeof(DeepUninstallerView).GetField("busy",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(deep,false);
            Wait((Task)Invoke(deep,"RunRemovalActionAsync",(Func<Task>)(()=>Task.FromException(new InvalidOperationException("Synthetic action failure"))))!);
            Check(((TextBlock)deep.FindName("Status")).Text.Length>0,"Unexpected advanced-action failure gets visible feedback instead of escaping AR="+ar);
            var failedInventory=new DeepUninstallerView(ar,log,new ApplicationInventoryService(_=>Task.FromException<IReadOnlyList<InstalledProgram>>(new IOException("Synthetic inventory failure"))),settings);
            Wait((Task)Invoke(failedInventory,"RefreshSharedInventoryAsync")!);Pump();Check(log.Entries.Any(e=>e.Severity=="WARNING"&&e.Message.Contains(ar?"تأكدت الإزالة":"Removal was verified")),"Post-removal refresh failure is observed and logged AR="+ar);
            var dash=new DashboardView(ar,log,null,new ApplicationInventoryService());Check(dash.RowRemoveLabel==(ar?"أزل من القائمة فقط":"Remove from this list only"),"Dashboard row action tooltip follows current language AR="+ar);
            deep.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        }
        log.Write("INFO","Quick cleanup scan finished.");Pump();log.SetLanguage(true);Check(log.Entries.Last().Display.Contains("اكتمل فحص"),"Existing Activity Log entry immediately switches to Arabic");log.SetLanguage(false);Check(log.Entries.Last().Display.Contains("Quick cleanup"),"Existing Activity Log entry immediately switches back to English");
        log.Write("INFO","Queued before clear");log.Clear();Pump();Check(log.Entries.Count==0,"Clear removes queued as well as visible Activity Log entries");
        log.WriteBilingual("INFO","Fixture report","تقرير تجريبي");Pump();log.SetLanguage(true);Check(log.Entries.Last().Display.Contains("تقرير تجريبي"),"Structured report retains exact Arabic display after a language change");
        string source=File.ReadAllText(Path.Combine(project,"DeepUninstallerView.xaml.cs"));Check(!source.Contains("Start the verified uninstall workflow for ")&&!source.Contains("بدء الإزالة المتحقق منها لـ "),"Start confirmation and both unused strings are absent");
        Check(System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(Path.Combine(project,"PermanentDeleteDialog.cs")),@"IsCancel\s*=\s*true"),"File Browser permanent-delete cancellation/confirmation remains");
        Check(AppInfo.Version=="1.0.150","Current release is v1.0.150");
        Console.WriteLine($"{checks} checks, {failures} failures. No real uninstaller, launcher, Recycle Bin, user cleanup or external feedback request ran. Fixtures: {root}");return failures==0?0:1;
    }
    private sealed class OfflineHandler:HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>throw new HttpRequestException("Synthetic offline state; no network request.");
    }
    private sealed class SuccessHandler:HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"success\":true}")});
    }
}
