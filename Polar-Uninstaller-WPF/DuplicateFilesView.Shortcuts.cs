using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;

namespace PolarUninstallerWpf;

public partial class DuplicateFilesView
{
    private SelectionTipBar? duplicateTip;
    private ShortcutHelpWindow? duplicateHelp;
    private DuplicateRow? copyAnchor;
    private void InitializeDuplicateShortcuts()
    {
        duplicateTip=new SelectionTipBar(settings,ShowDuplicateHelp,(Style)Resources["ExplorerActionButton"],(Style)Resources["ExplorerFocus"]){Margin=new Thickness(0,0,0,8)};DuplicateTipHost.Content=duplicateTip;
        duplicateTip.Dismissed+=()=>Results.Focus();
        PreviewKeyDown+=DuplicateKeyDown;
        PreviewTextInput+=(_,e)=>{if(ExplorerShortcuts.IsHelpText(e.Text,ShortcutUi.TextInputFocused(e.OriginalSource as DependencyObject))){e.Handled=true;ShowDuplicateHelp();}};
        Results.PreviewMouseLeftButtonDown+=DuplicateCopyClick;
    }
    private void LocalizeDuplicateShortcuts()
    {
        if(duplicateTip==null)return;
        duplicateTip.Configure(arabic,true);DuplicateShortcutHint.Configure(arabic,true);
        foreach(var pair in new[]{(DuplicateHelpButton,T("HelpToolbar"),"F1"),(BackButton,BackButton.Content.ToString()!,"Alt+Left / Backspace"),(ScanButton,ScanButton.Content.ToString()!,"F5"),(BrowseButton,BrowseButton.Content.ToString()!,""),(CancelButton,CancelButton.Content.ToString()!,""),(SmartSelectButton,SmartSelectButton.Content.ToString()!,""),(RecycleButton,T("Recycle"),"Delete"),(PermanentButton,T("Permanent"),"Shift+Delete"),(ClearSelectionButton,T("DupClear"),"Ctrl+D / Esc")})
        {ShortcutUi.SetTooltip(pair.Item1,pair.Item2,pair.Item3,arabic);System.Windows.Automation.AutomationProperties.SetName(pair.Item1,pair.Item2);}
        ShortcutUi.SetTooltip(ResultsSearch,T("DupSearch"),"Ctrl+F",arabic);FitDuplicateHint();
    }
    private void DuplicateHelpClick(object sender,RoutedEventArgs e)=>ShowDuplicateHelp();
    private void ShowDuplicateHelp()
    {
        if(duplicateHelp!=null){duplicateHelp.Activate();return;}
        duplicateHelp=new ShortcutHelpWindow(arabic,true,(Style)Resources["ExplorerActionButton"],(Style)Resources["ExplorerFocus"]){Owner=Window.GetWindow(this)};
        try{duplicateHelp.ShowDialog();}finally{duplicateHelp=null;}
    }
    private async void DuplicateKeyDown(object sender,KeyEventArgs e)
    {
        if(e.Handled)return;
        var command=ExplorerShortcuts.Resolve(e.Key==Key.System?e.SystemKey:e.Key,Keyboard.Modifiers,true,ShortcutUi.TextInputFocused(e.OriginalSource as DependencyObject),Results.IsKeyboardFocusWithin);
        if(command==ExplorerCommand.None)return;e.Handled=true;
        if(e.IsRepeat&&command is ExplorerCommand.Help or ExplorerCommand.Recycle or ExplorerCommand.Permanent)return;
        if(command==ExplorerCommand.Help){ShowDuplicateHelp();return;}
        if(command==ExplorerCommand.Search){ResultsSearch.Focus();ResultsSearch.SelectAll();return;}
        if(command==ExplorerCommand.Back){BackClick(sender,e);return;}
        if(IsBusy)return;
        if(command==ExplorerCommand.Refresh){if(ScanButton.IsEnabled)await StartScanAsync();return;}
        if(command==ExplorerCommand.SelectAll)SelectVisibleCopies(true);
        else if(command==ExplorerCommand.Deselect)SelectVisibleCopies(false);
        else if(command is ExplorerCommand.Recycle or ExplorerCommand.Permanent)await CleanupAsync(command==ExplorerCommand.Permanent);
    }
    private void SelectVisibleCopies(bool selected)
    {
        changingSelection=true;
        try
        {
            if(selected)foreach(var card in Results.Items.OfType<DuplicateGroupCard>())card.Selected=true;
            else foreach(var row in rows)row.Selected=false;
        }
        finally{changingSelection=false;}
        UpdateButtons();
    }
    private void DuplicateCopyClick(object sender,MouseButtonEventArgs e)
    {
        if(IsBusy)return;
        var source=e.OriginalSource as DependencyObject;
        var row=ShortcutUi.Ancestor<ListBoxItem>(source)?.DataContext as DuplicateRow;if(row==null)return;
        if(ShortcutUi.Ancestor<ButtonBase>(source) is ButtonBase button&&button is not CheckBox)return;
        bool control=(Keyboard.Modifiers&ModifierKeys.Control)!=0,shift=(Keyboard.Modifiers&ModifierKeys.Shift)!=0;
        if(ApplyCopySelection(row,control,shift)){ShortcutUi.Ancestor<ListBoxItem>(source)?.Focus();e.Handled=true;}
    }
    private bool ApplyCopySelection(DuplicateRow row,bool control,bool shift)
    {
        if(IsBusy)return false;
        if(control||shift)
        {
            duplicateTip?.Dismiss();
            if(shift&&row.Owner is {} group)
            {
                var copies=group.Copies.ToList();int end=copies.IndexOf(row),start=copyAnchor?.Owner==group?copies.IndexOf(copyAnchor):end;
                changingSelection=true;
                try{if(!control)foreach(var copy in rows)copy.Selected=false;for(int i=Math.Min(start,end);i<=Math.Max(start,end);i++)copies[i].Selected=true;}
                finally{changingSelection=false;}
            }
            else row.Selected=!row.Selected;
            UpdateButtons();
        }
        if(!shift)copyAnchor=row;
        return control||shift;
    }
    private void DuplicateFooterSizeChanged(object sender,SizeChangedEventArgs e)=>FitDuplicateHint();
    private void FitDuplicateHint()
    {
        if(DuplicateShortcutHint==null||DuplicateShortcutHint.Children.Count==0||SelectionSummary==null)return;
        var text=SelectionSummary.Text;
        DuplicateShortcutHint.Fit(Math.Min(ActualWidth,DuplicateStatusFooter.ActualWidth),ShortcutUi.TextWidth(text,SelectionSummary,SelectionSummary.FontSize));
    }
}
