using System.Windows;
using System.Windows.Controls;
using PolarUninstallerWpf.Services;

namespace PolarUninstallerWpf;

public sealed class ShortcutHintLine:StackPanel
{
    public ShortcutHintLine(){Orientation=Orientation.Horizontal;VerticalAlignment=VerticalAlignment.Center;IsHitTestVisible=false;}
    public void Configure(bool arabic,bool duplicate)
    {
        Children.Clear();FlowDirection=arabic?FlowDirection.RightToLeft:FlowDirection.LeftToRight;
        var parts=new[]{("Ctrl+Click",duplicate?"HintCopies":"HintMultiple"),("Shift+Click","HintRange"),("Ctrl+A",duplicate?"HintExtras":"HintAll")};
        foreach(var part in parts)
        {
            if(Children.Count>0){var dot=new TextBlock{Text=" · ",FontSize=11,VerticalAlignment=VerticalAlignment.Center};dot.SetResourceReference(TextBlock.ForegroundProperty,"HomeDimBrush");Children.Add(dot);}
            Children.Add(ShortcutUi.Keycap(part.Item1));
            var description=new TextBlock{Text=" "+BrowserText.Get(part.Item2,arabic),FontSize=11,VerticalAlignment=VerticalAlignment.Center};description.SetResourceReference(TextBlock.ForegroundProperty,"HomeDimBrush");Children.Add(description);
        }
    }
    public bool Fit(double available,double statusWidth)
    {
        double width=0;foreach(UIElement child in Children){child.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));width+=child.DesiredSize.Width;}
        bool fits=available>0&&width+statusWidth+32<=available;Visibility=fits?Visibility.Visible:Visibility.Collapsed;return fits;
    }
}
