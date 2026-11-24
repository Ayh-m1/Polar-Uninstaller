using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using PolarUninstallerWpf.Services;

namespace PolarUninstallerWpf;

public sealed record ThemeOption(string Id,string ArabicName,string Background,string Glow,string Card,string Border,string Accent,string Text,string Dim,string? Sidebar=null,string? Hover=null,string? SecondaryBackground=null,string? SecondaryAccent=null,string? AccentText=null);

public partial class ThemeGalleryView : UserControl
{
    private readonly IReadOnlyList<ThemeOption> themes;
    private readonly Dictionary<string,(Button Button,Border Check,TextBlock Title)> tiles=new();
    private bool arabic;
    private string originalTheme="";
    public string PendingTheme {get;private set;}="";
    public event Action<string>? ApplyRequested;
    public event Action? CancelRequested;
    private string L(string en,string ar)=> BidiText.Localize(arabic,en,ar);
    private static SolidColorBrush Brush(string hex)=>(SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

    public ThemeGalleryView(IReadOnlyList<ThemeOption> themes)
    {this.themes=themes;InitializeComponent();}

    public void Open(string current,bool isArabic)
    {
        originalTheme=current;
        bool rebuild=tiles.Count==0||arabic!=isArabic;
        arabic=isArabic;FlowDirection=arabic?FlowDirection.RightToLeft:FlowDirection.LeftToRight;
        Heading.Text=L("Appearance","المظهر");
        Subtitle.Text=L("Choose the appearance that suits you to personalize Polar Uninstaller.","اختر المظهر الذي يناسبك لتخصيص واجهة البرنامج.");
        CountText.Text=arabic?$"المظاهر المتاحة: {DisplayFormat.Isolate(themes.Count.ToString())}":$"{themes.Count} themes available";
        CancelButton.Content=L("Cancel","إلغاء");ApplyButton.Content=L("✓  Apply Theme","✓  تطبيق المظهر");
        ApplyButton.FlowDirection=SelectionText.FlowDirection=arabic?FlowDirection.RightToLeft:FlowDirection.LeftToRight;
        CloseButton.ToolTip=L("Close without applying","إغلاق دون تطبيق");
        if(rebuild)BuildCards();
        Select(current);
        GalleryScroll.ScrollToTop();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,new Action(()=>
        {if(tiles.TryGetValue(PendingTheme,out var tile))tile.Button.Focus();}));
    }
    private void BuildCards()
    {
        Cards.Children.Clear();tiles.Clear();
        // Responsive rows mirror naturally in Arabic, without fixed positions.
        foreach(var theme in themes)
        {
            var content=new Grid{FlowDirection=arabic?FlowDirection.RightToLeft:FlowDirection.LeftToRight};
            content.RowDefinitions.Add(new(){Height=GridLength.Auto});content.RowDefinitions.Add(new(){Height=GridLength.Auto});
            var miniature=new Border{CornerRadius=new(12),ClipToBounds=true,Child=Preview(theme),Margin=new(0,0,0,11)};
            content.Children.Add(miniature);
            var names=new Grid();names.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});names.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var labels=new StackPanel{Margin=new(4,0,12,1)};
            var englishTitle=new TextBlock{Text=theme.Id,FontFamily=(FontFamily)Application.Current.Resources["InterFont"],FontSize=16,TextWrapping=TextWrapping.Wrap,FontWeight=FontWeights.SemiBold,Foreground=Brush("#F3F0F4"),FlowDirection=FlowDirection.LeftToRight,HorizontalAlignment=arabic?HorizontalAlignment.Right:HorizontalAlignment.Left};
            labels.Children.Add(englishTitle);
            var arabicTitle=new TextBlock{Text=theme.ArabicName,FontFamily=(FontFamily)Application.Current.Resources["TajawalFont"],FontSize=14,FlowDirection=FlowDirection.RightToLeft,HorizontalAlignment=arabic?HorizontalAlignment.Right:HorizontalAlignment.Left,Margin=new(0,4,0,0)};
            arabicTitle.SetResourceReference(TextBlock.ForegroundProperty,"DialogDimBrush");labels.Children.Add(arabicTitle);
            names.Children.Add(labels);
            var dot=new Ellipse{Width=25,Height=25,Fill=Brush(theme.Id=="Pure Black"?"#030303":theme.Id=="Beige"?"#E5D1B4":theme.Id=="White"?"#F4F4F4":theme.Id=="Espresso"?theme.Glow:theme.Accent),Stroke=Brush("#BFFFFFFF"),StrokeThickness=1.5,VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,4,0)};
            Grid.SetColumn(dot,1);names.Children.Add(dot);Grid.SetRow(names,1);content.Children.Add(names);
            var check=new Border{Width=29,Height=29,CornerRadius=new(15),Background=Brush(theme.Accent),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Margin=new(5),Visibility=Visibility.Collapsed,Child=new TextBlock{Text="✓",FontWeight=FontWeights.Bold,FontSize=18,Foreground=Brush("#160D13"),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};
            content.Children.Add(check);
            ((TextBlock)check.Child).Foreground=theme.AccentText is string accentText?Brush(accentText):
                new SolidColorBrush((.299*Brush(theme.Accent).Color.R+.587*Brush(theme.Accent).Color.G+.114*Brush(theme.Accent).Color.B)/255>.62?Color.FromRgb(18,24,31):Colors.White);
            var button=new Button{Content=content,Style=(Style)Resources["GalleryCard"],ToolTip=theme.Id+" · "+theme.ArabicName};
            System.Windows.Automation.AutomationProperties.SetName(button,theme.Id+" · "+theme.ArabicName);
            button.Click+=(_,_)=>Select(theme.Id);tiles.Add(theme.Id,(button,check,englishTitle));Cards.Children.Add(button);
        }
    }
    private void Select(string id)
    {
        var selected=themes.FirstOrDefault(t=>t.Id==id)??themes[0];PendingTheme=selected.Id;
        DialogThemeResources.Apply(Resources,selected.Background,selected.Card,selected.Border,selected.Accent,selected.Text,selected.Dim,selected.AccentText);
        PanelFrame.Effect=selected.Id=="Pure Black"?null:new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=26,ShadowDepth=5,Opacity=.2};
        foreach(var (key,tile) in tiles)
        {
            bool active=key==PendingTheme;
            tile.Button.BorderBrush=(Brush)Resources[active?"DialogAccentBrush":"DialogBorderBrush"];
            tile.Button.BorderThickness=new Thickness(active?1.5:1);
            tile.Button.Background=(Brush)Resources[active?"DialogHoverBrush":"DialogCardBrush"];
            tile.Check.Visibility=active?Visibility.Visible:Visibility.Collapsed;
            tile.Title.Foreground=(Brush)Resources[active?"DialogAccentBrush":"DialogTextBrush"];
            System.Windows.Automation.AutomationProperties.SetHelpText(tile.Button,active?L("Selected, pending Apply","محدد بانتظار التطبيق"):L("Select this appearance","تحديد هذا المظهر"));
        }
        SelectionText.Text=L("Selected: ","المحدد: ")+(arabic?selected.ArabicName:selected.Id);
        ApplyButton.Background=Brush(selected.Accent);
        ApplyButton.BorderBrush=Brush(selected.Accent);
        ApplyButton.Foreground=(Brush)Resources["DialogAccentTextBrush"];
    }
    private Viewbox Preview(ThemeOption theme)
    {
        var canvas=new Canvas{Width=300,Height=162,Background=Brush(theme.Id=="Black"?"#050505":theme.Id=="Beige"?"#E5D1B4":theme.Background),ClipToBounds=true,Clip=new RectangleGeometry(new Rect(0,0,300,162),12,12),FlowDirection=FlowDirection.LeftToRight};
        if(theme.Id==ThemeCatalog.DefaultTheme)
        {
            canvas.Background=ChampagneLighting.Background;
            foreach(var lighting in new[]{ChampagneLighting.Center,ChampagneLighting.UpperLeft,ChampagneLighting.LowerRight})
                canvas.Children.Add(new Rectangle{Width=300,Height=162,Fill=lighting});
        }
        else if(theme.Id!="Pure Black")
        {
            var color=Brush(theme.Id=="Black"?"#FFFFFF":theme.Id=="Beige"?"#F4E8D7":theme.Id==ThemeCatalog.DefaultTheme?theme.Glow:theme.Accent).Color;
            canvas.Children.Add(new Rectangle{Width=300,Height=162,Fill=new RadialGradientBrush(new GradientStopCollection{new(Color.FromArgb((byte)(theme.Id==ThemeCatalog.DefaultTheme?24:46),color.R,color.G,color.B),0),new(Colors.Transparent,1)}){Center=new(0,0),GradientOrigin=new(0,0),RadiusX=.85,RadiusY=.85}});
        }
        double side=arabic?255:0,main=arabic?0:45;
        void Rect(double x,double y,double w,double h,string fill,string? stroke=null,double radius=3)
        {var rect=new Rectangle{Width=w,Height=h,RadiusX=radius,RadiusY=radius,Fill=Brush(fill),Stroke=stroke==null?null:Brush(stroke),StrokeThickness=.5};Canvas.SetLeft(rect,x);Canvas.SetTop(rect,y);canvas.Children.Add(rect);}
        void Text(string text,double x,double y,double size,string color,bool bold=false)
        {var label=new TextBlock{Text=text,FontSize=size,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,Foreground=Brush(color),FontFamily=(FontFamily)Application.Current.Resources[arabic?"TajawalFont":"InterFont"]};Canvas.SetLeft(label,x);Canvas.SetTop(label,y);canvas.Children.Add(label);}
        bool navy=theme.Id==ThemeCatalog.DefaultTheme;
        Rect(side,0,45,162,theme.Sidebar??(theme.Id=="Pure Black"?"#050505":theme.Card));
        Text("Polar",side+6,9,5,theme.Text,true);
        var logo=new Image{Width=15,Height=15,Source=BrandingAssets.ForTheme(theme.Id,preview:true),Stretch=Stretch.Uniform,FlowDirection=FlowDirection.LeftToRight};
        Canvas.SetLeft(logo,main+117);Canvas.SetTop(logo,3);canvas.Children.Add(logo);
        Rect(side+3,31,39,13,navy?theme.Accent:theme.Card,theme.Border);
        for(int i=0;i<6;i++)
        {
            string activeText=navy?theme.Sidebar!:theme.Text;
            var icon=SidebarIcons.Create(new[]{"home","uninstall","cleaner","dashboard","files","whatsnew"}[i],Brush(i==0?(navy?activeText:theme.Accent):theme.Dim),5);
            Canvas.SetLeft(icon,side+5);Canvas.SetTop(icon,35+i*14);canvas.Children.Add(icon);
            Rect(side+14,37+i*14,23,1.2,i==0?activeText:theme.Dim,null,0);
        }
        Text(arabic?"اختر قسمًا للبدء":"Choose a section to start",main+35,46,8,theme.Text,true);
        Text(arabic?"افحص البرامج وأزل بقاياها":"Scan, remove and clean up",main+35,58,4.5,theme.Dim);
        Rect(main+30,74,130,29,theme.Card,theme.Border,5);Rect(main+165,74,61,29,theme.Card,theme.Border,5);
        Rect(main+30,109,96,27,theme.Card,theme.Border,5);Rect(main+132,109,94,27,theme.Card,theme.Border,5);
        foreach(var (x,y) in new[]{(main+36,79d),(main+171,79d),(main+36,114d),(main+138,114d)})
        {Rect(x,y,12,12,theme.Card,theme.Accent,3);Rect(x+17,y+3,32,2,theme.Text);Rect(x+17,y+8,24,1,theme.Dim);}
        Rect(main+12,147,230,11,theme.Card,theme.Border,3);Rect(main+18,151,40,1.5,theme.Accent);
        return new Viewbox{Child=canvas,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.Both};
    }
    internal void RestorePreview()=>Select(originalTheme);
    private void CancelClick(object sender,RoutedEventArgs e){RestorePreview();CancelRequested?.Invoke();}
    private void ApplyClick(object sender,RoutedEventArgs e)=>ApplyRequested?.Invoke(PendingTheme);
    private void BackdropClick(object sender,MouseButtonEventArgs e){if(ReferenceEquals(e.OriginalSource,Backdrop)){RestorePreview();CancelRequested?.Invoke();}}
    private void GalleryKeyDown(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.Escape){RestorePreview();CancelRequested?.Invoke();e.Handled=true;return;}
        var direction=e.Key switch{Key.Left=>FocusNavigationDirection.Left,Key.Right=>FocusNavigationDirection.Right,Key.Up=>FocusNavigationDirection.Up,Key.Down=>FocusNavigationDirection.Down,_=>(FocusNavigationDirection?)null};
        if(direction.HasValue&&Keyboard.FocusedElement is UIElement focused){focused.MoveFocus(new(direction.Value));e.Handled=true;}
    }
}

