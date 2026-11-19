using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PolarUninstallerWpf;
using PolarUninstallerWpf.Services;

internal static partial class Parts146Checks
{
    internal static int RunRtlLayout147()
    {
        Setup("RtlLayout147");
        var log=new ActivityLogService(app.Dispatcher);
        string folder=Path.Combine(root,"مجلد العرض");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder,"sample.bin"),new byte[4096]);
        var records=Enumerable.Range(1,104).Select(i=>new InstalledProgram($"Layout Fixture {i:D3}","Polar Fixtures",
            "1.2.3","","",6256640,"","Registry",$"layout-fixture-{i}")).ToArray();
        var inventory=new ApplicationInventoryService(_=>Task.FromResult<IReadOnlyList<InstalledProgram>>(records));
        Wait(inventory.RefreshAsync());
        var freed=new SpaceFreedService(Path.Combine(root,"freed"));
        Wait(freed.AddAsync((long)(6.11*(1L<<30)),"layout-fixture"));
        var host=(ContentControl)main.FindName("PageContent");
        foreach(bool arabic in new[]{false,true})
        {
            string locale=arabic?"ar":"en";
            var dashboard=new DashboardView(arabic,log,inventory:inventory,freedSpace:freed);
            Invoke(dashboard,"ShowDrive",new DriveSummary("C:\\",920L<<30,(long)(84.2*(1L<<30))){Label="Local Disk"});
            Render(dashboard,"Dashboard-"+locale,surface=>
            {
                foreach(var (prefix,expected) in new[]{("Programs","104"),("Freed","6.11 GB"),("Free","84.2 GB")})
                {
                    var value=(TextBlock)dashboard.FindName(prefix+"Value");
                    var label=(TextBlock)dashboard.FindName(prefix+"Label");
                    var detail=(TextBlock)dashboard.FindName(prefix+"Detail");
                    var stack=(StackPanel)value.Parent;
                    var dock=(DockPanel)stack.Parent;
                    var icon=(FrameworkElement)dock.Children[0];
                    var card=(FrameworkElement)dock.Parent;
                    Rect RectOf(FrameworkElement element)=>element.TransformToAncestor(surface).TransformBounds(new Rect(element.RenderSize));
                    double Edge(Rect rect)=>arabic?rect.Right:rect.Left;
                    Rect Glyphs(TextBlock block)
                    {
                        var start=block.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                        var end=block.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
                        return block.TransformToAncestor(surface).TransformBounds(Rect.Union(start,end));
                    }
                    var valueBox=Glyphs(value);
                    var labelBox=Glyphs(label);
                    var detailBox=Glyphs(detail);
                    var iconBox=RectOf(icon);
                    var cardBox=RectOf(card);
                    Check(Math.Abs(Edge(valueBox)-Edge(labelBox))<2 && Math.Abs(Edge(valueBox)-Edge(detailBox))<2,
                        $"{locale} {prefix}: label, value and detail text share their { (arabic?"right":"left") } edge");
                    Check(cardBox.Contains(valueBox) && (arabic?iconBox.Left-valueBox.Right:valueBox.Left-iconBox.Right) is >= -2 and < 48,
                        $"{locale} {prefix}: value stays inside its card next to its icon");
                    Check(value.ReadLocalValue(FrameworkElement.FlowDirectionProperty)==DependencyProperty.UnsetValue &&
                        value.FlowDirection==label.FlowDirection && string.Concat(value.Inlines.OfType<Run>().Select(r=>r.Text))==expected,
                        $"{locale} {prefix}: value inherits layout and preserves text order");
                    if(arabic) Check(value.Inlines.OfType<Run>().Single().FlowDirection==FlowDirection.LeftToRight,
                        $"{locale} {prefix}: only the numeric run is LTR");
                }
            });

            Render(new HomeView(arabic,inventory),"Home-"+locale);
            if((bool)Field(main,"arabic")! !=arabic)Invoke(main,"SwitchLanguage",main,new RoutedEventArgs());
            Set(main,"page","cleaner");
            var cleaner=(FrameworkElement)Invoke(main,"BuildPageSafely")!;
            cleaner.FlowDirection=arabic?FlowDirection.RightToLeft:FlowDirection.LeftToRight;
            host.Content=null;
            Render(cleaner,"QuickCleaner-"+locale);

            var deep=new DeepUninstallerView(arabic,log,inventory,new SettingsService(Path.Combine(root,"deep-"+locale)));
            ((ListBox)deep.FindName("List")).ItemsSource=records.Take(7).Select(r=>new ProgramCard(r with { Location=folder },arabic)).ToArray();
            Render(deep,"DeepUninstaller-"+locale);

            var browser=new FileBrowserView(arabic,folder,log,settings:new SettingsService(Path.Combine(root,"browser-"+locale)));
            Wait(browser.ShowFolderAsync(folder));
            ((ListBox)browser.FindName("Files")).SelectedIndex=0;
            Render(browser,"FileBrowser-"+locale);
        }

        foreach(var file in new[]{"Services/BidiText.cs","Services/DisplayFormat.cs","Services/LocalizedPresentation.cs"})
        {
            var code=File.ReadAllText(Path.Combine(project,file));
            Check(!System.Text.RegularExpressions.Regex.IsMatch(code,
                @"\b(?:text|owner)\s*\.\s*(?:FlowDirection|TextAlignment|HorizontalAlignment)\s*=|\b(?:FrameworkElement|TextBlock|UIElement)\.(?:FlowDirection|TextAlignment|HorizontalAlignment)Property\b"),
                file+" cannot set container direction or alignment");
        }
        return Finish("RtlLayout147");

        void Render(FrameworkElement page,string name,Action<Border>? measure=null)
        {
            const int width=1320,height=770;
            var surface=new Border{Child=page};
            surface.SetResourceReference(Border.BackgroundProperty,"ChromeBackground");
            surface.Measure(new Size(width,height));
            surface.Arrange(new Rect(0,0,width,height));
            surface.UpdateLayout();
            measure?.Invoke(surface);
            var image=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
            image.Render(surface);
            var png=new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(image));
            using var stream=File.Create(Path.Combine(root,name+".png"));
            png.Save(stream);
            surface.Child=null;
        }
    }
}
