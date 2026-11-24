using System.Windows.Input;

namespace PolarUninstallerWpf;

public enum ExplorerCommand {None,Help,Back,Forward,Up,Refresh,Open,SelectAll,Deselect,Recycle,Permanent,Search,Address,ToggleHidden}
public sealed record ShortcutHelpEntry(string Group,string Keys,string Description);

public static class ExplorerShortcuts
{
    public static ExplorerCommand Resolve(Key key,ModifierKeys modifiers,bool duplicate,bool textInput,bool listFocus)
    {
        if(key==Key.F1&&modifiers==ModifierKeys.None&&!textInput)return ExplorerCommand.Help;
        if(key==Key.F&&modifiers==ModifierKeys.Control)return ExplorerCommand.Search;
        if(modifiers==ModifierKeys.Alt)return key switch{Key.Left=>ExplorerCommand.Back,Key.Right when !duplicate=>ExplorerCommand.Forward,Key.Up when !duplicate=>ExplorerCommand.Up,_=>ExplorerCommand.None};
        if(modifiers==ModifierKeys.None&&key==Key.F5)return ExplorerCommand.Refresh;
        if(!duplicate&&(modifiers==ModifierKeys.None&&key==Key.F4||modifiers==ModifierKeys.Control&&key==Key.L))return ExplorerCommand.Address;
        if(textInput)return ExplorerCommand.None;
        if(!duplicate && key==Key.H && modifiers==ModifierKeys.Control)return ExplorerCommand.ToggleHidden;
        if(key==Key.Back&&modifiers==ModifierKeys.None)return ExplorerCommand.Back;
        if(!listFocus)return ExplorerCommand.None;
        if(key==Key.A&&modifiers==ModifierKeys.Control)return ExplorerCommand.SelectAll;
        if((key==Key.D&&modifiers==ModifierKeys.Control)||(key==Key.Escape&&modifiers==ModifierKeys.None))return ExplorerCommand.Deselect;
        if(key==Key.Delete)return modifiers==ModifierKeys.Shift?ExplorerCommand.Permanent:modifiers==ModifierKeys.None?ExplorerCommand.Recycle:ExplorerCommand.None;
        if(!duplicate&&key==Key.Enter&&modifiers==ModifierKeys.None)return ExplorerCommand.Open;
        return ExplorerCommand.None;
    }
    public static bool IsHelpText(string text,bool textInput)=>!textInput&&(text=="?"||text=="؟");
    public static IReadOnlyList<ShortcutHelpEntry> Help(bool duplicate)
    {
        var rows=new List<ShortcutHelpEntry>{new("HelpNavigation","Alt+Left",duplicate?"HelpBackBrowser":"HelpBack")};
        if(!duplicate){rows.Add(new("HelpNavigation","Alt+Right","HelpForward"));rows.Add(new("HelpNavigation","Alt+Up","HelpUp"));rows.Add(new("HelpNavigation","Ctrl+L / F4","HelpAddress"));}
        if(!duplicate)rows.Add(new("View","Ctrl+H","ShowHiddenItems"));
        rows.Add(new("HelpNavigation","F5",duplicate?"HelpRescan":"HelpRefresh"));
        rows.Add(new("HelpNavigation","Backspace",duplicate?"HelpBackBrowser":"HelpBackOutsideText"));
        rows.Add(new("HelpNavigation","Enter",duplicate?"HelpExpandGroup":"HelpOpen"));
        if(duplicate)rows.Add(new("Selection","Space","HelpToggleCopy"));
        rows.Add(new("Selection","Ctrl+Click",duplicate?"HelpSelectCopies":"HelpMultiSelect"));
        rows.Add(new("Selection","Shift+Click",duplicate?"HelpCopyRange":"HelpRange"));
        rows.Add(new("Selection","Ctrl+A",duplicate?"HelpSelectExtras":"HelpSelectAll"));
        rows.Add(new("Selection","Ctrl+D / Esc","HelpDeselect"));
        rows.Add(new("HelpActions","Delete","HelpRecycle"));rows.Add(new("HelpActions","Shift+Delete","HelpPermanent"));
        rows.Add(new("HelpSearchSort","Ctrl+F","HelpSearch"));
        rows.Add(new("HelpSearchSort","Enter / Space",duplicate?"HelpSortDropdown":"HelpSortHeader"));
        rows.Add(new("HelpSearchSort","F1 / ?","HelpOpenHelp"));return rows;
    }
}
