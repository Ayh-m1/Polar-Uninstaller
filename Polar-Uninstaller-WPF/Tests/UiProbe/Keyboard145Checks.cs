using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PolarUninstallerWpf;
using PolarUninstallerWpf.Services;

internal static class Keyboard145Checks
{
    internal static int Run()
    {
        int checks=0,failures=0;void Check(bool value,string label){checks++;if(!value)failures++;Console.WriteLine((value?"PASS ":"FAIL ")+label);}
        var app=new Application();System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        string project=AppContext.BaseDirectory;while(!File.Exists(Path.Combine(project,"PolarUninstallerWpf.csproj")))project=Directory.GetParent(project)!.FullName;
        var xml=System.Xml.Linq.XDocument.Load(Path.Combine(project,"App.xaml"));System.Xml.Linq.XNamespace ns="http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        app.Resources=(ResourceDictionary)System.Windows.Markup.XamlReader.Parse(new System.Xml.Linq.XElement(ns+"ResourceDictionary",new System.Xml.Linq.XAttribute(System.Xml.Linq.XNamespace.Xmlns+"x","http://schemas.microsoft.com/winfx/2006/xaml"),xml.Root!.Element(ns+"Application.Resources")!.Elements()).ToString().Replace("clr-namespace:PolarUninstallerWpf\"","clr-namespace:PolarUninstallerWpf;assembly=PolarUninstaller\""));
        string root=Path.Combine(project,"Build","Validation","Keyboard145-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var settings=new SettingsService(Path.Combine(root,"main-settings"));var ctor=typeof(MainWindow).GetConstructor(BindingFlags.NonPublic|BindingFlags.Instance,null,new[]{typeof(SettingsService),typeof(ActivityMetricsService)},null)!;
        var window=(MainWindow)ctor.Invoke(new object[]{settings,new ActivityMetricsService(root)});
        object? Field(object value,string name)=>value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(value);
        object? Invoke(object value,string name,params object?[] args)=>value.GetType().GetMethods(BindingFlags.DeclaredOnly|BindingFlags.Instance|BindingFlags.NonPublic).Single(method=>method.Name==name&&method.GetParameters().Length==args.Length).Invoke(value,args);
        void Set(object value,string name,object? field)=>value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(value,field);
        void Pump(){var frame=new DispatcherFrame();app.Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}
        void Layout(FrameworkElement view,double width,double height){view.Measure(new Size(width,height));view.Arrange(new Rect(0,0,width,height));view.UpdateLayout();}
        void Render(FrameworkElement view,string name,double width,double height)
        {
            var original=view.Parent as ContentControl;if(original!=null)original.Content=null;
            var frame=new Border{Child=view,Background=new SolidColorBrush((Color)ColorConverter.ConvertFromString(ThemeCatalog.All[ThemeCatalog.DefaultTheme].Background)),Padding=new Thickness(16)};Layout(frame,width,height);
            var bitmap=new RenderTargetBitmap((int)width,(int)height,96,96,PixelFormats.Pbgra32);bitmap.Render(frame);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(Path.Combine(root,name+".png")))png.Save(stream);frame.Child=null;if(original!=null)original.Content=view;
        }
        IEnumerable<T> Visuals<T>(DependencyObject node) where T:DependencyObject
        {for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++){var child=VisualTreeHelper.GetChild(node,i);if(child is T match)yield return match;foreach(var item in Visuals<T>(child))yield return item;}}
        var navigation=new[]{(Key.Left,ModifierKeys.Alt,ExplorerCommand.Back),(Key.Right,ModifierKeys.Alt,ExplorerCommand.Forward),(Key.Up,ModifierKeys.Alt,ExplorerCommand.Up),(Key.F5,ModifierKeys.None,ExplorerCommand.Refresh),(Key.F4,ModifierKeys.None,ExplorerCommand.Address),(Key.Back,ModifierKeys.None,ExplorerCommand.Back),(Key.Enter,ModifierKeys.None,ExplorerCommand.Open),(Key.A,ModifierKeys.Control,ExplorerCommand.SelectAll),(Key.D,ModifierKeys.Control,ExplorerCommand.Deselect),(Key.Escape,ModifierKeys.None,ExplorerCommand.Deselect),(Key.Delete,ModifierKeys.None,ExplorerCommand.Recycle),(Key.Delete,ModifierKeys.Shift,ExplorerCommand.Permanent),(Key.F,ModifierKeys.Control,ExplorerCommand.Search),(Key.F1,ModifierKeys.None,ExplorerCommand.Help)};
        foreach(var key in navigation)Check(ExplorerShortcuts.Resolve(key.Item1,key.Item2,false,false,true)==key.Item3,"Browser routing: "+key.Item2+"+"+key.Item1+" -> "+key.Item3);
        foreach(var key in new[]{(Key.A,ModifierKeys.Control),(Key.D,ModifierKeys.Control),(Key.Escape,ModifierKeys.None),(Key.Delete,ModifierKeys.None),(Key.Delete,ModifierKeys.Shift),(Key.Back,ModifierKeys.None),(Key.Enter,ModifierKeys.None),(Key.F1,ModifierKeys.None)})
            Check(ExplorerShortcuts.Resolve(key.Item1,key.Item2,false,true,true)==ExplorerCommand.None,"Typing field retains "+key.Item2+"+"+key.Item1);
        Check(ExplorerShortcuts.Resolve(Key.Delete,ModifierKeys.None,false,false,false)==ExplorerCommand.None&&ExplorerShortcuts.Resolve(Key.A,ModifierKeys.Control,false,false,false)==ExplorerCommand.None,"List-only keys cannot act from toolbar or tip buttons");
        Check(ExplorerShortcuts.IsHelpText("?",false)&&ExplorerShortcuts.IsHelpText("؟",false)&&!ExplorerShortcuts.IsHelpText("?",true),"Both question marks open help only outside text fields");
        Check(ExplorerShortcuts.Resolve(Key.F2,ModifierKeys.None,false,false,true)==ExplorerCommand.None&&!ExplorerShortcuts.Help(false).Any(row=>row.Keys=="F2"),"Unsupported F2 rename is not advertised");
        Check(ExplorerShortcuts.Resolve(Key.Right,ModifierKeys.Alt,true,false,true)==ExplorerCommand.None&&ExplorerShortcuts.Resolve(Key.Up,ModifierKeys.Alt,true,false,true)==ExplorerCommand.None&&ExplorerShortcuts.Resolve(Key.F4,ModifierKeys.None,true,false,true)==ExplorerCommand.None,"Duplicate help does not advertise nonexistent navigation");
        foreach(bool duplicate in new[]{false,true})foreach(var entry in ExplorerShortcuts.Help(duplicate))Check(!string.IsNullOrWhiteSpace(BrowserText.Get(entry.Description,false))&&!string.IsNullOrWhiteSpace(BrowserText.Get(entry.Description,true)),"Bilingual help: "+duplicate+" "+entry.Keys);
        var log=new ActivityLogService(app.Dispatcher);int logged=0;log.EntryAdded+=_=>logged++;
        foreach(bool rtl in new[]{false,true})
        {
            var isolated=new SettingsService(Path.Combine(root,rtl?"ar":"en"));int windows=app.Windows.Count;
            var browser=new FileBrowserView(rtl,root,log,settings:isolated);var list=(ListView)browser.FindName("Files");
            var entries=Enumerable.Range(0,4).Select(i=>new BrowserEntry("fixture-"+i+".bin",Path.Combine(root,"fixture-"+i+".bin"),false,1024)).ToArray();list.ItemsSource=entries;Layout(browser,1880,900);
            var tip=(SelectionTipBar)Field(browser,"selectionTip")!;
            Check(app.Windows.Count==windows&&tip.Visibility==Visibility.Collapsed,"No popup or tip on page open (RTL="+rtl+")");
            list.SelectedItem=entries[0];Check(tip.Visibility==Visibility.Visible,"First selection displays inline tip (RTL="+rtl+")");
            var fileRow=(ListViewItem)list.ItemContainerGenerator.ContainerFromIndex(1);
            Invoke(browser,"ApplyFileSelection",fileRow,1,ModifierKeys.Control,1);
            Check(list.SelectedItems.Count==2&&tip.Visibility==Visibility.Collapsed&&isolated.Load().SelectionTipDismissed,"Actual Ctrl-click selection hides and persists the tip (RTL="+rtl+")");
            Invoke(browser,"ApplyFileSelection",(ListViewItem)list.ItemContainerGenerator.ContainerFromIndex(3),3,ModifierKeys.Shift,1);
            Check(list.SelectedItems.Count==3,"Actual Shift-click selects anchored range (RTL="+rtl+")");
            ((Task)Invoke(browser,"DispatchBrowserCommand",ExplorerCommand.SelectAll)!).GetAwaiter().GetResult();Check(list.SelectedItems.Count==4,"Ctrl+A dispatch selects the file list (RTL="+rtl+")");
            ((Task)Invoke(browser,"DispatchBrowserCommand",ExplorerCommand.Deselect)!).GetAwaiter().GetResult();Check(list.SelectedItems.Count==0,"Ctrl+D/Esc dispatch deselects (RTL="+rtl+")");
            list.SelectedItem=entries[0];Check(tip.Visibility==Visibility.Collapsed,"Dismissed tip never returns for later selections (RTL="+rtl+")");
            var second=new FileBrowserView(rtl,root,log,settings:new SettingsService(Path.Combine(root,rtl?"ar":"en")));var secondList=(ListView)second.FindName("Files");secondList.ItemsSource=entries;secondList.SelectedItem=entries[0];Check(((SelectionTipBar)Field(second,"selectionTip")!).Visibility==Visibility.Collapsed,"Saved dismissal restored by another page instance (RTL="+rtl+")");
            var hint=(ShortcutHintLine)browser.FindName("BrowserShortcutHint");Layout(browser,1920,900);Invoke(browser,"FitBrowserHint");Check(hint.Visibility==Visibility.Visible&&hint.Children.OfType<Border>().All(cap=>cap.FlowDirection==FlowDirection.LeftToRight),"Wide hint uses LTR keycaps in either language (RTL="+rtl+")");Layout(browser,480,620);Invoke(browser,"FitBrowserHint");Check(hint.Visibility==Visibility.Collapsed,"Narrow browser hides persistent hint (RTL="+rtl+")");
            var freshSettings=new SettingsService(Path.Combine(root,"dismiss-"+rtl));var fresh=new FileBrowserView(rtl,root,log,settings:freshSettings);var freshList=(ListView)fresh.FindName("Files");freshList.ItemsSource=entries;freshList.SelectedItem=entries[0];var freshTip=(SelectionTipBar)Field(fresh,"selectionTip")!;freshTip.Dismiss();Check(freshSettings.Load().SelectionTipDismissed&&freshTip.Visibility==Visibility.Collapsed,"Got it uses the same persistent preference (RTL="+rtl+")");
            var duplicate=new DuplicateFilesView(rtl,root,log,new ApplicationInventoryService(),isolated);
            var files=new[]{new DuplicateFile(Path.Combine(root,"one.txt"),1024,DateTime.UtcNow),new DuplicateFile(Path.Combine(root,"two.txt"),1024,DateTime.UtcNow),new DuplicateFile(Path.Combine(root,"three.txt"),1024,DateTime.UtcNow)};
            Set(duplicate,"result",new DuplicateScanResult(new[]{new DuplicateGroup("fixture",1024,files)},3,0){Root=root});Invoke(duplicate,"RenderResults");Layout(duplicate,1920,900);var results=(ListBox)duplicate.FindName("Results");var card=results.Items.Cast<DuplicateGroupCard>().Single();
            Invoke(duplicate,"ApplyCopySelection",card.Copies[1],true,false);Check(card.Copies[1].Selected&&!card.Copies[0].Selected,"Duplicate Ctrl-click toggles copies and protects keeper (RTL="+rtl+")");
            Invoke(duplicate,"ApplyCopySelection",card.Copies[2],false,true);Check(card.Copies.Count(row=>row.Selected)==2&&!card.Copies[0].Selected,"Duplicate Shift-click range protects keeper (RTL="+rtl+")");
            Invoke(duplicate,"SelectVisibleCopies",false);Invoke(duplicate,"SelectVisibleCopies",true);Check(card.Copies.Count(row=>row.Selected)==2,"Duplicate Ctrl+A selects extras only (RTL="+rtl+")");
            Check(((SelectionTipBar)Field(duplicate,"duplicateTip")!).Visibility==Visibility.Collapsed,"Shared dismissal also applies in Duplicate Finder (RTL="+rtl+")");
            var dialog=new ShortcutHelpWindow(rtl,false,(Style)browser.Resources["ExplorerActionButton"],(Style)browser.Resources["ExplorerFocus"]);
            Check(dialog.CloseButton.IsCancel&&dialog.CloseButton.IsDefault&&KeyboardNavigation.GetTabNavigation((DependencyObject)dialog.Content)==KeyboardNavigationMode.Cycle,"Help supports Escape, safe focus and cyclic Tab (RTL="+rtl+")");
            Check(dialog.Entries.Any(entry=>entry.Keys=="Shift+Delete")&&!dialog.Entries.Any(entry=>entry.Keys=="F2"),"Help lists implemented safety actions only (RTL="+rtl+")");
            Render(browser,"browser-"+rtl,1920,900);Render(duplicate,"duplicate-"+rtl,1920,900);Render((FrameworkElement)dialog.Content,"help-"+rtl,720,720);
            foreach(var view in new FrameworkElement[]{browser,second,fresh,duplicate})view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        }
        Pump();Check(logged==0,"Tip, dialog, keyboard education and selection commands add no visible Activity Log events");
        settings.Save(settings.Load() with{SelectionTipDismissed=true,DuplicateSort="Name",DuplicateSortAscending=true});Invoke(window,"SaveSettings");Check(settings.Load().SelectionTipDismissed&&settings.Load().DuplicateSort=="Name","Theme/sidebar settings saves preserve dismissal and other pages' sort preferences");
        var deep=new DeepUninstallerView(false,log,new ApplicationInventoryService(),settings);((ComboBox)deep.FindName("SortSelector")).SelectedIndex=2;Check(settings.Load().SelectionTipDismissed,"Deep sort save cannot reset the education preference");
        var credit=(Button)window.FindName("SidebarVersion");var label=(TextBlock)window.FindName("PreviewLabel");var community=(StackPanel)window.FindName("CommunityNav");
        Check(label.Text=="v"+AppInfo.Version+" · "+AppInfo.Publisher&&label.FlowDirection==FlowDirection.LeftToRight,"Credit keeps the dynamic original text and LTR direction");
        Check(ReferenceEquals(label.Parent,credit)&&ReferenceEquals(credit.Parent,community.Parent)&&((Panel)credit.Parent).Children.IndexOf(credit)>((Panel)community.Parent).Children.IndexOf(community),"Credit appears once below Feedback in the bottom community area");
        var rootView=(FrameworkElement)window.FindName("Root");Layout(rootView,1200,320);Pump();rootView.UpdateLayout();var sidebar=(ScrollViewer)window.FindName("SidebarScroll");Check(sidebar.ScrollableHeight>0,"Short sidebar scrolls instead of clipping community/version controls");
        Invoke(window,"OpenVersionNotes",credit,new RoutedEventArgs());Check((string)Field(window,"page")! =="whatsnew","Version credit opens the existing What's New page");
        Invoke(window,"ToggleSidebar",window.FindName("Toggle"),new RoutedEventArgs());Check(credit.Visibility==Visibility.Collapsed,"Collapsed sidebar hides the entire version control");Invoke(window,"ToggleSidebar",window.FindName("Toggle"),new RoutedEventArgs());
        var picker=(ComboBox)window.FindName("ThemePicker");
        double L(string hex){var c=(Color)ColorConverter.ConvertFromString(hex);double F(byte b){double v=b/255d;return v<=.04045?v/12.92:Math.Pow((v+.055)/1.055,2.4);}return .2126*F(c.R)+.7152*F(c.G)+.0722*F(c.B);}
        foreach(var theme in ThemeCatalog.All)
        {
            picker.SelectedItem=theme.Key;double foreground=L(theme.Value.Dim);bool aa=new[]{theme.Value.Background,theme.Value.Card}.All(color=>(Math.Max(foreground,L(color))+.05)/(Math.Min(foreground,L(color))+.05)>=4.5);
            Check(aa&&((SolidColorBrush)label.Foreground).Color==((SolidColorBrush)app.Resources["HomeDimBrush"]).Color,"Hint/credit use AA theme dim text in "+theme.Key);
        }
        picker.SelectedItem=ThemeCatalog.DefaultTheme;Set(window,"arabic",true);Invoke(window,"Render",false,true);Layout(rootView,1200,750);Render(rootView,"sidebar-ar",1200,750);
        Check(label.FlowDirection==FlowDirection.LeftToRight&&label.Text.StartsWith("v"+AppInfo.Version),"Arabic sidebar never reorders version/publisher");
        Check(AppInfo.Version=="1.0.150","Release version is v1.0.150");
        Console.WriteLine($"{checks} checks, {failures} failures. Offscreen only; no deletion, installer, real application removal or welcome windows. Renders: {root}");return failures==0?0:1;
    }
}

