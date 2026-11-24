using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PolarUninstallerWpf.Services;

namespace PolarUninstallerWpf;

public partial class DeepUninstallerView
{
    private async Task RunRemovalActionAsync(Func<Task> action)
    {
        try{await action();}
        catch(OperationCanceledException){ }
        catch(Exception error){CrashDiagnostics.Record("Removal action",error);SetStatus(lang => BidiText.Localize(lang,"The action could not finish. Refresh and review Activity Log.","تعذر إكمال الإجراء. حدّث القائمة وراجع سجل النشاط."));activity.Write("ERROR",Status.Text);}
    }
    private async Task RefreshSharedInventoryAsync()
    {
        try{await inventory.RefreshAsync();}
        catch(Exception error){CrashDiagnostics.Record("Post-removal inventory",error);activity.Write("WARNING",L("Removal was verified, but the inventory refresh failed. Refresh the list.","تأكدت الإزالة، لكن تعذر تحديث القائمة. حدّثها يدويًا."));}
    }
    private void WriteReport(UninstallReport report)
    {
        var english=report.LogLines(false);var translation=report.LogLines(true);
        for(int i=0;i<english.Count;i++)
            activity.WriteBilingual(report.Severity,english[i],i<translation.Count?translation[i]:english[i]);
    }
    private string kindFilter="all";
    private ComboBox? kindSelector;
    private (int Index,int Total,string Name,bool Shared)? batchProgress;
    private static bool SameSelection(InstalledProgram a,InstalledProgram b)=>
        string.Equals(a.Name,b.Name,StringComparison.OrdinalIgnoreCase)&&
        string.Equals(a.Identifier,b.Identifier,StringComparison.OrdinalIgnoreCase)&&
        string.Equals(InventoryMerger.NormalizePath(a.Location),InventoryMerger.NormalizePath(b.Location),StringComparison.OrdinalIgnoreCase)&&
        string.Equals(a.Publisher,b.Publisher,StringComparison.OrdinalIgnoreCase);

