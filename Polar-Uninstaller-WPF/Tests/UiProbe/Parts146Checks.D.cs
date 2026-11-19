using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using PolarUninstallerWpf;
using PolarUninstallerWpf.Services;
internal static partial class Parts146Checks
{
    private static Color ColorAt(string key)=>((SolidColorBrush)app.Resources[key]).Color;
    private static T Part<T>(Button b,string name)where T:DependencyObject=>(T)b.Template.FindName(name,b);
    private static object HoverState(Button button)
    {
        var property=(DependencyProperty)typeof(CalmHover).GetField("StateProperty",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;return button.GetValue(property);
    }
    private static void Hover(Button b,bool on,bool press=false,bool active=true,bool instant=true)=>Invoke(HoverState(b),"Render",on,press,false,active,instant);
    private static void Advance(double seconds){var clock=System.Diagnostics.Stopwatch.StartNew();while(clock.Elapsed.TotalSeconds<seconds){Pump();Thread.Sleep(3);}}
    internal static int RunD()
    {
        Setup("D");var report=new List<string>{"| Theme | Contrast OK? | Minimum text | Icon | Border |","|---|---|---:|---:|---:|"};
        var panel=new StackPanel{Margin=new(12)};var window=new Window{Left=-20000,Top=-20000,Width=380,Height=370,Content=panel,ShowInTaskbar=false};
        var nav=new Button{Style=(Style)app.Resources["SidebarNavButton"],Content=new StackPanel{Orientation=Orientation.Horizontal},Height=48,Width=290,ToolTip="Fixture navigation"};
        var icon=SidebarIcons.Create("home",(Brush)app.Resources["HomeDimBrush"],20);((StackPanel)nav.Content).Children.Add(CalmHover.WrapIcon(icon));((StackPanel)nav.Content).Children.Add(new TextBlock{Text="Fixture navigation",Margin=new(10,0,0,0),VerticalAlignment=VerticalAlignment.Center});
        nav.SetResourceReference(Control.ForegroundProperty,"HomeDimBrush");
        var standard=new Button{Style=(Style)app.Resources["ChromeButton"],Content="Toolbar / dialog",Height=40,Margin=new(0,8,0,0)};
        var primary=new Button{Style=(Style)app.Resources["ChromeButton"],Content="Primary",Height=40,Margin=new(0,8,0,0)};CalmHover.SetKind(primary,"Primary");primary.SetResourceReference(Control.BackgroundProperty,"AccentButtonBackground");primary.SetResourceReference(Control.ForegroundProperty,"ChromeAccentText");
        var danger=new Button{Style=(Style)app.Resources["ChromeButton"],Content="Danger",Height=40,Margin=new(0,8,0,0),Foreground=Brushes.White};CalmHover.SetKind(danger,"Danger");
        panel.Children.Add(nav);panel.Children.Add(standard);panel.Children.Add(primary);panel.Children.Add(danger);window.Show();Pump();
        foreach(string theme in ThemeCatalog.All.Keys)
        {
            ((ComboBox)main.FindName("ThemePicker")).SelectedItem=theme;Pump();panel.Background=(Brush)app.Resources["ChromeBackground"];
            double textRatio=Math.Min(CalmThemeBrushes.Contrast(ColorAt("HomeTextBrush"),ColorAt("CalmHoverFill")),CalmThemeBrushes.Contrast(ColorAt("HomeDimBrush"),ColorAt("CalmHoverFill")));
            var glow=(LinearGradientBrush)app.Resources["CalmGlowBrush"];var navFill=ColorAt("CalmNavHoverFill");
            double glowText=glow.GradientStops.Min(s=>CalmThemeBrushes.Contrast(ColorAt("HomeDimBrush"),CalmThemeBrushes.Mix(navFill,s.Color,s.Color.A/255d)));
            double icons=glow.GradientStops.Min(s=>CalmThemeBrushes.Contrast(ColorAt("CalmHoverIcon"),CalmThemeBrushes.Mix(navFill,s.Color,s.Color.A/255d)));
            double borders=CalmThemeBrushes.Contrast(ColorAt("CalmHoverBorder"),ColorAt("CalmHoverFill"));
            bool contrast=textRatio>=4.5&&glowText>=4.5&&icons>=3&&borders>=3;Check(contrast,theme+" measured hover text/icon/border contrast");report.Add($"| {theme} | {(contrast?"Yes":"NO")} | {Math.Min(textRatio,glowText):F2} | {icons:F2} | {borders:F2} |");
            Hover(nav,true);Check(Part<Border>(nav,"CalmOverlay").Opacity==1&&Part<Border>(nav,"CalmLight").Opacity==1,theme+" hover colors/light applied instantly in reduced-motion state");
            Hover(nav,false);Check(Part<Border>(nav,"CalmOverlay").Opacity==0&&!((TranslateTransform)Part<Border>(nav,"CalmLight").RenderTransform).HasAnimatedProperties,theme+" leave stops drift");
            foreach(var direction in new[]{FlowDirection.LeftToRight,FlowDirection.RightToLeft})
            {
                nav.FlowDirection=direction;Hover(nav,true,true,true,false);Advance(.65);
                var transforms=(TransformGroup)Part<Grid>(nav,"CalmRoot").RenderTransform;
                Check(Math.Abs(transforms.Children.OfType<TranslateTransform>().Single().X-(direction==FlowDirection.RightToLeft?-2:2))<.01&&Math.Abs(transforms.Children.OfType<ScaleTransform>().Single().ScaleX-.985)<.001,theme+" 2px inline slide and .985 press "+direction);
            }
            Check(Math.Abs(((ScaleTransform)((Grid)((StackPanel)nav.Content).Children[0]).RenderTransform).ScaleX-1.06)<.001,theme+" navigation icon scales to 1.06");
            nav.Tag="True";nav.SetResourceReference(Control.ForegroundProperty,"SidebarActiveText");Hover(nav,true);
            Check(((SolidColorBrush)Part<Border>(nav,"Surface").Background).Color==ColorAt("SidebarSelected")&&CalmThemeBrushes.Contrast(ColorAt("SidebarActiveText"),ColorAt("CalmSelectedHoverFill"))>=4.5,theme+" selected surface/text preserved with hover");
            nav.Tag=null;nav.SetResourceReference(Control.ForegroundProperty,"HomeDimBrush");Hover(nav,true,false,true,false);nav.IsEnabled=false;Pump();
            Check(Part<Border>(nav,"CalmOverlay").Opacity==0&&!((TranslateTransform)Part<Border>(nav,"CalmLight").RenderTransform).HasAnimatedProperties,theme+" disabled clears hover/drift");nav.IsEnabled=true;
            Hover(nav,true,false,true,false);Hover(nav,true,false,false);Check(Part<Border>(nav,"CalmLight").Opacity==0&&!((TranslateTransform)Part<Border>(nav,"CalmLight").RenderTransform).HasAnimatedProperties,theme+" inactive/minimized clears drift");
            Hover(primary,true);Hover(danger,true);Check(((SolidColorBrush)Part<Border>(danger,"CalmOverlay").Background).Color==ColorAt("CalmDangerHoverFill")&&((SolidColorBrush)Part<Border>(primary,"CalmOverlay").Background).Color==ColorAt("CalmPrimaryHoverFill"),theme+" primary and danger use their own hover fills");
            Check(CalmThemeBrushes.Contrast(ColorAt("ChromeAccentText"),ColorAt("CalmPrimaryHoverFill"))>=4.5&&CalmThemeBrushes.Contrast(Colors.White,ColorAt("CalmDangerHoverFill"))>=4.5,theme+" primary/destructive text contrast");
            Hover(nav,true);((ComboBox)main.FindName("ThemePicker")).SelectedItem=theme=="White"?"Black":"White";Pump();Hover(nav,true);
            Check(((SolidColorBrush)Part<Border>(nav,"CalmOverlay").Background).Color==ColorAt("CalmNavHoverFill"),theme+" theme change updates active overlay without color clocks");
            ((ComboBox)main.FindName("ThemePicker")).SelectedItem=theme;Pump();Hover(nav,true);Hover(standard,true);Hover(primary,true);Hover(danger,true);panel.Measure(new Size(340,340));panel.Arrange(new Rect(0,0,340,340));panel.UpdateLayout();
            var bitmap=new RenderTargetBitmap(340,340,96,96,PixelFormats.Pbgra32);bitmap.Render(panel);using var stream=File.Create(System.IO.Path.Combine(root,theme+".png"));var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));encoder.Save(stream);
        }
        var focusTriggers=nav.Template.Triggers.OfType<Trigger>().Where(t=>t.Property==UIElement.IsKeyboardFocusWithinProperty).ToArray();Check(focusTriggers.Any(t=>t.Setters.OfType<Setter>().Any(s=>s.TargetName=="CalmFocus")),"Shared template provides keyboard focus outline");
        Hover(nav,true,false,true,false);nav.Visibility=Visibility.Collapsed;Pump();Check(Part<Border>(nav,"CalmLight").Opacity==0,"Invisible navigation stops drift");nav.Visibility=Visibility.Visible;
        Hover(nav,true,false,true,false);window.Close();Pump();Check(!((TranslateTransform)Part<Border>(nav,"CalmLight").RenderTransform).HasAnimatedProperties,"Unload removes drift and lifecycle subscriptions");
        File.WriteAllLines(System.IO.Path.Combine(root,"contrast.md"),report);return Finish("D");
    }
}
