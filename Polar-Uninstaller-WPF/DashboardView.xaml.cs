using System.IO;
using System.Linq;
using System.Diagnostics;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PolarUninstallerWpf.Services;

namespace PolarUninstallerWpf;
public sealed record StorageRow(string Name,string Path,long Bytes,double Percent,string Icon,bool IsDirectory=false)
{
    /// <summary>Partial folder sizes are shown with "≥" because inaccessible
    /// children may add bytes that this scan could not read.</summary>
    public string Size=>(Partial?"≥ ":"")+DriveSummary.Format(Bytes);
    public string Source {get;init;}="";
    public bool Manual {get;init;}
    public bool Partial {get;init;}
}
public sealed record RecentUninstallRow(string Name,string Subtitle,string Size,ImageSource? Icon,string FallbackGlyph);
public partial class DashboardView : UserControl
{
    public string RowRefreshLabel=>L("Rescan item","أعد فحص العنصر");
    public string RowRemoveLabel=>L("Remove from this list only","أزل من القائمة فقط");
    private bool arabic;
    private readonly LocalizedPresentation language = new();
    private readonly DashboardScanSession scanSession;
    private StorageSnapshot? renderedScanSnapshot;
    private bool snapshotCached;
    private bool IsScanning => scan != null || scanSession.Phase == DashboardScanPhase.Running;
    private readonly ActivityLogService activity;
    private readonly Action<string,string?>? openInBrowser;
    private readonly ApplicationInventoryService inventory;
    private readonly SpaceFreedService freedSpace;
    public event Action<string>? NavigationRequested;
    private readonly DashboardStorageService storage=new();
    private readonly UninstallHistoryService history;
    private CancellationTokenSource? scan;
    private string scanRoot="";
    private bool scanIsDriveRoot;
    private bool scanIncomplete;
    private bool subscribed;
    private bool clearingCache;
    private bool updatingDrives;
    private int lifetime;
    private Task? driveRefresh;
    private Task? historyRefresh;
    private bool refreshing;
    private CancellationTokenSource? refreshCancellation;
    private StorageSnapshot? displayedSnapshot;
    private string[] hiddenDashboardPaths=[];
    private bool overviewScheduled;
    private readonly ManualStorageService manualService;
    private readonly Dictionary<string,ManualStorageItem> manual=new(StringComparer.OrdinalIgnoreCase);
    private StorageRow[] automaticFolders=[];
    private StorageRow[] automaticFiles=[];
    private StorageCategory category=StorageCategory.Folders;
    private int visibleLimit=DefaultVisible;
    private long minimumFileBytes;
    private bool sizeFilterReady;
    private int cacheGeneration;
    private Task pendingSave=Task.CompletedTask;
    private const int DefaultVisible=50;
    private const int PageSize=50;
    private enum StorageCategory { Folders, Files }
    private sealed record SizeFilterOption(string Label,long Bytes){public override string ToString()=>Label;}
    private string L(string en,string ar)=> BidiText.Localize(arabic,en,ar);
    public DashboardView(bool arabic,ActivityLogService activity,Action<string,string?>? openInBrowser=null,ApplicationInventoryService? inventory=null,SpaceFreedService? freedSpace=null,UninstallHistoryService? history=null,ManualStorageService? manualService=null,DashboardScanSession? scanSession=null)
    {
        this.arabic=arabic;this.scanSession=scanSession??new DashboardScanSession(activity);this.activity=activity;this.openInBrowser=openInBrowser;
        this.inventory=inventory??new ApplicationInventoryService();this.freedSpace=freedSpace??SpaceFreedService.Instance;
        this.history=history??new UninstallHistoryService();this.manualService=manualService??new ManualStorageService();
        InitializeComponent();UiPerformanceProbe.Attach(this);
        SetLanguage(arabic);
        language.Text(ScanStatus,"Select a drive and scan it, or choose a folder to analyze.","اختر قرصًا وافحصه، أو اختر مجلدًا للتحليل.");
        ScanScopeText.Text=L("No scan yet","لم يتم الفحص بعد");
        Loaded+=async (_,_)=>await RunDashboardActionAsync(LoadDashboardAsync);
        Unloaded+=(_,_)=> {lifetime++;this.scanSession.Changed-=ShowScanSession;if(subscribed) {this.inventory.Changed-=InventoryChanged;this.freedSpace.Changed-=OverviewChanged;FileChangeHub.Changed-=DashboardFilesChanged;UninstallHistoryService.HistoryChanged-=HistoryChanged;subscribed=false;} };
    }

