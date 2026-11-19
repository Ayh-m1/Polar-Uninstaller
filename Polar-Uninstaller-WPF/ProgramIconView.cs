using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using PolarUninstallerWpf.Services;
namespace PolarUninstallerWpf;

public sealed class ProgramIconView : Grid
{
    private readonly Border badge=new(){CornerRadius=new(6)};
    private readonly Border tint=new(){CornerRadius=new(6)};
    private readonly TextBlock letter=new(){FontSize=15,FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,FlowDirection=FlowDirection.LeftToRight};
    private readonly Viewbox component=CreateComponentGlyph();
    private readonly Image image=new(){Stretch=Stretch.Uniform,SnapsToDevicePixels=true,Visibility=Visibility.Collapsed,Margin=new Thickness(4)};
    private CancellationTokenSource? cancellation;
    public ProgramIconView()
    {
        SnapsToDevicePixels=true;UseLayoutRounding=true;
        badge.SetResourceReference(Border.BackgroundProperty,"HomeCardBrush");tint.SetResourceReference(Border.BackgroundProperty,"HomeAccentBrush");letter.SetResourceReference(TextBlock.ForegroundProperty,"HomeTextBrush");
        badge.CornerRadius=tint.CornerRadius=new(7);
        Children.Add(badge);Children.Add(tint);Children.Add(letter);Children.Add(component);Children.Add(image);
        Loaded+=(_,_)=>Start();Unloaded+=(_,_)=>Cancel();DataContextChanged+=(_,_)=>{Cancel();image.Source=null;if(IsLoaded)Start();};
    }
    protected override void OnDpiChanged(DpiScale oldDpi,DpiScale newDpi){base.OnDpiChanged(oldDpi,newDpi);Cancel();if(IsLoaded)Start();}
    private void Cancel(){cancellation?.Cancel();cancellation?.Dispose();cancellation=null;image.BeginAnimation(OpacityProperty,null);}
    private void ShowFallback(bool neutralComponent)
    {
        image.Source=null;image.Visibility=Visibility.Collapsed;
        badge.Visibility=tint.Visibility=Visibility.Visible;
        letter.Visibility=neutralComponent?Visibility.Collapsed:Visibility.Visible;
        component.Visibility=neutralComponent?Visibility.Visible:Visibility.Collapsed;
    }
    private void Start()=>_=SafeTasks.Run(StartAsync,"Program icon row");
    private async Task StartAsync()
    {
        Cancel();if(DataContext is not ProgramCard card)return;
        var program=card.Program;
        letter.Text=IconBadgeService.Letter(card.Name);tint.Opacity=IconBadgeService.Tint(card.Name);
        ShowFallback(ComponentIconPolicy.UseNeutral(program,null));
        cancellation=new();var token=cancellation.Token;
        try
        {
            int pixels=(int)Math.Ceiling(ActualWidth*VisualTreeHelper.GetDpi(this).DpiScaleX);
            var resolved=await ApplicationIconResolver.ResolveAsync(program,Math.Max(32,pixels),token,IsVisible);
            if(token.IsCancellationRequested||!IsLoaded||DataContext!=card)return;
            if(resolved==null){await Task.Delay(TimeSpan.FromSeconds(61),token);if(IsLoaded&&DataContext==card)Start();return;}
            // A runtime/component keeps the neutral app-drawn icon when the only
            // picture is a generic installer/redistributable image. Real vendor
            // logos are never replaced.
            if(resolved.Method!=IconResolutionMethod.Legacy&&ComponentIconPolicy.UseNeutral(program,resolved)){ShowFallback(true);return;}
            image.Source=resolved.Image;image.Visibility=Visibility.Visible;
            badge.Visibility=tint.Visibility=letter.Visibility=component.Visibility=Visibility.Collapsed;
            if(SystemParameters.ClientAreaAnimation&&!SystemParameters.IsRemoteSession)image.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(120)){FillBehavior=FillBehavior.Stop});
        }
        catch(OperationCanceledException){ }
        catch(Exception e)when(e is System.IO.IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException){CrashDiagnostics.Record("Application icon",e);}
    }

    /// <summary>A calm, theme-aware chip glyph used only for runtimes/components without a real logo.</summary>
    private static Viewbox CreateComponentGlyph()
    {
        var canvas=new Canvas{Width=24,Height=24};
        var body=new Rectangle{Width=10,Height=10,RadiusX=2.6,RadiusY=2.6,StrokeThickness=1.5,Opacity=.95};
        body.SetResourceReference(Shape.StrokeProperty,"HomeAccentBrush");body.SetResourceReference(Shape.FillProperty,"HomeCardBrush");
        Canvas.SetLeft(body,7);Canvas.SetTop(body,7);canvas.Children.Add(body);
        var core=new Ellipse{Width=3.4,Height=3.4,Opacity=.9};
        core.SetResourceReference(Shape.FillProperty,"HomeAccentBrush");
        Canvas.SetLeft(core,10.3);Canvas.SetTop(core,10.3);canvas.Children.Add(core);
        foreach(double offset in new[]{9.1,11.3,13.5})
        {
            AddPin(canvas,offset,3.2,1.4,3.8);
            AddPin(canvas,offset,17.0,1.4,3.8);
            AddPin(canvas,3.2,offset,3.8,1.4);
            AddPin(canvas,17.0,offset,3.8,1.4);
        }
        return new Viewbox{Child=canvas,Stretch=Stretch.Uniform,Margin=new Thickness(6),IsHitTestVisible=false,Visibility=Visibility.Collapsed};
    }
    private static void AddPin(Canvas canvas,double left,double top,double width,double height)
    {
        var pin=new Rectangle{Width=width,Height=height,RadiusX=.6,RadiusY=.6,Opacity=.7};
        pin.SetResourceReference(Shape.FillProperty,"HomeAccentBrush");
        Canvas.SetLeft(pin,left);Canvas.SetTop(pin,top);canvas.Children.Add(pin);
    }
}
