using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace PolarUninstallerWpf;

public static class ShortcutUi
{
    public static DependencyObject? Parent(DependencyObject child)=>child is Visual or System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(child):LogicalTreeHelper.GetParent(child);
    public static T? Ancestor<T>(DependencyObject? child) where T:DependencyObject
    {while(child!=null){if(child is T found)return found;child=Parent(child);}return null;}
    public static bool TextInputFocused(DependencyObject? source=null)=>Ancestor<System.Windows.Controls.Primitives.TextBoxBase>(source)!=null||Ancestor<System.Windows.Controls.Primitives.TextBoxBase>(Keyboard.FocusedElement as DependencyObject)!=null||Ancestor<PasswordBox>(source)!=null||Ancestor<PasswordBox>(Keyboard.FocusedElement as DependencyObject)!=null;
    public static Border Keycap(string keys)
    {
        var text=new TextBlock{Text=keys,FlowDirection=FlowDirection.LeftToRight,FontSize=11,VerticalAlignment=VerticalAlignment.Center};text.SetResourceReference(TextBlock.ForegroundProperty,"HomeTextBrush");
        var chip=new Border{Child=text,CornerRadius=new CornerRadius(4),BorderThickness=new Thickness(1),Padding=new Thickness(5,2,5,2),FlowDirection=FlowDirection.LeftToRight,VerticalAlignment=VerticalAlignment.Center};
        chip.SetResourceReference(Border.BackgroundProperty,"HomeCardBrush");chip.SetResourceReference(Border.BorderBrushProperty,"HomeBorderBrush");return chip;
    }
    public static void SetTooltip(Control control,string label,string keys,bool arabic)
    {
        string identity=label+"|"+keys;
        if(control.ToolTip is ToolTip same&&Equals(same.Tag,identity))return;
        bool open=control.ToolTip is ToolTip old&&old.IsOpen;if(control.ToolTip is ToolTip previous)previous.IsOpen=false;
        var content=new StackPanel{Orientation=Orientation.Horizontal,FlowDirection=arabic?FlowDirection.RightToLeft:FlowDirection.LeftToRight};
        var name=new TextBlock{Text=label,VerticalAlignment=VerticalAlignment.Center};name.SetResourceReference(TextBlock.ForegroundProperty,"HomeTextBrush");content.Children.Add(name);
        if(keys.Length>0){var cap=Keycap(keys);cap.Margin=new Thickness(8,0,0,0);content.Children.Add(cap);}
        control.ToolTip=new ToolTip{Content=content,Tag=identity,PlacementTarget=control,IsOpen=open};ToolTipService.SetShowOnDisabled(control,true);
        System.Windows.Automation.AutomationProperties.SetHelpText(control,label+(keys.Length>0?" ("+keys+")":""));
    }
    public static double TextWidth(string text,FrameworkElement element,double size)=>new FormattedText(text,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface((FontFamily)element.GetValue(TextElement.FontFamilyProperty),FontStyles.Normal,FontWeights.Normal,FontStretches.Normal),size,(Brush)Application.Current.Resources["HomeTextBrush"],VisualTreeHelper.GetDpi(element).PixelsPerDip).WidthIncludingTrailingWhitespace;
}
