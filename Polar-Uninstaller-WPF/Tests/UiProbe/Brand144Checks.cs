using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using PolarUninstallerWpf;
using PolarUninstallerWpf.Services;

internal static class Brand144Checks
{
    internal static int Run()
    {
        int count=0,failures=0;
        void Check(bool pass,string name){count++;if(!pass)failures++;Console.WriteLine((pass?"PASS ":"FAIL ")+name);}
        var app=new App();app.InitializeComponent();
        string project=AppContext.BaseDirectory;
        while(!File.Exists(Path.Combine(project,"PolarUninstallerWpf.csproj")))project=Directory.GetParent(project)?.FullName??throw new IOException("Project root missing");
        string supplied=Path.Combine(Directory.GetParent(project)!.FullName,"Logo-Candidates","Polar_Icons");
        byte[] Resource(string name)
        {
            using var stream=Application.GetResourceStream(new Uri("pack://application:,,,/PolarUninstaller;component/Assets/"+name))!.Stream;
            using var bytes=new MemoryStream();stream.CopyTo(bytes);return bytes.ToArray();
        }
        Check(Resource("Polar.ico").SequenceEqual(File.ReadAllBytes(Path.Combine(project,"Assets","Polar.ico"))),"Embedded Windows icon matches the enlarged build asset");
        foreach(string color in new[]{"white","black"})foreach(string size in new[]{"flat_24","flat_64","shadow_128"})
        {
            string name=$"PU_{color}_{size}.png";
            Check(Resource("Branding/"+name).SequenceEqual(File.ReadAllBytes(Path.Combine(supplied,"ui_logo_"+color,name))),"Original PNG embedded unchanged: "+name);
        }
        using(var stream=new MemoryStream(Resource("Polar.ico")))
        {
            var decoder=new IconBitmapDecoder(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
            Check(new[]{16,24,32,48,64,128,256}.All(size=>decoder.Frames.Any(frame=>frame.PixelWidth==size)),"Supplied ICO retains all required Windows resolutions");
            using var originalStream=File.OpenRead(Path.Combine(supplied,"app_icon","icon.ico"));
            var original=new IconBitmapDecoder(originalStream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
            (int Letters,int Opaque) Measure(BitmapSource source)
            {
                var pixels=new FormatConvertedBitmap(source,System.Windows.Media.PixelFormats.Bgra32,null,0);
                int size=pixels.PixelWidth,left=size,right=0,opaque=0;var data=new byte[size*size*4];pixels.CopyPixels(data,size*4,0);
                for(int y=0;y<size;y++)for(int x=0;x<size;x++)
                {
                    int i=(y*size+x)*4;if(data[i+3]>128)opaque++;
                    if(data[i+3]>128&&Math.Min(data[i],Math.Min(data[i+1],data[i+2]))>180){left=Math.Min(left,x);right=Math.Max(right,x);}
                }
                return(right-left+1,opaque);
            }
            var before=Measure(original.Frames.First(frame=>frame.PixelWidth==128));
            var after=Measure(decoder.Frames.First(frame=>frame.PixelWidth==128));
            Check(after.Letters>before.Letters*1.1,"PU letterforms occupy visibly more of the icon canvas");
            Check(after.Opaque>=before.Opaque,"Rounded background fills at least as much of the icon canvas");
        }
        foreach(string theme in ThemeCatalog.All.Keys)
        {
            BrandingAssets.Apply(app.Resources,theme);
            var image=(BitmapImage)app.Resources["PolarLogoImage"];
            string color=theme is "White" or "Beige"?"black":"white";
            Check(image.UriSource.ToString().Contains("PU_"+color+"_shadow_128.png")&&image.IsFrozen&&ReferenceEquals(image,BrandingAssets.ForTheme(theme)),theme+": correct original logo, frozen and reused");
        }
        string installer=File.ReadAllText(Path.Combine(project,"Installer-NSIS","PolarUninstaller.nsi"));
        Check(WindowsShellIconService.IconFileName=="PolarLogo-v144-crisp.ico"&&installer.Contains("File /oname=PolarLogo-v144-crisp.ico")&&installer.Contains("\"DisplayIcon\" \"$INSTDIR\\PolarLogo-v144-crisp.ico,0\""),"Installer and shortcut refresher agree on the enlarged icon identity");
        Check(AppInfo.Version=="1.0.150"&&ReleaseNotesService.LoadCurrent(false)?.Entries.Count==2&&ReleaseNotesService.LoadCurrent(true)?.Entries.Count==2,"Version and complete bilingual v1.0.150 notes embedded");
        Console.WriteLine($"{count} checks, {failures} failures; no real installation or pinned shortcuts changed.");
        return failures==0?0:1;
    }
}
