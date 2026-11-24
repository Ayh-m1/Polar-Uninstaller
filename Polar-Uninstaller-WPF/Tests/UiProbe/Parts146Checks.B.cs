using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PolarUninstallerWpf;
using PolarUninstallerWpf.Services;

internal static partial class Parts146Checks
{
    private static void Until(Func<bool> condition){var watch=System.Diagnostics.Stopwatch.StartNew();while(!condition()&&watch.Elapsed<TimeSpan.FromSeconds(10))Pump();if(!condition())throw new TimeoutException("UI fixture condition timed out");}
    internal static int RunB()
    {
        Setup("B");string start=Path.Combine(root,"Downloads"),child=Path.Combine(start,"HexV3 (Game + Mod) Patch 3"),other=Path.Combine(start,"other");Directory.CreateDirectory(child);Directory.CreateDirectory(other);
        var history=new FolderNavigationHistory(start);
        Check(history.Current==start&&!history.CanBack&&!history.CanForward,"Initial folder exists in history before first load");
        history.Commit(child);Check(history.CanBack&&!history.CanForward&&history.Entries.Count==2,"New navigation pushes initial location");
        var back=history.Find(-1)!;history.Commit(back.Path,back.Index);Check(history.Current==start&&history.CanForward&&!history.CanBack,"Back moves without adding a history entry");
        var forward=history.Find(1)!;history.Commit(forward.Path,forward.Index);Check(history.Current==child&&history.Entries.Count==2,"Forward moves without adding entries");
        history.Commit(child);Check(history.Entries.Count==2,"Refreshing the same folder adds nothing");
        back=history.Find(-1)!;history.Commit(back.Path,back.Index);history.Commit(other);Check(!history.CanForward&&history.Entries.SequenceEqual(new[]{start,other}),"A new branch clears Forward history");
        var missing=new FolderNavigationHistory(start);missing.Commit(child);missing.Commit(other);Directory.Delete(child);
        var recovered=missing.Find(-1)!;Check(recovered.Recovered&&recovered.Path==start,"A deleted history folder resolves to its nearest existing parent");missing.Commit(recovered.Path,recovered.Index);Check(!missing.CanBack&&missing.CanForward,"Recovered duplicate parents collapse and navigation flags remain exact");Directory.CreateDirectory(child);
        Check(ExplorerShortcuts.Resolve(Key.Back,ModifierKeys.None,false,false,true)==ExplorerCommand.Back&&ExplorerShortcuts.Resolve(Key.Back,ModifierKeys.None,false,true,true)==ExplorerCommand.None,"Backspace means Back and never intercepts text editing");
        Check(ExplorerShortcuts.Resolve(Key.L,ModifierKeys.Control,false,true,false)==ExplorerCommand.Address,"Ctrl+L edits the full path even from a text field");
        foreach(bool ar in new[]{false,true})
        {
            var log=new ActivityLogService(app.Dispatcher);var browser=new FileBrowserView(ar,start,log,settings:new SettingsService(Path.Combine(root,ar?"ar":"en")));
            Wait(browser.ShowFolderAsync(child));var nav=(Dictionary<string,Button>)Field(browser,"navigation")!;
            Check(nav[BrowserText.Get("Back",ar)].IsEnabled,"First folder opened before Loaded has enabled Back AR="+ar);
            Wait((Task)Invoke(browser,"GoHistoryAsync",-1)!);Check(browser.CurrentDirectory==start&&nav[BrowserText.Get("Forward",ar)].IsEnabled,"Actual Back loads the initial folder AR="+ar);
            Wait((Task)Invoke(browser,"GoHistoryAsync",1)!);Check(browser.CurrentDirectory==child,"Actual Forward loads the child AR="+ar);
            Invoke(browser,"ShowAddress");var address=(TextBox)browser.FindName("Address");Check(address.Text==child&&address.Visibility==Visibility.Visible&&address.FlowDirection==FlowDirection.LeftToRight,"Address editor exposes exact selectable LTR full path AR="+ar);
            string deep=child;for(int i=0;i<5;i++){deep=Path.Combine(deep,"very long middle segment "+i);Directory.CreateDirectory(deep);}Wait(browser.ShowFolderAsync(deep));
            browser.Measure(new Size(920,680));browser.Arrange(new Rect(0,0,920,680));browser.UpdateLayout();Invoke(browser,"BuildBreadcrumbs");
            var crumbs=(StackPanel)browser.FindName("Breadcrumbs");var buttons=crumbs.Children.OfType<Button>().ToArray();
            Check(crumbs.FlowDirection==FlowDirection.LeftToRight&&buttons[0].ToolTip?.ToString()==Path.GetPathRoot(deep)&&buttons[^1].ToolTip?.ToString()==deep,"Breadcrumb retains drive and current full-path tooltip in LTR order AR="+ar);
            Check(buttons.Any(b=>b.Content?.ToString()=="…"),"Long path collapses middle segments into a menu AR="+ar);
            var labels=buttons.Select(b=>b.Content).OfType<TextBlock>().ToArray();Check(labels.Last().TextTrimming==TextTrimming.CharacterEllipsis&&labels.Take(labels.Length-1).All(t=>t.TextTrimming==TextTrimming.None),"Only the current folder label truncates AR="+ar);
            Check(buttons[^1].DesiredSize.Width<=((Border)browser.FindName("BreadcrumbFrame")).ActualWidth,"Breadcrumb adapts within its toolbar cell AR="+ar);
            var lastParent=((List<(string Title,string Path)>)Field(browser,"breadcrumbParts")!).First(p=>p.Path==start);Check(lastParent.Title=="Downloads","Hidden breadcrumbs retain exact navigation targets AR="+ar);
            browser.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        }
        // Exercise the first Dashboard-origin folder request without loading any personal location.
        var ctor=typeof(MainWindow).GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(SettingsService),typeof(ActivityMetricsService)},null)!;
        var window=(MainWindow)ctor.Invoke(new object[]{new SettingsService(Path.Combine(root,"main")),new ActivityMetricsService(root)});Set(window,"currentDirectory",start);
        Invoke(window,"OpenStorageItem",child,child);var opened=(FileBrowserView)Field(window,"fileBrowserPage")!;Until(()=>opened.CurrentDirectory==child);
        Check(((Dictionary<string,Button>)Field(opened,"navigation")!)["Back"].IsEnabled,"First Dashboard navigation preserves the previous browser location");
        Wait((Task)Invoke(opened,"GoHistoryAsync",-1)!);Check(opened.CurrentDirectory==start,"Back after Dashboard entry returns to the captured origin");
        Check(AppInfo.Version=="1.0.150","Version stays v1.0.150");return Finish("B");
    }
}
