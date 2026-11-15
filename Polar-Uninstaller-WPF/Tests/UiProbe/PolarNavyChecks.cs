using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PolarUninstallerWpf;
using PolarUninstallerWpf.Services;

internal static class PolarNavyChecks
{
    internal static int Run()
    {
        int failed=0,count=0;
        void Check(bool pass,string message){count++;if(!pass)failed++;Console.WriteLine((pass?"PASS ":"FAIL ")+message);}
        string project=AppContext.BaseDirectory;
        while(!File.Exists(Path.Combine(project,"PolarUninstallerWpf.csproj")))project=Directory.GetParent(project)?.FullName??throw new IOException("Project root missing");
        string root=Path.Combine(project,"Build","Validation","Midnight142-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var service=new SettingsService(root);string file=Path.Combine(root,"settings.json");
        Check(service.Load().Theme=="Midnight Champagne","Fresh settings directory defaults to Midnight Champagne without creating a settings file");
        Check(!File.Exists(file),"Reading fresh preferences does not modify settings");
        foreach(string json in new[]{"{}","{\"Arabic\":true}","{\"Theme\":null}","{\"Theme\":\"\"}","{\"Theme\":\"Unavailable Theme\"}"})
        {
            File.WriteAllText(file,json);var loaded=service.Load();
            Check(loaded.Theme=="Midnight Champagne"&&File.ReadAllText(file)==json,"Missing/invalid preference falls back safely without rewriting user data: "+json);
        }
        File.WriteAllText(file,"invalid json");Check(service.Load().Theme=="Midnight Champagne"&&service.LoadError!=null,"Corrupt settings fall back without hiding the read error");
        Check(ThemeCatalog.All.Count==8&&new[]{"Blue","Black","White","Beige","Espresso","Rose Noir","Pure Black"}.All(ThemeCatalog.All.ContainsKey),"All seven existing themes remain alongside Midnight Champagne");
        Check(!ThemeCatalog.All.ContainsKey("Polar Navy")&&ThemeCatalog.Resolve("Polar Navy")=="Midnight Champagne","Legacy identity resolves to the new default without a duplicate gallery option");
        File.WriteAllText(file,"{\"Theme\":\"Polar Navy\",\"Arabic\":true,\"SidebarCollapsed\":true,\"CleanerCategories\":[\"UserTemp\"],\"DeepUninstallerSort\":\"sizeDesc\",\"FileBrowserSort\":\"date\",\"FileBrowserSortDirection\":\"descending\",\"FuturePreference\":{\"enabled\":true}}");
        var migrated=service.Load();
        using(var migratedFile=JsonDocument.Parse(File.ReadAllText(file)))
        {
            Check(migrated.Theme=="Midnight Champagne"&&migrated.Arabic&&migrated.SidebarCollapsed&&migrated.DeepUninstallerSort=="sizeDesc"&&migrated.FileBrowserSort=="date"&&migrated.FileBrowserSortDirection=="descending"&&migrated.CleanerCategories!.SequenceEqual(new[]{"UserTemp"}),"Legacy migration preserves all known user preferences");
            Check(migratedFile.RootElement.GetProperty("Theme").GetString()=="Midnight Champagne"&&migratedFile.RootElement.GetProperty("FuturePreference").GetProperty("enabled").GetBoolean(),"Migration is persisted atomically and preserves unknown future preferences");
        }
        foreach(string theme in ThemeCatalog.All.Keys)
        {
            service.Save(new(theme,true,true,["UserTemp"],"name","size","descending"));
            var reloaded=new SettingsService(root).Load();
            Check(reloaded.Theme==theme&&reloaded.Arabic&&reloaded.SidebarCollapsed&&reloaded.FileBrowserSort=="size"&&reloaded.CleanerCategories!.SequenceEqual(new[]{"UserTemp"}),"Restart simulation preserves "+theme+" and unrelated preferences");
        }
        var navy=ThemeCatalog.All[ThemeCatalog.DefaultTheme];
        Check(navy.Background=="#0A1626"&&navy.Sidebar=="#0A1728"&&navy.Card=="#14263B"&&navy.Hover=="#1B3048"&&navy.Accent=="#F0D7A7"&&navy.SecondaryAccent=="#D9B878","Navy and cream reference palette matches the requested values");
        Check(Contrast(navy.Text,navy.Card)>=4.5&&Contrast(navy.Dim,navy.Card)>=4.5&&Contrast(navy.Sidebar!,navy.Accent)>=7,"Primary, secondary and selected-navigation text have readable contrast");
        service.Save(new(ThemeCatalog.DefaultTheme));
        var app=new App();app.InitializeComponent(); // No Run/Show: no updater request or destructive page actions.
        var ctor=typeof(MainWindow).GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(SettingsService),typeof(ActivityMetricsService)},null)!;
        var window=(MainWindow)ctor.Invoke(new object[]{service,new ActivityMetricsService(root)});
        var picker=(ComboBox)window.FindName("ThemePicker");var page=(ContentControl)window.FindName("PageContent");var nav=(Panel)window.FindName("Nav");var side=(Border)window.FindName("Side");
        Check((string)picker.SelectedItem=="Midnight Champagne"&&((SolidColorBrush)side.Background).Color.A==0,"Sidebar uses the shared continuous background lighting without an opaque cutoff");
        Check(ChampagneLighting.Background.IsFrozen&&ChampagneLighting.UpperLeft.IsFrozen&&ChampagneLighting.Center.IsFrozen&&ChampagneLighting.LowerRight.IsFrozen,"All four shared lighting/background brushes are frozen");
        var top=(System.Windows.Shapes.Rectangle)typeof(MainWindow).GetField("backgroundTopLeft",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!;
        var center=(System.Windows.Shapes.Rectangle)typeof(MainWindow).GetField("backgroundCenter",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!;
        var lower=(System.Windows.Shapes.Rectangle)typeof(MainWindow).GetField("backgroundBottomRight",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!;
        Check(ReferenceEquals(top.Fill,ChampagneLighting.UpperLeft)&&ReferenceEquals(center.Fill,ChampagneLighting.Center)&&ReferenceEquals(lower.Fill,ChampagneLighting.LowerRight),"Main window reuses the three cached reference-distributed light brushes");
        var initialPage=page.Content;var firstButton=nav.Children[0];var columns=((Grid)window.FindName("Root")).ColumnDefinitions.ToArray();
        ((SolidColorBrush)app.Resources["SidebarActiveText"]).Freeze(); // Also exercise replacement rather than brush mutation.
        foreach(string theme in ThemeCatalog.All.Keys)
        {
            picker.SelectedItem=theme;
            Check(ReferenceEquals(page.Content,initialPage)&&ReferenceEquals(nav.Children[0],firstButton)&&((Grid)window.FindName("Root")).ColumnDefinitions.SequenceEqual(columns),theme+" switching reuses current page, navigation and layout columns");
            Check(service.Load().Theme==theme&&ColorOf((Brush)app.Resources["HomeTextBrush"])==ThemeCatalog.All[theme].Text,"Live colors and persisted selection match "+theme);
            var selectedButton=(Button)nav.Children[0];var selectedIcon=(System.Windows.Shapes.Path)((Panel)selectedButton.Content).Children[0];
            string expectedText=theme==ThemeCatalog.DefaultTheme?navy.Sidebar!:ThemeCatalog.All[theme].Text;
            Check(ColorOf(selectedButton.Foreground)==expectedText&&ColorOf(selectedIcon.Stroke)==expectedText,"Selected navigation text and icon rebind correctly in "+theme);
            if(theme!=ThemeCatalog.DefaultTheme)Check(ReferenceEquals(center.Fill,Brushes.Transparent),theme+" does not inherit the signature theme's center lighting");
        }
        picker.SelectedItem=ThemeCatalog.DefaultTheme;
        Check(ColorOf(((Button)nav.Children[0]).Foreground)==navy.Sidebar&&ColorOf((Brush)app.Resources["SidebarSelected"])==navy.Accent,"Active navigation uses cream with dark navy text/icons");
        var render=typeof(MainWindow).GetMethod("Render",BindingFlags.NonPublic|BindingFlags.Instance,null,new[]{typeof(bool)},null)!;
        void Set(string name,object value)=>typeof(MainWindow).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(window,value);
        Set("currentDirectory",root);
        foreach(bool rtl in new[]{false,true})
        {
            Set("arabic",rtl);
            foreach(string name in new[]{"home","uninstall","cleaner","dashboard","files","whatsnew"})
            {
                Set("page",name);render.Invoke(window,new object[]{true});
                var content=(FrameworkElement)window.Content;
                foreach(var size in new[]{new Size(1100,760),new Size(1920,1080)})
                {
                    content.Measure(size);content.Arrange(new Rect(size));content.UpdateLayout();
                    Check(page.Content is FrameworkElement element&&element.ActualWidth>0&&!double.IsNaN(element.ActualHeight)&&page.FlowDirection==(rtl?FlowDirection.RightToLeft:FlowDirection.LeftToRight),name+" offscreen WPF layout "+size+" / "+(rtl?"Arabic":"English"));
                }
            }
        }
        var options=ThemeCatalog.All.Select(pair=>{var p=pair.Value;return new ThemeOption(pair.Key,pair.Key,p.Background,p.Glow,p.Card,p.Border,p.Accent,p.Text,p.Dim,p.Sidebar,p.Hover,p.SecondaryBackground,p.SecondaryAccent);}).ToArray();
        var gallery=new ThemeGalleryView(options);gallery.Open(ThemeCatalog.DefaultTheme,true);
        Check(((Panel)gallery.FindName("Cards")).Children.Count==8&&gallery.PendingTheme==ThemeCatalog.DefaultTheme&&gallery.FlowDirection==FlowDirection.RightToLeft,"Appearance Gallery includes eight previews and Arabic selection");
        Check(ReleaseNotesService.LoadCurrent(false)?.Version=="1.0.142"&&ReleaseNotesService.LoadCurrent(true)?.Entries.Count==4,"Bilingual v1.0.142 notes are embedded");
        foreach(bool rtl in new[]{false,true})
        {
            Set("arabic",rtl);Set("page","home");render.Invoke(window,new object[]{true});
            var content=(FrameworkElement)window.Content;var size=new Size(1300,850);
            content.Measure(size);content.Arrange(new Rect(size));content.UpdateLayout();
            var active=nav.Children.OfType<Button>().Single(b=>Equals(b.Tag,"True"));active.ApplyTemplate();
            var surface=(Border)active.Template.FindName("Surface",active);
            Check(ColorOf(surface.Background)==navy.Accent,"Actual selected navigation template renders cream / "+(rtl?"Arabic":"English"));
            var image=new System.Windows.Media.Imaging.RenderTargetBitmap(1300,850,96,96,PixelFormats.Pbgra32);image.Render(content);
            var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
            string path=Path.Combine(root,rtl?"MidnightChampagne-Arabic.png":"MidnightChampagne-English.png");
            using(var output=File.Create(path))encoder.Save(output);
            Console.WriteLine("Offscreen preview: "+path);
        }
        window.Close();app.Shutdown();
        Console.WriteLine($"{count} focused Navy/settings/offscreen-layout checks, {failed} failures. Fixtures: {root}");
        Console.WriteLine("No real installation, on-screen resize/hover, snapping, or DWM appearance was tested. Isolated settings simulate installation/restart conditions.");
        return failed==0?0:1;
    }
    private static string ColorOf(Brush brush)=>((SolidColorBrush)brush).Color.ToString()[3..].Insert(0,"#");
    private static double Contrast(string a,string b)
    {
        double L(string value){var c=(Color)ColorConverter.ConvertFromString(value);double S(byte x){double v=x/255d;return v<=.04045?v/12.92:Math.Pow((v+.055)/1.055,2.4);}return .2126*S(c.R)+.7152*S(c.G)+.0722*S(c.B);}
        double x=L(a),y=L(b);return (Math.Max(x,y)+.05)/(Math.Min(x,y)+.05);
    }
}
