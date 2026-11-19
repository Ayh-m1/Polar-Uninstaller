using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using PolarUninstallerWpf.Services;

namespace PolarUninstallerWpf;

public sealed class SelectionTipBar:Border
{
    private readonly SelectionEducationState state;
    private readonly Action help;
    private readonly Style style,focus;
    private bool hasSelection;
    public event Action? Dismissed;
    public SelectionTipBar(SettingsService settings,Action help,Style style,Style focus)
    {
        state=SelectionEducationState.For(settings);this.help=help;this.style=style;this.focus=focus;CornerRadius=new CornerRadius(8);BorderThickness=new Thickness(1);Padding=new Thickness(10);Visibility=Visibility.Collapsed;
        SetResourceReference(BackgroundProperty,"HomeCardBrush");SetResourceReference(BorderBrushProperty,"HomeBorderBrush");
        state.Changed+=Refresh;Unloaded+=(_,_)=>state.Changed-=Refresh;Loaded+=(_,_)=>{state.Changed-=Refresh;state.Changed+=Refresh;Refresh();};
    }
    public void Configure(bool arabic,bool duplicate=false)
    {
        string T(string key)=>BrowserText.Get(key,arabic);FlowDirection=arabic?FlowDirection.RightToLeft:FlowDirection.LeftToRight;
        var layout=new DockPanel();var actions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(actions,Dock.Right);
        var dismiss=new Button{Content=T("TipGotIt"),Style=style,FocusVisualStyle=focus,Padding=new Thickness(8,4,8,4),Margin=new Thickness(8,0,0,0)};dismiss.Click+=(_,_)=>Dismiss();
        var linkText=new TextBlock{Text=T("TipShowAll"),TextDecorations=TextDecorations.Underline};linkText.SetResourceReference(TextBlock.ForegroundProperty,"ChromeAccent");
        var all=new Button{Content=linkText,Style=style,FocusVisualStyle=focus,Background=null,BorderThickness=new Thickness(0),Padding=new Thickness(8,4,8,4)};all.Click+=(_,_)=>help();System.Windows.Automation.AutomationProperties.SetName(all,T("TipShowAll"));
        actions.Children.Add(dismiss);actions.Children.Add(all);layout.Children.Add(actions);
        var text=new TextBlock{TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center};text.SetResourceReference(TextBlock.ForegroundProperty,"HomeTextBrush");
        foreach(string fragment in System.Text.RegularExpressions.Regex.Split(T(duplicate?"TipCopies":"TipSelection"),"(Ctrl|Shift)"))text.Inlines.Add(new Run(fragment){FlowDirection=fragment is "Ctrl" or "Shift"?FlowDirection.LeftToRight:FlowDirection});
        layout.Children.Add(text);Child=layout;Refresh();
    }
    public void SelectionChanged(bool selected){hasSelection=selected;Refresh();}
    public void Dismiss(){state.Dismiss();Dismissed?.Invoke();}
    private void Refresh()=>Visibility=hasSelection&&!state.Dismissed?Visibility.Visible:Visibility.Collapsed;
}
