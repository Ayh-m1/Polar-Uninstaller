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
            Check(decoder.Frames.Count==7&&new[]{16,24,32,48,64,128,256}.All(size=>decoder.Frames.Any(frame=>frame.PixelWidth==size)),"Supplied ICO contains exactly the seven required Windows resolutions");
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
        string csproj=File.ReadAllText(Path.Combine(project,"PolarUninstallerWpf.csproj"));
        Check(installer.Contains("!define MUI_ICON \"..\\Assets\\Polar.ico\"")&&csproj.Contains("<ApplicationIcon>Assets\\Polar.ico</ApplicationIcon>"),"Setup and application embed the same official multi-size .ico");
        Check(WindowsShellIconService.LegacyIconFileName=="PolarLogo-v144-crisp.ico"&&!installer.Contains("File /oname=PolarLogo")&&installer.Contains("Delete \"$INSTDIR\\PolarLogo*.ico\""),"Installer stops shipping a standalone icon and removes legacy .ico copies");
        Check(installer.Contains("\"$INSTDIR\\PolarUninstaller.exe\" 0")&&installer.Contains("\"DisplayIcon\" \"$INSTDIR\\PolarUninstaller.exe,0\"")&&!installer.Contains("PolarLogo-v144-crisp.ico\""),"Setup shortcuts and uninstall record use PolarUninstaller.exe icon index 0");
        Check(installer.Contains("shell32::SHChangeNotify(i 0x08000000")&&installer.Contains("Function .onInstSuccess")&&installer.Contains("Function un.onUnInstSuccess"),"Setup and uninstaller send SHCNE_ASSOCCHANGED to refresh the Windows icon cache");
        string? host=null;
        foreach(string candidate in new[]{
            Path.Combine(AppContext.BaseDirectory,"PolarUninstaller.exe"),
            Path.Combine(project,"bin","Release","net9.0-windows","win-x64","PolarUninstaller.exe"),
            Path.Combine(project,"bin","Release","net9.0-windows","PolarUninstaller.exe")})
            if(File.Exists(candidate)){host=candidate;break;}
        if(host!=null)
        {
            using var embedded=System.Drawing.Icon.ExtractAssociatedIcon(host);
            using var official=System.Drawing.Icon.ExtractAssociatedIcon(Path.Combine(project,"Assets","Polar.ico"));
            bool matches=false;
            if(embedded!=null&&official!=null)
            {
                static byte[] Pixels(System.Drawing.Bitmap bitmap)
                {
                    var data=bitmap.LockBits(new System.Drawing.Rectangle(0,0,bitmap.Width,bitmap.Height),System.Drawing.Imaging.ImageLockMode.ReadOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    try
                    {
                        var bytes=new byte[data.Stride*data.Height];
                        System.Runtime.InteropServices.Marshal.Copy(data.Scan0,bytes,0,bytes.Length);
                        return bytes;
                    }
                    finally{bitmap.UnlockBits(data);}
                }
                using var actual=embedded.ToBitmap();using var expected=official.ToBitmap();
                matches=actual.Width==expected.Width&&actual.Height==expected.Height&&Pixels(actual).SequenceEqual(Pixels(expected));
            }
            Check(matches,"Application host EXE embeds the official icon as its Win32 application icon");
        }
        Check(AppInfo.Version=="1.0.144"&&ReleaseNotesService.LoadCurrent(false)?.Entries.Count==7&&ReleaseNotesService.LoadCurrent(true)?.Entries.Count==7,"Version and concise bilingual v1.0.144 notes embedded");
        Console.WriteLine($"{count} checks, {failures} failures; no real installation or pinned shortcuts changed.");
        return failures==0?0:1;
    }
}