    public void SetLanguage(bool value)
    {
        arabic=value;
        language.Refresh(value);
        FlowDirection=arabic?FlowDirection.RightToLeft:FlowDirection.LeftToRight;
        Heading.Text=L("Dashboard","لوحة المعلومات"); Subtitle.Text=L("System overview and quick insights","نظرة على النظام وإحصاءات سريعة");
        RecentTitle.Text=L("Recent Uninstalls","آخر عمليات الإزالة");
        ProgramsLabel.Text=L("Programs installed","البرامج المثبّتة"); FreedLabel.Text=L("Space freed so far","المساحة المحررة حتى الآن"); FreeLabel.Text=L("Free Space","المساحة المتاحة");
        ProgramsDetail.Text=L("Shared with Deep Uninstaller","من فحص الإزالة العميقة"); FreedDetail.Text=L("Recorded cleanups + known uninstall sizes","التنظيف المسجّل وأحجام الإزالة المعروفة");
        StorageWarningText.Text=L("Storage is nearly full. Free up temporary files.","مساحة التخزين قاربت الامتلاء. يمكنك تنظيف الملفات المؤقتة.");
        OpenCleanerButton.Content=L("Open Quick Cleaner","فتح التنظيف السريع");
        OpenCleanerButton.HorizontalAlignment=arabic?HorizontalAlignment.Right:HorizontalAlignment.Left;
        RecentEmpty.Text=L("Your completed uninstalls will appear here.","ستظهر عمليات الإزالة المكتملة هنا.");
        OpenUninstallerButton.Content=L("Browse installed programs","استعراض البرامج المثبّتة");
        UpdateOverview();
        StorageTitle.Text=L("Storage Overview","نظرة على التخزين"); LargestTitle.Text=L("Largest Folders & Files","أكبر المجلدات والملفات"); UsedLabel.Text=L("Used","مستخدم");
        ClearCacheButton.Content=L("Clear Cache","إزالة الكاش"); RefreshButton.Content=L("Refresh","تحديث");
        AddFileButton.Content=L("Add File","إضافة ملف");AddFolderButton.Content=L("Choose Folder","اختيار مجلد");
        FoldersTab.Content=L("Largest Folders","أكبر المجلدات");FilesTab.Content=L("Largest Files","أكبر الملفات");
        ShowMoreButton.Content=L("Show more","عرض المزيد");
        FileSizeFilter.ToolTip=L("Only list files at or above this size","اعرض فقط الملفات بهذا الحجم أو أكبر");
        InitializeSizeFilter();
        UpdateCategoryChrome();
        ClearCacheButton.ToolTip=L("Reset dashboard statistics, uninstall history, cached scans and saved scan items","إعادة تعيين إحصاءات لوحة المعلومات وسجل الإزالة ونتائج الفحص المحفوظة وعناصر الفحص المضافة");
        
        CancelScanButton.Content=L("Cancel scan","إلغاء الفحص");
        AddFolderButton.ToolTip=L("Scan an entire folder","فحص مجلد كامل");
        UsedCaption.Text=L("Used","مستخدم");FreeCaption.Text=L("Available","متاح");TotalCaption.Text=L("Total","الإجمالي");
        RefreshButton.Content=refreshing?L("Refreshing…","جارٍ التحديث…"):L("Refresh","تحديث");
        ShowDrive(DriveSelector.SelectedItem as DriveSummary);
        LocalizedPresentation.SetRuns(ScanScopeText,string.IsNullOrWhiteSpace(scanRoot)?L("No scan yet","لم يتم الفحص بعد"):ScopeText());
        if(scanSession.Phase is DashboardScanPhase.Running or DashboardScanPhase.Cancelled or DashboardScanPhase.Failed) ShowScanSession();
        else if(displayedSnapshot is {} snapshot) ShowSnapshotStatus(snapshot,snapshotCached);
        else if(scanSession.Phase==DashboardScanPhase.None && string.IsNullOrWhiteSpace(scanRoot))
            language.Text(ScanStatus,"Select a drive and scan it, or choose a folder to analyze.","اختر قرصًا وافحصه، أو اختر مجلدًا للتحليل.");
    }
    private async Task LoadDashboardAsync()
    {
        int revision=++lifetime;
        if(!subscribed)
        {
            inventory.Changed+=InventoryChanged;freedSpace.Changed+=OverviewChanged;
            FileChangeHub.Changed+=DashboardFilesChanged;
            UninstallHistoryService.HistoryChanged+=HistoryChanged;
            scanSession.Changed+=ShowScanSession;
            subscribed=true;
        }
        ShowScanSession();
        UpdateOverview();
        await DashboardStorageService.LoadCacheAsync();
        await RefreshDrives();
        if(!IsLoaded||revision!=lifetime)return;
        if(scanSession.Phase==DashboardScanPhase.None && !refreshing) RestoreCachedScan(DriveSelector.SelectedItem as DriveSummary);
        await RefreshHistoryAsync();
        var saved=await manualService.LoadAsync();
        if(!IsLoaded||revision!=lifetime)return;
        foreach(var item in saved)if(File.Exists(item.Path)||Directory.Exists(item.Path))manual[item.Path]=item;
        RenderItems();
    }
    private async Task RunDashboardActionAsync(Func<Task> action)
    {
        try { await action(); }
        catch(OperationCanceledException) { }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException
            or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException or System.Text.Json.JsonException)
        {
            if(IsLoaded)ScanStatus.Text=L("Action could not finish; see Activity Log.","تعذر إكمال الإجراء؛ راجع سجل النشاط.");
            activity.Write("ERROR","Dashboard: "+ex.GetType().Name+" · "+ex.Message);
            CrashDiagnostics.Record("Dashboard action",ex);
        }
    }
    private void InitializeSizeFilter()
    {
        sizeFilterReady=false;
        var options=new[]
        {
            new SizeFilterOption(L("All file sizes","كل أحجام الملفات"),0),
            new SizeFilterOption(L("10 MB or larger","10 ميجابايت أو أكبر"),10L*1024*1024),
            new SizeFilterOption(L("50 MB or larger","50 ميجابايت أو أكبر"),50L*1024*1024),
            new SizeFilterOption(L("100 MB or larger","100 ميجابايت أو أكبر"),100L*1024*1024),
            new SizeFilterOption(L("500 MB or larger","500 ميجابايت أو أكبر"),500L*1024*1024),
            new SizeFilterOption(L("1 GB or larger","1 جيجابايت أو أكبر"),1024L*1024*1024),
            new SizeFilterOption(L("5 GB or larger","5 جيجابايت أو أكبر"),5L*1024*1024*1024)
        };
        FileSizeFilter.ItemsSource=options;
        FileSizeFilter.SelectedItem=options.FirstOrDefault(option=>option.Bytes==minimumFileBytes)??options[0];
        sizeFilterReady=true;
    }
    private void FileSizeFilterChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!sizeFilterReady)return;
        minimumFileBytes=(FileSizeFilter.SelectedItem as SizeFilterOption)?.Bytes??0;
        visibleLimit=DefaultVisible;
        RenderItems();
    }
    private void FoldersTabClick(object sender,RoutedEventArgs e)
    {
        category=StorageCategory.Folders;visibleLimit=DefaultVisible;RenderItems();
    }
    private void FilesTabClick(object sender,RoutedEventArgs e)
    {
        category=StorageCategory.Files;visibleLimit=DefaultVisible;RenderItems();
    }
    private void ShowMoreClick(object sender,RoutedEventArgs e)
    {
        visibleLimit+=PageSize;RenderItems();
    }
    private void UpdateCategoryChrome()
    {
        SetTabActive(FoldersTab,category==StorageCategory.Folders);
        SetTabActive(FilesTab,category==StorageCategory.Files);
        FileSizeFilter.Visibility=category==StorageCategory.Files?Visibility.Visible:Visibility.Collapsed;
    }
    private static void SetTabActive(Button button,bool active)
    {
        button.Opacity=active?1:0.62;
        button.FontWeight=active?FontWeights.SemiBold:FontWeights.Normal;
        if(active)button.SetResourceReference(Control.BorderBrushProperty,"ChromeAccent");
        else button.SetResourceReference(Control.BorderBrushProperty,"ChromeBorder");
    }
    private void OverviewChanged()
    {
        if(Dispatcher.HasShutdownStarted)return;
        // The callback may arrive from the inventory/persistence worker.
        if(!Dispatcher.CheckAccess()){Dispatcher.InvokeAsync(OverviewChanged);return;}
        if(overviewScheduled)return;
        overviewScheduled=true;
        Dispatcher.InvokeAsync(()=>{overviewScheduled=false;if(IsLoaded&&!refreshing)UpdateOverview();},System.Windows.Threading.DispatcherPriority.Background);
    }
    private void InventoryChanged(ApplicationInventory snapshot)=>OverviewChanged();
    private void OpenCleaner(object sender,RoutedEventArgs e)=>NavigationRequested?.Invoke("cleaner");
    private void OpenUninstaller(object sender,RoutedEventArgs e)=>NavigationRequested?.Invoke("uninstall");
    private void DashboardFilesChanged(FileChangeBatch batch) { refreshCancellation?.Cancel();if(Dispatcher.HasShutdownStarted)return; Dispatcher.InvokeAsync(()=>{if(IsLoaded)_=RunDashboardActionAsync(RefreshAfterExternalChange);}); }
    private void HistoryChanged() { if(Dispatcher.HasShutdownStarted)return; Dispatcher.InvokeAsync(()=>{if(IsLoaded)_=RunDashboardActionAsync(RefreshHistoryAsync);}); }
    private Task RefreshHistoryAsync() => historyRefresh is { IsCompleted:false } ? historyRefresh : historyRefresh=RefreshHistoryCoreAsync();
    private async Task RefreshHistoryCoreAsync()
    {
        IReadOnlyList<UninstallHistoryEntry> entries;
        try { entries=await history.LoadAsync(); }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { entries=Array.Empty<UninstallHistoryEntry>(); }
        if(!IsLoaded)return;
        ApplyHistory(entries);
    }
    private void ApplyHistory(IReadOnlyList<UninstallHistoryEntry> entries)
    {
        var rows=entries.Take(UninstallHistoryService.DisplayLimit).Select(entry =>
        {
            string? iconPath=history.ResolveIconPath(entry);
            ImageSource? icon=LoadHistoryIcon(iconPath);
            string source=UninstallHistoryFormat.Source(entry,arabic);
            string date=UninstallHistoryFormat.Date(entry,arabic);
            string subtitle=string.IsNullOrWhiteSpace(source)?date:source+" · "+date;
            return new RecentUninstallRow(entry.Name,subtitle,UninstallHistoryFormat.Size(entry,arabic),icon,icon is null?"▣":"");
        }).ToArray();
        RecentList.ItemsSource=rows;
        bool empty=rows.Length==0;
        RecentEmptyPanel.Visibility=empty?Visibility.Visible:Visibility.Collapsed;
        RecentList.Visibility=empty?Visibility.Collapsed:Visibility.Visible;
    }
    private static ImageSource? LoadHistoryIcon(string? path)
    {
        if(string.IsNullOrWhiteSpace(path))return null;
        try
        {
            var bitmap=new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption=BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth=48;
            bitmap.UriSource=new Uri(path,UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or FormatException or NotSupportedException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException) { return null; }
    }
    private async Task RefreshAfterExternalChange()
    {
        foreach (string path in manual.Keys.Where(p => !File.Exists(p) && !Directory.Exists(p)).ToArray()) manual.Remove(path);
        if(displayedSnapshot is {} previous&&DashboardStorageService.TryGetCached(previous.Root) is {} updated)ApplySnapshot(updated,true);
        await RefreshDrives();
        await SaveItems();RenderItems();
    }
    private void UpdateOverview()
    {
        var snapshot=inventory.Current;
        if(snapshot is {IsComplete:true}) SetNumericValue(ProgramsValue,DisplayFormat.Number(snapshot.Count));
        else ProgramsValue.Text=L("Loading…","جارٍ التحميل…");
        if(freedSpace.Available) SetNumericValue(FreedValue,DisplayFormat.Size(freedSpace.TotalBytes,false));
        else FreedValue.Text=L("Unavailable","غير متاح");
    }
    private Task RefreshDrives() => driveRefresh is { IsCompleted:false } ? driveRefresh : driveRefresh=RefreshDrivesCoreAsync();
    private async Task RefreshDrivesCoreAsync()
    {
        try
        {
            var drives=await storage.ReadDrivesAsync();var selected=(DriveSelector.SelectedItem as DriveSummary)?.Name;
            if(!IsLoaded)return;
            updatingDrives=true;
            try
            {
                string? previous=selected;DriveSelector.ItemsSource=drives;
                var drive=drives.FirstOrDefault(d=>string.Equals(d.Name,previous,StringComparison.OrdinalIgnoreCase))
                    ??drives.FirstOrDefault(d=>string.Equals(d.Name,Path.GetPathRoot(Environment.SystemDirectory),StringComparison.OrdinalIgnoreCase))
                    ??drives.FirstOrDefault();
                DriveSelector.SelectedItem=drive;ShowDrive(drive);RestoreCachedScan(drive);
            }
            finally { updatingDrives=false; }
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException) {activity.Write("WARNING","Dashboard drive information unavailable.");}
    }
    private void DriveChanged(object sender,SelectionChangedEventArgs e){if(updatingDrives)return;refreshCancellation?.Cancel();scan?.Cancel();scanSession.Reset();ShowDrive(DriveSelector.SelectedItem as DriveSummary);}
    private void ShowDrive(DriveSummary? drive)
    {
            bool warning=drive?.UsedPercent>=85;
            StorageWarning.Visibility=warning?Visibility.Visible:Visibility.Collapsed;
            UsageArc.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,warning?"LogWarning":"ChromeAccent");
            UsageBar.SetResourceReference(Control.ForegroundProperty,warning?"LogWarning":"ChromeAccent");
            UsagePercent.SetResourceReference(TextBlock.ForegroundProperty,warning?"LogWarning":"HomeTextBrush");
            FreeValue.SetResourceReference(TextBlock.ForegroundProperty,warning?"LogWarning":"ChromeAccent");
            if(drive==null) {FreeValue.Text=UsagePercent.Text="—";FreeDetail.Text=DriveSummaryText("Drive unavailable","القرص غير متاح");DriveTitle.Text="";DriveSummary.Text=L("Drive unavailable","القرص غير متاح");DriveDetails.Text="";UsageArc.Data=null;UsageBar.Value=0;UsedSpace.Text=FreeSpace.Text=TotalSpace.Text="—";return;}
            if(string.IsNullOrWhiteSpace(scanRoot))scanRoot=drive.Name;
            SetNumericValue(FreeValue,drive.Available);LocalizedPresentation.SetRuns(FreeDetail,L($"{drive.Letter} · {drive.UsedPercent:N0}% used",$"المستخدم: {drive.UsedPercent:N0}% · {drive.Letter}"));
            string title=string.IsNullOrWhiteSpace(drive.Label)?drive.Letter:drive.Label+" · "+drive.Letter;
            SetNumericValue(DriveTitle,title);
            LocalizedPresentation.SetRuns(DriveSummary,L($"{Services.DriveSummary.Format(drive.Used)} of {Services.DriveSummary.Format(drive.Total)} used",$"{Services.DriveSummary.Format(drive.Used)} مستخدم من {Services.DriveSummary.Format(drive.Total)}"));
            LocalizedPresentation.SetRuns(DriveDetails,L($"Available: {drive.Available}",$"المتاح: {drive.Available}"));
            UsageBar.Value=drive.UsedPercent;SetNumericValue(UsagePercent,DisplayFormat.Percent(drive.UsedPercent));
            SetNumericValue(UsedSpace,Services.DriveSummary.Format(drive.Used));
            SetNumericValue(FreeSpace,drive.Available);
            SetNumericValue(TotalSpace,Services.DriveSummary.Format(drive.Total));
            double angle=Math.Clamp(drive.UsedPercent,0,99.999)/100*2*Math.PI; var end=new Point(80+64*Math.Sin(angle),80-64*Math.Cos(angle));
            var figure=new PathFigure {StartPoint=new(80,16)};figure.Segments.Add(new ArcSegment(end,new(64,64),0,angle>Math.PI,SweepDirection.Clockwise,true));UsageArc.Data=new PathGeometry(new[]{figure});
    }
    private void SetNumericValue(SelectableText block,string value) => LocalizedPresentation.SetLeftToRight(block,value,arabic);

    private string DriveSummaryText(string en,string ar)=>L(en,ar);
    private async void RefreshClick(object sender,RoutedEventArgs e)=>await RefreshAllAsync();
    private async Task RefreshAllAsync()
    {
        if(refreshing||clearingCache)return;
        if(IsScanning)return;
        using var cts=new CancellationTokenSource();refreshCancellation=cts;
        string scope=scanRoot;
        activity.WriteBilingual("INFO","Dashboard refresh started.","بدأ تحديث لوحة المعلومات.");
        refreshing=true;RefreshButton.IsEnabled=ClearCacheButton.IsEnabled=false;RefreshButton.Content=L("Refreshing…","جارٍ التحديث…");
        try
        {
            var drivesTask=storage.ReadDrivesAsync();var inventoryTask=inventory.RefreshAsync(cts.Token);
            var historyTask=history.LoadAsync();var freedTask=freedSpace.ReloadAsync(cts.Token);
            var scanTask=string.IsNullOrWhiteSpace(scope)?Task.FromResult<StorageSnapshot?>(null):RefreshScopeAsync(scope,cts.Token);
            await Task.WhenAll(drivesTask,inventoryTask,historyTask,freedTask,scanTask);
            cts.Token.ThrowIfCancellationRequested();
            UpdateOverview();ApplyHistory(historyTask.Result);
            updatingDrives=true;
            try{string? selected=(DriveSelector.SelectedItem as DriveSummary)?.Name;DriveSelector.ItemsSource=drivesTask.Result;DriveSelector.SelectedItem=drivesTask.Result.FirstOrDefault(d=>d.Name==selected)??drivesTask.Result.FirstOrDefault();ShowDrive(DriveSelector.SelectedItem as DriveSummary);}
            finally{updatingDrives=false;}
            if(scanTask.Result is {} snapshot)ApplySnapshot(snapshot,false);
            activity.Write("INFO",L("Dashboard refreshed.","حُدّثت لوحة المعلومات."));
        }
        catch(OperationCanceledException){activity.WriteBilingual("INFO","Dashboard refresh cancelled.","أُلغي تحديث لوحة المعلومات.");}
        catch(Exception error){CrashDiagnostics.Record("Dashboard refresh",error);ScanStatus.Text=L("Could not refresh. Previous data is kept; check access and try again.","تعذر التحديث. بقيت البيانات السابقة؛ تحقق من الصلاحيات وحاول مجددًا.");activity.Write("ERROR",ScanStatus.Text);}
        finally{if(ReferenceEquals(refreshCancellation,cts))refreshCancellation=null;refreshing=false;RefreshButton.Content=L("Refresh","تحديث");RefreshButton.IsEnabled=ClearCacheButton.IsEnabled=true;}
    }
    private async Task<StorageSnapshot?> RefreshScopeAsync(string scope,CancellationToken token)=>await storage.ScanAsync(scope,token);
    private async void ClearCacheClick(object sender,RoutedEventArgs e)
    {
        if(clearingCache||refreshing)return;
        ClearCacheButton.IsEnabled=false;
        try
        {
            var prompt=L(
                "Clear cached dashboard data?\n\nThis permanently resets:\n• Application launch count\n• Total click count\n• Cached drive and folder scan results\n• Saved Largest Folders and Largest Files results\n• Files and folders manually added to the Dashboard scan list\n• Recent uninstalls and the persisted uninstall history\n\nOnly Polar Uninstaller's own cached data and history are removed. Your personal files and folders are never deleted.\n\nA new inventory baseline will be created so the cleared history cannot return.",
                "إزالة بيانات لوحة المعلومات المؤقتة؟\n\nسيتم نهائيًا إعادة تعيين:\n• عدد مرات فتح التطبيق\n• إجمالي النقرات\n• نتائج فحص الأقراص والمجلدات المحفوظة مؤقتًا\n• نتائج أكبر المجلدات وأكبر الملفات المحفوظة\n• الملفات والمجلدات المضافة يدويًا إلى قائمة فحص لوحة المعلومات\n• آخر عمليات الإزالة وسجل الإزالة المحفوظ\n\nتُزال فقط بيانات Polar Uninstaller المؤقتة وسجلاته. لا تُحذف ملفاتك أو مجلداتك الشخصية أبدًا.\n\nسيتم إنشاء خط أساس جديد للمخزون حتى لا يعود السجل المحذوف.");
            if(MessageBox.Show(Window.GetWindow(this),prompt,L("Clear Cache","إزالة الكاش"),MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
            clearingCache=true;
            ScanStatus.Text=L("Clearing cached dashboard data…","جارٍ إزالة بيانات لوحة المعلومات المؤقتة…");
            // Stop any active scan first so a late progress callback cannot
            // repopulate the results that are about to be cleared.
            scan?.Cancel();
            scanSession.Reset();
            Interlocked.Increment(ref cacheGeneration);
            DashboardStorageService.ClearCache();await DashboardStorageService.FlushCacheAsync();
            try { await pendingSave; }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
            await manualService.ClearAsync();
            manual.Clear();
            automaticFolders=[];automaticFiles=[];visibleLimit=DefaultVisible;
            scanRoot="";scanIsDriveRoot=false;scanIncomplete=false;
            await ActivityMetricsService.Instance.ResetAsync();
            await history.ClearAsync();
            InventorySnapshotService.Clear();
            InventorySnapshotService.ClearPending();
            await InventorySnapshotService.EstablishBaselineAsync();
            UpdateOverview();
            await RefreshHistoryAsync();
            ScanScopeText.Text=L("No scan yet","لم يتم الفحص بعد");
            RenderItems();
            var summary=L("Cached scans, saved scan items, statistics, and uninstall history were cleared. No personal files were deleted. A new inventory baseline was created.","تمت إزالة نتائج الفحص المؤقتة وعناصر الفحص المحفوظة والإحصاءات وسجل الإزالة. لم تُحذف أي ملفات شخصية. وتم إنشاء خط أساس جديد للمخزون.");
            ScanStatus.Text=summary;activity.Write("SUCCESS",summary);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException or OperationCanceledException)
        {ScanStatus.Text=L("Could not reset cached data. Check permissions and try again.","تعذرت إعادة تعيين البيانات المؤقتة. تحقق من الصلاحيات وحاول مجددًا.");CrashDiagnostics.Record("Dashboard reset",ex);activity.Write("WARNING",ScanStatus.Text);}
        finally {clearingCache=false;ClearCacheButton.IsEnabled=true;}
    }


    /// <summary>Recursively scans exactly the requested scope and labels the results.</summary>
    private async Task RunStorageScanAsync(string root,bool isDrive)
    {
        if(IsScanning||refreshing||clearingCache)return;
        await scanSession.RunAsync(storage,root,isDrive);
    }

    public void CancelWork()
    {
        scanSession.Cancel();
        scan?.Cancel();
        refreshCancellation?.Cancel();
    }

    private void CancelScanClick(object sender,RoutedEventArgs e) => scanSession.Cancel();

    private void ShowScanSession()
    {
        CancelScanButton.Visibility=scanSession.Phase==DashboardScanPhase.Running?Visibility.Visible:Visibility.Collapsed;
        if(scanSession.Phase==DashboardScanPhase.None)return;
        if(!string.Equals(scanRoot,scanSession.Root,StringComparison.OrdinalIgnoreCase) ||
            (scanSession.Phase==DashboardScanPhase.Running && displayedSnapshot!=null))
        {
            displayedSnapshot=null;
            renderedScanSnapshot=null;
            automaticFolders=[];automaticFiles=[];visibleLimit=DefaultVisible;
        }
        scanRoot=scanSession.Root;
        scanIsDriveRoot=scanSession.IsDrive;
        LocalizedPresentation.SetRuns(ScanScopeText,ScopeText());
        var snapshot=scanSession.Snapshot;
        switch(scanSession.Phase)
        {
            case DashboardScanPhase.Running:
                if(!ReferenceEquals(renderedScanSnapshot,snapshot))
                {
                    renderedScanSnapshot=snapshot;
                    scanIncomplete=true;
                    automaticFiles=snapshot?.Largest.Select(f=>new StorageRow(f.Name,f.Path,f.Bytes,0,"📄")).ToArray()??[];
                    RenderItems();
                }
                LocalizedPresentation.SetRuns(ScanStatus,L(
                    $"Scanning {ScopeText()} · {scanSession.Files:N0} files",
                    $"جارٍ الفحص: {ScopeText()} · الملفات: {scanSession.Files:N0}"));
                break;
            case DashboardScanPhase.Completed when snapshot!=null && !ReferenceEquals(renderedScanSnapshot,snapshot):
                renderedScanSnapshot=snapshot;
                ApplySnapshot(snapshot,false);
                break;
            case DashboardScanPhase.Cancelled:
                scanIncomplete=true;
                language.Text(ScanStatus,"Scan cancelled; partial results are shown.","أُلغي الفحص؛ تُعرض النتائج الجزئية.");
                break;
            case DashboardScanPhase.Failed:
                language.Text(ScanStatus,"Unable to scan this location.","تعذر فحص هذا الموقع.");
                break;
        }
    }

    private string ScopeText()
    {
        if(string.IsNullOrWhiteSpace(scanRoot))return L("no scope","بدون نطاق");
        return scanIsDriveRoot?L($"entire drive {scanRoot}",$"القرص بأكمله {DisplayFormat.Isolate(scanRoot)}"):L($"folder {scanRoot}",$"المجلد {DisplayFormat.Isolate(scanRoot)}");
    }

    private void RestoreCachedScan(DriveSummary? drive)
    {
        if(automaticFolders.Length>0||automaticFiles.Length>0)return;
        var cached=(drive is null?null:DashboardStorageService.TryGetCached(drive.Name))??DashboardStorageService.LastSnapshot;
        if(cached is null){ScanScopeText.Text=L("No scan yet","لم يتم الفحص بعد");return;}
        ApplySnapshot(cached,true);
    }

    private void ApplySnapshot(StorageSnapshot result,bool cached)
    {
        snapshotCached=cached;displayedSnapshot=result;hiddenDashboardPaths=result.HiddenPaths.ToArray();
        scanRoot=result.Root;scanIsDriveRoot=result.IsDriveRoot;scanIncomplete=result.Limited;
        // Folders and files are independent rankings. A large file can never
        // consume a folder slot, and folder results are never truncated.
        automaticFolders=result.LargestFolders
            .Select(f=>new StorageRow(f.Name,f.Path,f.Bytes,0,"📁",true){Partial=result.Limited})
            .ToArray();
        automaticFiles=result.Largest
            .Select(f=>new StorageRow(f.Name,f.Path,f.Bytes,0,"📄"))
            .ToArray();
        visibleLimit=DefaultVisible;
        RenderItems();
        ScanScopeText.Text=ScopeText();
        ShowSnapshotStatus(result,cached);
    }
    private void ShowSnapshotStatus(StorageSnapshot result,bool cached)
    {
        var status=L($"{ScopeText()} · {result.FilesScanned:N0} files · {Services.DriveSummary.Format(result.TotalBytes)} · {result.FoldersScanned:N0} folders",$"{ScopeText()} · {result.FilesScanned:N0} ملف · {Services.DriveSummary.Format(result.TotalBytes)} · {result.FoldersScanned:N0} مجلد");
        if(!cached)status=L($"Updated · {result.CompletedAt.ToLocalTime():G} · ",$"حُدّثت · {result.CompletedAt.ToLocalTime():G} · ")+status;
        if(cached)status=L($"Cached · {result.CompletedAt.ToLocalTime():g} · ",$"محفوظ مؤقتًا · {result.CompletedAt.ToLocalTime():g} · ")+status;
        if(result.IsDriveRoot && DriveSelector.SelectedItem is DriveSummary drive && result.TotalBytes>0 && result.TotalBytes<drive.Used)
            status+=L($" · scanned {Services.DriveSummary.Format(result.TotalBytes)} of {Services.DriveSummary.Format(drive.Used)} used; inaccessible or system-only areas are not included",$" · تم فحص {Services.DriveSummary.Format(result.TotalBytes)} من أصل {Services.DriveSummary.Format(drive.Used)} مستخدم؛ المناطق المتعذر الوصول إليها أو النظامية فقط غير مشمولة");
        if(result.Limited)status+=L($" · incomplete: {result.Inaccessible:N0} inaccessible · {result.Skipped:N0} skipped in total; folder sizes are at least the shown values",$" · غير مكتمل: {result.Inaccessible:N0} متعذر الوصول · {result.Skipped:N0} متجاوز إجمالًا؛ أحجام المجلدات لا تقل عن القيم المعروضة");
        LocalizedPresentation.SetRuns(ScanStatus,status);
    }
    private void LargestClick(object sender,MouseButtonEventArgs e)
    {
        if(IsWithinButton(e.OriginalSource as DependencyObject))return;
        var row=FindRow(e.OriginalSource as DependencyObject);
        if(row==null||openInBrowser==null)return;
        try
        {
            if(row.IsDirectory)openInBrowser(row.Path,row.Path);
            else openInBrowser(Path.GetDirectoryName(row.Path)??scanRoot,row.Path);
            e.Handled=true;
        }
        catch(Exception ex) when(ex is ArgumentException or IOException) {activity.Write("WARNING","Could not open storage item in File Browser: "+ex.Message);}
    }
    private static bool IsWithinButton(DependencyObject? source)
    {
        while(source!=null){if(source is Button)return true;source=UiTree.Parent(source);}
        return false;
    }
    private static StorageRow? FindRow(DependencyObject? source)
    {
        while(source!=null){if(source is FrameworkElement {DataContext:StorageRow row})return row;source=UiTree.Parent(source);}
        return null;
    }
    private void RenderItems()
    {
        string scanSource=scanIncomplete
            ? L($"Partial · {ScopeText()}",$"جزئي · {ScopeText()}")
            : ScopeText();
        string ManualSource(ManualStorageItem item)=>item.Updated.HasValue
            ? L($"Saved · {item.Updated:g}",$"محفوظ · {item.Updated:g}")
            : L("Partial size","حجم جزئي");

        var manualFolders=manual.Values.Where(i=>Directory.Exists(i.Path))
            .Select(i=>new StorageRow(Path.GetFileName(i.Path.TrimEnd('\\')),i.Path,i.Bytes,0,"📁",true)
            {Manual=true,Partial=i.Updated is null,Source=ManualSource(i)});
        var manualFiles=manual.Values.Where(i=>File.Exists(i.Path))
            .Select(i=>new StorageRow(Path.GetFileName(i.Path),i.Path,i.Bytes,0,"📄")
            {Manual=true,Source=ManualSource(i)});

        // Automatic results win when the same path was also added manually.
        var folders=automaticFolders.Concat(manualFolders).Where(r=>!hiddenDashboardPaths.Any(p=>StorageCacheUpdates.Within(r.Path,p)))
            .GroupBy(r=>r.Path,StringComparer.OrdinalIgnoreCase).Select(g=>g.First())
            .Select(r=>r.Manual?r:(r with{Source=scanSource}))
            .OrderByDescending(r=>r.Bytes).ThenBy(r=>r.Name,StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        var files=automaticFiles.Concat(manualFiles).Where(r=>!hiddenDashboardPaths.Any(p=>StorageCacheUpdates.Within(r.Path,p)))
            .GroupBy(r=>r.Path,StringComparer.OrdinalIgnoreCase).Select(g=>g.First())
            .Where(r=>r.Bytes>=minimumFileBytes)
            .Select(r=>r.Manual?r:(r with{Source=scanSource}))
            .OrderByDescending(r=>r.Bytes).ThenBy(r=>r.Name,StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        UpdateCategoryChrome();
        var active=category==StorageCategory.Folders?folders:files;
        long max=Math.Max(1,active.Select(r=>r.Bytes).DefaultIfEmpty(1).Max());
        var visible=active.Take(visibleLimit).Select(r=>r with{Percent=100.0*r.Bytes/max}).ToArray();
        Largest.ItemsSource=visible;
        int remaining=active.Length-visible.Length;
        ShowMoreButton.Visibility=remaining>0?Visibility.Visible:Visibility.Collapsed;
        if(remaining>0)ShowMoreButton.Content=L($"Show more ({remaining:N0} remaining)",$"عرض المزيد ({remaining:N0} متبقٍ)");
    }
    private async void AddFileClick(object sender,RoutedEventArgs e)=>await RunDashboardActionAsync(async()=>{if(IsScanning)return;var picker=new Microsoft.Win32.OpenFileDialog{Multiselect=true};if(picker.ShowDialog(Window.GetWindow(this))==true)await Analyze(picker.FileNames);});
    private async void AddFolderClick(object sender,RoutedEventArgs e)=>await RunDashboardActionAsync(async()=>{if(IsScanning)return;var picker=new Microsoft.Win32.OpenFolderDialog();if(picker.ShowDialog(Window.GetWindow(this))==true)await RunStorageScanAsync(picker.FolderName,false);});
    private async void StorageDrop(object sender,DragEventArgs e)=>await RunDashboardActionAsync(async()=>{if(e.Data.GetData(DataFormats.FileDrop) is string[] paths)await Analyze(paths);});
    private async void RefreshItem(object sender,RoutedEventArgs e)=>await RunDashboardActionAsync(async()=>{if(sender is Button{DataContext:StorageRow row})await Analyze(new[]{row.Path});});
    private async void RemoveItem(object sender,RoutedEventArgs e)
    {
        if(IsScanning||refreshing||sender is not Button{DataContext:StorageRow row})return;
        await FileChangeHub.PublishAsync(new[]{new FileChange(row.Path,true,row.Bytes,row.IsDirectory)},FileChangeKind.HiddenFromDashboard);
        hiddenDashboardPaths=hiddenDashboardPaths.Append(row.Path).ToArray();
        manual.Remove(row.Path);
        automaticFolders=automaticFolders.Where(r=>r.Path!=row.Path).ToArray();
        automaticFiles=automaticFiles.Where(r=>r.Path!=row.Path).ToArray();
        RenderItems();await SaveItems();
    }
    private async Task SaveItems()
    {
        if(clearingCache)return;
        int generation=cacheGeneration;
        try
        {
            var save=manualService.SaveAsync(manual.Values.ToArray());
            pendingSave=save;
            await save;
            // A Clear Cache that completed while this save was running must win.
            if(generation!=cacheGeneration)await manualService.ClearAsync();
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){ScanStatus.Text=L("Could not save selected items.","تعذر حفظ العناصر المحددة.");}
    }
    private async Task Analyze(IEnumerable<string> paths)
    {
        if(refreshing)return;
        if(IsScanning){ScanStatus.Text=L("A scan is already running.","يوجد فحص قيد التنفيذ.");return;}
        using var source=new CancellationTokenSource();scan=source;int generation=cacheGeneration;
        try
        {
            foreach(var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                source.Token.ThrowIfCancellationRequested();
                manual[path]=new(path,manual.TryGetValue(path,out var previous)?previous.Bytes:0,0,0,null);RenderItems();
                var progress=new Progress<ManualStorageItem>(item=>{if(generation!=cacheGeneration||!ReferenceEquals(scan,source)||manual[path].Updated.HasValue)return;manual[path]=item;RenderItems();ScanStatus.Text=L($"{item.Files:N0} files · {Services.DriveSummary.Format(item.Bytes)}",$"{item.Files:N0} ملف · {Services.DriveSummary.Format(item.Bytes)}");});
                manual[path]=await manualService.ScanAsync(path,progress,source.Token);
                if(generation!=cacheGeneration)return;
                RenderItems();await SaveItems();
            }
            var skipped=manual.Values.Sum(i=>i.Skipped);
            ScanStatus.Text=L("Analysis complete.","اكتمل التحليل.")+(skipped>0?L($"{skipped:N0} inaccessible or linked items were skipped.",$"تُجاوز {skipped:N0} عنصرًا متعذر الوصول إليه أو مرتبطًا."):"");
        }
        catch(OperationCanceledException){if(generation==cacheGeneration){ScanStatus.Text=L("Scan cancelled; partial sizes retained.","أُلغي الفحص؛ احتُفظ بالأحجام الجزئية.");await SaveItems();}}
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException){ScanStatus.Text=L("Unable to analyze this item.","تعذر تحليل هذا العنصر.");}
        finally{if(ReferenceEquals(scan,source)){scan=null;}}
    }
}
