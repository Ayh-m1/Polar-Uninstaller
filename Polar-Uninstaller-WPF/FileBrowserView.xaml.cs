using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using System.ComponentModel;
using System.Windows.Threading;
using PolarUninstallerWpf.Services;

namespace PolarUninstallerWpf;

public partial class FileBrowserView : UserControl
{
    public bool IsOperationActive => deleting;
    private bool arabic;
    private readonly ApplicationInventoryService inventory;
    private DuplicateFilesView? duplicates;
    private bool duplicatesActive, duplicateChanges, skipNextLoad;
    public string CurrentDirectory => current;
    public event Action<string>? LocationChanged;
    private readonly ActivityLogService activity;
    private readonly FileBrowserService browser = new();
    private readonly BrowserOperationsService operations = new();
    private bool deleting;
    private Button? openAction, recycleAction, permanentAction, sizeAction;
    private readonly List<Button> selectionActions = new();
    private readonly ContextMenu more = new();
    private readonly ContextMenu quickAccess = new();
    private readonly ContextMenu itemMenu = new();
    private readonly Dictionary<string,Button> navigation = new();
    private readonly DispatcherTimer filterTimer=new(){Interval=TimeSpan.FromMilliseconds(150)};
    private readonly DispatcherTimer sortRefreshTimer=new(){Interval=TimeSpan.FromMilliseconds(250)};
    private CancellationTokenSource? sizeRequest;
    private CancellationTokenSource? autoSizeRequest;
    private int navigationGeneration;
    private int autoSizeGeneration=-1;
    private bool autoSizeRunning;
    private bool sortRefreshScheduled;
    private List<BrowserEntry> items = new();
    private bool showDetails = true;
    private readonly List<string> history = new();
    private int position = -1;
    private CancellationTokenSource? load;
    private ListCollectionView? view;
    private string current;
    private string? pendingReveal;
    private string sort = nameof(BrowserEntry.Name);
    private ListSortDirection direction = ListSortDirection.Ascending;
    private readonly SettingsService settingsService;
    private TextBlock? nameSortArrow, sizeSortArrow, typeSortArrow, dateSortArrow;
    private bool bulkSelection;
    private int selectionAnchor = -1;
    private string L(string en, string ar) => arabic ? ar : en;
    public FileBrowserView(bool arabic, string initial, ActivityLogService activity, string? initialSelection = null, SettingsService? settings = null, ApplicationInventoryService? inventory = null)
    {
        this.inventory=inventory??new ApplicationInventoryService();
        this.arabic = arabic; this.activity = activity; current = initial; pendingReveal = initialSelection;
        this.settingsService = settings ?? new SettingsService();
        InitializeComponent(); FlowDirection = arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        LoadSortPreference();
        nameSortArrow = SetColumnHeader(NameColumn, L("Name", "الاسم"));
        sizeSortArrow = SetColumnHeader(SizeColumn, L("Size", "الحجم"));
        typeSortArrow = SetColumnHeader(TypeColumn, L("Type", "النوع"));
        dateSortArrow = SetColumnHeader(DateColumn, L("Modified", "التعديل"));
        // Native WPF extended selection is required for Ctrl+Click, Shift+Click
        // and Ctrl+A. It is set explicitly here as well as in XAML so a style
        // change can never silently downgrade the File Browser to single select.
        Files.SelectionMode = SelectionMode.Extended;
        Search.ToolTip = L("Search in current folder...", "بحث في المجلد الحالي...");
SearchPlaceholder.Text = L("Search in current folder...", "بحث في المجلد الحالي...");
SearchPlaceholder.VerticalAlignment = VerticalAlignment.Center;
SearchPlaceholder.HorizontalAlignment = arabic?HorizontalAlignment.Right:HorizontalAlignment.Left; SearchPlaceholder.Margin = new Thickness(30, 0, 30, 0);
SearchPlaceholder.Visibility = Visibility.Visible;
        DetailsHeading.Text = L("Details", "التفاصيل");
        DetailsLocation.Content=L("Open Location","فتح الموقع");DetailsSize.Content=L("Calculate Folder Size","حساب حجم المجلد");
        DetailsText.Text=L("Select an item to see its details.","حدد عنصرًا لعرض تفاصيله.");DetailsLocation.IsEnabled=DetailsSize.IsEnabled=false;
        filterTimer.Tick+=(_,_)=>{filterTimer.Stop();Filter();UpdateStatus();};
        sortRefreshTimer.Tick+=(_,_)=>
        {
            sortRefreshTimer.Stop();sortRefreshScheduled=false;
            if(view==null)return;
            if(sort==nameof(BrowserEntry.Size))RefreshSortPreservingSelection();
            UpdateSelectionState(false);
        };
        PreviewKeyDown+=async (_,e)=>await HandleBrowserKey(e);
        Files.PreviewKeyDown+=FilesPreviewKeyDown;
        SizeChanged += (_, _) =>
        {
            DetailsPanel.Visibility=showDetails&&ActualWidth>=850?Visibility.Visible:Visibility.Collapsed;
            double searchWidth=ActualWidth<700?180:260;
            SearchColumn.Width=new GridLength(searchWidth);
            Toolbar.MaxWidth=Math.Clamp(ActualWidth-searchWidth-160,160,450);
        };
        InitializeActions();
        ApplyExplorerAccessibility();
        InitializeShortcutExperience();
        Unloaded += (_,_) => { sizeRequest?.Cancel();autoSizeRequest?.Cancel();filterTimer.Stop();sortRefreshTimer.Stop(); };
        Loaded += async (_,_) => { LoadSortPreference(); if(skipNextLoad){skipNextLoad=false;return;} if(!duplicatesActive) await Navigate(current, position); };
        Unloaded += (_,_) => load?.Cancel();
    }
    private void InitializeActions()
    {
        Toolbar.Children.Clear();Actions.Children.Clear();more.Items.Clear();quickAccess.Items.Clear();itemMenu.Items.Clear();
        selectionActions.Clear();navigation.Clear();
        Add(L("Details", "التفاصيل"), () => { showDetails = !showDetails; DetailsPanel.Visibility = showDetails && ActualWidth >= 850 ? Visibility.Visible : Visibility.Collapsed; return Task.CompletedTask; });
        Add(L("Back", "رجوع"), async () => { if(duplicatesActive){await ReturnToBrowserAsync();return;} if(position > 0) await Navigate(history[position - 1], position - 1); });
        Add(L("Forward", "التالي"), async () => { if(position + 1 < history.Count) await Navigate(history[position + 1], position + 1); });
        Add(L("Up", "للأعلى"), async () => { var p = Directory.GetParent(current); if(p != null) await Navigate(p.FullName); });
        Add(L("Refresh", "تحديث"), async () => await Navigate(current, position));
        Add(T("Home"),async()=>await Navigate(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        Add(L("Browse", "استعراض"), async () => { var picker = new Microsoft.Win32.OpenFolderDialog(); if(picker.ShowDialog(Window.GetWindow(this)) == true) await Navigate(picker.FolderName); });
        Add(L("Desktop", "سطح المكتب"), async () => await Navigate(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)));
        Add(L("Documents", "المستندات"), async () => await Navigate(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)));
        Add(L("Downloads", "التنزيلات"), async () => await Navigate(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")));
        Add(L("This PC", "هذا الكمبيوتر"), async () => { var root = Path.GetPathRoot(Environment.SystemDirectory); if (!string.IsNullOrWhiteSpace(root)) await Navigate(root); }, true);
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if(drive.DriveType is not (DriveType.Fixed or DriveType.Removable))continue;
                var path=drive.Name;
                var label=drive.IsReady?drive.VolumeLabel:"";
                if(string.IsNullOrWhiteSpace(label))label=L("Local Disk","القرص المحلي");
                Add(label+" ("+path.TrimEnd(Path.DirectorySeparatorChar)+")",async()=>await Navigate(path),true);
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { activity.Write("WARNING","Unavailable drive skipped: "+ex.GetType().Name); }
        }
        Add(L("Select all", "تحديد الكل"), () => { Files.SelectAll(); selectionAnchor=Files.Items.Count>0?0:-1; UpdateSelectionState(false); return Task.CompletedTask; });
        Add(L("Deselect", "إلغاء التحديد"), () => { Files.UnselectAll(); selectionAnchor=-1; UpdateSelectionState(false); return Task.CompletedTask; });
        Add(L("Open", "فتح"), OpenSelectionAsync);
        Add(L("Open location", "فتح الموقع"), () => { OpenLocation(); return Task.CompletedTask; });
        Add(L("Copy path", "نسخ المسار"), () => { CopySelectedPaths();return Task.CompletedTask; });
        Add(L("Folder size", "حجم المجلد"), CalculateSize);
        Add(L("Cancel size calculation","إلغاء حساب الحجم"),()=>{sizeRequest?.Cancel();autoSizeRequest?.Cancel();return Task.CompletedTask;});
        Add(L("Move to Recycle Bin", "نقل إلى سلة المحذوفات"), async () => await Delete(false));
        Add(L("Delete permanently", "حذف نهائي"), async () => await Delete(true));
        AddMenuButton("▣", quickAccess,T("DrivesMenu"));
        AddMenuButton("⋯", more,L("More", "المزيد"));
        AddSortControl();
        ConfigureMenu(itemMenu); Files.ContextMenu=itemMenu;
        more.Items.Add(new Separator());
        var duplicateIcon=new System.Windows.Shapes.Path{Data=System.Windows.Media.Geometry.Parse("M 6,2 L 16,2 L 16,14 M 2,6 L 12,6 L 12,18 L 2,18 Z"),Width=18,Height=18,StrokeThickness=1.5,Stretch=System.Windows.Media.Stretch.Uniform};
        duplicateIcon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,"ChromeAccent");
        var duplicateItem=new MenuItem { Header=L("Find Duplicate Files","البحث عن الملفات المكررة"), Icon=duplicateIcon, Style=(Style)Resources["ExplorerMenuItem"] };
        duplicateItem.Click+=(_,_)=>OpenDuplicates();more.Items.Add(duplicateItem);
        ReorganizeMenus();UpdateNavigationState();
        AddShortcutHelpButton();
    }
    private void LoadSortPreference()
    {
        var saved = settingsService.Load();
        sort = saved.FileBrowserSort is nameof(BrowserEntry.Name) or nameof(BrowserEntry.Size) or nameof(BrowserEntry.Type) or nameof(BrowserEntry.Modified)
            ? saved.FileBrowserSort!
            : nameof(BrowserEntry.Name);
        direction=Enum.TryParse<ListSortDirection>(saved.FileBrowserSortDirection,true,out var stored)&&Enum.IsDefined(stored)
            ?stored:DefaultAscending(sort)?ListSortDirection.Ascending:ListSortDirection.Descending;
    }
    public void SetLanguage(bool value)
    {
        if(arabic==value)return;
        arabic=value;FlowDirection=arabic?FlowDirection.RightToLeft:FlowDirection.LeftToRight;
        nameSortArrow=SetColumnHeader(NameColumn,L("Name","الاسم"));sizeSortArrow=SetColumnHeader(SizeColumn,L("Size","الحجم"));
        typeSortArrow=SetColumnHeader(TypeColumn,L("Type","النوع"));dateSortArrow=SetColumnHeader(DateColumn,L("Modified","التعديل"));
        more.IsOpen=quickAccess.IsOpen=itemMenu.IsOpen=false;InitializeActions();
        Search.ToolTip=SearchPlaceholder.Text=L("Search in current folder...","بحث في المجلد الحالي...");
        SearchPlaceholder.HorizontalAlignment=arabic?HorizontalAlignment.Right:HorizontalAlignment.Left;
        SearchPlaceholder.Margin=new Thickness(30,0,30,0);
        DetailsHeading.Text=L("Details","التفاصيل");DetailsLocation.Content=L("Open Location","فتح الموقع");DetailsSize.Content=L("Calculate Folder Size","حساب حجم المجلد");
        foreach(var entry in items)entry.Arabic=arabic;
        view?.Refresh();UpdateSortIndicators();UpdateSelectionState(false);UpdateStatus();
        navigation[L("Back","رجوع")].IsEnabled=duplicatesActive||position>0;
        navigation[L("Forward","التالي")].IsEnabled=position+1<history.Count;
        DetailsCopy.Content=T("CopyPath");BuildBreadcrumbs();UpdateNavigationState();
        ClearSearch.ToolTip=T("ClearSearch");System.Windows.Automation.AutomationProperties.SetName(ClearSearch,T("ClearSearch"));
        System.Windows.Automation.AutomationProperties.SetName(Files,T("BrowserList"));
        System.Windows.Automation.AutomationProperties.SetName(Address,T("Address"));
        System.Windows.Automation.AutomationProperties.SetName(Search,T("Search"));
        duplicates?.SetLanguage(arabic);
        RefreshShortcutLanguage();
        if(duplicatesActive){Actions.Visibility=Visibility.Collapsed;Status.Visibility=Visibility.Collapsed;}
    }
    public Task ShowFolderAsync(string path,string? reveal=null)
    {pendingReveal=reveal;skipNextLoad=!IsLoaded;return Navigate(path);}

