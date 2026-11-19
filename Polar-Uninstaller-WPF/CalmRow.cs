using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
namespace PolarUninstallerWpf;

/// <summary>Repeated rows get just one lightweight, finite background opacity fade.</summary>
public static class CalmRow
{
    public static readonly DependencyProperty EnabledProperty=DependencyProperty.RegisterAttached("Enabled",typeof(bool),typeof(CalmRow),new PropertyMetadata(false,Changed));
    public static bool GetEnabled(DependencyObject o)=>(bool)o.GetValue(EnabledProperty);public static void SetEnabled(DependencyObject o,bool value)=>o.SetValue(EnabledProperty,value);
    private static void Changed(DependencyObject o,DependencyPropertyChangedEventArgs e)
    {
        if(o is not Border row||!(bool)e.NewValue)return;
        var overlay=new Border{Opacity=0,IsHitTestVisible=false,CornerRadius=row.CornerRadius};overlay.SetResourceReference(Border.BackgroundProperty,"CalmHoverFill");
        row.Loaded+=(_,_)=>{if(row.Child is Panel panel&&!panel.Children.Contains(overlay)){if(panel is Grid grid){Grid.SetColumnSpan(overlay,Math.Max(1,grid.ColumnDefinitions.Count));Grid.SetRowSpan(overlay,Math.Max(1,grid.RowDefinitions.Count));}panel.Children.Insert(0,overlay);}};
        void Apply()
        {
            double opacity=row.IsMouseOver&&row.IsEnabled?1:0;
            if((!SystemParameters.ClientAreaAnimation||SystemParameters.IsRemoteSession)||!row.IsEnabled){overlay.BeginAnimation(UIElement.OpacityProperty,null);overlay.Opacity=opacity;}
            else overlay.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(opacity,TimeSpan.FromSeconds(.5)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseInOut}});
        }
        row.MouseEnter+=(_,_)=>Apply();row.MouseLeave+=(_,_)=>Apply();row.IsEnabledChanged+=(_,_)=>Apply();
        row.Unloaded+=(_,_)=>{overlay.BeginAnimation(UIElement.OpacityProperty,null);overlay.Opacity=0;};
    }
}
