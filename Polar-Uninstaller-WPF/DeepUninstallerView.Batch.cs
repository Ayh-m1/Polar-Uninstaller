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
        List.PreviewKeyDown+=(_,e)=>
        {
            if(busy||batchActive||e.OriginalSource is TextBox)return;
            if(e.Key==Key.A&&Keyboard.Modifiers==ModifierKeys.Control){List.SelectAll();e.Handled=true;}
            else if(e.Key==Key.Escape||e.Key==Key.D&&Keyboard.Modifiers==ModifierKeys.Control){List.UnselectAll();e.Handled=true;}
        };
    }
    private void RefreshFilter()
    {
        view?.Refresh();
        // Hidden items are deliberately deselected; the next batch is exactly what is visible and selected.
        foreach(var card in List.SelectedItems.Cast<ProgramCard>().ToArray())if(view?.Contains(card)==false)List.SelectedItems.Remove(card);
        SelectionChanged(this,null!);
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
        var selected=List.SelectedItems.Cast<ProgramCard>().Where(c=>view?.Contains(c)!=false).ToArray();
        if(selected.Length==0)return;
        using var cts=CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
        batchCancellation=cts;batchActive=true;cancelRemoval.Visibility=Visibility.Visible;UpdateActions();
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
                return operationOutcome;
            },cts.Token,(index,total)=>
            {
                cancelRemoval.Visibility=Visibility.Visible;
                SetStatus(lang => BidiText.Localize(lang,"Removing item ","جارٍ إزالة العنصر ")+DisplayFormat.Directional(index+" / "+total,lang));
            },error=>{CrashDiagnostics.Record("Batch removal",error);activity.Write("ERROR",L("One removal failed; continuing with remaining items.","تعذرت إزالة عنصر؛ ستُستكمل بقية العناصر."));});
            SetStatus(lang => BidiText.Localize(lang,"Removal summary — succeeded: ","ملخص الإزالة — نجح: ")+DisplayFormat.Isolate(DisplayFormat.Number(result.Succeeded))+
                BidiText.Localize(lang," · failed: "," · تعذر: ")+DisplayFormat.Isolate(DisplayFormat.Number(result.Failed))+
                BidiText.Localize(lang," · skipped: "," · تُجاوز: ")+DisplayFormat.Isolate(DisplayFormat.Number(result.Skipped))+
                (result.Cancelled?BidiText.Localize(lang," · Stopped. A vendor uninstaller may still be running."," · توقفت العملية. قد يظل مزيل التثبيت الرسمي قيد التشغيل."):""));
            activity.Write(result.Failed>0?"WARNING":"INFO",Status.Text);
        }
        catch(Exception error){CrashDiagnostics.Record("Removal batch",error);SetStatus(lang => BidiText.Localize(lang,"Removal stopped. Refresh and review Activity Log.","توقفت الإزالة. حدّث القائمة وراجع سجل النشاط."));activity.Write("ERROR",Status.Text);}
        finally{batchActive=false;batchCancellation=null;cancelRemoval.Visibility=Visibility.Collapsed;UpdateActions();}
    }
}
