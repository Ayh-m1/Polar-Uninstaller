using System.Collections;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PolarUninstallerWpf;
using PolarUninstallerWpf.Services;

internal static class ExplorerABC144Checks
{
    private sealed class InlineProgress<T>(Action<T> action):IProgress<T>{public void Report(T value)=>action(value);}
    internal static int Run()
    {
        int checks=0,failures=0;
        void Check(bool pass,string label){checks++;if(!pass)failures++;Console.WriteLine((pass?"PASS ":"FAIL ")+label);}
        var app=new Application();System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        string project=AppContext.BaseDirectory;while(!File.Exists(Path.Combine(project,"PolarUninstallerWpf.csproj")))project=Directory.GetParent(project)!.FullName;
        var xml=System.Xml.Linq.XDocument.Load(Path.Combine(project,"App.xaml"));System.Xml.Linq.XNamespace ns="http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        app.Resources=(ResourceDictionary)System.Windows.Markup.XamlReader.Parse(new System.Xml.Linq.XElement(ns+"ResourceDictionary",new System.Xml.Linq.XAttribute(System.Xml.Linq.XNamespace.Xmlns+"x","http://schemas.microsoft.com/winfx/2006/xaml"),xml.Root!.Element(ns+"Application.Resources")!.Elements()).ToString());
        var root=Path.Combine(project,"Build","Validation","ExplorerABC144-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var ctor=typeof(MainWindow).GetConstructor(BindingFlags.NonPublic|BindingFlags.Instance,null,new[]{typeof(SettingsService),typeof(ActivityMetricsService)},null)!;
        var window=(MainWindow)ctor.Invoke(new object[]{new SettingsService(root),new ActivityMetricsService(root)});
        object? Field(object target,string name)=>target.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(target);
        object? Invoke(object target,string name,params object[] args)=>target.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(target,args);
        IEnumerable<T> Visuals<T>(DependencyObject parent) where T:DependencyObject
        {for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);if(child is T item)yield return item;foreach(var descendant in Visuals<T>(child))yield return descendant;}}
        void Wait(Task task)
        {
            DateTime limit=DateTime.UtcNow.AddSeconds(20);
            while(!task.IsCompleted&&DateTime.UtcNow<limit){var frame=new DispatcherFrame();app.Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);Thread.Sleep(5);}
            if(!task.IsCompleted)throw new TimeoutException();task.GetAwaiter().GetResult();
        }
        void Render(FrameworkElement view,bool rtl,string name,Size size)
        {
            if(view.Parent is ContentControl parent)parent.Content=null;
            string selectedTheme=((ComboBox)window.FindName("ThemePicker")).SelectedItem as string??ThemeCatalog.DefaultTheme;
            var frame=new Border{Background=new SolidColorBrush((Color)ColorConverter.ConvertFromString(ThemeCatalog.All[selectedTheme].Background)),Child=view,Padding=new Thickness(20)};
            frame.Measure(size);frame.Arrange(new Rect(size));frame.UpdateLayout();
            var bitmap=new RenderTargetBitmap((int)size.Width,(int)size.Height,96,96,PixelFormats.Pbgra32);bitmap.Render(frame);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(Path.Combine(root,name+(rtl?"-ar":"-en")+"-"+(int)size.Width+".png")))encoder.Save(stream);frame.Child=null;
        }
        string scanRoot=Path.Combine(root,"scan");Directory.CreateDirectory(scanRoot);
        foreach(string folder in new[]{"a","longer-folder","c"})Directory.CreateDirectory(Path.Combine(scanRoot,folder));
        foreach(var type in new[]{("report.txt",4096),("image.png",8192),("archive.zip",16384)})
            foreach(var folder in new[]{"a","longer-folder","c"}){var path=Path.Combine(scanRoot,folder,type.Item1);File.WriteAllBytes(path,Enumerable.Repeat((byte)(type.Item2%251),type.Item2).ToArray());File.SetLastWriteTimeUtc(path,new DateTime(2026,1,folder=="a"?1:folder=="c"?3:2,0,0,0,DateTimeKind.Utc));}
        int delivered=0;
        var scan=new DuplicateScanService().ScanAsync(scanRoot,null,CancellationToken.None,new InlineProgress<DuplicateGroup>(_=>Interlocked.Increment(ref delivered))).GetAwaiter().GetResult();
        Check(scan.Groups.Count==3&&delivered==3,"Scanner emits verified partial groups without changing final hashes");
        var log=new ActivityLogService(app.Dispatcher);
        int sortLogs=0;log.EntryAdded+=entry=>{if(entry.Message.StartsWith("File Browser sort:")||entry.Message.StartsWith("Duplicate results sort:"))sortLogs++;};
        foreach(bool rtl in new[]{false,true})
        {
            var settings=new SettingsService(Path.Combine(root,rtl?"ar-settings":"en-settings"));
            var browser=new FileBrowserView(rtl,scanRoot,log,settings:settings);Wait(browser.ShowFolderAsync(scanRoot));
            Render(browser,rtl,"browser",new Size(1200,800));
            var files=(ListView)browser.FindName("Files");var grid=(GridView)files.View;
            var headers=new[]{1,2,3,4}.Select(i=>(GridViewColumnHeader)grid.Columns[i].Header).ToArray();
            Check(headers.All(header=>header.Focusable&&header.Cursor==System.Windows.Input.Cursors.Hand&&header.ToolTip is ToolTip&&AutomationProperties.GetHelpText(header).Length>0),"C1 all sortable headers expose click and keyboard hints "+rtl);
            Check(headers.All(header=>((StackPanel)header.Content).Children.OfType<TextBlock>().Last().Text.Length>0),"C1 inactive columns have neutral sort icons "+rtl);
            var type=headers[2];Invoke(browser,"SortClick",type,new RoutedEventArgs());
            Check((ListSortDirection)Field(browser,"direction")! ==ListSortDirection.Ascending&&AutomationProperties.GetItemStatus(type)=="ascending","C1 Type first click ascending "+rtl);
            Check(((ToolTip)type.ToolTip).Content.ToString()!.Contains(BrowserText.Get("Descending",rtl)),"C1 tooltip describes the next reverse click "+rtl);
            Invoke(browser,"SortClick",type,new RoutedEventArgs());
            Check((ListSortDirection)Field(browser,"direction")! ==ListSortDirection.Descending,"C1 second click reverses "+rtl);
            Invoke(browser,"SortClick",type,new RoutedEventArgs());
            Check((string)Field(browser,"sort")! ==nameof(BrowserEntry.Size)&&(ListSortDirection)Field(browser,"direction")! ==ListSortDirection.Descending,"C1 third click restores Size descending "+rtl);
            var sortButton=(SortMenuButton)Field(browser,"sortButton")!;var sortMenu=(ContextMenu)Field(sortButton,"menu")!;
            var name=sortMenu.Items.OfType<MenuItem>().Single(item=>item.Tag is string key&&key==nameof(BrowserEntry.Name));name.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(AutomationProperties.GetItemStatus(headers[0])=="ascending"&&sortButton.Content.ToString()!.Contains(BrowserText.Get("Name",rtl)),"C2 dropdown and header indicators synchronize "+rtl);
            var directions=sortMenu.Items.OfType<MenuItem>().Where(item=>item.Tag is bool).ToArray();directions.Single(item=>Equals(item.Tag,false)).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(directions.Count(item=>item.IsChecked)==1&&AutomationProperties.GetItemStatus(headers[0])=="descending","C2 direction choices are mutually exclusive "+rtl);
            Check(settings.Load().FileBrowserSort==nameof(BrowserEntry.Name)&&settings.Load().FileBrowserSortDirection=="Descending","C3 File Browser saves sort preference "+rtl);
            var restored=new FileBrowserView(rtl,scanRoot,log,settings:settings);
            Check((string)Field(restored,"sort")! ==nameof(BrowserEntry.Name)&&(ListSortDirection)Field(restored,"direction")! ==ListSortDirection.Descending,"C3 reopening restores sort "+rtl);
            Check(((ContextMenu)Field(browser,"more")!).Items.OfType<MenuItem>().All(item=>!(item.Header?.ToString()??"").Contains(BrowserText.Get("SortBy",rtl))),"C2 More stays free of sorting "+rtl);
            files.SelectAll();Render(browser,rtl,"browser-selected",new Size(1200,800));
            Check(ReferenceEquals(((Grid)browser.FindName("Actions")).Parent,browser.FindName("BrowserBody"))&&Grid.GetColumn((UIElement)browser.FindName("Actions"))==0,"A4 selection bar belongs to file-list column in both directions "+rtl);
            Check(((ColumnDefinition)browser.FindName("SearchColumn")).ActualWidth==260&&((Button)browser.FindName("ClearSearch")).ToolTip!=null,"A2 fixed-width search and accessible clear control "+rtl);
            Check(grid.Columns[2].Width>=108&&grid.Columns[3].Width>=108&&grid.Columns[4].Width>=174,"A1 aligned compact numeric/type/date columns "+rtl);
            Invoke(browser,"OpenDuplicates");
            Check(((FrameworkElement)browser.FindName("NavigationBar")).Visibility==Visibility.Collapsed,"A2 browser navigation and search hidden in duplicates "+rtl);
            var duplicates=(DuplicateFilesView)((ContentControl)browser.FindName("DuplicateHost")).Content;Wait((Task)Invoke(duplicates,"StartScanAsync")!);
            var results=(ListBox)duplicates.FindName("Results");var groups=results.Items.Cast<DuplicateGroupCard>().ToArray();
            Check(groups.Length==3&&groups.All(group=>!group.Expanded&&group.Copies.All(row=>!row.Selected)),"B2 grouped, collapsed, no automatic selections "+rtl);
            Check(VirtualizingPanel.GetIsVirtualizing(results)&&VirtualizingPanel.GetVirtualizationMode(results)==VirtualizationMode.Recycling,"B2 group cards use recycling virtualization "+rtl);
            Check(groups.Select(group=>group.RecoverableBytes).SequenceEqual(groups.Select(group=>group.RecoverableBytes).OrderDescending()),"B1/C3 default recoverable-space descending "+rtl);
            Check(((Button)duplicates.FindName("ScanButton")).Visibility==Visibility.Visible&&((Button)duplicates.FindName("CancelButton")).Visibility==Visibility.Collapsed,"B1 start/cancel buttons exchange visibility "+rtl);
            var group=groups[0];group.Copies.First(row=>!row.IsRetained).Selected=true;
            Check(group.Selected==null,"B2 partial selection is tri-state "+rtl);
            group.Selected=true;
            Check(group.Copies.Count(row=>row.Selected)==group.CopyCount-1&&group.Copies.Single(row=>row.IsRetained).Selected==false,"B2 group selection always protects one copy "+rtl);
            var chosen=group.Copies.Last();group.Keep(chosen);group.Selected=true;
            Check(chosen.IsRetained&&!chosen.Selected&&!chosen.CanSelect&&group.Copies.Count(row=>row.IsRetained)==1,"B2 changing retained copy remains safe "+rtl);
            Invoke(duplicates,"ApplySmartSelection","newest",null!);
            Check(groups.All(card=>card.Copies.Single(row=>row.IsRetained).ModifiedUtc==card.Copies.Max(row=>row.ModifiedUtc)&&card.Copies.Count(row=>row.Selected)==card.CopyCount-1),"B3 keep newest selects only extras "+rtl);
            Invoke(duplicates,"ApplySmartSelection","oldest",null!);
            Check(groups.All(card=>card.Copies.Single(row=>row.IsRetained).ModifiedUtc==card.Copies.Min(row=>row.ModifiedUtc)),"B3 keep oldest "+rtl);
            Invoke(duplicates,"ApplySmartSelection","shortest",null!);
            Check(groups.All(card=>card.Copies.Single(row=>row.IsRetained).Path.Length==card.Copies.Min(row=>row.Path.Length)),"B3 keep shortest path "+rtl);
            Invoke(duplicates,"ApplySmartSelection","folder",Path.Combine(scanRoot,"longer-folder"));
            Check(groups.All(card=>card.Copies.Single(row=>row.IsRetained).ParentPath==Path.Combine(scanRoot,"longer-folder")),"B3 chosen-folder keep policy "+rtl);
            Invoke(duplicates,"ApplySmartSelection","clear",null!);
            Check(groups.All(card=>card.Copies.All(row=>!row.Selected))&&!((Button)duplicates.FindName("RecycleButton")).IsEnabled,"B4 clearing selection disables removal "+rtl);
            ((TextBox)duplicates.FindName("ResultsSearch")).Text="image.png";Invoke(duplicates,"RefreshGroups");
            Check(results.Items.Count==1&&results.Items.Cast<DuplicateGroupCard>().Single().Category=="Images","B1 results search filters names/paths "+rtl);
            ((TextBox)duplicates.FindName("ResultsSearch")).Clear();((TextBox)duplicates.FindName("MinimumSize")).Text="0.01";Invoke(duplicates,"RefreshGroups");
            Check(results.Items.Count==1&&results.Items.Cast<DuplicateGroupCard>().Single().Category=="Archives","B1 minimum MB filter "+rtl);
            ((TextBox)duplicates.FindName("MinimumSize")).Text="0";Invoke(duplicates,"RefreshGroups");
            var dupSort=(SortMenuButton)duplicates.FindName("ResultsSort");var dupMenu=(ContextMenu)Field(dupSort,"menu")!;
            dupMenu.Items.OfType<MenuItem>().Single(item=>Equals(item.Tag,nameof(DuplicateGroupCard.Name))).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(settings.Load().DuplicateSort==nameof(DuplicateGroupCard.Name)&&settings.Load().DuplicateSortAscending,"C3 duplicate sort uses same menu and persists independently "+rtl);
            group.Expanded=true;duplicates.SetLanguage(!rtl);Check(group.Expanded&&results.Items.Count==3,"B5 language switching preserves groups and expansion "+rtl);duplicates.SetLanguage(rtl);
            group.Selected=true;
            foreach(var size in new[]{new Size(760,620),new Size(1200,850),new Size(1920,1080)})
            {
                Render(duplicates,rtl,"duplicates",size);Check(results.ActualHeight>70&&((Button)duplicates.FindName("ScanButton")).ActualWidth>0,"B5 results and scan controls remain visible at "+size+" "+rtl);
                if(size.Width==760)
                {
                    var visiblePaths=Visuals<MiddlePathText>(duplicates).ToArray();Check(visiblePaths.Length>0&&visiblePaths.All(path=>path.FlowDirection==FlowDirection.LeftToRight&&path.ToolTip?.ToString()==path.FullPath&&path.Text.Contains('…')),"B2 long copy paths use middle ellipsis and retain the full tooltip "+rtl);
                    Check(Visuals<TextBlock>(duplicates).Where(text=>text.Text=="archive.zip").All(text=>text.Foreground==app.Resources["HomeTextBrush"]),"B5 group/copy names resolve high-contrast theme text "+rtl);
                }
            }
            Check(((TextBlock)duplicates.FindName("SelectionSummary")).Inlines.OfType<Run>().Any(run=>run.FlowDirection==FlowDirection.LeftToRight&&run.Text.Contains("KB")),"B4/B5 selected size uses LTR value "+rtl);
            duplicates.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));browser.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));restored.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        }
        var large=new DuplicateFilesView(false,scanRoot,log,new ApplicationInventoryService(),new SettingsService(Path.Combine(root,"large-settings")));
        var synthetic=Enumerable.Range(0,1200).Select(i=>new DuplicateGroup("synthetic-"+i,1024,new[]{new DuplicateFile(Path.Combine(scanRoot,$"group-{i}-a.txt"),1024,DateTime.UtcNow),new DuplicateFile(Path.Combine(scanRoot,$"group-{i}-b.txt"),1024,DateTime.UtcNow)})).ToArray();
        typeof(DuplicateFilesView).GetField("result",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(large,new DuplicateScanResult(synthetic,2400,0){Root=scanRoot});Invoke(large,"RenderResults");
        Render(large,false,"duplicates-large",new Size(1200,850));var largeList=(ListBox)large.FindName("Results");
        Check(largeList.Items.Count==1200&&largeList.ItemContainerGenerator.ContainerFromIndex(0)!=null&&largeList.ItemContainerGenerator.ContainerFromIndex(1199)==null,"B2 1,200 groups realize only the visible card containers");
        var partialView=new DuplicateFilesView(false,scanRoot,log,new ApplicationInventoryService(),new SettingsService(Path.Combine(root,"partial-settings")));
        ((List<DuplicateGroup>)Field(partialView,"pendingGroups")!).Add(scan.Groups[0]);Invoke(partialView,"FlushPartialResults");
        Check(((ListBox)partialView.FindName("Results")).Items.Count==1&&((ListBox)partialView.FindName("Results")).Items.Cast<DuplicateGroupCard>().All(card=>card.Copies.All(row=>!row.Selected)),"B5 partial verified groups are visible without selection or a completed scan");
        var cancelView=new DuplicateFilesView(false,scanRoot,log,new ApplicationInventoryService(),new SettingsService(Path.Combine(root,"cancel-settings")));
        var cancelled=(Task)Invoke(cancelView,"StartScanAsync")!;cancelView.CancelOperation();Wait(cancelled);
        Check(!cancelView.IsBusy&&((TextBlock)cancelView.FindName("Status")).Text.StartsWith("Scan cancelled")&&Directory.GetFiles(scanRoot,"*",SearchOption.AllDirectories).Length==9,"B5 cancellation restores responsive controls without touching scanned files");
        string emptyFolder=Path.Combine(root,"empty");Directory.CreateDirectory(emptyFolder);
        var emptyView=new DuplicateFilesView(false,emptyFolder,log,new ApplicationInventoryService());Wait((Task)Invoke(emptyView,"StartScanAsync")!);
        Check(((TextBlock)emptyView.FindName("EmptyHint")).Text==BrowserText.Get("DupEmpty",false),"B5 completed empty scan shows the friendly clean-folder state");
        Check(sortLogs==0,"C3 sorting does not add entries to visible Activity Log");
        var picker=(ComboBox)window.FindName("ThemePicker");partialView.SetLanguage(true);
        foreach(var theme in ThemeCatalog.All.Keys)
        {
            picker.SelectedItem=theme;Render(partialView,true,"theme-"+theme.Replace(' ','-'),new Size(1000,720));
            var title=Visuals<TextBlock>(partialView).FirstOrDefault(text=>text.DataContext is DuplicateGroupCard card&&text.Text==card.Name);
            var expectedCard=(Color)ColorConverter.ConvertFromString(ThemeCatalog.All[theme].Card);expectedCard.A=(byte)(theme is ThemeCatalog.DefaultTheme or "Pure Black"?255:185);
            Check(title?.Foreground is SolidColorBrush foreground&&foreground.Color==(Color)ColorConverter.ConvertFromString(ThemeCatalog.All[theme].Text)&&((SolidColorBrush)app.Resources["HomeCardBrush"]).Color==expectedCard,"B5 actual group header/card follow "+theme+" resources");
        }
        foreach(var view in new[]{large,partialView,cancelView,emptyView})view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        // Perform real removal only on a dedicated three-file fixture, never on the above UI or personal files.
        string cleanRoot=Path.Combine(root,"cleanup");Directory.CreateDirectory(cleanRoot);var paths=new[]{"keep.txt","remove.txt","leave.txt"}.Select(name=>Path.Combine(cleanRoot,name)).ToArray();foreach(var path in paths)File.WriteAllText(path,"Disposable verification payload");
        var cleanupScan=new DuplicateScanService().ScanAsync(cleanRoot,null,CancellationToken.None).GetAwaiter().GetResult();
        var outcome=new DuplicateCleanupService().CleanAsync(cleanupScan,new[]{paths[1]},true,Array.Empty<string>(),null,CancellationToken.None).GetAwaiter().GetResult();
        Check(outcome.Items.Single().Removed&&!File.Exists(paths[1])&&File.Exists(paths[0])&&File.Exists(paths[2]),"B4 real cleanup removes only explicitly selected fixture copy");
        cleanupScan=new DuplicateScanService().ScanAsync(cleanRoot,null,CancellationToken.None).GetAwaiter().GetResult();File.WriteAllText(paths[2],"Modified after scan");
        outcome=new DuplicateCleanupService().CleanAsync(cleanupScan,new[]{paths[2]},true,Array.Empty<string>(),null,CancellationToken.None).GetAwaiter().GetResult();
        Check(!outcome.Items.Any(item=>item.Removed)&&File.Exists(paths[2]),"B4 changed content is protected by the unchanged cleanup backend");
        string allRoot=Path.Combine(root,"all-requested");Directory.CreateDirectory(allRoot);var allPaths=new[]{"one.txt","two.txt","three.txt"}.Select(name=>Path.Combine(allRoot,name)).ToArray();foreach(var path in allPaths)File.WriteAllText(path,"All-copy guard fixture");
        var allScan=new DuplicateScanService().ScanAsync(allRoot,null,CancellationToken.None).GetAwaiter().GetResult();
        var allOutcome=new DuplicateCleanupService().CleanAsync(allScan,allPaths,true,Array.Empty<string>(),null,CancellationToken.None).GetAwaiter().GetResult();
        Check(allPaths.Count(File.Exists)==1&&allOutcome.Items.Any(item=>item.Code=="last_copy"),"B4 backend retains a verified copy even if every fixture path is requested");
        Check(AppInfo.Version=="1.0.144","Version unchanged");
        Console.WriteLine($"{checks} checks, {failures} failures. Owned fixtures only; no live Recycle Bin or installer. Renders: {root}");return failures==0?0:1;
    }
}
