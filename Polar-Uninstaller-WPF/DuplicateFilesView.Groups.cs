using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using PolarUninstallerWpf.Services;

namespace PolarUninstallerWpf;

public partial class DuplicateFilesView
{
    private readonly DuplicateCardCollection cards=new();
    private readonly List<DuplicateGroup> pendingGroups=new();
    private ListCollectionView? groupView;
    private string filterCategory="All",groupSort=nameof(DuplicateGroupCard.RecoverableBytes);
    private bool groupAscending;
    private readonly DispatcherTimer partialTimer=new(){Interval=TimeSpan.FromMilliseconds(200)};
    private readonly DispatcherTimer resultFilterTimer=new(){Interval=TimeSpan.FromMilliseconds(150)};
    private readonly DispatcherTimer toastTimer=new(){Interval=TimeSpan.FromSeconds(5)};
    private string T(string key,params object[] values)=>BrowserText.Get(key,arabic,values);
    private void InitializeGroupedResults()
    {
        var saved=settings.Load();groupSort=saved.DuplicateSort is nameof(DuplicateGroupCard.Name) or nameof(DuplicateGroupCard.FileBytes) or nameof(DuplicateGroupCard.CopyCount) or nameof(DuplicateGroupCard.RecoverableBytes)?saved.DuplicateSort:nameof(DuplicateGroupCard.RecoverableBytes);groupAscending=saved.DuplicateSortAscending;
        groupView=new ListCollectionView(cards);Results.ItemsSource=groupView;
        partialTimer.Tick+=(_,_)=>FlushPartialResults();
        resultFilterTimer.Tick+=(_,_)=>{resultFilterTimer.Stop();RefreshGroups();};
        toastTimer.Tick+=(_,_)=>{toastTimer.Stop();CleanupToast.Visibility=Visibility.Collapsed;};
        ResultsSort.SortRequested+=(key,ascending)=>
        {
            groupSort=key;groupAscending=ascending;RefreshGroups();
            try{settings.Save(settings.Load() with{DuplicateSort=key,DuplicateSortAscending=ascending});}catch(Exception error)when(ItemError(error)){Debug.WriteLine(error);}
            Debug.WriteLine($"Duplicate results sort: {key} {ascending}");
        };
    }
    private void LocalizeGroupedResults()
    {
        ResultsSearch.Tag=T("DupSearch");
        Filters.Children.Clear();
        foreach(string category in new[]{"All","Images","Videos","Documents","Archives","Executables","Other"})
        {
            var chip=new RadioButton{Content=T("Dup"+category),GroupName="DuplicateCategory",IsChecked=filterCategory==category,Style=(Style)Resources["FilterChip"]};
            chip.Checked+=(_,_)=>{filterCategory=category;RefreshGroups();};Filters.Children.Add(chip);
        }
        ResultsSort.Configure(arabic,new[]{new SortChoice(nameof(DuplicateGroupCard.RecoverableBytes),T("DupRecovery"),false),new SortChoice(nameof(DuplicateGroupCard.FileBytes),T("DupFileSize"),false),new SortChoice(nameof(DuplicateGroupCard.CopyCount),T("DupCopyCount"),false),new SortChoice(nameof(DuplicateGroupCard.Name),T("Name"),true)},groupSort,groupAscending,(Style)Resources["ExplorerActionButton"],(Style)Resources["ExplorerMenuItem"],(Style)Resources["ExplorerFocus"]);
        foreach(var card in cards)card.SetLanguage(arabic);
        foreach(var pair in new[]{((DependencyObject)PathInput,"DupScanPath"),((DependencyObject)ResultsSearch,"DupSearch"),((DependencyObject)MinimumSize,"DupMinimum"),((DependencyObject)Results,"DupResults")})AutomationProperties.SetName(pair.Item1,T(pair.Item2));
        RefreshGroups();
    }
    private void ShowCleanupToast()
    {
        if(lastCleanup==null)return;
        CleanupToast.Text=T("DupCleanupCounts",lastCleanup.Items.Count(item=>item.Removed),lastCleanup.Items.Count(item=>!item.Removed))+" · ";
        CleanupToast.Inlines.Add(new Run(DisplayFormat.Size(lastCleanup.Reclaimed,false)){FlowDirection=FlowDirection.LeftToRight});CleanupToast.Inlines.Add(" "+T("DupFreed"));
        CleanupToast.Visibility=Visibility.Visible;toastTimer.Stop();toastTimer.Start();
    }
    private void FlushPartialResults()
    {
        if(pendingGroups.Count==0||groupView==null)return;
        var added=new List<DuplicateGroupCard>();
        var known=cards.Select(card=>card.Source.Hash).ToHashSet(StringComparer.Ordinal);
        foreach(var group in pendingGroups)
        {
            if(!known.Add(group.Hash))continue;
            var copies=CreateRows(new[]{group},arabic);rows.AddRange(copies);
            var card=new DuplicateGroupCard(group,copies,arabic);card.SelectionUpdated+=()=>{if(!changingSelection)UpdateButtons();};added.Add(card);
        }
        pendingGroups.Clear();cards.AddRange(added);UpdateSummary();UpdateButtons();
    }
    private void RefreshGroups()
    {
        if(groupView==null||MinimumSize==null||ResultsSearch==null)return;
        bool valid=decimal.TryParse(MinimumSize.Text,NumberStyles.Number,DisplayFormat.NumericCulture,out decimal minimum)&&minimum>=0&&minimum<=long.MaxValue/1048576m;
        MinimumSize.ToolTip=T(valid?"DupMinimum":"DupInvalidMinimum");
        decimal bytes=valid?minimum*1048576m:0;
        string query=ResultsSearch.Text.Trim();
        using(groupView.DeferRefresh())
        {
            groupView.Filter=item=>item is DuplicateGroupCard card&&(filterCategory=="All"||card.Category==filterCategory)&&card.FileBytes>=bytes&&(query.Length==0||card.Copies.Any(row=>row.Path.Contains(query,StringComparison.CurrentCultureIgnoreCase)));
            groupView.SortDescriptions.Clear();groupView.SortDescriptions.Add(new SortDescription(groupSort,groupAscending?ListSortDirection.Ascending:ListSortDirection.Descending));
            if(groupSort!=nameof(DuplicateGroupCard.Name))groupView.SortDescriptions.Add(new SortDescription(nameof(DuplicateGroupCard.Name),ListSortDirection.Ascending));
        }
        ResultsSort.SetState(groupSort,groupAscending);UpdateSummary();
    }
    private void FiltersChanged(object sender,TextChangedEventArgs e){if(groupView!=null){resultFilterTimer.Stop();resultFilterTimer.Start();}}
    private void ScanPathChanged(object sender,TextChangedEventArgs e)
    {
        if(IsBusy)return;
        if(result!=null&&!string.Equals(PathInput.Text.TrimEnd('\\'),result.Root.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))
        {
            result=null;lastCleanup=null;latestProgress=null;rows.Clear();cards.Clear();pendingGroups.Clear();state="idle";
            Progress.Value=0;CleanupDetails.Visibility=CleanupToast.Visibility=Visibility.Collapsed;UpdateSummary();UpdateStatus();
        }
        if(ScanButton!=null)UpdateButtons();
    }
    private void UpdateSelectionSummary()
    {
        if(SelectionSummary==null)return;
        SelectionSummary.Text=T("DupSelected",rows.Count(row=>row.Selected))+" · ";
        SelectionSummary.Inlines.Add(new Run(DisplayFormat.Size(rows.Where(row=>row.Selected).Sum(row=>row.Bytes),false)){FlowDirection=FlowDirection.LeftToRight});
        SelectionSummary.Inlines.Add(" "+T("DupWillFree"));
    }
    private void ClearSelection(object sender,RoutedEventArgs e)=>ApplySmartSelection("clear");
    private void SmartSelectMenu(object sender,RoutedEventArgs e)
    {
        var menu=new ContextMenu{FlowDirection=FlowDirection};menu.SetResourceReference(BackgroundProperty,"HomeCardBrush");menu.SetResourceReference(ForegroundProperty,"HomeTextBrush");menu.SetResourceReference(FontFamilyProperty,"InterfaceFont");
        foreach(var choice in new[]{("newest","DupKeepNewest"),("oldest","DupKeepOldest"),("shortest","DupKeepShortest"),("folder","DupKeepFolder"),("clear","DupClear")})
        {
            var item=new MenuItem{Header=T(choice.Item2),Style=(Style)Resources["ExplorerMenuItem"],Icon=new TextBlock{Text="✓",Width=16}};
            item.Click+=(_,_)=>
            {
                string? folder=null;
                if(choice.Item1=="folder")
                {
                    var picker=new Microsoft.Win32.OpenFolderDialog{Title=T("DupKeepFolder")};var owner=Window.GetWindow(this);
                    if((owner==null?picker.ShowDialog():picker.ShowDialog(owner))!=true)return;folder=picker.FolderName;
                }
                ApplySmartSelection(choice.Item1,folder);
            };menu.Items.Add(item);
        }
        menu.PlacementTarget=SmartSelectButton;menu.IsOpen=true;
    }
    internal void ApplySmartSelection(string strategy,string? folder=null)
    {
        if(IsBusy)return;
        changingSelection=true;
        try
        {
            foreach(var card in cards)
            {
                if(strategy=="clear"){card.Selected=false;continue;}
                var copies=card.Copies.OrderBy(row=>row.Path,StringComparer.OrdinalIgnoreCase);
                DuplicateRow keep=strategy switch
                {
                    "newest"=>copies.OrderByDescending(row=>row.ModifiedUtc).First(),
                    "oldest"=>copies.OrderBy(row=>row.ModifiedUtc).First(),
                    "folder"=>copies.FirstOrDefault(row=>folder!=null&&row.Path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))??copies.OrderBy(row=>row.Path.Length).First(),
                    _=>copies.OrderBy(row=>row.Path.Length).First()
                };
                card.Keep(keep);card.Selected=true;
            }
        }
        finally{changingSelection=false;}
        UpdateButtons();Status.Text=T("DupSmartResult",rows.Count(row=>row.Selected));activity.Write("INFO",Status.Text);
    }
    private void KeepCopyClick(object sender,RoutedEventArgs e)
    {
        if(IsBusy||sender is not FrameworkElement {DataContext:DuplicateRow row})return;
        row.Owner?.Keep(row);UpdateButtons();activity.Write("INFO",T("DupKept",row.Path));
    }
    private void CopyLocationClick(object sender,RoutedEventArgs e)
    {
        if(sender is not FrameworkElement{DataContext:DuplicateRow row})return;
        try{var start=new ProcessStartInfo("explorer.exe"){UseShellExecute=true};start.ArgumentList.Add("/select,");start.ArgumentList.Add(row.Path);Process.Start(start);}
        catch(Exception error)when(error is IOException or Win32Exception){Status.Text=T("ActionFailed",error.Message);}
    }
    private void ResultsKeyDown(object sender,KeyEventArgs e)
    {
        if(IsBusy||Keyboard.FocusedElement is not ListBoxItem{DataContext:DuplicateGroupCard card})return;
        if(e.Key==Key.Enter){card.Expanded=!card.Expanded;e.Handled=true;}
        else if(e.Key==Key.Space){card.Selected=card.Selected!=true;e.Handled=true;}
    }
    private void GroupToggleKeyDown(object sender,KeyEventArgs e)
    {if(e.Key==Key.Enter&&sender is System.Windows.Controls.Primitives.ToggleButton toggle){toggle.IsChecked=toggle.IsChecked!=true;e.Handled=true;}}
}