/// <summary>Responsive native layout with centered incomplete rows. No resize
/// callbacks, fixed screen positions, or UI reconstruction while dragging.</summary>
public sealed class ThemeGalleryPanel : Panel
{
    private const double Gap=16;
    private int columns=1;private double width;private double[] heights=Array.Empty<double>();
    protected override Size MeasureOverride(Size available)
    {
        double total=double.IsInfinity(available.Width)?1080:Math.Max(1,available.Width);
        columns=Math.Clamp((int)((total+Gap)/230),1,4);width=Math.Max(1,(total-(columns-1)*Gap)/columns);
        heights=new double[(Children.Count+columns-1)/columns];
        for(int i=0;i<Children.Count;i++){Children[i].Measure(new(width,double.PositiveInfinity));heights[i/columns]=Math.Max(heights[i/columns],Children[i].DesiredSize.Height);}
        return new(total,heights.Sum()+Math.Max(0,heights.Length-1)*Gap);
    }
    protected override Size ArrangeOverride(Size final)
    {
        double y=0;
        for(int row=0;row<heights.Length;row++)
        {int count=Math.Min(columns,Children.Count-row*columns);double x=(final.Width-count*width-(count-1)*Gap)/2;
            for(int col=0;col<count;col++){Children[row*columns+col].Arrange(new Rect(x,y,width,heights[row]));x+=width+Gap;}y+=heights[row]+Gap;}
        return final;
    }
}
