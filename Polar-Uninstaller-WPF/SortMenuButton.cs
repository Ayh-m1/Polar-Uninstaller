using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using PolarUninstallerWpf.Services;

namespace PolarUninstallerWpf;

public sealed record SortChoice(string Key,string Label,bool DefaultAscending);

/// <summary>One sorting vocabulary and exclusive menu model shared by both explorer pages.</summary>
public sealed class SortMenuButton:Button
{
    private readonly ContextMenu menu=new();
    private IReadOnlyList<SortChoice> choices=Array.Empty<SortChoice>();
    private bool arabic;
    private string key="";
    private bool ascending;
    public event Action<string,bool>? SortRequested;
    public SortMenuButton()
    {
        Click+=(_,_)=>{menu.PlacementTarget=this;menu.Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom;menu.IsOpen=true;};
        GotKeyboardFocus+=(_,_)=>{if(ToolTip is ToolTip tip&&System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.Tab))tip.IsOpen=true;};
        LostKeyboardFocus+=(_,_)=>{if(ToolTip is ToolTip tip)tip.IsOpen=false;};
    }
    public void Configure(bool rtl,IReadOnlyList<SortChoice> options,string selected,bool asc,Style buttonStyle,Style menuStyle,Style focus)
    {
        arabic=rtl;choices=options;Style=buttonStyle;FocusVisualStyle=focus;
        FlowDirection=rtl?FlowDirection.RightToLeft:FlowDirection.LeftToRight;menu.FlowDirection=FlowDirection;
        menu.SetResourceReference(BackgroundProperty,"HomeCardBrush");menu.SetResourceReference(ForegroundProperty,"HomeTextBrush");menu.SetResourceReference(FontFamilyProperty,"InterfaceFont");
        menu.Items.Clear();
        foreach(var choice in choices)
        {
            var item=new MenuItem{Header=choice.Label,Tag=choice.Key,IsCheckable=true,Style=menuStyle};
            item.Click+=(_,_)=>SortRequested?.Invoke(choice.Key,choice.DefaultAscending);menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        foreach(bool direction in new[]{true,false})
        {
            var item=new MenuItem{Header=BrowserText.Get(direction?"Ascending":"Descending",rtl),Tag=direction,IsCheckable=true,Style=menuStyle};
            item.Click+=(_,_)=>SortRequested?.Invoke(key,direction);menu.Items.Add(item);
        }
        ToolTip=new ToolTip{Content=BrowserText.Get("SortBy",rtl),PlacementTarget=this};SetState(selected,asc);
    }
    public void SetState(string selected,bool asc)
    {
        key=selected;ascending=asc;
        string label=BrowserText.Get("SortState",arabic,choices.FirstOrDefault(c=>c.Key==key)?.Label??key,asc?"↑":"↓");
        Content=label+" ▾";AutomationProperties.SetName(this,label);
        foreach(var item in menu.Items.OfType<MenuItem>())
        {
            bool active=item.Tag is string field?field==key:item.Tag is bool direction&&direction==ascending;
            item.IsChecked=active;
            var icon=new TextBlock{Text=active?"●":"○",Width=16,FontSize=13,TextAlignment=TextAlignment.Center};icon.SetResourceReference(TextBlock.ForegroundProperty,active?"ChromeAccent":"HomeTextBrush");item.Icon=icon;
        }
    }
}
