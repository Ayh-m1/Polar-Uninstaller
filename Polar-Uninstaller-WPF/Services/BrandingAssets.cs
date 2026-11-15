using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PolarUninstallerWpf.Services;

public static class BrandingAssets
{
    private static readonly Dictionary<string,ImageSource> Cache=new();
    public static ImageSource ForTheme(string theme,bool small=false,bool preview=false)
    {
        string color=theme is "White" or "Beige"?"black":"white";
        string name=preview?$"PU_{color}_flat_24.png":small?$"PU_{color}_flat_64.png":$"PU_{color}_shadow_128.png";
        if(Cache.TryGetValue(name,out var cached))return cached;
        var image=new BitmapImage(new Uri("pack://application:,,,/PolarUninstaller;component/Assets/Branding/"+name));
        image.Freeze();Cache[name]=image;return image;
    }
    public static void Apply(ResourceDictionary resources,string theme)
    {
        resources["PolarLogoImage"]=ForTheme(theme);
        resources["PolarLogoSmallImage"]=ForTheme(theme,small:true);
    }
}
