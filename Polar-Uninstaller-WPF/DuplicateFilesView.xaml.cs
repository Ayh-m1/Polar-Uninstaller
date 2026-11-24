using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PolarUninstallerWpf.Services;

namespace PolarUninstallerWpf;

public partial class DuplicateFilesView : UserControl
{
    private bool arabic;
    private readonly ActivityLogService activity;
    private readonly ApplicationInventoryService inventory;
    private CancellationTokenSource? operation;
    private DuplicateScanResult? result;
    private List<DuplicateRow> rows=new();
    private bool changingSelection;
    private readonly SettingsService settings;
    private string state="idle";
    private DuplicateProgress? latestProgress;
    private DuplicateCleanupResult? lastCleanup;
    public event EventHandler? BackRequested;
    public event EventHandler? FilesChanged;
    public bool IsBusy=>operation!=null;
    private string L(string en,string ar)=> BidiText.Localize(arabic,en,ar);

    public DuplicateFilesView(bool arabic,string directory,ActivityLogService activity,ApplicationInventoryService inventory,SettingsService? settings=null)
    {
        this.activity=activity;this.inventory=inventory;this.settings=settings??new SettingsService();InitializeComponent();UiPerformanceProbe.Attach(this);
        InitializeGroupedResults();
        InitializeDuplicateShortcuts();
        PathInput.Text=Directory.Exists(directory)?Path.GetFullPath(directory):"";
        SetLanguage(arabic);
        Unloaded+=(_,_)=>{CancelOperation();partialTimer.Stop();resultFilterTimer.Stop();toastTimer.Stop();};
    }
    public void SetLanguage(bool value)
    {
        arabic=value;FlowDirection=arabic?FlowDirection.RightToLeft:FlowDirection.LeftToRight;
        Heading.Text=L("Find Duplicate Files","البحث عن الملفات المكررة");
        Subtitle.Text=L("Compare file contents in the selected folder and its subfolders. Nothing is deleted automatically.","مقارنة محتوى الملفات في المجلد المحدد ومجلداته الفرعية. لا تُحذف الملفات تلقائيًا.");
        BackButton.Content=L("← Back to File Browser","العودة إلى متصفح الملفات →");
        BrowseButton.Content=L("Browse Folder","اختيار مجلد");ScanButton.Content=L("Start Scan","بدء الفحص");
        CancelButton.Content=state=="cleaning"?L("Cancel Cleanup","إلغاء التنظيف"):L("Cancel Scan","إلغاء الفحص");
        GroupsLabel.Text=L("Duplicate groups","المجموعات المكررة");CopiesLabel.Text=L("Extra copies","النسخ الزائدة");
        SpaceLabel.Text=L("Potential recovery","المساحة المحتملة");ScannedLabel.Text=L("Files examined","الملفات المفحوصة");
        RecycleButton.Content=T("Recycle");PermanentButton.Content=T("Permanent");ClearSelectionButton.Content=T("DupClear");
        SmartSelectButton.Content=T("DupSmart");MinimumLabel.Text=T("DupMinimum");ResultsSearch.ToolTip=T("DupSearch");
        LocalizeGroupedResults();
        LocalizeDuplicateShortcuts();
        foreach(var row in rows)row.SetLanguage(arabic);
        if(lastCleanup!=null)CleanupDetails.Text=string.Join(Environment.NewLine,lastCleanup.Items.Select(i=>Reason(i)+" · "+i.Path));
        UpdateSummary();UpdateStatus();UpdateButtons();
    }
    public void CancelOperation()=>operation?.Cancel();
    private void Cancel(object sender,RoutedEventArgs e)=>CancelOperation();
    private void BackClick(object sender,RoutedEventArgs e){CancelOperation();BackRequested?.Invoke(this,EventArgs.Empty);}
    private void Browse(object sender,RoutedEventArgs e)
    {
        if(IsBusy)return;
        var picker=new OpenFolderDialog{Title=L("Choose a folder to scan","اختر مجلدًا للفحص")};
        var owner=Window.GetWindow(this);
        if((owner==null?picker.ShowDialog():picker.ShowDialog(owner))==true)
        {
            PathInput.Text=picker.FolderName;result=null;lastCleanup=null;latestProgress=null;rows.Clear();cards.Clear();pendingGroups.Clear();
            state="idle";Progress.Value=0;CleanupDetails.Visibility=Visibility.Collapsed;UpdateSummary();UpdateStatus();UpdateButtons();
        }
    }
    private async void Scan(object sender,RoutedEventArgs e)=>await StartScanAsync();
    internal async Task StartScanAsync()
    {
        if(IsBusy)return;
        if(!Path.IsPathFullyQualified(PathInput.Text)){state="choose_folder";UpdateStatus();return;}
        using var cts=new CancellationTokenSource();operation=cts;state="scanning";latestProgress=null;lastCleanup=null;
        result=null;rows.Clear();cards.Clear();pendingGroups.Clear();CleanupDetails.Visibility=Visibility.Collapsed;
        partialTimer.Start();
        Progress.IsIndeterminate=true;UpdateSummary();UpdateButtons();UpdateStatus();
        activity.Write("INFO",L("Duplicate scan started: ","بدأ فحص الملفات المكررة: ")+PathInput.Text);
        try
        {
            var progress=new Progress<DuplicateProgress>(p=>
            {
                if(!ReferenceEquals(operation,cts)||cts.IsCancellationRequested)return;
                latestProgress=p;Progress.IsIndeterminate=p.Phase=="enumerating";
                if(p.Total>0)Progress.Value=100d*p.Compared/p.Total;
                ScannedValue.Text=DisplayFormat.Number(p.Count);UpdateStatus();
            });
            string scanRoot=PathInput.Text;
            var partial=new Progress<DuplicateGroup>(group=>{if(ReferenceEquals(operation,cts)&&!cts.IsCancellationRequested)pendingGroups.Add(group);});
            var completed=await new DuplicateScanService().ScanAsync(scanRoot,progress,cts.Token,partial);
            cts.Token.ThrowIfCancellationRequested();
            result=completed;
            state="complete";Progress.IsIndeterminate=false;Progress.Value=100;RenderResults();
            activity.Write("INFO",L("Duplicate scan completed: ","اكتمل فحص المكررات: ")+result.Groups.Count+L(" groups; skipped: "," مجموعة؛ المتجاوز: ")+result.Skipped);
        }
        catch(OperationCanceledException){state="cancelled";activity.Write("INFO",L("Duplicate scan cancelled.","أُلغي فحص المكررات."));}
        catch(Exception ex)when(ItemError(ex)){state="error";activity.Write("WARNING",L("Duplicate scan could not read the selected folder: ","تعذر فحص المجلد المحدد: ")+ex.GetType().Name);}
        finally{FlushPartialResults();partialTimer.Stop();if(result==null&&cards.Count>0)result=new(cards.Select(card=>card.Source).ToArray(),latestProgress?.Count??0,latestProgress?.Skipped??0){Root=PathInput.Text};operation=null;Progress.IsIndeterminate=false;UpdateButtons();UpdateSummary();UpdateStatus();}
    }
    private void RenderResults()
    {
        var selection=rows.Where(r=>r.Selected).Select(r=>r.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var kept=cards.ToDictionary(card=>card.Source.Hash,card=>card.Copies.Single(row=>row.IsRetained).Path);
        var expanded=cards.Where(card=>card.Expanded).Select(card=>card.Source.Hash).ToHashSet();
        rows=CreateRows(result?.Groups??Array.Empty<DuplicateGroup>(),arabic,selection);
        pendingGroups.Clear();var replacement=new List<DuplicateGroupCard>();
        var paths=rows.ToDictionary(row=>row.Path,StringComparer.OrdinalIgnoreCase);
        foreach(var group in result?.Groups??Array.Empty<DuplicateGroup>())
        {
            var card=new DuplicateGroupCard(group,group.Files.Select(file=>paths[file.Path]).ToArray(),arabic);
            if(kept.TryGetValue(group.Hash,out var path)&&card.Copies.FirstOrDefault(row=>row.Path==path) is {} keeper)card.Keep(keeper);
            card.Expanded=expanded.Contains(group.Hash);card.SelectionUpdated+=()=>{if(!changingSelection)UpdateButtons();};replacement.Add(card);
        }
        cards.ReplaceWith(replacement);
        RefreshGroups();UpdateSummary();UpdateButtons();
    }
    // Presentation only: groups and SHA-256 identity remain in the scan result.
    // Stable group ordering keeps copies adjacent, regardless of filesystem enumeration order.
    internal static List<DuplicateRow> CreateRows(IReadOnlyList<DuplicateGroup> groups,bool arabic,ISet<string>? selection=null)
    {
        var output=new List<DuplicateRow>();int groupNumber=0;
        foreach(var group in groups.OrderByDescending(g=>g.Reclaimable).ThenBy(g=>g.Hash,StringComparer.Ordinal))
        {
            groupNumber++;int index=0;
            foreach(var file in group.Files.OrderBy(f=>f.Path,StringComparer.OrdinalIgnoreCase).ThenBy(f=>f.Path,StringComparer.Ordinal))
            {
                var row=new DuplicateRow(groupNumber,file,arabic,index==0,index==0&&output.Count>0);
                row.Selected=selection?.Contains(file.Path)==true;output.Add(row);index++;
            }
        }
        return output;
    }
    private void SelectCopies(object sender,RoutedEventArgs e)
    {
        if(IsBusy)return;
        changingSelection=true;
        try{foreach(var row in rows)row.Selected=!row.IsRetained;}
        finally{changingSelection=false;}
        UpdateButtons();
    }
    private async void Recycle(object sender,RoutedEventArgs e)=>await CleanupAsync(false);
    private async void Permanent(object sender,RoutedEventArgs e)=>await CleanupAsync(true);
    private async Task CleanupAsync(bool permanent)
    {
        if(IsBusy||result==null)return;
        var selected=rows.Where(r=>r.Selected).Select(r=>r.Path).ToArray();if(selected.Length==0)return;
        if(permanent)
        {
            var review=new PermanentDeleteDialog(arabic,selected.Length,rows.Where(row=>row.Selected).Sum(row=>row.Bytes),false,selected,(Style)Resources["ExplorerDangerButton"],(Style)Resources["ExplorerActionButton"]){Owner=Window.GetWindow(this)};
            if(review.ShowDialog()!=true)return;
        }
        using var cts=new CancellationTokenSource();operation=cts;state="cleaning";lastCleanup=null;CleanupDetails.Visibility=Visibility.Collapsed;Progress.IsIndeterminate=true;UpdateButtons();UpdateStatus();
        bool changed=false;
        try
        {
            var installed=await inventory.RefreshAsync(cts.Token);
            if(!installed.IsComplete)throw new IOException("A complete installation inventory is required for safe cleanup.");
            // The shared inventory intentionally hides Polar itself. Read the
            // raw registrations too so other Polar installations stay protected.
            var registrations=await new WindowsInventoryService().GetInstalledAsync(cts.Token);
            var progress=new Progress<DuplicateCleanupItem>(item=>
            {
                if(item.Removed){changed=true;FilesChanged?.Invoke(this,EventArgs.Empty);}
                activity.Write(item.Removed?"SUCCESS":"WARNING",Reason(item)+" · "+item.Path);
            });
            lastCleanup=await new DuplicateCleanupService().CleanAsync(result,selected,permanent,
                installed.Programs.Concat(registrations).Select(p=>p.Location).Where(p=>!string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),progress,cts.Token);
            changed|=lastCleanup.Items.Any(i=>i.Removed);
            result=result with{Groups=lastCleanup.Groups};state="cleaned";RenderResults();
            CleanupDetails.Text=string.Join(Environment.NewLine,lastCleanup.Items.Select(i=>Reason(i)+" · "+i.Path));
            CleanupDetails.Visibility=lastCleanup.Items.Count>0?Visibility.Visible:Visibility.Collapsed;
            ShowCleanupToast();
        }
        catch(OperationCanceledException){state="cleanup_cancelled";activity.Write("INFO",T("DupCleanupCancelled"));}
        catch(Exception ex)when(ItemError(ex)){state="cleanup_error";activity.Write("WARNING",L("Duplicate cleanup stopped: ","توقف تنظيف المكررات: ")+ex.Message);}
        finally
        {
            operation=null;Progress.IsIndeterminate=false;UpdateButtons();UpdateStatus();
            if(changed)FilesChanged?.Invoke(this,EventArgs.Empty);
        }
    }
    private string Reason(DuplicateCleanupItem item)=>item.Code switch
    {
        "removed"=>L("Removed","حُذف"),"last_copy"=>L("Retained: last verified copy","احتُفظ بآخر نسخة متحقق منها"),
        "missing"=>L("Skipped: already missing","تُجاوز: محذوف مسبقًا"),"protected"=>L("Skipped: protected location or application file","تُجاوز: موقع محمي أو ملف برنامج"),
        "no_verified_keeper"=>L("Skipped: no other verified copy","تُجاوز: لا توجد نسخة أخرى متحقق منها"),
        "not_scanned"=>L("Skipped: outside the scan results","تُجاوز: خارج نتائج الفحص"),
        "delete_failed"=>L("Not removed: locked file, permission or Recycle Bin error","لم يُحذف: ملف مقفل أو مشكلة صلاحيات أو سلة المحذوفات"),
        _=>L("Skipped: file changed, locked or inaccessible","تُجاوز: ملف تغيّر أو مقفل أو يتعذر الوصول إليه")
    };
    private void SelectionChanged(object sender,SelectionChangedEventArgs e)=>UpdateButtons();
    private void UpdateButtons()
    {
        if(ScanButton==null)return;
        ScanButton.IsEnabled=!IsBusy&&!string.IsNullOrWhiteSpace(PathInput.Text);BrowseButton.IsEnabled=PathInput.IsEnabled=!IsBusy;
        ScanButton.Visibility=IsBusy?Visibility.Collapsed:Visibility.Visible;CancelButton.Visibility=IsBusy?Visibility.Visible:Visibility.Collapsed;
        CancelButton.IsEnabled=IsBusy;CancelButton.Content=state=="cleaning"?L("Cancel Cleanup","إلغاء التنظيف"):L("Cancel Scan","إلغاء الفحص");
        SmartSelectButton.IsEnabled=!IsBusy&&rows.Count>0;ClearSelectionButton.IsEnabled=!IsBusy&&rows.Any(row=>row.Selected);
        RecycleButton.IsEnabled=PermanentButton.IsEnabled=!IsBusy&&rows.Any(r=>r.Selected);
        Results.IsEnabled=state!="cleaning";UpdateSelectionSummary();
        duplicateTip?.SelectionChanged(rows.Any(row=>row.Selected));
    }
    private void UpdateSummary()
    {
        GroupsValue.Text=DisplayFormat.Number(cards.Count);
        CopiesValue.Text=DisplayFormat.Number(cards.Sum(group=>group.CopyCount-1));
        SpaceValue.Text=DisplayFormat.Size(cards.Sum(group=>group.RecoverableBytes),false);
        ScannedValue.Text=DisplayFormat.Number(result?.Scanned??latestProgress?.Count??0);
        EmptyState.Visibility=Results.Items.Count==0?Visibility.Visible:Visibility.Collapsed;
        EmptyHint.Text=T(cards.Count>0?"DupNoMatches":state=="scanning"?"DupScanningEmpty":result==null?"DupIdle":"DupEmpty");
    }
    private void UpdateStatus()
    {
        Status.Text=state switch
        {
            "scanning"=>T("DupProgress",latestProgress?.Count??0,T(latestProgress?.Phase=="hashing"?"DupHashing":"DupEnumerating"),latestProgress?.Compared??0,latestProgress?.Total??0,latestProgress?.Skipped??0),
            "cleaning"=>L("Revalidating selected copies and protecting one verified original per group…","جارٍ التحقق من النسخ المحددة وحماية نسخة سليمة من كل مجموعة…"),
            "complete"=>L("Scan complete · Skipped: ","اكتمل الفحص · المتجاوز: ")+(result?.Skipped??0),
            "cancelled"=>L("Scan cancelled. No files were deleted.","أُلغي الفحص. لم تُحذف أي ملفات."),
            "cleanup_cancelled"=>L("Cleanup cancelled. Completed deletions cannot be undone; scan again to refresh results.","أُلغي التنظيف. لا يمكن التراجع عن الحذف المنفذ؛ أعد الفحص لتحديث النتائج."),
            "cleaned"=>L("Cleanup complete · Removed: ","اكتمل التنظيف · المحذوف: ")+(lastCleanup?.Items.Count(i=>i.Removed)??0)+L(" · Skipped: "," · المتجاوز: ")+(lastCleanup?.Items.Count(i=>!i.Removed)??0),
            "cleanup_error"=>L("Cleanup stopped. Check permissions and installation-inventory access; details are in Activity Log.","توقف التنظيف. تحقق من الصلاحيات وإمكانية قراءة قائمة التثبيت؛ التفاصيل في سجل النشاط."),
            "error"=>L("Cannot scan this folder. It may be missing, inaccessible, or reached through a link.","تعذر فحص المجلد. قد يكون مفقودًا أو محميًا أو موجودًا عبر وصلة."),
            "choose_folder"=>L("Choose a valid physical folder before scanning.","اختر مجلدًا فعليًا صالحًا قبل بدء الفحص."),
            _=>PathInput.Text.Length==0?L("Select a physical folder before scanning.","اختر مجلدًا فعليًا قبل الفحص."):L("Ready. Scanning starts only when you press Start Scan.","جاهز. يبدأ الفحص فقط عند الضغط على بدء الفحص.")
        };
        if(state=="cleaned"){Status.Inlines.Add(" · ");Status.Inlines.Add(new System.Windows.Documents.Run(DisplayFormat.Size(lastCleanup?.Reclaimed??0,false)){FlowDirection=FlowDirection.LeftToRight});}
    }
    private static bool ItemError(Exception ex)=>ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException;
}

public sealed class DuplicateRow(int group,DuplicateFile file,bool arabic,bool retained=false,bool setStart=false) : INotifyPropertyChanged
{
    private bool selected;
    public int Group=>group;
    public string Name=>System.IO.Path.GetFileName(file.Path);
    public string Path=>file.Path;
    public string ParentPath=>System.IO.Path.GetDirectoryName(file.Path)??file.Path;
    public long Bytes=>file.Size;
    public DateTime ModifiedUtc=>file.Modified;
    public DuplicateGroupCard? Owner {get;internal set;}
    public string Size=>DisplayFormat.Size(file.Size,false);
    public string Modified=>DisplayFormat.Date(file.Modified.ToLocalTime());
    public bool IsRetained=>retained;
    public bool CanSelect=>!retained;
    public Thickness RowSpacing=>new(0,setStart?12:3,0,3);
    public string CopyRole=>BrowserText.Get(retained?"DupKeepBadge":"DupDuplicateBadge",arabic);
    public string SelectionHint=>BrowserText.Get(retained?"DupKeepHint":"DupDuplicateBadge",arabic);
    public string KeepLabel=>BrowserText.Get("DupKeepThis",arabic);
    public string LocationLabel=>BrowserText.Get("OpenLocation",arabic);
    private static readonly System.Windows.Media.Brush KeepBrush=CreateKeepBrush();
    private static System.Windows.Media.Brush CreateKeepBrush(){var brush=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(151,232,183));brush.Freeze();return brush;}
    public System.Windows.Media.Brush BadgeBrush=>retained?KeepBrush:(System.Windows.Media.Brush)Application.Current.Resources["HomeTextBrush"];
    public void SetRetained(bool value){if(retained==value)return;retained=value;if(retained)Selected=false;foreach(string key in new[]{nameof(IsRetained),nameof(CanSelect),nameof(CopyRole),nameof(SelectionHint),nameof(BadgeBrush)})Changed(key);}
    public bool Selected{get=>selected;set{value=value&&!retained;if(selected==value)return;selected=value;Changed();}}
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new(name));
    public void SetLanguage(bool value){arabic=value;foreach(string key in new[]{nameof(Size),nameof(Modified),nameof(CopyRole),nameof(SelectionHint),nameof(KeepLabel),nameof(LocationLabel),nameof(BadgeBrush)})Changed(key);}
}
