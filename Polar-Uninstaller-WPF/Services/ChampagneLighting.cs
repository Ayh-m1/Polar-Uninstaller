using System.Windows;
using System.Windows.Media;

namespace PolarUninstallerWpf.Services;

/// <summary>Shared frozen brushes using the same corner falloff and intensity
/// as the standard themes, without resize callbacks or bitmap regeneration.</summary>
public static class ChampagneLighting
{
    private static ThemePalette Palette => ThemeCatalog.All[ThemeCatalog.DefaultTheme];
    public static Brush Background {get;}=Freeze(new LinearGradientBrush(ColorOf(Palette.Background),ColorOf(Palette.SecondaryBackground!),90));
    public static Brush UpperLeft {get;}=Glow(Palette.Accent,new(0,0));
    public static Brush Center {get;}=Brushes.Transparent;
    public static Brush LowerRight {get;}=Glow(Palette.Accent,new(1,1));
    private static Color ColorOf(string color)=>(Color)ColorConverter.ConvertFromString(color);
    private static Brush Freeze(Brush brush){brush.Freeze();return brush;}
    private static Brush Glow(string hex,Point origin)
    {
        var color=ColorOf(hex);
        return Freeze(new RadialGradientBrush
        {
            MappingMode=BrushMappingMode.RelativeToBoundingBox,Center=origin,GradientOrigin=origin,
            RadiusX=.42,RadiusY=.42,
            GradientStops=new GradientStopCollection
            {
                new(Color.FromArgb(40,color.R,color.G,color.B),0),
                new(Color.FromArgb(20,color.R,color.G,color.B),.35),
                new(Color.FromArgb(0,color.R,color.G,color.B),1)
            }
        });
    }
}