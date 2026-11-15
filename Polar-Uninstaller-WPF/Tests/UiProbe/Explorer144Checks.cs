using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PolarUninstallerWpf;
using PolarUninstallerWpf.Services;

internal static class Explorer144Checks
{
    internal static int Run()
    {
        int count=0,failures=0;
        void Check(bool value,string label){count++;if(!value)failures++;Console.WriteLine((value?"PASS ":"FAIL ")+label);}
        // Use the real styles without creating the production App startup path.
        var app=new Application();
        System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        string project=AppContext.BaseDirectory;
        while(!File.Exists(Path.Combine(project,"PolarUninstallerWpf.csproj")))project=Directory.GetParent(project)!.FullName;
        var xml=System.Xml.Linq.XDocument.Load(Path.Combine(project,"App.xaml"));
        System.Xml.Linq.XNamespace ns="http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var dictionary=new System.Xml.Linq.XElement(ns+"ResourceDictionary",new System.Xml.Linq.XAttribute(System.Xml.Linq.XNamespace.Xmlns+"x","http://schemas.microsoft.com/winfx/2006/xaml"),xml.Root!.Element(ns+"Application.Resources")!.Elements());
        app.Resources=(ResourceDictionary)System.Windows.Markup.XamlReader.Parse(dictionary.ToString());
        string root=Path.Combine(project,"Build","Validation","Explorer144-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var ctor=typeof(MainWindow).GetConstructor(BindingFlags.NonPublic|BindingFlags.Instance,null,new[]{typeof(SettingsService),typeof(ActivityMetricsService)},null)!;
        var window=(MainWindow)ctor.Invoke(new object[]{new SettingsService(root),new ActivityMetricsService(root)});
        object? Field(object target,string name)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(target);
        object? Invoke(object target,string name,params object[] args)=>target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(target,args);
        void Wait(Task task)
        {
            var limit=DateTime.UtcNow.AddSeconds(15);
            while(!task.IsCompleted&&DateTime.UtcNow<limit)
            {var frame=new DispatcherFrame();app.Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);Thread.Sleep(5);}
            if(!task.IsCompleted)throw new TimeoutException("Fixture task timed out");task.GetAwaiter().GetResult();
        }
        var en=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(project,"Localization","FileBrowser.en.json")))!;
        var ar=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(project,"Localization","FileBrowser.ar.json")))!;
        Check(en.Keys.Order().SequenceEqual(ar.Keys.Order()),"Arabic and English resource keys match");
        Check(BrowserText.ActionLabel("Recycle",1,true).Contains("واحد")&&BrowserText.ActionLabel("Recycle",2,true).Contains("عنصرين")&&BrowserText.ActionLabel("Recycle",3,true).Contains("عناصر")&&BrowserText.ActionLabel("Recycle",11,true).Contains("عنصرًا")&&BrowserText.ActionLabel("Recycle",100,true).Contains("100"),"Arabic one/two/few/many/other action plurals");
        string nested=root;
        for(int i=0;i<7;i++)nested=Path.Combine(nested,"fixture-folder-"+i);
        Directory.CreateDirectory(nested);File.WriteAllBytes(Path.Combine(nested,"sample.txt"),new byte[4096]);
        Directory.CreateDirectory(Path.Combine(nested,"child"));File.WriteAllBytes(Path.Combine(nested,"child","bytes.bin"),new byte[8192]);
        var log=new ActivityLogService(app.Dispatcher);
        foreach(bool rtl in new[]{false,true})
        {
            var browser=new FileBrowserView(rtl,nested,log,settings:new SettingsService(root));
            T Get<T>(string name) where T:class=>(T)browser.FindName(name);
            var files=Get<ListView>("Files");var actions=Get<Grid>("Actions");
            Wait(browser.ShowFolderAsync(nested));
            browser.Measure(new Size(1200,760));browser.Arrange(new Rect(0,0,1200,760));browser.UpdateLayout();
            Check(files.Items.Count==2&&browser.CurrentDirectory==nested,"Real fixture navigation completes "+rtl);
            Check(((ContextMenu)Field(browser,"more")!).Items.OfType<MenuItem>().Count(i=>!i.IsEnabled)==3,"More contains three named groups "+rtl);
            var more=(ContextMenu)Field(browser,"more")!;
            Check(more.Items.OfType<MenuItem>().Where(i=>i.Style==browser.Resources["ExplorerMenuItem"]).All(i=>i.Icon!=null)&&!more.Items.OfType<MenuItem>().Any(i=>(i.Header?.ToString()??"").Contains("Sort by Size")),"All menu commands have icons; no old size-sort entries "+rtl);
            Check(more.Items.OfType<MenuItem>().Any(i=>i.InputGestureText=="Ctrl+A")&&more.Items.OfType<MenuItem>().Any(i=>i.InputGestureText=="Ctrl+D / Esc"),"Selection shortcut hints are visible "+rtl);
            files.SelectAll();
            Check(actions.Visibility==Visibility.Visible&&actions.Children.OfType<Button>().All(b=>b.Content.ToString()!.Contains(rtl?"عنصرين":"2")),"All selected actions include live two-item count "+rtl);
            var danger=actions.Children.OfType<Button>().Single(b=>Grid.GetColumn(b)==3);
            Check(danger.Style==browser.Resources["ExplorerDangerButton"]&&actions.Children.OfType<Button>().Single(b=>Grid.GetColumn(b)==1).Style==browser.Resources["ExplorerPrimaryButton"],"Recycle primary; permanent red at opposite end "+rtl);
            Check(files.ContextMenu.Items.OfType<MenuItem>().Count()==6&&files.ContextMenu.Items.OfType<MenuItem>().All(i=>i.Icon!=null&&i.IsEnabled),"Six multi-selection context commands including Recycle "+rtl);
            Check(Get<TextBlock>("Status").Inlines.OfType<Run>().Any(r=>r.FlowDirection==FlowDirection.LeftToRight&&r.Text.Contains("KB")),"Status size is an LTR inline inside either reading direction "+rtl);
            var entry=files.Items.Cast<BrowserEntry>().Single(e=>e.IsDirectory);files.SelectedItem=entry;files.UnselectAll();files.SelectedItem=entry;
            Check(Get<TextBox>("DetailsPath").IsReadOnly&&Get<TextBox>("DetailsPath").Text==entry.FullPath&&Get<Button>("DetailsCopy").IsEnabled,"Selectable full path and enabled copy action "+rtl);
            var task=(Task)Invoke(browser,"CalculateSize")!;
            Check(Get<ProgressBar>("DetailsProgress").Visibility==Visibility.Visible&&Get<Button>("DetailsSize").Content.ToString()==BrowserText.Get("CancelCalculation",rtl),"Manual size calculation shows progress and cancel "+rtl);
            Wait(task);
            Check(entry.Size==8192&&Get<ProgressBar>("DetailsProgress").Visibility==Visibility.Collapsed,"Actual disposable folder size updates and progress stops "+rtl);
            var cancelled=(Task)Invoke(browser,"CalculateSize")!;
            Invoke(browser,"DetailsCalculateSize",Get<Button>("DetailsSize"),new RoutedEventArgs());Wait(cancelled);
            Check(Get<ProgressBar>("DetailsProgress").Visibility==Visibility.Collapsed&&!(bool)Field(browser,"manualSizing")!,"Cancel calculation stops progress without stale busy state "+rtl);
            Check(Get<TextBlock>("DetailsText").Inlines.OfType<Run>().Count(r=>r.FlowDirection==FlowDirection.LeftToRight)>=3,"Details sizes and dates use LTR runs "+rtl);
            var grid=(GridView)files.View;
            Check(new[]{grid.Columns[2],grid.Columns[4]}.All(c=>((FrameworkElement)c.CellTemplate.LoadContent()).FlowDirection==FlowDirection.LeftToRight),"Size/date cells isolate RTL values "+rtl);
            var typeHeader=(GridViewColumnHeader)grid.Columns[3].Header;
            Invoke(browser,"SortClick",typeHeader,new RoutedEventArgs());
            Check((ListSortDirection)Field(browser,"direction")! ==ListSortDirection.Ascending,"Type first click ascending "+rtl);
            Invoke(browser,"SortClick",typeHeader,new RoutedEventArgs());
            Check((ListSortDirection)Field(browser,"direction")! ==ListSortDirection.Descending,"Type second click descending "+rtl);
            var dialog=new PermanentDeleteDialog(rtl,2,12288,true,new[]{entry.FullPath},(Style)browser.Resources["ExplorerDangerButton"],(Style)browser.Resources["ExplorerActionButton"]);
            Check(dialog.CancelButton.IsDefault&&dialog.CancelButton.IsCancel&&!dialog.ConfirmButton.IsDefault&&System.Windows.Input.KeyboardNavigation.GetTabNavigation((DependencyObject)dialog.Content)==System.Windows.Input.KeyboardNavigationMode.Cycle,"Permanent confirmation defaults Cancel, Escape and cyclic focus "+rtl);
            Check(new ListViewAutomationPeer(files).GetAutomationControlType()==AutomationControlType.List&&AutomationProperties.GetName(files).Length>0,"Native WPF list automation and translated accessible name "+rtl);
            foreach(var size in new[]{new Size(1000,720),new Size(1920,1080)}){browser.Measure(size);browser.Arrange(new Rect(size));browser.UpdateLayout();}
            Check(Get<StackPanel>("Breadcrumbs").Children.OfType<Button>().Any(b=>b.Content?.ToString()=="…"),"Deep path uses clickable overflow "+rtl);
            var bitmap=new RenderTargetBitmap(1920,1080,96,96,PixelFormats.Pbgra32);bitmap.Render(browser);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(Path.Combine(root,rtl?"Arabic.png":"English.png")))encoder.Save(stream);
            files.UnselectAll();Invoke(browser,"ShowActionBar",false);
            Check(!actions.IsHitTestVisible,"Deselect immediately disables the fading action bar "+rtl);
            typeof(FileBrowserView).GetField("current",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(browser,Path.GetPathRoot(nested));Invoke(browser,"UpdateNavigationState");
            var nav=(Dictionary<string,Button>)Field(browser,"navigation")!;
            Check(!nav[BrowserText.Get("Up",rtl)].IsEnabled&&!nav[BrowserText.Get("Back",rtl)].IsEnabled&&!nav[BrowserText.Get("Forward",rtl)].IsEnabled,"Root and empty history disable impossible directions "+rtl);
            browser.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        }
        foreach(var pair in ThemeCatalog.All)
        {
            double L(string hex){var c=(Color)ColorConverter.ConvertFromString(hex);double Linear(byte b){double v=b/255.0;return v<=0.04045?v/12.92:Math.Pow((v+0.055)/1.055,2.4);}return .2126*Linear(c.R)+.7152*Linear(c.G)+.0722*Linear(c.B);}
            double text=L(pair.Value.Text),background=L(pair.Value.Card);double ratio=(Math.Max(text,background)+.05)/(Math.Min(text,background)+.05);
            Check(ratio>=4.5,"Primary detail/header/status text exceeds AA on "+pair.Key);
        }
        Check(AppInfo.Version=="1.0.144","Version unchanged");
        Console.WriteLine($"{count} checks, {failures} failures. Fixtures only; no deletion, installation or personal files touched. Renders: {root}");
        return failures==0?0:1;
    }
}