    private void InitializeSelectionControls()
    {
        kindSelector=new ComboBox{Width=115,Height=38,Margin=new Thickness(8,0,0,0),Style=SortSelector.Style};
        foreach(string label in new[]{L("All","الكل"),L("Programs","البرامج"),L("Games","الألعاب")})kindSelector.Items.Add(label);
        kindSelector.SelectedIndex=0;kindSelector.ToolTip=L("Filter applications","تصفية البرامج");
        kindSelector.SelectionChanged+=(_,_)=>{if(updatingKindLanguage)return;kindFilter=kindSelector.SelectedIndex switch{1=>"programs",2=>"games",_=>"all"};RefreshFilter();};
        DockPanel.SetDock(kindSelector,Dock.Right);((DockPanel)SortSelector.Parent).Children.Insert(0,kindSelector);
        InitializeSelectionGestures();
    }
    private void RefreshFilter()
    {
        if (List == null || view == null) return;
        gestureRevision++;
        var selection = List.SelectedItems.Cast<ProgramCard>().ToArray();
        applyingSelection = true;
        try
        {
            view.Refresh();
            foreach (var card in selection)
                if (view.Contains(card) && !List.SelectedItems.Contains(card)) List.SelectedItems.Add(card);
            if (selectionAnchor is not null && !view.Contains(selectionAnchor)) selectionAnchor = null;
        }
        finally { applyingSelection = false; }
        RefreshSelectionActions(!SelectionLocked && selection.Length != List.SelectedItems.Count);
    }
    private async Task EvaluateBatchActionsAsync(long revision)
    {
        var selected=List.SelectedItems.Cast<ProgramCard>().ToArray();
        var records=(List.ItemsSource as IEnumerable<ProgramCard>??Array.Empty<ProgramCard>()).Select(c=>c.Program).ToArray();
        try
        {
            var decisions=await Task.Run(()=>selected.Select(c=>SmartUninstallSelectionService.Evaluate(c.Program,records)).ToArray(),cancellation.Token);
            if(revision!=selectionRevision||busy||batchActive||refreshingInventory)return;
            Deep.IsEnabled=decisions.Any(d=>d.DeepEnabled);Games.IsEnabled=decisions.Any(d=>d.GamesEnabled);
            string hint=L("Runs selected compatible items in sequence; unsupported items are skipped.","تُزال العناصر المحددة المناسبة بالتتابع، وتُتجاوز العناصر غير المدعومة.");
            Deep.ToolTip=Games.ToolTip=hint;
        }
        catch(OperationCanceledException){ }
        catch(Exception error){CrashDiagnostics.Record("Batch selection",error);SetStatus(lang => BidiText.Localize(lang,"Could not evaluate selection. Refresh the list.","تعذر التحقق من التحديد. حدّث القائمة."));}
    }
    private async Task UninstallSelectionAsync(bool deep)
    {
        if(batchActive||busy||monitorCancellation!=null)return;
        var selected=List.Items.OfType<ProgramCard>().Where(List.SelectedItems.Contains).ToArray();
        if(selected.Length==0)return;
        if(selected.Length==1){await RunRemovalActionAsync(()=>Uninstall(deep,selected[0]));return;}
        using var stop=new CancellationTokenSource();
        batchCancellation=stop;batchActive=true;SetRemovalControl(true);UpdateActions();
        bool shared=HasSharedSelection(selected);
        if(shared) activity.WriteBilingual("WARNING",BrowserText.Get("DeepSharedWarning",false),BrowserText.Get("DeepSharedWarning",true));
        try
        {
            var result=await UninstallBatchService.RunAsync(selected,async(card,token)=>
            {
                token.ThrowIfCancellationRequested();operationOutcome=BatchItemOutcome.Failed;
                var records=(List.ItemsSource as IEnumerable<ProgramCard>??Array.Empty<ProgramCard>()).Select(c=>c.Program).ToArray();
                var decision=await Task.Run(()=>SmartUninstallSelectionService.Evaluate(card.Program,records),token);
                if(deep?!decision.DeepEnabled:!decision.GamesEnabled)
                {activity.Write("INFO",L("Skipped: ","تُجاوز: ")+DisplayFormat.Isolate(card.Name)+" · "+decision.Explanation(arabic));return BatchItemOutcome.Skipped;}
                await Uninstall(deep,card);
                if(monitorTask is {} monitoring)await monitoring;
                activity.WriteBilingual(operationOutcome==BatchItemOutcome.Succeeded?"INFO":"WARNING",
                    BrowserText.Get(BatchOutcomeKey(operationOutcome),false,card.Name),BrowserText.Get(BatchOutcomeKey(operationOutcome),true,card.Name));
                return operationOutcome;
            },cancellation.Token,(index,total)=>
            {
                batchProgress=(index,total,selected[index-1].Name,shared);
                SetStatus(_=>"");
            },error=>{CrashDiagnostics.Record("Batch removal",error);activity.WriteBilingual("ERROR",
                BrowserText.Get("DeepBatchFailed",false,batchProgress?.Name??""),BrowserText.Get("DeepBatchFailed",true,batchProgress?.Name??""));},stop.Token);
            batchProgress=null;
            string Summary(bool lang)=>BrowserText.Get("DeepBatchSummary",lang,result.Succeeded,result.Failed,result.Manual,result.Skipped)+
                (result.Cancelled?" "+BrowserText.Get("DeepBatchStopped",lang):"");
            SetStatus(Summary);
            activity.WriteBilingual(result.Failed>0||result.Manual>0?"WARNING":"INFO",Summary(false),Summary(true));
        }
        catch(Exception error){batchProgress=null;CrashDiagnostics.Record("Removal batch",error);SetStatus("Removal stopped. Refresh and review Activity Log.","توقفت الإزالة. حدّث القائمة وراجع سجل النشاط.");activity.Write("ERROR",Status.Text);}
        finally{batchProgress=null;batchActive=false;batchCancellation=null;SetRemovalControl(false);UpdateActions();}
    }

    private static string BatchOutcomeKey(BatchItemOutcome outcome) => outcome switch
    {
        BatchItemOutcome.Succeeded => "DeepBatchSucceeded",
        BatchItemOutcome.Manual => "DeepBatchManual",
        _ => "DeepBatchFailed"
    };

    private bool HasSharedSelection(IEnumerable<ProgramCard> selected)
    {
        var records=(List.ItemsSource as IEnumerable<ProgramCard>??Array.Empty<ProgramCard>()).ToArray();
        return selected.Any(card =>
        {
            var program=card.Program;
            if (RiotGameService.IsProtectedComponent(program) ||
                program.Name.Equals("Adobe Creative Cloud",StringComparison.OrdinalIgnoreCase) ||
                program.Name.Equals("Apple Mobile Device Support",StringComparison.OrdinalIgnoreCase)) return true;

            string location=InventoryMerger.NormalizePath(program.Location);
            return location.Length>0&&records.Any(other=>!ReferenceEquals(card,other)&&
                string.Equals(location,InventoryMerger.NormalizePath(other.Program.Location),StringComparison.OrdinalIgnoreCase));
        });
    }

    private void SetRemovalControl(bool visible,bool monitoring=false)
    {
        cancelRemoval.Visibility = visible || batchActive ? Visibility.Visible : Visibility.Collapsed;
        cancelRemoval.IsEnabled = !batchActive || batchCancellation?.IsCancellationRequested != true;
        language.Set(cancelRemoval,ContentControl.ContentProperty,lang=>batchActive ? BrowserText.Get("DeepBatchStop",lang) :
            (monitoring ? BidiText.Localize(lang,"Cancel monitoring","إلغاء المراقبة") : BidiText.Localize(lang,"Cancel removal","إلغاء الإزالة")));
    }
}