    private void OpenDuplicates()
    {
        if(duplicatesActive)return;
        duplicates=new DuplicateFilesView(arabic,current,activity,inventory,settingsService);
        duplicates.BackRequested+=async(_,_)=>await ReturnToBrowserAsync();
        duplicates.FilesChanged+=(_,_)=>{duplicateChanges=true;if(!duplicatesActive)_=Navigate(current,position);};
        DuplicateHost.Content=duplicates;duplicateChanges=false;duplicatesActive=true;
        NavigationBar.Visibility=Visibility.Collapsed;
        BrowserBody.Visibility=Actions.Visibility=Status.Visibility=Visibility.Collapsed;
        DuplicateHost.Visibility=Visibility.Visible;
        navigation[L("Back","رجوع")].IsEnabled=true;
    }
    private void HideDuplicates()
    {
        duplicates?.CancelOperation();duplicatesActive=false;DuplicateHost.Visibility=Visibility.Collapsed;
        NavigationBar.Visibility=Visibility.Visible;
        BrowserBody.Visibility=Status.Visibility=Visibility.Visible;UpdateSelectionState(false);
        navigation[L("Back","رجوع")].IsEnabled=position>0;
    }
    private async Task ReturnToBrowserAsync()
    {
        HideDuplicates();
        if(duplicateChanges){duplicateChanges=false;await Navigate(current,position);}
    }
    private static TextBlock SetColumnHeader(GridViewColumn column, string label)
    {
        var arrow = new TextBlock { FontSize = 10, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        arrow.SetResourceReference(TextBlock.ForegroundProperty, "ChromeAccent");
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(arrow);
        var header=new GridViewColumnHeader{Content=panel,Focusable=true,IsTabStop=true,Cursor=Cursors.Hand};
        header.PreviewKeyDown+=(_,e)=>{if(e.Key is Key.Enter or Key.Space){if(!e.IsRepeat)header.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));e.Handled=true;}};
        header.GotKeyboardFocus+=(_,_)=>{if(header.ToolTip is ToolTip tip&&Keyboard.IsKeyDown(Key.Tab))tip.IsOpen=true;};
        header.LostKeyboardFocus+=(_,_)=>{if(header.ToolTip is ToolTip tip)tip.IsOpen=false;};
        System.Windows.Automation.AutomationProperties.SetName(header,label);column.Header=header;
        return arrow;
    }
    private void UpdateSortIndicators()
    {
        UpdateHeader(NameColumn,nameof(BrowserEntry.Name),nameSortArrow);UpdateHeader(SizeColumn,nameof(BrowserEntry.Size),sizeSortArrow);
        UpdateHeader(TypeColumn,nameof(BrowserEntry.Type),typeSortArrow);UpdateHeader(DateColumn,nameof(BrowserEntry.Modified),dateSortArrow);
        sortButton?.SetState(sort,direction==ListSortDirection.Ascending);
    }
    private void SaveSortPreference()
    {
        try
        {
            // Reload first so theme, language, and other pages' preferences are
            // preserved instead of being overwritten by a stale snapshot.
            var current = settingsService.Load();
            settingsService.Save(current with
            {
                FileBrowserSort = sort,
                FileBrowserSortDirection = direction.ToString()
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
    }
    private void Add(string label, Func<Task> action, bool quick = false)
    {
        var button = new Button { Content=label, Style=(Style)Resources["ExplorerActionButton"], Margin=new Thickness(0,0,6,6) };
        DecorateButton(button,label);
        button.Click += async (_,_) => await RunBrowserAction(action);
        bool bottom = new[] { L("Open", "فتح"), L("Move to Recycle Bin", "نقل إلى سلة المحذوفات"), L("Delete permanently", "حذف نهائي") }.Contains(label);
        bool primary = new[] { L("Back", "رجوع"),L("Forward", "التالي"), L("Up", "للأعلى"), L("Refresh", "تحديث"),T("Home") }.Contains(label);
        if(primary)
        {
            button.Width=34;button.Height=34;button.Padding=new Thickness(0);
            button.Content=label==L("Back","رجوع")?(arabic?"→":"←"):label==L("Forward","التالي")?(arabic?"←":"→"):label==L("Up","للأعلى")?"↑":label==T("Home")?"⌂":"↻";
            navigation[label]=button;
            System.Windows.Automation.AutomationProperties.SetName(button,label);
        }
        // Commands that only make sense for the selected item live in the
        // right-click menu; global navigation commands live in More.
        bool contextOnly = new[] { L("Open location","فتح الموقع"), L("Copy path","نسخ المسار"), L("Folder size","حجم المجلد") }.Contains(label);
        if(bottom)
        {
            Grid.SetColumn(button,label==L("Open","فتح")?0:label==L("Move to Recycle Bin","نقل إلى سلة المحذوفات")?1:3);
            if(label==L("Move to Recycle Bin","نقل إلى سلة المحذوفات"))button.Style=(Style)Resources["ExplorerPrimaryButton"];
            if(label==L("Delete permanently","حذف نهائي")){button.Style=(Style)Resources["ExplorerDangerButton"];button.Margin=new Thickness(20,0,0,6);}
            Actions.Children.Add(button);
        }
        else if(primary)
        {
            Toolbar.Children.Add(button);
            if(label==L("Up","للأعلى")){var divider=new Border{Width=1,Height=26,Margin=new Thickness(4,0,10,6)};divider.SetResourceReference(Border.BackgroundProperty,"HomeBorderBrush");Toolbar.Children.Add(divider);}
        }
        else if(!contextOnly)
        {
            bool quickMenu = quick || new[] { L("Desktop", "سطح المكتب"), L("Documents", "المستندات"), L("Downloads", "التنزيلات") }.Contains(label) || Path.IsPathRooted(label);
            var item=new MenuItem { Header=label, Style=(Style)Resources["ExplorerMenuItem"] };
            item.SetBinding(IsEnabledProperty,new Binding(nameof(Button.IsEnabled)) { Source=button });
            item.Click+=(_,_)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            (quickMenu ? quickAccess : more).Items.Add(item);
        }
        if (label == L("Open", "فتح") || label == L("Move to Recycle Bin", "نقل إلى سلة المحذوفات") || label == L("Delete permanently", "حذف نهائي") || label == L("Folder size", "حجم المجلد"))
        {
            selectionActions.Add(button);
            button.IsEnabled = false;
            if (label == L("Open", "فتح")) openAction = button;
            else if (label == L("Move to Recycle Bin", "نقل إلى سلة المحذوفات")) recycleAction = button;
            else if (label == L("Delete permanently", "حذف نهائي")) permanentAction = button;
            else sizeAction = button;
        }
        if(contextOnly || bottom)
        {
            if(contextOnly&&!selectionActions.Contains(button)){selectionActions.Add(button);button.IsEnabled=false;}
            if(label==L("Delete permanently","حذف نهائي"))itemMenu.Items.Add(new Separator());
            var item=new MenuItem{Header=label,Style=(Style)Resources["ExplorerMenuItem"]};
            if(bottom)item.SetBinding(HeaderedItemsControl.HeaderProperty,new Binding(nameof(Button.Content)){Source=button});
            if(label==L("Delete permanently","حذف نهائي"))item.SetResourceReference(Control.ForegroundProperty,"LogError");
            item.SetBinding(IsEnabledProperty,new Binding(nameof(Button.IsEnabled)){Source=button});
            item.Click+=(_,_)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));itemMenu.Items.Add(item);
        }
    }
    private void ConfigureMenu(ContextMenu menu)
    {
        menu.SetResourceReference(Control.BackgroundProperty,"HomeCardBrush");
        menu.SetResourceReference(Control.ForegroundProperty,"HomeTextBrush");
        menu.SetResourceReference(Control.BorderBrushProperty,"HomeBorderBrush");
        menu.SetResourceReference(Control.FontFamilyProperty,"InterfaceFont");
        menu.FlowDirection=FlowDirection; menu.Padding=new Thickness(6); menu.BorderThickness=new Thickness(1);
    }
    private void AddMenuButton(string title, ContextMenu menu,string tooltip)
    {
        ConfigureMenu(menu);
        var button=new Button { Content=title, ToolTip=tooltip, Width=34,Height=34,Padding=new Thickness(0),Style=(Style)Application.Current.Resources["ChromeButton"], Margin=new Thickness(0,0,6,6) };
        DecorateButton(button,tooltip);
        button.Click+=(_,_)=> { menu.PlacementTarget=button; menu.Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom; menu.IsOpen=true; };
        Toolbar.Children.Add(button);
    }
    private void FilesPreviewKeyDown(object sender,KeyEventArgs e)
    {
        // Native ListView handles Ctrl+Click and Shift+Click in Extended mode.
        // Ctrl+A is handled explicitly so it also works after filtering or
        // when the focus is on a row rather than the ListView surface.
        if(e.Key==Key.A && Keyboard.Modifiers==ModifierKeys.Control)
        {
            Files.SelectAll();
            selectionAnchor=Files.Items.Count>0?0:-1;
            UpdateSelectionState(false);
            e.Handled=true;
        }
        else if((e.Key==Key.D&&Keyboard.Modifiers==ModifierKeys.Control)||e.Key==Key.Escape)
        {Files.UnselectAll();selectionAnchor=-1;UpdateSelectionState(false);e.Handled=true;}
        else if(e.Key==Key.Enter&&Keyboard.Modifiers==ModifierKeys.None)
        {e.Handled=true;_=RunBrowserAction(OpenCurrent);}
    }
    private async Task Delete(bool permanent)
    {
        if(deleting||confirmationOpen) return;
        var selected=Files.SelectedItems.Cast<BrowserEntry>().Select(e=>e.FullPath).ToArray();
        if(selected.Length==0) return;

        IReadOnlyList<string> paths;
        try { paths=BrowserOperationsService.CollapseNestedPaths(selected); }
        catch(Exception ex) when(ex is ArgumentException or NotSupportedException or PathTooLongException)
        { Status.Text=L("Some selected paths are invalid.","بعض المسارات المحددة غير صالحة."); return; }

        // Protected locations (drive roots, Windows, Program Files, links) block
        // the whole batch instead of silently deleting the remaining items.
        var blocked=new List<string>();
        foreach(var path in paths)
        {
            try { BrowserOperationsService.ValidateDeletion(path); }
            catch(DeletionProtectionException ex) { blocked.Add($"{path} — {ex.Message}"); }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException) { }
        }
        if(blocked.Count>0)
        {
            string warning=L($"Deletion was cancelled because {blocked.Count:N0} selected item(s) are protected or unsafe to remove:",$"أُلغي الحذف لأن {blocked.Count:N0} من العناصر المحددة محمية أو غير آمنة للإزالة:")+"\n\n"+string.Join("\n",blocked.Take(10))+(blocked.Count>10?"\n…":"");
            MessageBox.Show(Window.GetWindow(this),warning,L("Deletion blocked","تم منع الحذف"),MessageBoxButton.OK,MessageBoxImage.Warning);
            activity.Write("WARNING","Batch deletion blocked by safety validation.");
            return;
        }

        int count=paths.Count;
        string header=permanent
            ? L($"Permanently delete {count:N0} selected item(s)? This cannot be undone.",$"حذف {count:N0} عنصرًا محددًا نهائيًا؟ لا يمكن التراجع.")
            : L($"Move {count:N0} selected item(s) to the Recycle Bin?",$"نقل {count:N0} عنصرًا محددًا إلى سلة المحذوفات؟");
        string listing=string.Join("\n",paths.Take(12))+(count>12?"\n…":"");
        string detail=permanent
            ? L("\n\nPermanent deletion bypasses the Recycle Bin.", "\n\nالحذف النهائي يتجاوز سلة المحذوفات.")
            : L("\n\nItems can be restored from the Recycle Bin.", "\n\nيمكن استعادة العناصر من سلة المحذوفات.");
        var prompt=header+"\n\n"+listing+detail;
        confirmationOpen=true;
        try
        {
            if(permanent)
            {
                var rows=Files.SelectedItems.Cast<BrowserEntry>().Where(entry=>paths.Contains(entry.FullPath,StringComparer.OrdinalIgnoreCase)).ToArray();
                var dialog=new PermanentDeleteDialog(arabic,Files.SelectedItems.Count,rows.Sum(entry=>entry.Size??0),rows.Any(entry=>!entry.Size.HasValue||entry.SizeState==BrowserSizeState.Partial),paths,(Style)Resources["ExplorerDangerButton"],(Style)Resources["ExplorerActionButton"]){Owner=Window.GetWindow(this),Icon=Window.GetWindow(this)?.Icon};
                if(dialog.ShowDialog()!=true)return;
            }
            else if(MessageBox.Show(Window.GetWindow(this),prompt,L("Confirm deletion","تأكيد الحذف"),MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
        }
        finally{confirmationOpen=false;}

        deleting=true; Toolbar.IsEnabled=Actions.IsEnabled=Files.IsEnabled=false;
        try
        {
            var results=await operations.DeleteAsync(paths,permanent,CancellationToken.None);
            await Navigate(current,position);
            int failed=results.Count(r=>!r.Success);
            int removed=results.Count-failed;
            Status.Text=L($"{removed:N0} removed · {failed:N0} failed",$"حُذف {removed:N0} · تعذر {failed:N0}");
            if(failed>0)
                Status.Text+=" · "+string.Join(" · ",results.Where(r=>!r.Success).Take(3).Select(r=>Path.GetFileName(r.Path)+": "+r.Error));
            foreach(var failure in results.Where(r=>!r.Success)) activity.Write("WARNING",L("Deletion failed: ","تعذر الحذف: ")+Path.GetFileName(failure.Path)+" · "+failure.Error);
            if(removed>0) activity.Write("SUCCESS",permanent
                ?L($"Permanently deleted and verified: {removed:N0} selected item(s).",$"حُذف نهائيًا وتُحقق منه: {removed:N0} عنصرًا محددًا.")
                :L($"Original items moved to Windows Recycle Bin and verified: {removed:N0}.",$"نُقلت العناصر الأصلية إلى سلة Windows وتُحقق منها: {removed:N0}."));
        }
        catch(OperationCanceledException) { Status.Text=L("Deletion cancelled.","أُلغي الحذف."); }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        { Status.Text=L("Deletion failed: ","تعذر الحذف: ")+ex.Message; }
        finally { deleting=false; Toolbar.IsEnabled=Actions.IsEnabled=Files.IsEnabled=true; }
    }
    private async Task Navigate(string path, int? index = null)
    {
        if(duplicatesActive)HideDuplicates();
        sizeRequest?.Cancel();
        autoSizeRequest?.Cancel();
        autoSizeRunning=false;
        autoSizeGeneration=-1;
        load?.Cancel(); var request=load=new CancellationTokenSource();
        int generation=++navigationGeneration;
        try
        {
            Status.Text=L("Loading…","جارٍ التحميل…"); path=Path.GetFullPath(path);
            var entries=await browser.ListAsync(path,request.Token);
            if(request.IsCancellationRequested || generation!=navigationGeneration) return;
            current=path; LocationChanged?.Invoke(current); Address.Text=path;Address.Visibility=Visibility.Collapsed;
            breadcrumbParts.Clear();
            string? root=Path.GetPathRoot(path); string accumulated=root ?? path;
            void Crumb(string title,string target)=>breadcrumbParts.Add((title,target));
            if(root!=null) Crumb(root,root);
            foreach(var segment in path[(root?.Length ?? 0)..].Split(Path.DirectorySeparatorChar,StringSplitOptions.RemoveEmptyEntries)) { accumulated=Path.Combine(accumulated,segment); Crumb(segment,accumulated); }
            BuildBreadcrumbs();
            if(index is >=0) position=index.Value;
            else { if(position+1<history.Count) history.RemoveRange(position+1,history.Count-position-1); history.Add(path); position=history.Count-1; }
            items=entries.ToList();
            foreach(var entry in items) entry.Arabic=arabic;
            // Fresh cached measurements are applied before the first sort so
            // Back/Forward/Refresh and restarted views show the correct order
            // immediately instead of waiting for the debounced refresh.
            foreach(var entry in items)
            {
                if(!entry.IsDirectory||entry.Size.HasValue)continue;
                if(FolderSizeService.TryGetCached(entry.FullPath) is { } cached) ApplyFolderSize(entry,cached);
            }
            view=new ListCollectionView(items); Files.ItemsSource=view; Filter(); ApplySort();
            selectionAnchor=-1;
            UpdateStatus();
            ApplyPendingReveal();
            UpdateNavigationState();
            activity.Write("INFO", L("Opened folder: ", "فُتح المجلد: ")+path);
            // Start automatic folder measurements after the first layout pass so
            // visible rows are prioritized over off-screen work.
            _=Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if(generation==navigationGeneration) _=AutoSizeFoldersAsync(items,generation);
            }));
        }
        catch(OperationCanceledException) { }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException) { if(!request.IsCancellationRequested) Status.Text=L("Cannot open folder: ","تعذر فتح المجلد: ")+ex.Message; }
    }
    private void Filter() { if(view != null) { var query=Search.Text; view.Filter=item=>item is BrowserEntry e && e.Name.Contains(query,StringComparison.CurrentCultureIgnoreCase); } }
    private void ApplyPendingReveal()
    {
        if (string.IsNullOrWhiteSpace(pendingReveal)) return;
        string reveal;
        try { reveal = Path.GetFullPath(pendingReveal!); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { pendingReveal = null; return; }

        string trimmedCurrent = current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string trimmedReveal = reveal.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(trimmedReveal, trimmedCurrent, StringComparison.OrdinalIgnoreCase))
        {
            pendingReveal = null;
            Status.Text = L("Opened selected folder: ", "فُتح المجلد المحدد: ") + current;
            return;
        }

        string? parent;
        try { parent = Path.GetDirectoryName(reveal); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { pendingReveal = null; return; }
        if (!string.Equals(parent, current, StringComparison.OrdinalIgnoreCase)) return;

        var entry = view?.Cast<BrowserEntry>().FirstOrDefault(e => string.Equals(e.FullPath, reveal, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            pendingReveal = null;
            Status.Text = L("The selected item is no longer available in this folder.", "العنصر المحدد لم يعد متوفرًا في هذا المجلد.");
            return;
        }
        pendingReveal = null;
        Files.SelectedItem = entry;
        selectionAnchor = Files.Items.IndexOf(entry);
        Files.ScrollIntoView(entry);
        Files.Focus();
        Status.Text = L("Selected: ", "المحدد: ") + entry.FullPath;
    }
    private void SearchChanged(object sender, TextChangedEventArgs e) { SearchPlaceholder.Visibility = string.IsNullOrEmpty(Search.Text) ? Visibility.Visible : Visibility.Collapsed; filterTimer.Stop();filterTimer.Start(); }
    private async void AddressKey(object sender, KeyEventArgs e) { if(e.Key==Key.Enter) await Navigate(Address.Text);else if(e.Key==Key.Escape){Address.Text=current;Address.Visibility=Visibility.Collapsed;} }
    private void ShowAddress(){Address.Text=current;Address.Visibility=Visibility.Visible;Address.Focus();Address.SelectAll();}
    private void EditAddress(object sender,MouseButtonEventArgs e){if(e.ClickCount==2)ShowAddress();}
    private void AddressBlur(object sender,KeyboardFocusChangedEventArgs e)=>Address.Visibility=Visibility.Collapsed;
    private async void OpenSelected(object sender, MouseButtonEventArgs e)
    {
        if(e.OriginalSource is DependencyObject source&&ItemsControl.ContainerFromElement(Files,source) is ListViewItem row)
        {row.Focus();await RunBrowserAction(OpenCurrent);}
    }
    private async Task OpenCurrent()
    {
        var focused=Keyboard.FocusedElement as DependencyObject;
        var entry=(focused==null?null:(ItemsControl.ContainerFromElement(Files,focused) as ListViewItem)?.Content as BrowserEntry)??Files.SelectedItem as BrowserEntry;
        if(entry==null)return;
        if(entry.IsDirectory) await Navigate(entry.FullPath);
        else { try { Process.Start(new ProcessStartInfo(entry.FullPath) { UseShellExecute=true }); } catch(Exception ex) when(ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Status.Text=ex.Message; } }
    }
    private async void SortClick(object sender, RoutedEventArgs e)
    {
        if(sender is not GridViewColumnHeader header) return;
        if(header.Column!=NameColumn&&header.Column!=SizeColumn&&header.Column!=TypeColumn&&header.Column!=DateColumn)return;
        var key=header.Column==NameColumn ? nameof(BrowserEntry.Name) : header.Column==SizeColumn ? nameof(BrowserEntry.Size) : header.Column==TypeColumn ? nameof(BrowserEntry.Type) : nameof(BrowserEntry.Modified);
        var next=NextHeaderSort(key);
        cycleStep=cycleColumn==key?cycleStep+1:1;cycleColumn=key;
        if(cycleStep>=3){cycleColumn=null;cycleStep=0;}
        await RunBrowserAction(()=>SetSortAsync(next.Key,next.Ascending));
    }
    private void ApplySort()
    {
        UpdateSortIndicators();
        if(view==null)return;
        using(view.DeferRefresh())
        {
            view.SortDescriptions.Clear();
            if(sort==nameof(BrowserEntry.Size))
                view.CustomSort=new BrowserEntryComparer(direction==ListSortDirection.Descending);
            else
            {
                view.CustomSort=null;
                view.SortDescriptions.Add(new SortDescription(sort,direction));
                // Stable secondary ordering for equal names, types, or dates.
                if(sort!=nameof(BrowserEntry.Name))
                    view.SortDescriptions.Add(new SortDescription(nameof(BrowserEntry.Name),ListSortDirection.Ascending));
            }
        }
    }
    private async Task SortBySizeAsync(bool largest)
    {
        sort=nameof(BrowserEntry.Size);
        direction=largest?ListSortDirection.Descending:ListSortDirection.Ascending;
        ApplySort();
        SaveSortPreference();
        if(!autoSizeRunning || autoSizeGeneration!=navigationGeneration)
            await AutoSizeFoldersAsync(items,navigationGeneration);
    }
    private async Task AutoSizeFoldersAsync(List<BrowserEntry> snapshot,int generation)
    {
        if(generation!=navigationGeneration) return;
        if(autoSizeRunning && autoSizeGeneration==generation) return;
        autoSizeRunning=true;autoSizeGeneration=generation;
        try
        {
            var folders=snapshot.Where(entry=>entry.IsDirectory).ToArray();
            if(folders.Length==0)return;
            var pending=new List<BrowserEntry>();
            foreach(var folder in folders)
            {
                if(folder.Size.HasValue) continue;
                if(FolderSizeService.TryGetCached(folder.FullPath) is { } cached)
                {
                    ApplyFolderSize(folder,cached);
                    continue;
                }
                folder.SizeState=BrowserSizeState.Calculating;
                pending.Add(folder);
            }
            if(pending.Count==0)
            {
                // Every folder was already measured (for example a revisit or a
                // refresh), so re-sort right away instead of waiting for the timer.
                if(sort==nameof(BrowserEntry.Size))RefreshSortPreservingSelection();
                return;
            }

            autoSizeRequest?.Cancel();
            var request=autoSizeRequest=new CancellationTokenSource();
            int completed=0;
            Status.Text=L($"Calculating folder sizes… 0 of {pending.Count:N0}",$"جارٍ حساب أحجام المجلدات… 0 من {pending.Count:N0}");

            // Visible/current-order rows are queued first; the shared service
            // still limits concurrent disk scans to four folders.
            var order=ViewOrder(snapshot);
            var prioritized=pending
                .OrderBy(entry=>order.TryGetValue(entry,out int index)?index:int.MaxValue)
                .ThenBy(entry=>entry.Name,StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            var tasks=prioritized.Select(async entry=>
            {
                try
                {
                    var result=await FolderSizeService.GetAsync(entry.FullPath,request.Token);
                    if(request.IsCancellationRequested||generation!=navigationGeneration)return;
                    if(!snapshot.Contains(entry))return;
                    ApplyFolderSize(entry,result);
                    ScheduleSortRefresh();
                    int done=Interlocked.Increment(ref completed);
                    if(done==prioritized.Length||done%5==0)
                        _=Dispatcher.BeginInvoke(new Action(()=>
                        {
                            if(generation==navigationGeneration&&!request.IsCancellationRequested)
                                Status.Text=L($"Calculating folder sizes… {done:N0} of {prioritized.Length:N0}",$"جارٍ حساب أحجام المجلدات… {done:N0} من {prioritized.Length:N0}");
                        }));
                }
                catch(OperationCanceledException) { }
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
                {
                    if(!request.IsCancellationRequested&&generation==navigationGeneration&&snapshot.Contains(entry))
                    {
                        ApplyFolderSize(entry,new FolderSizeResult(null,false,true));
                        ScheduleSortRefresh();
                    }
                }
            }).ToArray();
            try { await Task.WhenAll(tasks); }
            catch(OperationCanceledException) { }
            if(request.IsCancellationRequested||generation!=navigationGeneration)return;
            if(sort==nameof(BrowserEntry.Size))RefreshSortPreservingSelection();
            UpdateSelectionState(false);
            Status.Text=L($"{snapshot.Count:N0} items · folder sizes updated",$"{snapshot.Count:N0} عنصر · تم تحديث أحجام المجلدات");
        }
        finally
        {
            if(autoSizeGeneration==generation)autoSizeRunning=false;
        }
    }
    private Dictionary<BrowserEntry,int> ViewOrder(List<BrowserEntry> snapshot)
    {
        var map=new Dictionary<BrowserEntry,int>(ReferenceEqualityComparer.Instance);
        if(view!=null)
        {
            int index=0;
            foreach(var entry in view.Cast<BrowserEntry>())map[entry]=index++;
        }
        else
        {
            for(int i=0;i<snapshot.Count;i++)map[snapshot[i]]=i;
        }
        return map;
    }
    private void ApplyFolderSize(BrowserEntry entry,FolderSizeResult result)
    {
        // A measurement continuation may complete on a worker thread in hosts
        // that do not install a DispatcherSynchronizationContext. Bound rows must
        // only ever be mutated on the UI thread.
        if(!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(()=>ApplyFolderSize(entry,result)));
            return;
        }
        if(result.Unavailable||!result.Bytes.HasValue)
        {
            entry.Size=null;
            entry.SizeState=BrowserSizeState.Unavailable;
        }
        else
        {
            entry.Size=result.Bytes;
            entry.SizeState=result.Partial?BrowserSizeState.Partial:BrowserSizeState.Known;
        }
    }
    private void ScheduleSortRefresh()
    {
        if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(ScheduleSortRefresh));return;}
        if(sortRefreshScheduled)return;
        sortRefreshScheduled=true;
        sortRefreshTimer.Stop();sortRefreshTimer.Start();
    }
    private void RefreshSortPreservingSelection()
    {
        if(view==null)return;
        var selected=Files.SelectedItems.Cast<BrowserEntry>().ToArray();
        view.Refresh();
        if(selected.Length>0)
        {
            bulkSelection=true;
            try
            {
                foreach(var entry in selected)
                    if(!Files.SelectedItems.Contains(entry))Files.SelectedItems.Add(entry);
            }
            finally { bulkSelection=false; }
        }
        UpdateSortIndicators();
    }
    private void FilesSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if(bulkSelection)return;
        UpdateSelectionState(true);
    }
    private void UpdateSelectionState(bool cancelManualSize)
    {
        if(DetailsText==null)return;
        var selected=Files.SelectedItems.Cast<BrowserEntry>().ToArray();
        if(cancelManualSize)sizeRequest?.Cancel();
        ShowActionBar(selected.Length>0&&!duplicatesActive);
        bool hasSelection=selected.Length>0&&!deleting;
        foreach(var action in selectionActions)action.IsEnabled=hasSelection;
        if(openAction!=null) openAction.IsEnabled=hasSelection;
        if(recycleAction!=null) recycleAction.IsEnabled=hasSelection;
        if(permanentAction!=null) permanentAction.IsEnabled=hasSelection;
        if(sizeAction!=null) sizeAction.IsEnabled=hasSelection&&selected.Any(x=>x.IsDirectory);
        foreach(var pair in new[]{(openAction,"Open"),(recycleAction,"Recycle"),(permanentAction,"Permanent")})
            if(pair.Item1 is {} button){button.Content=BrowserText.ActionLabel(pair.Item2,selected.Length,arabic);System.Windows.Automation.AutomationProperties.SetName(button,button.Content.ToString());}
        DetailsHeading.Text=selected.Length>1?T("SelectedCount",selected.Length):T("Details");
        DetailsIcon.Source=null;DetailsLocation.IsEnabled=DetailsCopy.IsEnabled=hasSelection;
        DetailsSize.IsEnabled=hasSelection&&(manualSizing||selected.Any(x=>x.IsDirectory));
        DetailsSize.Content=T(manualSizing?"CancelCalculation":"CalculateSize");
        DetailsPath.Text=selected.Length==1?selected[0].FullPath:"";
        DetailsPath.ToolTip=DetailsPath.Text;
        if(selected.Length==1)
        {
            DetailsText.Text=selected[0].Name+"\n";
            AddDetailField(T("Size"),selected[0].DisplaySize,true);
            AddDetailField(T("Type"),selected[0].IsDirectory?L("Folder","مجلد"):selected[0].Type,false);
            AddDetailField(T("Created"),selected[0].Created.ToString("g",DisplayFormat.NumericCulture),true);
            AddDetailField(T("Modified"),selected[0].Modified.ToString("g",DisplayFormat.NumericCulture),true);
        }
        else if(selected.Length>1)
        {
            long known=selected.Sum(x=>x.Size??0);
            int folders=selected.Count(x=>x.IsDirectory);
            int calculating=selected.Count(x=>x.IsDirectory&&x.SizeState==BrowserSizeState.Calculating);
            int unknown=selected.Count(x=>x.IsDirectory&&!x.Size.HasValue&&x.SizeState!=BrowserSizeState.Calculating);
            DetailsText.Text=T("SelectedSummary",selected.Length,selected.Length-folders,folders);
            AddDetailField(T("KnownSize"),BrowserEntry.FormatBytes(known),true);
            if(calculating>0)DetailsText.Inlines.Add(new System.Windows.Documents.Run("\n"+T("Calculating")));
            else if(unknown>0)DetailsText.Inlines.Add(new System.Windows.Documents.Run("\n"+T("UnknownSizes")));
        }
        else DetailsText.Text=L("Select an item to see its details.","حدد عنصرًا لعرض تفاصيله.");
        UpdateStatus();if(selected.Length==1)_=UpdateDetailsIcon(selected[0]);
    }
    private void FilesPreviewMouseLeftButtonDown(object sender,MouseButtonEventArgs e)
    {
        if(e.OriginalSource is not DependencyObject source)return;
        if(ItemsControl.ContainerFromElement(Files,source) is not ListViewItem container)return;
        int index=Files.ItemContainerGenerator.IndexFromContainer(container);
        if(index<0)return;
        if(e.ClickCount==2)
        {Files.UnselectAll();container.IsSelected=true;container.Focus();e.Handled=true;_=RunBrowserAction(OpenCurrent);return;}
        var modifiers=Keyboard.Modifiers;
        bool control=(modifiers&ModifierKeys.Control)!=0;
        bool shift=(modifiers&ModifierKeys.Shift)!=0;
        if(shift)
        {
            int anchor=selectionAnchor>=0?selectionAnchor:index;
            SelectRange(anchor,index,control);
            selectionAnchor=anchor;
            e.Handled=true;
        }
        else if(control)
        {
            selectionAnchor=index;
            container.IsSelected=!container.IsSelected;
            container.Focus();
            UpdateSelectionState(false);
            e.Handled=true;
        }
        else
        {
            selectionAnchor=index;
            bool deselect=container.IsSelected&&Files.SelectedItems.Count==1&&e.ClickCount==1;
            Files.UnselectAll();container.IsSelected=!deselect;container.Focus();
            UpdateSelectionState(false);e.Handled=true;
        }
    }
    private void SelectionCircleClick(object sender,MouseButtonEventArgs e)
    {
        if(sender is not DependencyObject source)return;
        if(ItemsControl.ContainerFromElement(Files,source) is not ListViewItem container)return;
        int index=Files.ItemContainerGenerator.IndexFromContainer(container);
        if(index>=0)selectionAnchor=index;
        container.IsSelected=!container.IsSelected;
        container.Focus();
        UpdateSelectionState(false);
        e.Handled=true;
    }
    private void SelectRange(int from,int to,bool add)
    {
        bulkSelection=true;
        try
        {
            if(!add)Files.SelectedItems.Clear();
            int start=Math.Min(from,to),end=Math.Max(from,to);
            for(int i=start;i<=end;i++)
            {
                if(i<0||i>=Files.Items.Count)continue;
                if(Files.Items[i] is BrowserEntry entry&&!Files.SelectedItems.Contains(entry))Files.SelectedItems.Add(entry);
            }
        }
        finally { bulkSelection=false; }
        UpdateSelectionState(false);
    }
    private async void FileIconLoaded(object sender, RoutedEventArgs e)
        => await LoadFileIcon(sender);
    private async void FileIconChanged(object sender,DependencyPropertyChangedEventArgs e)=>await LoadFileIcon(sender);
    private async Task LoadFileIcon(object sender)
    {
        if(sender is not Image image || image.DataContext is not BrowserEntry entry) return;
        image.Source=null;
        try
        {
            var icon=await ProgramIconService.LoadAsync(entry.FullPath);
            if(ReferenceEquals(image.DataContext,entry)) image.Source=icon?.Image;
        }
        catch(Exception ex) when(ex is IOException or InvalidOperationException or System.Runtime.InteropServices.COMException) { }
    }
    private async Task UpdateDetailsIcon(BrowserEntry entry)
    {
        try{var icon=await ProgramIconService.LoadAsync(entry.FullPath);if(ReferenceEquals(Files.SelectedItem,entry))DetailsIcon.Source=icon?.Image;}
        catch(Exception ex)when(ex is IOException or InvalidOperationException or System.Runtime.InteropServices.COMException){ }
    }
    private void UpdateStatus()
    {
        if(Files==null)return;
        var selected=Files.SelectedItems.Cast<BrowserEntry>().ToArray();
        long known=selected.Sum(x=>x.Size??0);
        int calculating=selected.Count(x=>x.IsDirectory&&x.SizeState==BrowserSizeState.Calculating);
        int unknown=selected.Count(x=>x.IsDirectory&&!x.Size.HasValue&&x.SizeState!=BrowserSizeState.Calculating);
        Status.Text=T("ItemCount",Files.Items.Count)+" · "+T("SelectedCount",selected.Length)+" · "+T("KnownSize")+": ";
        Status.Inlines.Add(new System.Windows.Documents.Run(BrowserEntry.FormatBytes(known)){FlowDirection=FlowDirection.LeftToRight});
        if(calculating>0)Status.Inlines.Add(" · "+T("Calculating"));
        else if(unknown>0)Status.Inlines.Add(" · "+T("UnknownSizes"));
    }
    private void OpenLocation()
    {
        var selected=Files.SelectedItems.Cast<BrowserEntry>().ToArray();
        foreach(var path in selected.Select(entry=>entry.FullPath).DefaultIfEmpty(current))
        {
            var start=new ProcessStartInfo("explorer.exe"){UseShellExecute=true};
            if(selected.Length>0)start.ArgumentList.Add("/select,");start.ArgumentList.Add(path);Process.Start(start);
        }
    }
    private void DetailsOpenLocation(object sender,RoutedEventArgs e){try{OpenLocation();}catch(Exception ex){Status.Text=ex.Message;}}
    private async void DetailsCalculateSize(object sender,RoutedEventArgs e)
    {if(manualSizing){sizeRequest?.Cancel();return;}await RunBrowserAction(CalculateSize);}
    private async Task CalculateSize()
    {
        var selected=Files.SelectedItems.Cast<BrowserEntry>().Where(entry=>entry.IsDirectory).ToArray();
        if(selected.Length==0)return;
        sizeRequest?.Cancel();var request=sizeRequest=new CancellationTokenSource();
        manualSizing=true;DetailsProgress.Visibility=Visibility.Visible;DetailsProgress.IsIndeterminate=true;UpdateSelectionState(false);
        try
        {
        foreach(var entry in selected)entry.SizeState=BrowserSizeState.Calculating;
        Status.Text=L($"Calculating {selected.Length:N0} folder size(s)… Cancel is available in More.",$"جارٍ حساب حجم {selected.Length:N0} مجلد… الإلغاء متاح في المزيد.");
        int completed=0;
        var tasks=selected.Select(async entry=>
        {
            try
            {
                var result=await FolderSizeService.RecalculateAsync(entry.FullPath,request.Token);
                if(request.IsCancellationRequested)return;
                ApplyFolderSize(entry,result);
                ScheduleSortRefresh();
                Interlocked.Increment(ref completed);
            }
            catch(OperationCanceledException) { }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
            {
                if(!request.IsCancellationRequested){ApplyFolderSize(entry,new FolderSizeResult(null,false,true));ScheduleSortRefresh();}
            }
        }).ToArray();
        try { await Task.WhenAll(tasks); }
        catch(OperationCanceledException) { }
        if(request.IsCancellationRequested)return;
        RefreshSortPreservingSelection();
        long total=selected.Sum(entry=>entry.Size??0);
        Status.Text=L($"{selected.Length:N0} folder(s) · {BrowserEntry.FormatBytes(total)}",$"{selected.Length:N0} مجلد · {BrowserEntry.FormatBytes(total)}");
        UpdateSelectionState(false);
        }
        finally
        {
            if(ReferenceEquals(sizeRequest,request))
            {
                foreach(var entry in selected.Where(entry=>entry.SizeState==BrowserSizeState.Calculating))entry.SizeState=entry.Size.HasValue?BrowserSizeState.Known:BrowserSizeState.Unknown;
                manualSizing=false;DetailsProgress.IsIndeterminate=false;DetailsProgress.Visibility=Visibility.Collapsed;UpdateSelectionState(false);
            }
        }
    }
    private void FilesSizeChanged(object sender,SizeChangedEventArgs e)
    {
        if(NameColumn==null)return;
        double width=Math.Min(1440,Files.ActualWidth)-96;
        bool compact=width<740;
        SizeColumn.Width=compact?108:134;TypeColumn.Width=compact?108:134;DateColumn.Width=compact?174:214;
        NameColumn.Width=Math.Max(100,width-SizeColumn.Width-TypeColumn.Width-DateColumn.Width);
    }
}
