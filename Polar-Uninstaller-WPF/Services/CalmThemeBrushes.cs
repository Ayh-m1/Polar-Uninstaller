using System.Windows;
using System.Windows.Media;
namespace PolarUninstallerWpf.Services;

/// <summary>Derived centrally for every palette, including future catalog entries.</summary>
public static class CalmThemeBrushes
{
    public static event Action? Changed;
    public static Color Mix(Color a,Color b,double t)=>Color.FromRgb((byte)Math.Round(a.R+(b.R-a.R)*t),(byte)Math.Round(a.G+(b.G-a.G)*t),(byte)Math.Round(a.B+(b.B-a.B)*t));
    private static double Luminance(Color c){double F(byte b){double x=b/255d;return x<=.04045?x/12.92:Math.Pow((x+.055)/1.055,2.4);}return .2126*F(c.R)+.7152*F(c.G)+.0722*F(c.B);}
    public static double Contrast(Color a,Color b){double x=Luminance(a),y=Luminance(b);return (Math.Max(x,y)+.05)/(Math.Min(x,y)+.05);}
    private static Color Read(ResourceDictionary r,string key,Color fallback)=>r[key] is SolidColorBrush brush?brush.Color:fallback;
    private static Color Legible(Color candidate,Color surface,Color text,double ratio)
    {
        for(int i=0;i<=100;i++){var color=Mix(candidate,text,i/100d);if(Contrast(color,surface)>=ratio)return color;}return text;
    }
    public static void Apply(ResourceDictionary r,ThemePalette palette)
    {
        Color C(string value)=>(Color)ColorConverter.ConvertFromString(value);
        var accent=C(palette.Accent);var surface=C(palette.Card);var text=C(palette.Text);var dim=C(palette.Dim);
        var selected=Read(r,"SidebarSelected",surface);var activeText=Read(r,"SidebarActiveText",text);
        Color Fill(Color background,Color foreground,Color secondary)
        {
            for(int i=14;i>=0;i--){var color=Mix(background,accent,i/100d);if(Contrast(color,foreground)>=4.5&&Contrast(color,secondary)>=4.5)return color;}return background;
        }
        void Put(string key,Color color){var brush=new SolidColorBrush(color);brush.Freeze();r[key]=brush;}
        var fill=Fill(surface,text,dim);
        var nav=Fill(C(palette.Sidebar??palette.Background),text,dim);
        var selectedFill=Fill(selected,activeText,activeText);
        Put("CalmHoverFill",fill);Put("CalmHoverBorder",Legible(Mix(surface,accent,.5),fill,text,3));
        Put("CalmNavHoverFill",nav);Put("CalmNavHoverBorder",Legible(Mix(nav,accent,.5),nav,text,3));
        Put("CalmSelectedHoverFill",selectedFill);Put("CalmSelectedHoverBorder",Legible(accent,selectedFill,activeText,3));
        Put("CalmHoverIcon",Legible(accent,nav,text,3));Put("CalmSelectedHoverIcon",Legible(accent,selectedFill,activeText,3));
        Put("CalmFocusBrush",Legible(accent,C(palette.Background),text,3));
        var primary=Read(r,"AccentButtonBackground",accent);var primaryText=Read(r,"ChromeAccentText",text);
        // Brighten only as far as the existing primary text contrast permits.
        var bright=Mix(primary,C("#FFFFFF"),.08);if(Contrast(bright,primaryText)<4.5)bright=primary;
        if(Contrast(bright,primaryText)<4.5)for(int i=1;i<=100;i++){bright=Mix(primary,C(palette.Background),i/100d);if(Contrast(bright,primaryText)>=4.5)break;}
        Put("CalmPrimaryHoverFill",bright);Put("CalmPrimaryHoverBorder",Legible(primaryText,bright,primaryText,3));
        var danger=C("#B3261E");var dangerText=C("#FFFFFF");var dangerHover=Mix(danger,dangerText,.08);
        if(Contrast(dangerHover,dangerText)<4.5)dangerHover=danger;
        Put("CalmDangerHoverFill",dangerHover);Put("CalmDangerHoverBorder",Legible(dangerText,dangerHover,dangerText,3));
        var secondaryAccent=C(palette.SecondaryAccent??palette.Glow);
        byte lightAlpha=51;
        while(lightAlpha>0&&new[]{accent,secondaryAccent}.Any(c=>Contrast(dim,Mix(nav,c,lightAlpha/255d))<4.5||Contrast(text,Mix(nav,c,lightAlpha/255d))<4.5||Contrast(Legible(accent,nav,text,3),Mix(nav,c,lightAlpha/255d))<3))lightAlpha--;
        var light=new LinearGradientBrush(new GradientStopCollection{new(Color.FromArgb(0,accent.R,accent.G,accent.B),0),new(Color.FromArgb(lightAlpha,accent.R,accent.G,accent.B),.35),new(Color.FromArgb(lightAlpha,secondaryAccent.R,secondaryAccent.G,secondaryAccent.B),.65),new(Color.FromArgb(0,secondaryAccent.R,secondaryAccent.G,secondaryAccent.B),1)},new Point(0,0),new Point(1,0));light.Freeze();r["CalmGlowBrush"]=light;
        Changed?.Invoke();
    }
}
