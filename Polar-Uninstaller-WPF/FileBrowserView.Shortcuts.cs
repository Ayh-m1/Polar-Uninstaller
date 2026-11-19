using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace PolarUninstallerWpf;

public partial class FileBrowserView
{
    private SelectionTipBar? selectionTip;
    private ShortcutHelpWindow? shortcutHelp;
    private void InitializeShortcutExperience()
    {
        selectionTip=new SelectionTipBar(settingsService,ShowShortcutHelp,(Style)Resources["ExplorerActionButton"],(Style)Resources["ExplorerFocus"]){Margin=new Thickness(0,0,0,8)};
        SelectionTipHost.Content=selectionTip;RefreshShortcutLanguage();
        selectionTip.Dismissed+=()=>Files.Focus();
        PreviewTextInput+=(_,e)=>{if(!duplicatesActive&&ExplorerShortcuts.IsHelpText(e.Text,ShortcutUi.TextInputFocused(e.OriginalSource as DependencyObject))){e.Handled=true;ShowShortcutHelp();}};
    }
    private void RefreshShortcutLanguage()
    {
        selectionTip?.Configure(arabic);BrowserShortcutHint.Configure(arabic,false);
        ShortcutUi.SetTooltip(Search,T("Search"),"Ctrl+F",arabic);ShortcutUi.SetTooltip(ClearSearch,T("ClearSearch"),"",arabic);
        FitBrowserHint();
    }
    private void AddShortcutHelpButton()
    {
        var button=new Button{Content="?",Width=34,Height=34,Padding=new Thickness(0),Margin=new Thickness(0,0,6,6),Style=(Style)Resources["ExplorerActionButton"]};
        DecorateButton(button,T("HelpToolbar"));button.Click+=(_,_)=>ShowShortcutHelp();Toolbar.Children.Add(button);
    }
    private void ShowShortcutHelp()
    {
        if(deleting||duplicatesActive)return;
        if(shortcutHelp!=null){shortcutHelp.Activate();return;}
        var owner=Window.GetWindow(this);
        shortcutHelp=new ShortcutHelpWindow(arabic,false,(Style)Resources["ExplorerActionButton"],(Style)Resources["ExplorerFocus"]){Owner=owner};
        try{shortcutHelp.ShowDialog();}finally{shortcutHelp=null;}
    }
    private async Task<bool> HandleBrowserKey(KeyEventArgs e,bool fromList=false)
    {
        if(e.Handled||deleting||duplicatesActive)return false;
        Key key=e.Key==Key.System?e.SystemKey:e.Key;
        if((key is Key.Enter or Key.Space)&&ShortcutUi.Ancestor<GridViewColumnHeader>(e.OriginalSource as DependencyObject)!=null)return false;
        var command=ExplorerShortcuts.Resolve(key,Keyboard.Modifiers,false,ShortcutUi.TextInputFocused(e.OriginalSource as DependencyObject),fromList||Files.IsKeyboardFocusWithin);
        if(command==ExplorerCommand.None)return false;
        e.Handled=true;if(e.IsRepeat&&command is ExplorerCommand.Recycle or ExplorerCommand.Permanent or ExplorerCommand.Help)return true;
        await RunBrowserAction(()=>DispatchBrowserCommand(command));return true;
    }
    private async Task DispatchBrowserCommand(ExplorerCommand command)
    {
        if(command==ExplorerCommand.Help){ShowShortcutHelp();return;}
        if(command==ExplorerCommand.Search){Search.Focus();Search.SelectAll();return;}
        if(command==ExplorerCommand.Address){ShowAddress();return;}
        string? navigationKey=command switch{ExplorerCommand.Back=>L("Back","رجوع"),ExplorerCommand.Forward=>L("Forward","التالي"),ExplorerCommand.Up=>L("Up","للأعلى"),ExplorerCommand.Refresh=>L("Refresh","تحديث"),_=>null};
        if(navigationKey!=null){if(navigation.TryGetValue(navigationKey,out var button)&&button.IsEnabled)button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));return;}
        if(command==ExplorerCommand.SelectAll){Files.SelectAll();selectionAnchor=Files.Items.Count>0?0:-1;UpdateSelectionState(false);}
        else if(command==ExplorerCommand.Deselect){Files.UnselectAll();selectionAnchor=-1;UpdateSelectionState(false);}
        else if(command==ExplorerCommand.Open)await OpenCurrent();
        else if(command==ExplorerCommand.Recycle)await Delete(false);
        else if(command==ExplorerCommand.Permanent)await Delete(true);
    }
    private void StatusFooterSizeChanged(object sender,SizeChangedEventArgs e)=>FitBrowserHint();
    private void FitBrowserHint()
    {
        if(BrowserShortcutHint==null||BrowserShortcutHint.Children.Count==0||Status==null)return;
        string text=Status.Text;
        BrowserShortcutHint.Fit(Math.Min(ActualWidth,StatusFooter.ActualWidth),ShortcutUi.TextWidth(text,Status,Status.FontSize));
    }
}
