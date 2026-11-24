using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PolarUninstallerWpf.Services;

namespace PolarUninstallerWpf;

public sealed class ShortcutHelpWindow:Window
{
    public Button CloseButton {get;}
    public IReadOnlyList<ShortcutHelpEntry> Entries {get;}
    public ShortcutHelpWindow(bool arabic,bool duplicate,Style buttonStyle,Style focusStyle)
        : this(arabic,ExplorerShortcuts.Help(duplicate),BrowserText.Get("HelpScope",arabic),buttonStyle,focusStyle) { }

    public ShortcutHelpWindow(bool arabic,IReadOnlyList<ShortcutHelpEntry> entries,string scope,Style buttonStyle,Style focusStyle)
    {
        string T(string key)=>BrowserText.Get(key,arabic);
        Title=T("HelpTitle");Width=Math.Min(760,SystemParameters.WorkArea.Width-40);MinWidth=Math.Min(460,Width);MaxHeight=Math.Min(760,Math.Max(300,SystemParameters.WorkArea.Height-80));SizeToContent=SizeToContent.Height;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;FlowDirection=arabic?FlowDirection.RightToLeft:FlowDirection.LeftToRight;
        SetResourceReference(BackgroundProperty,"ChromeBackground");SetResourceReference(ForegroundProperty,"HomeTextBrush");SetResourceReference(FontFamilyProperty,"InterfaceFont");
        var root=new Grid{Margin=new Thickness(20),FlowDirection=FlowDirection};root.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty,"HomeTextBrush");root.SetResourceReference(System.Windows.Documents.TextElement.FontFamilyProperty,"InterfaceFont");root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});KeyboardNavigation.SetTabNavigation(root,KeyboardNavigationMode.Cycle);
        var heading=new StackPanel();heading.Children.Add(new TextBlock{Text=Title,FontSize=21,FontWeight=FontWeights.SemiBold});heading.Children.Add(new TextBlock{Text=scope,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,7,0,12)});root.Children.Add(heading);
        var list=new StackPanel();Entries=entries;
        foreach(var group in Entries.GroupBy(entry=>entry.Group))
        {
            var title=new TextBlock{Text=T(group.Key),FontWeight=FontWeights.SemiBold,FontSize=15,Margin=new Thickness(0,12,0,6)};title.SetResourceReference(TextBlock.ForegroundProperty,"ChromeAccent");list.Children.Add(title);
            foreach(var entry in group)
            {
                var row=new Grid{Margin=new Thickness(0,4,0,4)};row.ColumnDefinitions.Add(new(){Width=new GridLength(200)});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
                var keys=ShortcutUi.Keycap(entry.Keys);keys.HorizontalAlignment=HorizontalAlignment.Left;row.Children.Add(keys);
                var description=new TextBlock{Text=T(entry.Description),TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,0,0,0)};Grid.SetColumn(description,1);row.Children.Add(description);list.Children.Add(row);
                System.Windows.Automation.AutomationProperties.SetName(row,entry.Keys+": "+description.Text);
            }
        }
        var scroll=new ScrollViewer{Content=list,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,MaxHeight=Math.Max(160,MaxHeight-190),Focusable=true,FocusVisualStyle=focusStyle};Grid.SetRow(scroll,1);root.Children.Add(scroll);
        CloseButton=new Button{Content=T("HelpClose"),Style=buttonStyle,FocusVisualStyle=focusStyle,IsCancel=true,IsDefault=true,MinWidth=100,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,15,0,0)};CloseButton.Click+=(_,_)=>DialogResult=false;Grid.SetRow(CloseButton,2);root.Children.Add(CloseButton);Content=root;
        System.Windows.Automation.AutomationProperties.SetName(CloseButton,T("HelpClose"));Loaded+=(_,_)=>CloseButton.Focus();
    }
}
