using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Documents;
using PolarUninstallerWpf.Services;

namespace PolarUninstallerWpf;

public partial class FileBrowserView
{
    private readonly List<(string Title,string Path)> breadcrumbParts=new();
    private int actionRevision;
    private bool manualSizing,confirmationOpen;
    private readonly System.Windows.Threading.DispatcherTimer copyFeedback=new(){Interval=TimeSpan.FromSeconds(1.6)};
    private string T(string key,params object[] values)=>BrowserText.Get(key,arabic,values);
    private async Task RunBrowserAction(Func<Task> action)
    {
        try{await action();}
        catch(OperationCanceledException){}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException)
        {Status.Text=T("ActionFailed",ex.Message);activity.Write("WARNING",Status.Text);}
    }
    private void ApplyExplorerAccessibility()
    {
        foreach(var control in new Control[]{Files,Address,Search,DetailsPath,DetailsCopy,DetailsLocation,DetailsSize})control.FocusVisualStyle=(Style)Resources["ExplorerFocus"];
        System.Windows.Automation.AutomationProperties.SetName(Files,T("BrowserList"));
        System.Windows.Automation.AutomationProperties.SetName(Address,T("Address"));
        System.Windows.Automation.AutomationProperties.SetName(Search,T("Search"));
        System.Windows.Automation.AutomationProperties.SetName(DetailsPath,T("Address"));
        DetailsCopy.Content=T("CopyPath");DetailsCopy.IsEnabled=false;
        ClearSearch.ToolTip=T("ClearSearch");System.Windows.Automation.AutomationProperties.SetName(ClearSearch,T("ClearSearch"));
        DetailsSize.Style=(Style)Resources["ExplorerActionButton"];
        PreviewKeyDown+=BrowserNavigationKey;
        Files.PreviewMouseRightButtonDown+=(_,e)=>
        {
            if(e.OriginalSource is DependencyObject source&&ItemsControl.ContainerFromElement(Files,source) is ListViewItem row&&!row.IsSelected)
            {Files.UnselectAll();row.IsSelected=true;row.Focus();UpdateSelectionState(false);}
        };
        copyFeedback.Tick+=(_,_)=>{copyFeedback.Stop();DetailsCopy.Content=T("CopyPath");if(Status.Text==T("Copied"))UpdateStatus();};
        Unloaded+=(_,_)=>copyFeedback.Stop();
        InitializeShortcutExperience();
    }
    private async void BrowserNavigationKey(object sender,KeyEventArgs e)
    {
        await HandleBrowserKey(e);
    }
    private void UpdateNavigationState()
    {
        if(navigation.TryGetValue(L("Back","رجوع"),out var back))back.IsEnabled=duplicatesActive||folderHistory.CanBack;
        if(navigation.TryGetValue(L("Forward","التالي"),out var next))next.IsEnabled=folderHistory.CanForward;
        if(navigation.TryGetValue(L("Up","للأعلى"),out var up))
            up.IsEnabled=!string.Equals(Path.TrimEndingDirectorySeparator(current),Path.TrimEndingDirectorySeparator(Path.GetPathRoot(current)??current),StringComparison.OrdinalIgnoreCase);
    }
    private FrameworkElement MenuIcon(string label)
    {
        string path=label==L("Delete permanently","حذف نهائي")||label==L("Move to Recycle Bin","نقل إلى سلة المحذوفات")?"M3 6 H21 M5 6 V21 H19 V6 M8 6 V3 H16 V6 M9 10 V17 M15 10 V17":
            label==L("Select all","تحديد الكل")?"M3 3 H21 V21 H3 Z M7 12 L11 16 L18 8":
            label==L("Deselect","إلغاء التحديد")||label==L("Cancel size calculation","إلغاء حساب الحجم")?"M4 4 L20 20 M20 4 L4 20":
            label==L("Copy path","نسخ المسار")?"M8 3 H21 V17 H17 M3 8 H16 V22 H3 Z":
            label==L("Folder size","حجم المجلد")?"M3 4 H21 V20 H3 Z M8 4 V10 M13 4 V8 M18 4 V10":
            label==T("Duplicates")?"M8 2 H21 V16 H17 M3 7 H16 V22 H3 Z":"M2 5 H10 L13 8 H22 V21 H2 Z";
        var icon=new System.Windows.Shapes.Path{Data=Geometry.Parse(path),Width=16,Height=16,StrokeThickness=1.5,Stretch=Stretch.Uniform,StrokeLineJoin=PenLineJoin.Round,IsHitTestVisible=false};
        icon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,label==L("Delete permanently","حذف نهائي")?"LogError":"ChromeAccent");return icon;
    }
    private string Shortcut(string label)=>label==T("HelpToolbar")?"F1":label==L("Back","رجوع")?"Alt+Left / Backspace / XButton1":label==L("Forward","التالي")?"Alt+Right / XButton2":label==L("Up","للأعلى")?"Alt+Up":label==L("Refresh","تحديث")?"F5":label==L("Select all","تحديد الكل")?"Ctrl+A":label==L("Deselect","إلغاء التحديد")?"Ctrl+D / Esc":label==L("Open","فتح")?"Enter":label==L("Move to Recycle Bin","نقل إلى سلة المحذوفات")?"Delete":label==L("Delete permanently","حذف نهائي")?"Shift+Delete":"";
    private void DecorateButton(Button button,string label)
    {
        button.FocusVisualStyle=(Style)Resources["ExplorerFocus"];
        System.Windows.Automation.AutomationProperties.SetName(button,label);
        ShortcutUi.SetTooltip(button,label,Shortcut(label),arabic);var tip=(ToolTip)button.ToolTip;
        button.GotKeyboardFocus+=(_,_)=>{if(Keyboard.IsKeyDown(Key.Tab)||Keyboard.IsKeyDown(Key.Left)||Keyboard.IsKeyDown(Key.Right))tip.IsOpen=true;};
        button.LostKeyboardFocus+=(_,_)=>tip.IsOpen=false;
    }
    private void ReorganizeMenus()
    {
        var entries=more.Items.OfType<MenuItem>().Where(i=>i.Header is string).ToDictionary(i=>i.Header.ToString()!);
        more.Items.Clear();
        void Group(string title,IEnumerable<string> labels)
        {
            if(more.Items.Count>0)more.Items.Add(new Separator());
            var heading=new MenuItem{Header=title,IsEnabled=false,FontWeight=FontWeights.SemiBold};more.Items.Add(heading);
            foreach(string label in labels)if(entries.TryGetValue(label,out var item))more.Items.Add(item);
        }
        Group(T("View"),new[]{L("Details","التفاصيل"),L("Browse","استعراض")});
        var check = new TextBlock();
        hiddenItemsMenu = new MenuItem
        {
            Header = T("ShowHiddenItems"), InputGestureText = "Ctrl+H",
            IsCheckable = true, Icon = check, Style = (Style)Resources["ExplorerMenuItem"]
        };
        UpdateHiddenMenu();
        hiddenItemsMenu.Click += async (_, _) => await RunBrowserAction(ToggleHiddenItemsAsync);
        more.Items.Add(hiddenItemsMenu);
        Group(T("Selection"),new[]{L("Select all","تحديد الكل"),L("Deselect","إلغاء التحديد")});
        Group(T("Tools"),new[]{T("Duplicates"),L("Cancel size calculation","إلغاء حساب الحجم")});
        foreach(var menu in new[]{more,quickAccess,itemMenu})foreach(var item in menu.Items.OfType<MenuItem>().Where(i=>i.Style==Resources["ExplorerMenuItem"] && i!=hiddenItemsMenu))
        {string label=item.Header?.ToString()??"";item.Icon=MenuIcon(label);item.InputGestureText=Shortcut(label);}
    }
    private void ShowActionBar(bool visible)
    {
        if(visible==(Actions.Visibility==Visibility.Visible)&&Actions.IsHitTestVisible==visible)return;
        int revision=++actionRevision;Actions.IsHitTestVisible=visible;
        if(visible)Actions.Visibility=Visibility.Visible;
        if(!SystemParameters.MenuAnimation){Actions.Visibility=visible?Visibility.Visible:Visibility.Collapsed;Actions.Opacity=1;return;}
        var animation=new DoubleAnimation(visible?0:1,visible?1:0,TimeSpan.FromMilliseconds(90));
        animation.Completed+=(_,_)=>{if(revision==actionRevision&&!visible)Actions.Visibility=Visibility.Collapsed;};
        Actions.BeginAnimation(OpacityProperty,animation);
    }
    private void CopySelectedPaths()
    {
        try
        {
            var paths=Files.SelectedItems.Cast<BrowserEntry>().Select(e=>e.FullPath).DefaultIfEmpty(current).ToArray();
            Clipboard.SetText(string.Join(Environment.NewLine,paths));Status.Text=T("Copied");DetailsCopy.Content=T("Copied");
            activity.Write("INFO",T("PathCopiedLog",paths.Length));copyFeedback.Stop();copyFeedback.Start();
        }
        catch(System.Runtime.InteropServices.COMException){Status.Text=T("ClipboardFailed");}
    }
    private void DetailsCopyPath(object sender,RoutedEventArgs e)=>CopySelectedPaths();
    private double lastBreadcrumbWidth=-1;
    private void BreadcrumbSizeChanged(object sender,SizeChangedEventArgs e)
    {if(Math.Abs(e.NewSize.Width-lastBreadcrumbWidth)>1){lastBreadcrumbWidth=e.NewSize.Width;BuildBreadcrumbs();}}
    private void BuildBreadcrumbs()
    {
        Breadcrumbs.Children.Clear();Breadcrumbs.FlowDirection=FlowDirection.LeftToRight;
        if(breadcrumbParts.Count==0)return;
        double available=Math.Max(24,BreadcrumbFrame.ActualWidth-16);if(BreadcrumbFrame.ActualWidth<=0)available=160;
        double Width((string Title,string Path) part)=>ShortcutUi.TextWidth(part.Title+" ›",Breadcrumbs,12)+12;
        var first=breadcrumbParts[0];var currentPart=breadcrumbParts[^1];
        var visible=new List<int>{0};var hidden=new List<int>();
        if(breadcrumbParts.Count>1)
        {
            double all=breadcrumbParts.Sum(Width);
            if(all<=available){visible=Enumerable.Range(0,breadcrumbParts.Count).ToList();}
            else
            {
                double reserve=Width(first)+32+Math.Min(140,Width(currentPart));double remaining=available-reserve;
                var tail=new List<int>();for(int index=breadcrumbParts.Count-2;index>=1;index--){double width=Width(breadcrumbParts[index]);if(width>remaining)break;tail.Insert(0,index);remaining-=width;}
                visible.AddRange(tail);visible.Add(breadcrumbParts.Count-1);hidden=Enumerable.Range(1,Math.Max(0,breadcrumbParts.Count-2)).Except(tail).ToList();
            }
        }
        double fixedWidth=visible.Where(index=>index!=breadcrumbParts.Count-1).Sum(index=>Width(breadcrumbParts[index]))+(hidden.Count>0?32:0);
        void Add(int index)
        {
            var part=breadcrumbParts[index];bool last=index==breadcrumbParts.Count-1;
            var text=new TextBlock{Text=part.Title+(last?"":" ›"),FontSize=12,VerticalAlignment=VerticalAlignment.Center,FlowDirection=FlowDirection.LeftToRight};
            if(last){text.TextTrimming=TextTrimming.CharacterEllipsis;text.MaxWidth=Math.Max(12,available-fixedWidth-12);}
            var button=new Button{Content=text,Style=(Style)Resources["ExplorerActionButton"],Padding=new Thickness(5,2,5,2),Background=Brushes.Transparent,BorderThickness=new Thickness(0),ToolTip=part.Path,FlowDirection=FlowDirection.LeftToRight,FocusVisualStyle=(Style)Resources["ExplorerFocus"]};
            System.Windows.Automation.AutomationProperties.SetName(button,part.Path);button.Click+=async(_,_)=>await RunBrowserAction(()=>Navigate(part.Path));Breadcrumbs.Children.Add(button);
        }
        Add(0);
        if(hidden.Count>0)
        {
            var menu=new ContextMenu{FlowDirection=FlowDirection.LeftToRight};ConfigureMenu(menu);
            foreach(int index in hidden){var part=breadcrumbParts[index];var item=new MenuItem{Header=part.Title,ToolTip=part.Path,Style=(Style)Resources["ExplorerMenuItem"]};item.Click+=async(_,_)=>await RunBrowserAction(()=>Navigate(part.Path));menu.Items.Add(item);}
            var overflow=new Button{Content="…",Width=32,Padding=new Thickness(0),Style=(Style)Resources["ExplorerActionButton"]};DecorateButton(overflow,T("Overflow"));overflow.Click+=(_,_)=>{menu.PlacementTarget=overflow;menu.IsOpen=true;};Breadcrumbs.Children.Add(overflow);
        }
        foreach(int index in visible.Skip(1))Add(index);
    }
    private async Task OpenSelectionAsync()
    {
        var selected=Files.SelectedItems.Cast<BrowserEntry>().ToArray();
        if(selected.Length<=1){await OpenCurrent();return;}
        foreach(var entry in selected)
        {
            if(entry.IsDirectory){var start=new System.Diagnostics.ProcessStartInfo("explorer.exe"){UseShellExecute=true};start.ArgumentList.Add(entry.FullPath);System.Diagnostics.Process.Start(start);}
            else System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(entry.FullPath){UseShellExecute=true});
            await Task.Yield();
        }
    }
    private void AddDetailField(string label,string value,bool leftToRight)
    {
        DetailsText.Inlines.Add(new LineBreak());DetailsText.Inlines.Add(new Run(label+": "));
        DetailsText.Inlines.Add(new Run(value){FlowDirection=leftToRight?FlowDirection.LeftToRight:FlowDirection});
    }
}
