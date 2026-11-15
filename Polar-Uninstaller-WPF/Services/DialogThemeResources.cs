using System.Windows;
using System.Windows.Media;

namespace PolarUninstallerWpf.Services;

/// <summary>Shared translucent content surfaces. Gallery previews use a local
/// dictionary so pending selections never mutate the saved/application theme.</summary>
internal static class DialogThemeResources
{
    internal static void Apply(ResourceDictionary resources, string background, string card,
        string border, string accent, string text, string dim)
    {
        static Color ColorOf(string value)=>(Color)ColorConverter.ConvertFromString(value);
        static Color Alpha(Color color,byte alpha)=>Color.FromArgb(alpha,color.R,color.G,color.B);
        static Color Mix(Color a,Color b,double amount)=>Color.FromRgb(
            (byte)(a.R+(b.R-a.R)*amount),(byte)(a.G+(b.G-a.G)*amount),(byte)(a.B+(b.B-a.B)*amount));
        void Put(string key,Color value)
        {
            if(resources[key] is SolidColorBrush old&&!old.IsFrozen)old.Color=value;
            else resources[key]=new SolidColorBrush(value);
        }
        var bg=ColorOf(background);var surface=ColorOf(card);var edge=ColorOf(border);var highlight=ColorOf(accent);
        resources["DialogSurfaceBrush"]=new LinearGradientBrush(Alpha(surface,237),Alpha(bg,240),45);
        Put("DialogBackdropBrush",Alpha(bg,153));
        Put("DialogCardBrush",Alpha(surface,160));
        Put("DialogHoverBrush",Alpha(Mix(surface,highlight,.10),210));
        Put("DialogBorderBrush",Alpha(edge,180));
        Put("DialogTextBrush",ColorOf(text));Put("DialogDimBrush",ColorOf(dim));
        Put("DialogAccentBrush",highlight);
        Put("DialogAccentTextBrush",(.299*highlight.R+.587*highlight.G+.114*highlight.B)/255>.62
            ?Color.FromRgb(18,24,31):Colors.White);
    }
}
