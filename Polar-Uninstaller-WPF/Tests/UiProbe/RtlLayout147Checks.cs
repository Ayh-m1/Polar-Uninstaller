using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PolarUninstallerWpf;
using PolarUninstallerWpf.Services;

internal static partial class Parts146Checks
{
    internal static int RunRtlLayout147()
    {
        Setup("RtlLayout147");
        CrashDiagnostics.LogDirectory=Path.Combine(root,"Diagnostics");
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
                    var value=(SelectableText)dashboard.FindName(prefix+"Value");
                    var label=(TextBlock)dashboard.FindName(prefix+"Label");
                    var detail=(TextBlock)dashboard.FindName(prefix+"Detail");
                    var stack=(StackPanel)value.Parent;
                    var dock=(DockPanel)stack.Parent;
                    var icon=(FrameworkElement)dock.Children[0];
                    var card=(FrameworkElement)dock.Parent;
                    Rect RectOf(FrameworkElement element)=>element.TransformToAncestor(surface).TransformBounds(new Rect(element.RenderSize));
                    double Edge(Rect rect)=>arabic?rect.Right:rect.Left;
                    Rect Glyphs(FrameworkElement block,TextPointer first,TextPointer last)
                    {
                        var start=first.GetCharacterRect(LogicalDirection.Forward);
                        var end=last.GetCharacterRect(LogicalDirection.Backward);
                        return block.TransformToAncestor(surface).TransformBounds(Rect.Union(start,end));
                    }
                    var valueRun=value.Inlines.OfType<Run>().Single();
                    var valueBox=Glyphs(value,valueRun.ContentStart,valueRun.ContentEnd);
                    var labelBox=Glyphs(label,label.ContentStart,label.ContentEnd);
                    var detailBox=Glyphs(detail,detail.ContentStart,detail.ContentEnd);
                    var iconBox=RectOf(icon);
                    var cardBox=RectOf(card);
                    Check(Math.Abs(Edge(valueBox)-Edge(labelBox))<2 && Math.Abs(Edge(valueBox)-Edge(detailBox))<2,
                        $"{locale} {prefix}: label, value and detail share their {(arabic?"right":"left")} edge ({Edge(labelBox):F1}, {Edge(valueBox):F1}, {Edge(detailBox):F1})");
                    Check(cardBox.Contains(valueBox) && (arabic?iconBox.Left-valueBox.Right:valueBox.Left-iconBox.Right) is >= -2 and < 48,
                        $"{locale} {prefix}: value stays inside its card next to its icon");
                    Check(value.ReadLocalValue(FrameworkElement.FlowDirectionProperty)==DependencyProperty.UnsetValue &&
                        value.FlowDirection==label.FlowDirection && string.Concat(value.Inlines.OfType<Run>().Select(r=>r.Text))==expected,
                        $"{locale} {prefix}: value inherits layout and preserves text order");
                    value.SelectAll();
                    Check(value.IsReadOnly&&value.Selection.Text.Trim()==expected&&ApplicationCommands.Copy.CanExecute(null,value),
                        $"{locale} {prefix}: the numeric value can be selected and copied");
                    if(arabic) Check(value.Inlines.OfType<Run>().Single().FlowDirection==FlowDirection.LeftToRight,
                        $"{locale} {prefix}: only the numeric run is LTR");
                }
            });

            var pendingHomeScan=new TaskCompletionSource<IReadOnlyList<InstalledProgram>>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<IReadOnlyList<InstalledProgram>> nextHomeScan=pendingHomeScan.Task;
            int homeScans=0;
            var homeInventory=new ApplicationInventoryService(_=>{homeScans++;return nextHomeScan;});
            var home=new HomeView(arabic,homeInventory);
            var count=(TextBlock)home.FindName("Count");
            var caption=(TextBlock)home.FindName("CountCaption");
            using (var source=new HwndSource(new HwndSourceParameters("Polar home layout fixture",1320,770)))
            {
                source.RootVisual=home;
                Pump();
                if(homeScans==0) Invoke(home,"OnLoaded",home,new RoutedEventArgs(FrameworkElement.LoadedEvent));
                Check(home.IsLoaded&&homeScans==1&&count.Text=="…",$"{locale}: Home shows a loading indicator while the inventory is pending");
                var published=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                homeInventory.Changed+=_=>published.TrySetResult();
                pendingHomeScan.SetResult(records);
                Wait(published.Task);
                Pump();
                Check(count.Text=="104"&&caption.Text==(arabic?"برامج مسجلة":"programs found"),$"{locale}: Home shows the verified inventory count after loading");
                nextHomeScan=Task.FromResult<IReadOnlyList<InstalledProgram>>(Array.Empty<InstalledProgram>());
                Wait(homeInventory.RefreshAsync());
                Pump();
                Check(count.Text=="104",$"{locale}: an incomplete rescan retains the verified count");
                source.RootVisual=null;
            }
            Render(home,"Home-"+locale,_=>
            {
                var first=caption.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                var last=caption.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
                Check(caption.ActualWidth>40&&Math.Abs(first.Y-last.Y)<1,$"{locale}: the Home count caption fits horizontally on one line");
            });
            if((bool)Field(main,"arabic")! !=arabic)Invoke(main,"SwitchLanguage",main,new RoutedEventArgs());
            Set(main,"page","cleaner");
            var cleaner=(FrameworkElement)Invoke(main,"BuildPageSafely")!;
            cleaner.FlowDirection=arabic?FlowDirection.RightToLeft:FlowDirection.LeftToRight;
            host.Content=null;
            Render(cleaner,"QuickCleaner-"+locale);

            var deep=new DeepUninstallerView(arabic,log,inventory,new SettingsService(Path.Combine(root,"deep-"+locale)));
            var programList=(ListBox)deep.FindName("List");
            programList.ItemsSource=records.Select(r=>new ProgramCard(r with { Location=folder },arabic)).ToArray();
            Render(deep,"DeepUninstaller-"+locale,surface=>
            {
                Check(programList.ActualHeight>surface.ActualHeight*0.75 && deep.FindName("SelectedDetails") is TextBlock && deep.FindName("Status") is TextBlock,
                    $"{locale}: Deep Uninstaller list fills the available height with its original footer");
            });

            var browser=new FileBrowserView(arabic,folder,log,settings:new SettingsService(Path.Combine(root,"browser-"+locale)));
            Wait(browser.ShowFolderAsync(folder));
            ((ListBox)browser.FindName("Files")).SelectedIndex=0;
            Render(browser,"FileBrowser-"+locale);
            Render(new ReleaseNotesView(arabic),"WhatsNew-"+locale);
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
