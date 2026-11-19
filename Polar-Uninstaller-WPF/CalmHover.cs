using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using PolarUninstallerWpf.Services;
namespace PolarUninstallerWpf;

/// <summary>One lifecycle for shared button overlays. No color clocks, layout motion, idle timers, or global strong UI references.</summary>
public static class CalmHover
{
    public static readonly DependencyProperty EnabledProperty=DependencyProperty.RegisterAttached("Enabled",typeof(bool),typeof(CalmHover),new PropertyMetadata(false,EnabledChanged));
    public static readonly DependencyProperty NavProperty=DependencyProperty.RegisterAttached("Nav",typeof(bool),typeof(CalmHover),new PropertyMetadata(false));
    public static readonly DependencyProperty KindProperty=DependencyProperty.RegisterAttached("Kind",typeof(string),typeof(CalmHover),new PropertyMetadata(""));
    public static readonly DependencyProperty RadiusProperty=DependencyProperty.RegisterAttached("Radius",typeof(CornerRadius),typeof(CalmHover),new PropertyMetadata(new CornerRadius(8)));
    private static readonly DependencyProperty StateProperty=DependencyProperty.RegisterAttached("State",typeof(State),typeof(CalmHover));
    public static bool GetEnabled(DependencyObject o)=>(bool)o.GetValue(EnabledProperty);public static void SetEnabled(DependencyObject o,bool v)=>o.SetValue(EnabledProperty,v);
    public static bool GetNav(DependencyObject o)=>(bool)o.GetValue(NavProperty);public static void SetNav(DependencyObject o,bool v)=>o.SetValue(NavProperty,v);
    public static string GetKind(DependencyObject o)=>(string)o.GetValue(KindProperty);public static void SetKind(DependencyObject o,string v)=>o.SetValue(KindProperty,v);
    public static CornerRadius GetRadius(DependencyObject o)=>(CornerRadius)o.GetValue(RadiusProperty);public static void SetRadius(DependencyObject o,CornerRadius v)=>o.SetValue(RadiusProperty,v);
    private static void EnabledChanged(DependencyObject o,DependencyPropertyChangedEventArgs e)
    {
        if(o is not ButtonBase button)return;
        if(button.GetValue(StateProperty) is State old)old.Detach();
        if((bool)e.NewValue){var state=new State(button);button.SetValue(StateProperty,state);state.Attach();}
    }
    public static FrameworkElement WrapIcon(FrameworkElement original)
    {
        var wrapper=new Grid{Width=original.Width,Height=original.Height,VerticalAlignment=original.VerticalAlignment,HorizontalAlignment=original.HorizontalAlignment,RenderTransformOrigin=new(.5,.5),RenderTransform=new ScaleTransform(1,1),Tag="CalmIcon"};
        // An opacity mask preserves the exact existing glyph/geometry and theme-driven original.
        var overlay=new Rectangle{Opacity=0,IsHitTestVisible=false,OpacityMask=new VisualBrush(original){Stretch=Stretch.Fill}};
        wrapper.Children.Add(original);wrapper.Children.Add(overlay);return wrapper;
    }
    private sealed class State
    {
        private readonly ButtonBase button;
        private Window? window;private bool attached,live,pressed;
        private Grid? shell,icon;private Border? hover,light;private Rectangle? iconOverlay;
        private ScaleTransform? scale;private TranslateTransform? slide,drift;private ScaleTransform? iconScale;
        public State(ButtonBase button)=>this.button=button;
        public void Attach(){if(attached)return;attached=true;button.Loaded+=Loaded;button.Unloaded+=Unloaded;if(button.IsLoaded)Start();}
        public void Detach(){Stop();button.Loaded-=Loaded;button.Unloaded-=Unloaded;attached=false;}
        private void Loaded(object s,RoutedEventArgs e)=>Start();
        private void Unloaded(object s,RoutedEventArgs e)=>Stop();
        private void Start()
        {
            if(live)return;live=true;button.ApplyTemplate();
            shell=button.Template.FindName("CalmRoot",button) as Grid;hover=button.Template.FindName("CalmOverlay",button) as Border;light=button.Template.FindName("CalmLight",button) as Border;
            if(shell?.RenderTransform is TransformGroup templateGroup){var group=templateGroup.Clone();shell.RenderTransform=group;scale=group.Children.OfType<ScaleTransform>().FirstOrDefault();slide=group.Children.OfType<TranslateTransform>().FirstOrDefault();}
            if(light!=null){drift=new TranslateTransform();light.RenderTransform=drift;}
            icon=FindIcon(button.Content as DependencyObject);iconScale=icon?.RenderTransform as ScaleTransform;iconOverlay=icon?.Children.OfType<Rectangle>().LastOrDefault();
            button.MouseEnter+=Mouse;button.MouseLeave+=Mouse;button.IsEnabledChanged+=Status;button.IsVisibleChanged+=Status;
            button.PreviewMouseLeftButtonDown+=Down;button.PreviewMouseLeftButtonUp+=Up;button.LostMouseCapture+=Capture;
            button.PreviewKeyDown+=KeyDown;button.PreviewKeyUp+=KeyUp;button.LostKeyboardFocus+=FocusLost;
            window=Window.GetWindow(button);if(window!=null){window.Deactivated+=WindowChange;window.Activated+=WindowChange;window.StateChanged+=WindowChange;}
            SystemParameters.StaticPropertyChanged+=Parameters;CalmThemeBrushes.Changed+=ThemeChanged;Update();
        }
        private static Grid? FindIcon(DependencyObject? root)
        {
            if(root==null)return null;if(root is Grid {Tag:"CalmIcon"} found)return found;
            if(root is Panel panel)foreach(UIElement child in panel.Children)if(FindIcon(child) is{} match)return match;return null;
        }
        private void Stop()
        {
            if(!live)return;live=false;Render(false,false,false,false,true);
            button.MouseEnter-=Mouse;button.MouseLeave-=Mouse;button.IsEnabledChanged-=Status;button.IsVisibleChanged-=Status;
            button.PreviewMouseLeftButtonDown-=Down;button.PreviewMouseLeftButtonUp-=Up;button.LostMouseCapture-=Capture;
            button.PreviewKeyDown-=KeyDown;button.PreviewKeyUp-=KeyUp;button.LostKeyboardFocus-=FocusLost;
            if(window!=null){window.Deactivated-=WindowChange;window.Activated-=WindowChange;window.StateChanged-=WindowChange;}window=null;
            SystemParameters.StaticPropertyChanged-=Parameters;CalmThemeBrushes.Changed-=ThemeChanged;
        }
        private void Mouse(object s,MouseEventArgs e){pressed=button.IsPressed;Update();}
        private void Status(object s,DependencyPropertyChangedEventArgs e)=>Update();
        private void Down(object s,MouseButtonEventArgs e){pressed=true;Update();}
        private void Up(object s,MouseButtonEventArgs e){pressed=false;Update();}
        private void Capture(object s,MouseEventArgs e){pressed=false;Update();}
        private void KeyDown(object s,KeyEventArgs e){if(e.Key is Key.Space or Key.Enter){pressed=true;Update();}}
        private void KeyUp(object s,KeyEventArgs e){if(e.Key is Key.Space or Key.Enter){pressed=false;Update();}}
        private void FocusLost(object s,KeyboardFocusChangedEventArgs e){pressed=false;Update();}
        private void WindowChange(object? s,EventArgs e)=>Update();
        private void Parameters(object? s,PropertyChangedEventArgs e)=>Update();
        private void ThemeChanged()=>Update();
        private void Update()=>Render(button.IsMouseOver,pressed,button.IsKeyboardFocusWithin,window==null||window.IsActive&&window.WindowState!=WindowState.Minimized,(!SystemParameters.ClientAreaAnimation||SystemParameters.IsRemoteSession));
        private static void Animate(Animatable? target,DependencyProperty property,double value,double seconds,bool instant)
        {
            if(target==null)return;
            if(instant){target.BeginAnimation(property,null);target.SetValue(property,value);return;}
            var animation=new DoubleAnimation(value,TimeSpan.FromSeconds(seconds)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseInOut},FillBehavior=FillBehavior.HoldEnd};
            target.BeginAnimation(property,animation,HandoffBehavior.SnapshotAndReplace);
        }
        private static void Fade(UIElement? element,double value,double seconds,bool instant)
        {
            if(element==null)return;
            if(instant){element.BeginAnimation(UIElement.OpacityProperty,null);element.Opacity=value;}
            else element.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(value,TimeSpan.FromSeconds(seconds)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseInOut}});
        }
        private bool Same(Brush? brush,string key)=>brush is SolidColorBrush a&&button.TryFindResource(key) is SolidColorBrush b&&a.Color==b.Color;
        // Explicit booleans also allow deterministic state probes without sending input to a user's desktop.
        private void Render(bool over,bool down,bool focus,bool active,bool instant)
        {
            bool valid=button.IsEnabled&&button.IsVisible&&active,nav=GetNav(button),selected=nav&&string.Equals(button.Tag?.ToString(),"True",StringComparison.OrdinalIgnoreCase);
            bool on=valid&&over;instant|=!valid;
            string kind=GetKind(button);bool danger=kind=="Danger"||Same(button.Background,"ExplorerDangerBrush"),primary=!nav&&(kind=="Primary"||Same(button.Background,"ChromeAccent")||Same(button.Background,"AccentButtonBackground"));
            string prefix=danger?"CalmDanger":primary?"CalmPrimary":selected?"CalmSelected":nav?"CalmNav":"Calm";
            hover?.SetResourceReference(Border.BackgroundProperty,prefix+"HoverFill");hover?.SetResourceReference(Border.BorderBrushProperty,prefix+"HoverBorder");
            iconOverlay?.SetResourceReference(Shape.FillProperty,selected?"CalmSelectedHoverIcon":"CalmHoverIcon");
            Fade(hover,on?1:0,.5,instant);
            Animate(slide,TranslateTransform.XProperty,on&&nav&&!instant?(button.FlowDirection==FlowDirection.RightToLeft?-2:2):0,.5,instant);
            Animate(scale,ScaleTransform.ScaleXProperty,valid&&down&&nav&&!instant?.985:1,.1,instant);
            Animate(scale,ScaleTransform.ScaleYProperty,valid&&down&&nav&&!instant?.985:1,.1,instant);
            Animate(iconScale,ScaleTransform.ScaleXProperty,on&&nav&&!instant?1.06:1,.5,instant);
            Animate(iconScale,ScaleTransform.ScaleYProperty,on&&nav&&!instant?1.06:1,.5,instant);
            Fade(iconOverlay,on&&nav?1:0,.5,instant);
            // A selected cream surface already carries its accent; keep its text contrast intact.
            Fade(light,on&&nav?1:0,.6,instant);
            if(drift!=null)
            {
                drift.BeginAnimation(TranslateTransform.XProperty,null);drift.X=0;
                if(on&&nav&&!selected&&!instant)drift.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(-15,15,TimeSpan.FromSeconds(3)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut}});
            }
        }
    }
}
