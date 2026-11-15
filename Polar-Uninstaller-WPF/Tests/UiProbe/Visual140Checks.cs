using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PolarUninstallerWpf;
using PolarUninstallerWpf.Services;

internal static class Visual140Checks
{
    internal static int Run()
    {
        int failures=0,checks=0;
        void Check(bool pass,string message){checks++;if(!pass)failures++;Console.WriteLine((pass?"PASS ":"FAIL ")+message);}
        string root=AppContext.BaseDirectory;
        while(!File.Exists(Path.Combine(root,"PolarUninstallerWpf.csproj")))root=Directory.GetParent(root)?.FullName??throw new IOException("Project root not found.");
        var app=new App();app.InitializeComponent(); // No Run/Show/dispatcher pump: no application startup or network requests.
        var themes=Regex.Matches(File.ReadAllText(Path.Combine(root,"MainWindow.xaml.cs")),"\\[\"([^\"]+)\"\\] = new\\(\"(#[^\"]+)\", \"(#[^\"]+)\", \"(#[^\"]+)\", \"(#[^\"]+)\", \"(#[^\"]+)\", \"(#[^\"]+)\", \"(#[^\"]+)\"\\)")
            .Select(m=>new ThemeOption(m.Groups[1].Value,m.Groups[1].Value,m.Groups[2].Value,m.Groups[3].Value,m.Groups[4].Value,m.Groups[5].Value,m.Groups[6].Value,m.Groups[7].Value,m.Groups[8].Value)).ToArray();
        Check(themes.Length==7,"All seven actual application palettes loaded");
        var apply=typeof(AppInfo).Assembly.GetType("PolarUninstallerWpf.Services.DialogThemeResources")!.GetMethod("Apply",BindingFlags.Static|BindingFlags.NonPublic)!;
        void Palette(ResourceDictionary resources,ThemeOption t)=>apply.Invoke(null,new object[]{resources,t.Background,t.Card,t.Border,t.Accent,t.Text,t.Dim});
        var gallery=new ThemeGalleryView(themes);
        var select=typeof(ThemeGalleryView).GetMethod("Select",BindingFlags.Instance|BindingFlags.NonPublic)!;
        foreach(var t in themes)
        {
            Palette(app.Resources,t);
            var surface=(LinearGradientBrush)app.Resources["DialogSurfaceBrush"];
            Check(surface.GradientStops.All(s=>s.Color.A is >0 and <255),t.Id+" retains translucent surfaces");
            var bg=(Color)ColorConverter.ConvertFromString(t.Background);
            double contrast=surface.GradientStops.Min(s=>Math.Min(Contrast(((SolidColorBrush)app.Resources["DialogTextBrush"]).Color,Composite(s.Color,bg)),Contrast(((SolidColorBrush)app.Resources["DialogDimBrush"]).Color,Composite(s.Color,bg))));
            Check(contrast>=4.5,t.Id+" primary/secondary dialog contrast >= 4.5 ("+contrast.ToString("F2")+")");
            gallery.Open(t.Id,false);
            var cards=(Panel)gallery.FindName("Cards");var first=cards.Children[0];
            var next=themes.First(p=>p.Id!=t.Id);
            select.Invoke(gallery,new object[]{next.Id});
            Check(gallery.PendingTheme==next.Id&&ReferenceEquals(first,cards.Children[0])&&((SolidColorBrush)gallery.Resources["DialogTextBrush"]).Color==(Color)ColorConverter.ConvertFromString(next.Text),t.Id+" previews locally without recreating cards");
            Check(((SolidColorBrush)app.Resources["DialogTextBrush"]).Color==(Color)ColorConverter.ConvertFromString(t.Text),"Pending preview leaves application resources unchanged");
            string? committed=null;Action<string> handler=id=>committed=id;gallery.ApplyRequested+=handler;
            ((Button)gallery.FindName("ApplyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));gallery.ApplyRequested-=handler;
            Check(committed==next.Id,"Apply requests exactly the pending theme");
            ((Button)gallery.FindName("CancelButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(gallery.PendingTheme==t.Id,"Cancel restores original gallery palette");
        }
        gallery.Open("Pure Black",true);
        Check(((Border)gallery.FindName("PanelFrame")).Effect==null&&gallery.FlowDirection==FlowDirection.RightToLeft,"Pure Black has no shadow/glow; Arabic gallery mirrors");
        var now=DateTime.UtcNow;
        var groups=new[]{new DuplicateGroup("B",10,new[]{new DuplicateFile(@"C:\fixture\z.txt",10,now),new DuplicateFile(@"C:\fixture\a.txt",10,now)}),new DuplicateGroup("A",10,new[]{new DuplicateFile(@"C:\fixture\c.txt",10,now),new DuplicateFile(@"C:\fixture\b.txt",10,now)})};
        var build=typeof(DuplicateFilesView).GetMethod("CreateRows",BindingFlags.Static|BindingFlags.NonPublic)!;
        var rows=(List<DuplicateRow>)build.Invoke(null,new object?[]{groups,false,null})!;
        Check(rows.Select(r=>r.Name).SequenceEqual(new[]{"b.txt","c.txt","a.txt","z.txt"}),"Stable ordering keeps verified sets adjacent");
        foreach(var row in rows)row.Selected=true;
        Check(rows.Count(r=>r.Selected)==2&&rows.Count(r=>r.IsRetained&&!r.Selected)==2,"Even manual select-all cannot select retained copies");
        foreach(var row in rows)row.SetLanguage(true);
        Check(rows.Where(r=>r.IsRetained).All(r=>r.CopyRole.Contains("محفوظة"))&&rows[2].RowSpacing.Top>rows[1].RowSpacing.Top,"Localized retained labels and subtle set separators");
        app.Resources["HomeTextBrush"]=new SolidColorBrush(Colors.White);
        var view=new DuplicateFilesView(true,root,new ActivityLogService(app.Dispatcher),new ApplicationInventoryService());
        var list=(ListBox)view.FindName("Results");
        Check(list.Foreground==app.Resources["HomeTextBrush"],"Duplicate list resolves the explicit theme foreground");
        Check(System.Windows.Controls.VirtualizingPanel.GetIsVirtualizing(list),"Unified list keeps WPF virtualization");
        var notes=ReleaseNotesService.LoadCurrent(false);var arabicNotes=ReleaseNotesService.LoadCurrent(true);
        Check(AppInfo.Version=="1.0.140"&&notes?.Entries.Count==5&&arabicNotes?.Entries.Count==5,"Embedded version and bilingual release notes are 1.0.140");
        Check(notes!=null&&notes.DisplayFeatures.Count+notes.DisplayImprovements.Count+notes.DisplayFixes.Count==3,"What's New has three concise highlights");
        string fixture=Path.Combine(root,"Build","Validation","Visual140-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        try
        {
            var files=new[]{"a.txt","b.txt","c.txt"}.Select(n=>Path.Combine(fixture,n)).ToArray();
            foreach(var path in files)File.WriteAllText(path,"Owned duplicate verification payload");
            var scanner=new DuplicateScanService();
            var scan=scanner.ScanAsync(fixture,null,CancellationToken.None).GetAwaiter().GetResult();
            var cleanup=new DuplicateCleanupService().CleanAsync(scan,files,true,Array.Empty<string>(),null,CancellationToken.None).GetAwaiter().GetResult();
            Check(files.Count(File.Exists)==1&&cleanup.Items.Any(i=>i.Code=="last_copy"),"Real cleanup retains a SHA-256-verified copy even if all copies are requested");
            foreach(var path in files)File.WriteAllText(path,"Owned second duplicate verification payload");
            scan=scanner.ScanAsync(fixture,null,CancellationToken.None).GetAwaiter().GetResult();
            File.WriteAllText(files[1],"Changed after scan");
            cleanup=new DuplicateCleanupService().CleanAsync(scan,new[]{files[1]},true,Array.Empty<string>(),null,CancellationToken.None).GetAwaiter().GetResult();
            Check(!cleanup.Items.Any(i=>i.Removed)&&File.ReadAllText(files[1])=="Changed after scan","Changed duplicate is preserved during content revalidation");
        }
        finally
        {
            // Only this GUID-owned folder and the three exact fixture files are removed.
            foreach(var name in new[]{"a.txt","b.txt","c.txt"})File.Delete(Path.Combine(fixture,name));
            Directory.Delete(fixture);
        }
        Console.WriteLine($"{checks} focused checks, {failures} failures. No real windows shown, user files deleted, or personal settings changed.");
        return failures==0?0:1;
    }
    private static Color Composite(Color top,Color bottom)=>Color.FromRgb((byte)((top.R*top.A+bottom.R*(255-top.A))/255),(byte)((top.G*top.A+bottom.G*(255-top.A))/255),(byte)((top.B*top.A+bottom.B*(255-top.A))/255));
    private static double Contrast(Color a,Color b)
    {
        static double L(Color c){double F(byte v){double x=v/255d;return x<=.04045?x/12.92:Math.Pow((x+.055)/1.055,2.4);}return .2126*F(c.R)+.7152*F(c.G)+.0722*F(c.B);}
        return (Math.Max(L(a),L(b))+.05)/(Math.Min(L(a),L(b))+.05);
    }
}
