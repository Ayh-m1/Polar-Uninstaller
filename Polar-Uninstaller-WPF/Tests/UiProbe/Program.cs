using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using PolarUninstallerWpf;
using PolarUninstallerWpf.Services;

internal static class Program
{
    private static int failures;

    [STAThread]
    private static void Main(string[] args)
    {
        if(args.Contains("--audit-process-child")){Thread.Sleep(900);return;}
        if(args.Contains("--audit-process-parent"))
        {
            var start=new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};
            start.ArgumentList.Add(typeof(Program).Assembly.Location);start.ArgumentList.Add("--audit-process-child");
            using var child=System.Diagnostics.Process.Start(start);return;
        }
        DashboardStorageService.UseCacheDirectory(Path.Combine(AppContext.BaseDirectory,"fixtures","storage-cache-"+Guid.NewGuid().ToString("N")));
        if(args.Contains("--icons-audit")){Environment.ExitCode=IconAudit146.Run();return;}
        if(args.Contains("--dup-audit")){Environment.ExitCode=DuplicateAudit146.Run();return;}
        if(args.Contains("--hidden148")){Environment.ExitCode=Parts146Checks.RunHidden148();return;}
        if(args.Contains("--deep149")){Environment.ExitCode=Parts146Checks.RunDeep149();return;}
        if(args.Contains("--deep150")){Environment.ExitCode=Parts146Checks.RunDeep150();return;}
        if(args.Contains("--themes150")){Environment.ExitCode=Parts146Checks.RunThemes150();return;}
        if(args.Contains("--rtl-layout147")){Environment.ExitCode=Parts146Checks.RunRtlLayout147();return;}
        if(args.Contains("--locale147")){Environment.ExitCode=Parts146Checks.RunLocale147();return;}
        if(args.Contains("--stability147")){Environment.ExitCode=Parts146Checks.RunStability147();return;}
        if(args.Contains("--parts146-d")){Environment.ExitCode=Parts146Checks.RunD();return;}
        if(args.Contains("--parts146-c")){Environment.ExitCode=Parts146Checks.RunC();return;}
        if(args.Contains("--parts146-a")){Environment.ExitCode=Parts146Checks.RunA();return;}
        if(args.Contains("--parts146-b")){Environment.ExitCode=Parts146Checks.RunB();return;}
        if(args.Contains("--audit146")){Environment.ExitCode=Audit146Checks.Run();return;}
        if(args.Contains("--keyboard145")){Environment.ExitCode=Keyboard145Checks.Run();return;}
        if(args.Contains("--explorer-abc144")){Environment.ExitCode=ExplorerABC144Checks.Run();return;}
        if(args.Contains("--explorer144")){Environment.ExitCode=Explorer144Checks.Run();return;}
        if(args.Contains("--dashboard144")){Environment.ExitCode=Dashboard144Checks.Run();return;}
        if(args.Contains("--brand144")){Environment.ExitCode=Brand144Checks.Run();return;}
        if(args.Contains("--cleaner143")){Environment.ExitCode=Cleaner143Checks.Run();return;}
        if(args.Contains("--stability143")){Environment.ExitCode=Stability143Checks.Run();return;}
        if(args.Contains("--polar-navy")){Environment.ExitCode=PolarNavyChecks.Run();return;}
        if(args.Contains("--visual140")){Environment.ExitCode=Visual140Checks.Run();return;}
        var app = new App();
        app.InitializeComponent();
        // WPF queues Application.OnStartup on the dispatcher; pump once so the
        // harness reaches a steady state before creating test windows.
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));

        string baseDir = Path.Combine(AppContext.BaseDirectory, "fixtures", "polar-uiprobe-" + Guid.NewGuid().ToString("N"));
        try
        {
            string folderA = Path.Combine(baseDir, "folder-a");
            string folderB = Path.Combine(baseDir, "folder-b");
            CreateFixture(folderA, new[] { ("a1.bin", 100), ("a2.bin", 300), ("a3.bin", 200) },
                new[] { ("folder-a1", 1000), ("folder-a2", 2000) });
            CreateFixture(folderB, new[] { ("b1.bin", 500), ("b2.bin", 100), ("b3.bin", 300) },
                new[] { ("folder-b1", 4000), ("folder-b2", 500) });

            string settingsDir = Path.Combine(baseDir, "settings");
            var log = new ActivityLogService(app.Dispatcher);
            var settings = new SettingsService(settingsDir);


            // Removal actions are tested against synthetic cards only. No real
            // uninstall button is clicked and no personal application is removed.
            foreach(bool removalRtl in new[]{false,true})
            {
                SetPalette(app, "#100C0B", "#F9EAD9", "#C4A78D", "#DEA66E", "#302018", "#765239");
                var removalView=new DeepUninstallerView(removalRtl,log,new ApplicationInventoryService(),settings);
                var removalList=(ListBox)removalView.FindName("List")!;
                var deepButton=(Button)removalView.FindName("Deep")!;
                var gameButton=(Button)removalView.FindName("Games")!;
                var advancedButton=(Button)Field(removalView,"advancedDeepRemoval")!;
                string fixturePath=Path.Combine(baseDir,"RemovalStateFixture");
                Directory.CreateDirectory(fixturePath);
                var product=new InstalledProgram("RemovalStateFixture","PolarFixture","1",fixturePath);
                var firstRemovalCard=new ProgramCard(product,removalRtl);
                var other=new ProgramCard(product with{Name="Other fixture",Location=Path.Combine(baseDir,"OtherFixture")},removalRtl);
                removalList.ItemsSource=new[]{firstRemovalCard,other};removalList.SelectedItem=firstRemovalCard;
                Check(WaitFor(()=>deepButton.IsEnabled&&!gameButton.IsEnabled,app.Dispatcher),"Regular application enables unified Deep Uninstall ("+(removalRtl?"AR":"EN")+")");
                var captured=new UninstallSnapshot(product,Array.Empty<RegistryLeftover>(),Array.Empty<ShortcutLeftover>(),Array.Empty<OwnedDataFolder>(),Array.Empty<string>());
                var plan=new RemovalPlan(captured,fixturePath,Array.Empty<PlannedFile>(),new[]{fixturePath},Array.Empty<string>());
                var contextMethod=typeof(DeepUninstallerView).GetMethod("SetAdvancedContext",BindingFlags.Instance|BindingFlags.NonPublic)!;
                contextMethod.Invoke(removalView,new object[]{firstRemovalCard,plan,true,Array.Empty<LeftoverCandidate>()});
                Check(advancedButton.IsEnabled&&!deepButton.IsEnabled,"Completed attempt enables advanced and disables direct ("+(removalRtl?"AR":"EN")+")");
                removalList.SelectedItem=other;
                Check(advancedButton.Visibility==Visibility.Collapsed,"A different product never inherits another cleanup action ("+(removalRtl?"AR":"EN")+")");
                removalList.SelectedItem=firstRemovalCard;
                Check(advancedButton.IsEnabled,"Returning to the original product preserves cleanup context ("+(removalRtl?"AR":"EN")+")");
                var refreshed=new ProgramCard(product,removalRtl);
                removalList.ItemsSource=new[]{refreshed};removalList.SelectedItem=refreshed;
                Check(advancedButton.IsEnabled,"Replacing inventory rows does not lose valid context ("+(removalRtl?"AR":"EN")+")");
                typeof(DeepUninstallerView).GetField("busy",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(removalView,true);
                typeof(DeepUninstallerView).GetMethod("UpdateActions",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(removalView,null);
                Check(!advancedButton.IsEnabled&&!deepButton.IsEnabled&&!gameButton.IsEnabled&&!removalList.IsEnabled,
                    "Running removal disables all competing actions ("+(removalRtl?"AR":"EN")+")");
                typeof(DeepUninstallerView).GetField("busy",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(removalView,false);
                contextMethod.Invoke(removalView,new object[]{firstRemovalCard,plan,false,Array.Empty<LeftoverCandidate>()});
                Check(WaitFor(()=>advancedButton.Visibility==Visibility.Collapsed&&deepButton.IsEnabled,app.Dispatcher),
                    "Successful cleanup hides advanced and restores normal actions ("+(removalRtl?"AR":"EN")+")");
                Check((string)deepButton.Content==(removalRtl?"الإزالة العميقة":"Deep Uninstall"),"Removal actions are localized ("+(removalRtl?"AR":"EN")+")");
                string testExecutable=Path.Combine(fixturePath,"RemovalStateFixture.exe");
                string retainedLog=Path.Combine(fixturePath,"remaining.log");
                File.WriteAllText(testExecutable,"fixture");File.WriteAllText(retainedLog,"eligible routine leftover");
                string personalSettings=Path.Combine(fixturePath,"settings.json");File.WriteAllText(personalSettings,"personal fixture settings");
                var officialIdentity=product with{IconPath=testExecutable};
                var officialCard=new ProgramCard(officialIdentity,removalRtl);
                removalList.ItemsSource=new[]{officialCard};removalList.SelectedItem=officialCard;
                var originalIdentity=new UninstallSnapshot(officialIdentity,Array.Empty<RegistryLeftover>(),Array.Empty<ShortcutLeftover>(),Array.Empty<OwnedDataFolder>(),new[]{testExecutable});
                var retainedInfo=new FileInfo(retainedLog);
                var officialRemovalPlan=new RemovalPlan(originalIdentity,fixturePath,new[]{new PlannedFile(retainedLog,retainedInfo.Length,retainedInfo.LastWriteTimeUtc)},new[]{fixturePath},Array.Empty<string>());
                typeof(DeepUninstallerView).GetField("officialPlan",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(removalView,officialRemovalPlan);
                File.Delete(testExecutable); // Simulate only the disposable official uninstaller.
                WaitTask(InvokeTask(removalView,"CompleteRemovalAsync",officialCard,new RemovalProbe(true,false,true,"Fixture official removal completed"),null,originalIdentity,CancellationToken.None),app.Dispatcher);
                Check(!File.Exists(retainedLog)&&File.Exists(personalSettings)&&advancedButton.Visibility==Visibility.Collapsed,
                    "Unified completion automatically cleans eligible leftovers and preserves personal settings ("+(removalRtl?"AR":"EN")+")");
                Check(Field(removalView,"advancedPlan")==null,
                    "Successful automatic cleanup does not require an advanced second stage ("+(removalRtl?"AR":"EN")+")");
                contextMethod.Invoke(removalView,new object[]{officialCard,officialRemovalPlan,false,Array.Empty<LeftoverCandidate>()});
                var gameCard=new ProgramCard(product with{Name="Steam fixture",Identifier="steam:123",Source="Steam",RequiresOfficialRemoval=true,Uninstall="\""+Environment.ProcessPath+"\" --test-never-executed"},removalRtl);
                removalList.ItemsSource=new[]{gameCard};removalList.SelectedItem=gameCard;
                Check(WaitFor(()=>gameButton.IsEnabled&&!deepButton.IsEnabled,app.Dispatcher),"Verified launcher game keeps its official route ("+(removalRtl?"AR":"EN")+")");
                var managedCard=new ProgramCard(product with{Name="Managed fixture",RequiresOfficialRemoval=true,Uninstall="\""+Environment.ProcessPath+"\""},removalRtl);
                removalList.ItemsSource=new[]{managedCard};removalList.SelectedItem=managedCard;
                Check(WaitFor(()=>deepButton.IsEnabled&&!gameButton.IsEnabled,app.Dispatcher),
                    "Windows-managed application uses unified Deep with an internal official stage ("+(removalRtl?"AR":"EN")+")");
            }


            // === Duplicate Files is embedded, preserves navigation and localizes live ===
            string dupUiFolder=Path.Combine(baseDir,"duplicate-ui");
            Directory.CreateDirectory(Path.Combine(dupUiFolder,"nested"));
            File.WriteAllText(Path.Combine(dupUiFolder,"one.txt"),"verified duplicate UI fixture");
            File.Copy(Path.Combine(dupUiFolder,"one.txt"),Path.Combine(dupUiFolder,"nested","two.txt"));
            File.Copy(Path.Combine(dupUiFolder,"one.txt"),Path.Combine(dupUiFolder,"three.txt"));
            var dupBrowser=new FileBrowserView(false,dupUiFolder,log,null,settings);
            var dupHost=new Window{Content=dupBrowser,Width=1050,Height=740,Left=-4000,Top=-4000,ShowInTaskbar=false};
            dupHost.Show();
            Check(WaitFor(()=>((ListView)dupBrowser.FindName("Files")!).Items.Count>0,app.Dispatcher),"Duplicate integration browser loads the physical folder");
            var dupMore=(ContextMenu)Field(dupBrowser,"more")!;
            var dupContext=(ContextMenu)Field(dupBrowser,"itemMenu")!;
            MenuItem? DuplicateMenu(ContextMenu menu)=>menu.Items.OfType<MenuItem>().FirstOrDefault(m=>m.Header?.ToString() is "Find Duplicate Files" or "البحث عن الملفات المكررة");
            var dupEntry=DuplicateMenu(dupMore);
            Check(dupEntry!=null&&dupEntry.Icon!=null,"The three-dot menu provides a labeled Duplicate Files entry with an icon");
            Check(DuplicateMenu(dupContext)==null,"Duplicate Files is absent from the file/folder right-click menu");
            var dupHistory=((System.Collections.Generic.List<string>)Field(dupBrowser,"history")!).ToArray();
            int dupWindowCount=app.Windows.Count;
            var dupFilesBefore=((ListView)dupBrowser.FindName("Files")!).ItemsSource;
            dupEntry!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var dupView=(DuplicateFilesView)((ContentControl)dupBrowser.FindName("DuplicateHost")!).Content;
            dupHost.UpdateLayout();Pump(app.Dispatcher);
            Check(app.Windows.Count==dupWindowCount&&dupView.IsVisible&&Window.GetWindow(dupView)==dupHost,"Opening Duplicate Files uses the current page without a top-level window");
            Check(((TextBox)dupView.FindName("PathInput")!).Text==dupUiFolder,"The current File Browser folder is selected automatically");
            Check(Field(dupView,"result")==null&&!dupView.IsBusy,"Opening Duplicate Files does not automatically start a scan");
            WaitTask(InvokeTask(dupView,"StartScanAsync"),app.Dispatcher);
            var dupRows=(ListBox)dupView.FindName("Results")!;
            Check(dupRows.Items.Count==1&&((SelectableText)dupView.FindName("GroupsValue")!).Text=="1","Integrated results display one actual content-matched group card");
            typeof(DuplicateFilesView).GetMethod("ApplySmartSelection",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(dupView,new object?[]{"shortest",null});
            Check(dupRows.Items.Cast<DuplicateGroupCard>().SelectMany(card=>card.Copies).Count(r=>r.Selected)==2,"Smart selection leaves an initial keeper unchecked");
            object? dupResultBefore=Field(dupView,"result");
            dupBrowser.SetLanguage(true);
            Check(dupView.FlowDirection==FlowDirection.RightToLeft&&((TextBlock)dupView.FindName("Heading")!).Text=="البحث عن الملفات المكررة","Duplicate interface switches immediately to Arabic RTL");
            Check(ReferenceEquals(dupResultBefore,Field(dupView,"result"))&&dupRows.Items.Cast<DuplicateGroupCard>().SelectMany(card=>card.Copies).Count(r=>r.Selected)==2,"Language switching preserves scan results and checkbox selections");
            Check(DuplicateMenu((ContextMenu)Field(dupBrowser,"more")!)?.Header?.ToString()=="البحث عن الملفات المكررة","The sole toolbar entry is localized in Arabic");
            dupBrowser.SetLanguage(false);
            Check(dupView.FlowDirection==FlowDirection.LeftToRight&&((TextBlock)dupView.FindName("Heading")!).Text=="Find Duplicate Files","Duplicate interface switches back to English LTR");
            bool dupThemeCompatible=true;
            foreach(var dupPalette in new[]{"#214D80","#171A20","#FFFFFF","#F7EDDE","#302018"})
            {
                SetPalette(app,dupPalette,"#EAF4FF","#A4B9CE","#34C5F5",dupPalette,"#765239");
                Pump(app.Dispatcher);dupHost.UpdateLayout();
                var dupSurface=FindAllVisual<Border>(dupView).FirstOrDefault(b=>b.CornerRadius.TopLeft==12);
                dupThemeCompatible&=dupSurface?.Background is SolidColorBrush dupBrush&&dupBrush.Color==(Color)ColorConverter.ConvertFromString(dupPalette);
            }
            Check(dupThemeCompatible,"Integrated duplicate surfaces follow all five theme resources");
            dupHost.Width=760;dupHost.Height=620;dupHost.UpdateLayout();Pump(app.Dispatcher);
            Check(((Button)dupView.FindName("ScanButton")!).ActualWidth>0&&dupRows.ActualHeight>0,"Duplicate controls and virtualized results remain usable at a smaller window size");
            ((Button)dupView.FindName("BackButton")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(app.Dispatcher);
            Check(((ContentControl)dupBrowser.FindName("DuplicateHost")!).Visibility==Visibility.Collapsed&&dupBrowser.CurrentDirectory==dupUiFolder,"Back returns to the original folder inside File Browser");
            Check(dupHistory.SequenceEqual((System.Collections.Generic.List<string>)Field(dupBrowser,"history")!)&&ReferenceEquals(dupFilesBefore,((ListView)dupBrowser.FindName("Files")!).ItemsSource),"Returning without cleanup preserves the exact list and navigation history");
            DuplicateMenu((ContextMenu)Field(dupBrowser,"more")!)!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var dupCancelView=(DuplicateFilesView)((ContentControl)dupBrowser.FindName("DuplicateHost")!).Content;
            var dupPending=InvokeTask(dupCancelView,"StartScanAsync");
            ((Button)dupCancelView.FindName("CancelButton")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitTask(dupPending,app.Dispatcher);
            Check(((SelectableText)dupCancelView.FindName("Status")!).Text.StartsWith("Scan cancelled",StringComparison.Ordinal),"Cancel Scan provides visible feedback even for a fast fixture scan");
            Check((string)Field(dupCancelView,"state")! =="cancelled"&&((ListBox)dupCancelView.FindName("Results")).Items.Cast<DuplicateGroupCard>().All(card=>card.Copies.All(row=>!row.Selected)),"Cancelled scans retain only verified partial groups without selecting copies");
            var invalidDuplicate=new DuplicateFilesView(false,"",log,new ApplicationInventoryService());
            Check(!((Button)invalidDuplicate.FindName("ScanButton")!).IsEnabled,"Virtual or missing locations require a physical folder before scanning");
            dupHost.Close();
            SetPalette(app,"#100C0B","#F9EAD9","#C4A78D","#DEA66E","#302018","#765239");


            // === Version display ===
            var home = new HomeView(false, new ApplicationInventoryService());
            Check(((SelectableText)home.FindName("VersionText")!).Text == "v" + AppInfo.Version,
                "Home view displays v" + AppInfo.Version);

            // === What's New page shows the installed version only ===
            foreach (bool arabicNotes in new[] { false, true })
            {
                // Re-apply the palette per host: the first dispatcher pump lets
                // the real App.OnStartup restore the saved user theme.
                SetPalette(app, "#100C0B", "#F9EAD9", "#C4A78D", "#DEA66E", "#302018", "#765239");
                string language = arabicNotes ? "AR" : "EN";
                var notesPage = new ReleaseNotesView(arabicNotes);
                var notesHost = new Window
                {
                    Content = notesPage,
                    Width = 900,
                    Height = 700,
                    Background = (Brush)app.Resources["ChromeBackground"],
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -4000,
                    Top = -4000,
                    ShowInTaskbar = false
                };
                notesHost.Show();
                Pump(app.Dispatcher);
                notesHost.UpdateLayout();

                var expander = FindAllVisual<Expander>(notesPage).Single();
                Check((expander.Header as string) == (arabicNotes ? "عرض جميع التغييرات" : "Show All Changes") && !expander.IsExpanded,
                    "Show All Changes keeps its localized label and initial state (" + language + ")");
                var noteTexts = FindAllVisual<TextBlock>(notesPage).Select(block => block.Text).ToList();
                Check(noteTexts.Contains((arabicNotes ? "ما الجديد في v" : "What's New in v") + AppInfo.Version),
                    "The What's New heading names the installed version (" + language + ")");
                Check(!noteTexts.Any(text => text.Contains("Previous Releases", StringComparison.Ordinal) ||
                                             text.Contains("الإصدارات السابقة", StringComparison.Ordinal)),
                    "Previous Releases is absent from the What's New page (" + language + ")");

                var currentNote = ReleaseNotesService.FindCurrent(ReleaseNotesService.Load(arabicNotes), AppInfo.Version);
                Check(currentNote != null && currentNote.Version == AppInfo.Version,
                    "Release notes exist for the installed version (" + language + ")");

                if (Environment.GetEnvironmentVariable("POLAR_UI_SNAPSHOT") == "1")
                {
                    string snapshotDir = Path.Combine(Path.GetTempPath(), "polar-update-snapshots");
                    Directory.CreateDirectory(snapshotDir);
                    Snapshot(notesHost, Path.Combine(snapshotDir, arabicNotes ? "whatsnew-ar.png" : "whatsnew-en.png"));
                }

                expander.IsExpanded = true;
                var allChanges = (RichTextBox)expander.Content;
                Check(currentNote != null && allChanges.Document.Blocks.Count == currentNote.Entries.Count,
                    "The expanded list contains only the installed version's entries (" + language + ")");
                notesHost.Close();
            }

            // === What's New page follows all five themes ===
            var notesThemes = new (string Text, string Accent)[]
            {
                ("#EAF4FF", "#34C5F5"),
                ("#F4F7FA", "#B5D7E9"),
                ("#172333", "#126CAC"),
                ("#302219", "#79512F"),
                ("#F9EAD9", "#DEA66E")
            };
            bool notesThemeText = true;
            foreach (var theme in notesThemes)
            {
                app.Resources["HomeTextBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(theme.Text));
                app.Resources["HomeDimBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(theme.Text));
                app.Resources["HomeBorderBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(theme.Accent));
                app.Resources["ChromeAccent"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(theme.Accent));
                var themedPage = new ReleaseNotesView(false);
                var themedHost = new Window
                {
                    Content = themedPage,
                    Width = 800,
                    Height = 600,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -4000,
                    Top = -4000,
                    ShowInTaskbar = false
                };
                themedHost.Show();
                Pump(app.Dispatcher);
                themedHost.UpdateLayout();
                var pageTitle = FindAllVisual<TextBlock>(themedPage).FirstOrDefault(block => block.Text == "What's New in Polar?");
                notesThemeText &= pageTitle?.Foreground is SolidColorBrush titleBrush &&
                                  titleBrush.Color == (Color)ColorConverter.ConvertFromString(theme.Text);
                themedHost.Close();
            }
            Check(notesThemeText, "The What's New page follows all five theme foregrounds");

            // === Activity Log remains functional ===
            var mainWindow = Application.Current.MainWindow as MainWindow;
            Check(mainWindow != null, "The main window is available for the Activity Log check");
            if (mainWindow != null)
            {
                var logList = mainWindow.FindName("Log") as ListBox;
                var activity = Field(mainWindow, "activity") as ActivityLogService;
                int entriesBefore = logList?.Items.Count ?? -1;
                activity?.Write("INFO", "Activity Log probe entry");
                Pump(app.Dispatcher);
                Check(logList != null && activity != null && logList.Items.Count > entriesBefore,
                    "The Activity Log still receives and displays entries");
                Check(mainWindow.FindName("LogToggleButton") is Button &&
                      mainWindow.FindName("LogClearButton") is Button,
                    "Activity Log controls remain available");
            }

            // === Dedicated update window ===
            string longNotes = "## What's New\n\n" + string.Join("\n", Enumerable.Range(1, 70)
                .Select(i => $"- **Change {i}**: Improved update reliability and readable release notes."));
            var sampleRelease = new WpfRelease("1.0.138", "Polar Uninstaller v1.0.138", longNotes,
                DateTimeOffset.Now, "https://github.com/Ayh-m1/Polar-Uninstaller/releases/tag/v1.0.138",
                new[] { "PolarUninstaller.exe" })
            {
                Downloads = new[]
                {
                    new WpfAsset("PolarUninstaller.exe",
                        "https://github.com/Ayh-m1/Polar-Uninstaller/releases/download/v1.0.138/PolarUninstaller.exe",
                        64 * 1024 * 1024, "sha256:" + new string('a', 64))
                }
            };
            var updateWindow = new UpdateCenterWindow(false, new UpdateResult(UpdateState.Available, sampleRelease))
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -4000, Top = -4000, ShowInTaskbar = false
            };
            updateWindow.Show();
            Pump(app.Dispatcher);
            updateWindow.UpdateLayout();

            Check(updateWindow.WindowStyle == WindowStyle.SingleBorderWindow && updateWindow.ResizeMode == ResizeMode.CanResize,
                "Update window keeps native title-bar controls and free resizing");
            Check(updateWindow.MinWidth == 600 && updateWindow.MinHeight == 600,
                "Update window exposes a practical minimum size");
            var nowButton = (Button)updateWindow.FindName("UpdateNowButton")!;
            var laterButton = (Button)updateWindow.FindName("UpdateLaterButton")!;
            Check(nowButton.Content as string == "Update Now" && laterButton.Content as string == "Update Later",
                "Update window shows Update Now and Update Later");
            Check(nowButton.IsEnabled && Equals(nowButton.Visibility, Visibility.Visible) &&
                  Equals(laterButton.Visibility, Visibility.Visible),
                "Both decision buttons are available at the available-update state");
            Check(nowButton.Foreground is SolidColorBrush primaryText &&
                  primaryText.Color == Color.FromRgb(0x12, 0x18, 0x1F),
                "The primary Update Now label uses a high-contrast text color");
            var notesViewer = (FlowDocumentScrollViewer)updateWindow.FindName("NotesViewer")!;
            Check(notesViewer.Document.Blocks.Count >= 70,
                "The complete long release notes are rendered into the scrollable document");
            Check(notesViewer.VerticalScrollBarVisibility == ScrollBarVisibility.Auto &&
                  notesViewer.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled,
                "Release notes scroll vertically and never overflow horizontally");
            var notesScroller = FindFirstVisual<ScrollViewer>(notesViewer);
            Check(notesScroller != null && notesScroller.ScrollableHeight > 0,
                "Long release notes are scrollable");

            if (Environment.GetEnvironmentVariable("POLAR_UI_SNAPSHOT") == "1")
            {
                // Re-apply the Espresso palette: pumping the dispatcher lets the
                // real App.OnStartup run and restore the saved user theme.
                SetPalette(app, "#100C0B", "#F9EAD9", "#C4A78D", "#DEA66E", "#302018", "#765239");
                Pump(app.Dispatcher);
                string snapshotDir = Path.Combine(Path.GetTempPath(), "polar-update-snapshots");
                Directory.CreateDirectory(snapshotDir);
                Snapshot(updateWindow, Path.Combine(snapshotDir, "update-en.png"));
            }

            updateWindow.Width = updateWindow.MinWidth;
            updateWindow.Height = updateWindow.MinHeight;
            var progressPanel = (StackPanel)updateWindow.FindName("ProgressPanel")!;
            var statusText = (SelectableText)updateWindow.FindName("StatusText")!;
            progressPanel.Visibility = Visibility.Visible;
            statusText.Text = "Downloading update… 42% · 27.0 / 64.0 MB · Speed: 8.20 MB/s";
            updateWindow.UpdateLayout();
            Pump(app.Dispatcher);
            var nowBounds = Bounds(nowButton, updateWindow);
            var laterBounds = Bounds(laterButton, updateWindow);
            Check(nowBounds.Right <= updateWindow.ActualWidth + 0.5 && nowBounds.Bottom <= updateWindow.ActualHeight + 0.5 &&
                  laterBounds.Left >= -0.5 && laterBounds.Bottom <= updateWindow.ActualHeight + 0.5,
                "Both footer buttons stay inside the visible area at the minimum size with progress shown");
            Check(notesViewer.ActualHeight >= 100,
                "Release notes keep a usable reading area at the minimum window size");
            Check(nowBounds.Left > laterBounds.Left,
                "English layout places the primary Update Now button at the trailing edge");

            var bilingualRelease = sampleRelease with
            {
                Notes = "## English\n### What's New\n- **Faster** update checks\n- Redesigned update window\n\n## العربية\n### ما الجديد\n- **فحص تحديثات أسرع**\n- نافذة تحديث مُعاد تصميمها\n- إصلاح عرض ملاحظات الإصدار"
            };
            var rtlUpdate = new UpdateCenterWindow(true, new UpdateResult(UpdateState.Available, bilingualRelease))
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -4000, Top = -4000, ShowInTaskbar = false
            };
            rtlUpdate.Show();
            Pump(app.Dispatcher);
            rtlUpdate.UpdateLayout();
            var rtlNow = (Button)rtlUpdate.FindName("UpdateNowButton")!;
            var rtlLater = (Button)rtlUpdate.FindName("UpdateLaterButton")!;
            Check(rtlUpdate.FlowDirection == FlowDirection.RightToLeft,
                "Arabic update window uses right-to-left flow");
            var rtlNotes = (FlowDocumentScrollViewer)rtlUpdate.FindName("NotesViewer")!;
            Check(rtlNotes.Document.FlowDirection == FlowDirection.RightToLeft,
                "Arabic release notes flow right-to-left in the Arabic window");

            var arFallbackWindow = new UpdateCenterWindow(true,
                new UpdateResult(UpdateState.Available, sampleRelease))
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -4000, Top = -4000, ShowInTaskbar = false
            };
            arFallbackWindow.Show();
            Pump(app.Dispatcher);
            var fallbackNotes = (FlowDocumentScrollViewer)arFallbackWindow.FindName("NotesViewer")!;
            Check(fallbackNotes.Document.FlowDirection == FlowDirection.LeftToRight,
                "English fallback notes stay left-to-right inside the Arabic window");
            arFallbackWindow.Close();

            var arabicNotesRelease = sampleRelease with
            {
                Notes = "## العربية\n- تحسين سرعة التحديث\n- إصلاح عرض ملاحظات الإصدار"
            };
            var ltrArabicNotesWindow = new UpdateCenterWindow(false,
                new UpdateResult(UpdateState.Available, arabicNotesRelease))
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -4000, Top = -4000, ShowInTaskbar = false
            };
            ltrArabicNotesWindow.Show();
            Pump(app.Dispatcher);
            var ltrArabicNotes = (FlowDocumentScrollViewer)ltrArabicNotesWindow.FindName("NotesViewer")!;
            Check(ltrArabicNotes.Document.FlowDirection == FlowDirection.RightToLeft,
                "Arabic notes flow right-to-left even in the English window");
            ltrArabicNotesWindow.Close();
            Check(rtlNow.Content as string == "تحديث الآن" && rtlLater.Content as string == "تحديث لاحقًا",
                "Arabic update window shows both localized decisions");
            Check(Bounds(rtlNow, rtlUpdate).Left < Bounds(rtlLater, rtlUpdate).Left,
                "Arabic layout mirrors the two decision buttons correctly");
            if (Environment.GetEnvironmentVariable("POLAR_UI_SNAPSHOT") == "1")
            {
                SetPalette(app, "#100C0B", "#F9EAD9", "#C4A78D", "#DEA66E", "#302018", "#765239");
                Pump(app.Dispatcher);
                string snapshotDir = Path.Combine(Path.GetTempPath(), "polar-update-snapshots");
                Directory.CreateDirectory(snapshotDir);
                Snapshot(rtlUpdate, Path.Combine(snapshotDir, "update-ar.png"));
            }

            // === Live language synchronization ===
            var syncRelease = sampleRelease with
            {
                Notes = "## English\n### What's New\n- **Faster** update checks\n\n## العربية\n### ما Nuevo\n- **فحص تحديثات أسرع**"
            };
            var syncWindow = new UpdateCenterWindow(false, new UpdateResult(UpdateState.Available, syncRelease))
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -4000, Top = -4000, ShowInTaskbar = false
            };
            syncWindow.Show();
            Pump(app.Dispatcher);
            syncWindow.UpdateLayout();
            var syncNow = (Button)syncWindow.FindName("UpdateNowButton")!;
            var syncLater = (Button)syncWindow.FindName("UpdateLaterButton")!;
            var syncHeading = (TextBlock)syncWindow.FindName("Heading")!;
            var syncStatus = (TextBlock)syncWindow.FindName("StatusPillText")!;
            var syncViewer = (FlowDocumentScrollViewer)syncWindow.FindName("NotesViewer")!;
            Check(syncWindow.Title == "Polar Uninstaller Update" && syncNow.Content as string == "Update Now" &&
                  syncLater.Content as string == "Update Later" && syncHeading.Text == "Update Available" &&
                  syncStatus.Text == "Update available",
                "English Update Center starts in English");
            Check(syncWindow.FlowDirection == FlowDirection.LeftToRight,
                "English Update Center uses left-to-right flow");

            syncWindow.SetLanguage(true);
            Pump(app.Dispatcher);
            syncWindow.UpdateLayout();
            var syncViewerAr = (FlowDocumentScrollViewer)syncWindow.FindName("NotesViewer")!;
            string arNotes = new TextRange(syncViewerAr.Document.ContentStart, syncViewerAr.Document.ContentEnd).Text;
            Check(syncWindow.Title == "تحديث Polar Uninstaller" && syncNow.Content as string == "تحديث الآن" &&
                  syncLater.Content as string == "تحديث لاحقًا" && syncHeading.Text == "يتوفر تحديث جديد" &&
                  syncStatus.Text == "يتوفر تحديث",
                "SetLanguage switches the Update Center to Arabic");
            Check(syncWindow.FlowDirection == FlowDirection.RightToLeft,
                "Arabic Update Center uses right-to-left flow");
            Check(arNotes.Contains("أسرع", StringComparison.Ordinal) && !arNotes.Contains("Faster", StringComparison.Ordinal),
                "Live language switch shows Arabic release notes");
            Check(syncViewerAr.Document.FlowDirection == FlowDirection.RightToLeft,
                "Arabic notes flow right-to-left after a live switch");

            syncWindow.SetLanguage(false);
            Pump(app.Dispatcher);
            syncWindow.UpdateLayout();
            var syncViewerEn = (FlowDocumentScrollViewer)syncWindow.FindName("NotesViewer")!;
            string enNotes = new TextRange(syncViewerEn.Document.ContentStart, syncViewerEn.Document.ContentEnd).Text;
            Check(syncWindow.Title == "Polar Uninstaller Update" && syncNow.Content as string == "Update Now",
                "SetLanguage switches the Update Center back to English");
            Check(enNotes.Contains("Faster", StringComparison.Ordinal) && !enNotes.Contains("أسرع", StringComparison.Ordinal),
                "Live language switch restores English release notes");
            syncWindow.Close();

            var themes = new (string Background, string Text, string Accent)[]
            {
                ("#061125", "#EAF4FF", "#34C5F5"),
                ("#08090C", "#F4F7FA", "#B5D7E9"),
                ("#EDF1F5", "#172333", "#126CAC"),
                ("#E3D0B3", "#302219", "#79512F"),
                ("#100C0B", "#F9EAD9", "#DEA66E")
            };
            bool themeBackgrounds = true, themeText = true;
            foreach (var theme in themes)
            {
                app.Resources["ChromeBackground"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(theme.Background));
                app.Resources["ChromeText"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(theme.Text));
                app.Resources["ChromeAccent"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(theme.Accent));
                app.Resources["ChromeAccentText"] = Brushes.White;
                app.Resources["HomeCardBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(theme.Background));
                app.Resources["HomeDimBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(theme.Text));
                app.Resources["ChromeBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(theme.Accent));
                Pump(app.Dispatcher);
                themeBackgrounds &= updateWindow.Background is SolidColorBrush background &&
                                     background.Color == (Color)ColorConverter.ConvertFromString(theme.Background);
                var firstBlock = notesViewer.Document.Blocks.OfType<Paragraph>()
                    .FirstOrDefault(block => block.FontSize == 14);
                themeText &= firstBlock?.Foreground is SolidColorBrush foreground &&
                              foreground.Color == (Color)ColorConverter.ConvertFromString(theme.Text);
            }
            Check(themeBackgrounds, "The update window follows all five theme backgrounds");
            Check(themeText, "Release-note text follows the active theme foreground");

            rtlUpdate.Close();
            updateWindow.Close();

            var first = new FileBrowserView(false, folderA, log, null, settings);
            var firstWindow = Show(first);
            var firstFiles = (ListView)first.FindName("Files")!;
            Check(WaitFor(() => firstFiles.Items.Count > 0, app.Dispatcher), "First folder loads its items");
            Check(WaitFor(() => AllFoldersSized(firstFiles), app.Dispatcher, 15000),
                "Folder sizes are calculated automatically without pressing Calculate Folder Size");
            CheckFolderSize(firstFiles, "folder-a1", 1000);
            CheckFolderSize(firstFiles, "folder-a2", 2000);
            Check(firstFiles.SelectionMode == SelectionMode.Extended, "Runtime selection mode is Extended");

            WaitTask(InvokeTask(first, "SortBySizeAsync", true), app.Dispatcher);
            WaitFor(() => IsSortedBySize(firstFiles), app.Dispatcher, 10000);
            Check(IsSortedBySize(firstFiles), "Size — Largest to Smallest sorts the first folder by real sizes");
            Check(SortArrow(first) == "▼", "Size header shows the descending indicator");

            WaitTask(InvokeTask(first, "Navigate", folderB, null), app.Dispatcher);
            WaitFor(() => firstFiles.Items.Count > 0 && AllFoldersSized(firstFiles), app.Dispatcher, 15000);
            CheckFolderSize(firstFiles, "folder-b1", 4000);
            CheckFolderSize(firstFiles, "folder-b2", 500);
            Check(IsSortedBySize(firstFiles), "Navigating to another folder keeps the size order applied");

            WaitTask(InvokeTask(first, "Navigate", folderA, null), app.Dispatcher);
            WaitFor(() => firstFiles.Items.Count > 0 && AllFoldersSized(firstFiles), app.Dispatcher, 15000);
            Check(IsSortedBySize(firstFiles), "Navigating back keeps the size order applied");

            firstWindow.Close();

            // Clearing the measurement cache forces a genuine asynchronous
            // calculation, as after a real application restart.
            FolderSizeService.ClearCache();

            // A brand new view sharing the same settings simulates a restart.
            var second = new FileBrowserView(false, folderB, log, null, new SettingsService(settingsDir));
            var secondWindow = Show(second);
            var secondFiles = (ListView)second.FindName("Files")!;
            Check(WaitFor(() => secondFiles.Items.Count > 0 && AllFoldersSized(secondFiles), app.Dispatcher, 15000),
                "Restarted view loads folder sizes automatically");
            Check((string)Field(second, "sort")! == "Size", "Restarted view restores the Size sort field");
            Check((ListSortDirection)Field(second, "direction")! == ListSortDirection.Descending,
                "Restarted view restores the descending direction");
            Check(WaitFor(() => IsSortedBySize(secondFiles), app.Dispatcher, 10000),
                "Restarted view re-sorts automatically once the asynchronous sizes arrive");
            Check(SortArrow(second) == "▼", "Restarted view shows the matching sort indicator");

            // A stale size pass must never touch the collection of a newer folder.
            var sizesBeforeStalePass = secondFiles.Items.Cast<BrowserEntry>().Select(entry => entry.Size).ToArray();
            WaitTask(InvokeTask(second, "AutoSizeFoldersAsync",
                secondFiles.Items.Cast<BrowserEntry>().ToList(), 0), app.Dispatcher);
            var sizesAfterStalePass = secondFiles.Items.Cast<BrowserEntry>().Select(entry => entry.Size).ToArray();
            Check(sizesBeforeStalePass.SequenceEqual(sizesAfterStalePass),
                "A stale size pass from a previous folder cannot change the current folder");

            // Multi-selection model, range selection and the Details summary.
            secondFiles.UnselectAll();
            secondFiles.SelectedItems.Add(secondFiles.Items[0]);
            secondFiles.SelectedItems.Add(secondFiles.Items[1]);
            Pump(app.Dispatcher);
            Check(secondFiles.SelectedItems.Count == 2, "Two items can remain selected at the same time");
            var heading = (TextBlock)second.FindName("DetailsHeading")!;
            Check(heading.Text.Contains("2 selected", StringComparison.OrdinalIgnoreCase),
                "Details panel reports the number of selected items");

            Invoke(second, "SelectRange", 0, 3, false);
            Pump(app.Dispatcher);
            Check(secondFiles.SelectedItems.Count == 4, "Shift-style range selection selects a continuous range");

            secondFiles.SelectAll();
            Pump(app.Dispatcher);
            Check(secondFiles.SelectedItems.Count == secondFiles.Items.Count, "Select all selects every current-folder item");

            secondFiles.UnselectAll();
            var container0 = secondFiles.ItemContainerGenerator.ContainerFromIndex(0) as ListViewItem;
            var container1 = secondFiles.ItemContainerGenerator.ContainerFromIndex(1) as ListViewItem;
            Check(container0 != null && container1 != null, "Row containers are realized for interaction");
            if (container0 != null && container1 != null)
            {
                RaiseLeftClick(container0);
                RaiseLeftClick(container1);
                Pump(app.Dispatcher);
                Check(secondFiles.SelectedItems.Count == 1 && container1.IsSelected,
                    "A normal click still selects exactly one row");
            }

            // === Expanded selection hitbox ===
            secondFiles.UnselectAll();
            Pump(app.Dispatcher);
            var hitboxes = new List<Border>();
            bool hitboxSizeOk = true, hitboxClearsIcon = true, indicatorSmall = true, hitboxOnLeadingEdge = true, hitboxFitsRow = true;
            for (int i = 0; i < 5; i++)
            {
                var container = secondFiles.ItemContainerGenerator.ContainerFromIndex(i) as ListViewItem;
                if (container == null) { hitboxSizeOk = false; break; }
                container.UpdateLayout();
                var hitbox = FindVisual<Border>(container, "SelectionHitbox");
                var icon = FindVisual<Grid>(container, "EntryIcon");
                var indicator = FindVisual<TextBlock>(container, "SelectionIndicator");
                if (hitbox == null || icon == null || indicator == null) { hitboxSizeOk = false; break; }
                hitboxes.Add(hitbox);
                hitboxSizeOk &= hitbox.Width >= 36 && hitbox.Width <= 44 &&
                                hitbox.Height >= 36 && hitbox.Height <= 44;
                var hb = Bounds(hitbox, container);
                var ib = Bounds(icon, container);
                hitboxClearsIcon &= !hb.IntersectsWith(ib);
                hitboxOnLeadingEdge &= hb.Right <= ib.Left + 0.5;
                hitboxFitsRow &= hitbox.ActualHeight <= container.ActualHeight;
                indicatorSmall &= indicator.ActualWidth < hitbox.ActualWidth / 2 &&
                                  indicator.ActualHeight < hitbox.ActualHeight / 2;
            }
            Check(hitboxes.Count == 5 && hitboxSizeOk, "Selection hitboxes are 36–44 DIPs on every row");
            Check(hitboxClearsIcon, "The expanded hitbox never overlaps the entry icon");
            Check(hitboxOnLeadingEdge, "The hitbox extends into the row's leading blank space");
            Check(indicatorSmall, "The visible circle/checkmark keeps its original small size");
            Check(hitboxFitsRow, "The expanded hitbox fits inside the existing row height");

            bool consecutive = hitboxes.Count == 5;
            for (int i = 0; i < hitboxes.Count; i++)
            {
                RaiseLeftClick(hitboxes[i]);
                Pump(app.Dispatcher);
                consecutive &= secondFiles.SelectedItems.Count == i + 1;
            }
            Check(consecutive, "Clicking five expanded hitboxes without Ctrl keeps all five items selected");
            Check(secondFiles.SelectedItems.Count == 5, "All five hitbox selections remain in the selection model");

            if (hitboxes.Count >= 3)
            {
                var third = secondFiles.SelectedItems.Cast<BrowserEntry>().ElementAtOrDefault(2);
                RaiseLeftClick(hitboxes[2]);
                Pump(app.Dispatcher);
                Check(secondFiles.SelectedItems.Count == 4 && third != null && !secondFiles.SelectedItems.Contains(third),
                    "Clicking a selected hitbox again deselects only that item");
            }

            if (hitboxes.Count > 0 && secondFiles.ItemContainerGenerator.ContainerFromIndex(0) is ListViewItem hitContainer)
            {
                var hitbox = hitboxes[0];
                var hb = Bounds(hitbox, hitContainer);
                var blank = new Point(2, hb.Top + hb.Height / 2);
                Check(ReferenceEquals(hitContainer.InputHitTest(blank), hitbox),
                    "The transparent area around the circle receives the click");
                bool allPointsHit = true;
                foreach (double x in new[] { 1.0, 10.0, 23.0, 36.0 })
                    foreach (double y in new[] { hb.Top + 2, hb.Top + hb.Height / 2, hb.Bottom - 2 })
                        allPointsHit &= ReferenceEquals(hitContainer.InputHitTest(new Point(x, y)), hitbox);
                Check(allPointsHit, "Every point inside the expanded hitbox is clickable");
                var icon = FindVisual<Grid>(hitContainer, "EntryIcon");
                if (icon != null)
                {
                    var ib = Bounds(icon, hitContainer);
                    var iconHit = hitContainer.InputHitTest(new Point(ib.Left + ib.Width / 2, ib.Top + ib.Height / 2));
                    Check(!ReferenceEquals(iconHit, hitbox),
                        "Clicking the entry icon is not captured by the selection hitbox");
                }
            }

            // A normal row click outside the hitbox still selects exactly one item.
            secondFiles.UnselectAll();
            Pump(app.Dispatcher);
            secondFiles.UpdateLayout();
            var normal1 = secondFiles.ItemContainerGenerator.ContainerFromIndex(1) as ListViewItem;
            if (normal1 != null)
            {
                var normalEntry=normal1.DataContext;
                RaiseLeftClick(normal1);
                Pump(app.Dispatcher);
                // An asynchronous size/sort refresh can recycle normal1 after
                // the click. Assert the actual selection, not that stale shell.
                Check(secondFiles.SelectedItems.Count == 1 && ReferenceEquals(secondFiles.SelectedItem,normalEntry),
                    "A row click outside the hitbox still selects exactly one item");
            }

            // Keyboard selection remains native; verify a focused row responds to Space.
            secondFiles.UnselectAll();
            Pump(app.Dispatcher);
            var keyContainer = secondFiles.ItemContainerGenerator.ContainerFromIndex(2) as ListViewItem;
            if (keyContainer != null)
            {
                bool keyFocused = false;
                for (int attempt = 0; attempt < 20 && !keyFocused; attempt++)
                {
                    keyContainer.Focus();
                    Pump(app.Dispatcher);
                    keyFocused = keyContainer.IsKeyboardFocused;
                    if (!keyFocused) Thread.Sleep(10);
                }
                if (keyFocused)
                {
                    RaiseKey(keyContainer, System.Windows.Input.Key.Space);
                    bool spaceSelected = WaitFor(
                        () => keyContainer.IsSelected && secondFiles.SelectedItems.Count == 1,
                        app.Dispatcher, 2000);
                    Check(spaceSelected,
                        "Keyboard Space selects the focused row without breaking extended selection");
                    RaiseKey(keyContainer, System.Windows.Input.Key.Down);
                    var arrowNext = secondFiles.ItemContainerGenerator.ContainerFromIndex(3) as ListViewItem;
                    bool arrowSelected = WaitFor(
                        () => arrowNext != null && arrowNext.IsSelected && secondFiles.SelectedItems.Count == 1,
                        app.Dispatcher, 2000);
                    Check(arrowSelected,
                        "Arrow Down moves the selection to the next row");
                }
                else Console.WriteLine("NOTE: keyboard focus could not be acquired in this session; keyboard navigation was not exercised");
            }

            // === Arabic RTL uses the same expanded hitbox on the mirrored edge ===
            var rtl = new FileBrowserView(true, folderB, log, null, new SettingsService(settingsDir));
            var rtlWindow = Show(rtl);
            var rtlFiles = (ListView)rtl.FindName("Files")!;
            Check(WaitFor(() => rtlFiles.Items.Count > 0, app.Dispatcher, 15000), "RTL view loads its items");
            Check(rtl.FlowDirection == FlowDirection.RightToLeft, "RTL view uses right-to-left flow");
            rtlFiles.UnselectAll();
            Pump(app.Dispatcher);
            var rtlHitboxes = new List<Border>();
            bool rtlSizeOk = true, rtlClearsIcon = true, rtlOnTrailingEdge = true;
            for (int i = 0; i < 5; i++)
            {
                var container = rtlFiles.ItemContainerGenerator.ContainerFromIndex(i) as ListViewItem;
                if (container == null) { rtlSizeOk = false; break; }
                container.UpdateLayout();
                var hitbox = FindVisual<Border>(container, "SelectionHitbox");
                var icon = FindVisual<Grid>(container, "EntryIcon");
                if (hitbox == null || icon == null) { rtlSizeOk = false; break; }
                rtlHitboxes.Add(hitbox);
                rtlSizeOk &= hitbox.Width >= 36 && hitbox.Width <= 44 && hitbox.Height >= 36 && hitbox.Height <= 44;
                var hb = Bounds(hitbox, container);
                var ib = Bounds(icon, container);
                rtlClearsIcon &= !hb.IntersectsWith(ib);
                rtlOnTrailingEdge &= hb.Right <= ib.Left + 0.5;
            }
            Check(rtlHitboxes.Count == 5 && rtlSizeOk, "RTL selection hitboxes are 36–44 DIPs on every row");
            Check(rtlClearsIcon, "The RTL hitbox never overlaps the entry icon");
            Check(rtlOnTrailingEdge, "The RTL hitbox stays on the indicator's leading edge without covering the icon");
            bool rtlConsecutive = rtlHitboxes.Count == 5;
            for (int i = 0; i < rtlHitboxes.Count; i++)
            {
                RaiseLeftClick(rtlHitboxes[i]);
                Pump(app.Dispatcher);
                rtlConsecutive &= rtlFiles.SelectedItems.Count == i + 1;
            }
            Check(rtlConsecutive, "RTL: five consecutive hitbox clicks keep all five items selected");
            rtlWindow.Close();

            secondWindow.Close();
        }
        catch (Exception error)
        {
            failures++;
            Console.WriteLine("FAIL: probe threw " + error.GetType().Name + ": " + error.Message);
        }
        finally
        {
            try { Directory.Delete(baseDir, true); } catch { }
            app.Shutdown();
        }

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "UI PROBE PASSED" : $"UI PROBE FAILED ({failures})");
        Environment.Exit(failures == 0 ? 0 : 1);
    }

    private static void CreateFixture(string root, (string Name, int Bytes)[] files, (string Name, int Bytes)[] folders)
    {
        Directory.CreateDirectory(root);
        foreach (var file in files) File.WriteAllBytes(Path.Combine(root, file.Name), new byte[file.Bytes]);
        foreach (var folder in folders)
        {
            string path = Path.Combine(root, folder.Name);
            Directory.CreateDirectory(path);
            File.WriteAllBytes(Path.Combine(path, "data.bin"), new byte[folder.Bytes]);
        }
    }

    private static Window Show(FileBrowserView view)
    {
        var window = new Window
        {
            Content = view,
            Width = 1000,
            Height = 700,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Left = -4000,
            Top = -4000
        };
        window.Show();
        window.Activate();
        return window;
    }

    private static bool AllFoldersSized(ListView files) =>
        files.Items.Cast<BrowserEntry>().Where(entry => entry.IsDirectory)
            .All(entry => entry.Size.HasValue || entry.SizeState == BrowserSizeState.Unavailable);

    private static void CheckFolderSize(ListView files, string name, long expected)
    {
        var entry = files.Items.Cast<BrowserEntry>().FirstOrDefault(item => item.Name == name);
        Check(entry != null && entry.Size == expected,
            $"Folder {name} has its cumulative size ({entry?.Size?.ToString() ?? "null"} vs {expected})");
    }

    private static bool IsSortedBySize(ListView files)
    {
        var sizes = files.Items.Cast<BrowserEntry>().Where(entry => entry.Size.HasValue).Select(entry => entry.Size!.Value).ToArray();
        for (int i = 1; i < sizes.Length; i++) if (sizes[i] > sizes[i - 1]) return false;
        return sizes.Length > 1;
    }

    private static string SortArrow(FileBrowserView view)
    {
        var arrow = Field(view, "sizeSortArrow") as TextBlock;
        return arrow?.Text ?? "";
    }

    private static object? Field(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);

    private static Task InvokeTask(object target, string name, params object?[] args) =>
        (Task)target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args)!;

    private static void Invoke(object target, string name, params object?[] args) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);

    private static void Check(bool condition, string name)
    {
        if (!condition) failures++;
        Console.WriteLine((condition ? "PASS" : "FAIL") + ": " + name);
    }

    private static bool WaitFor(Func<bool> condition, Dispatcher dispatcher, int timeoutMs = 10000)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.ElapsedMilliseconds < timeoutMs)
        {
            Pump(dispatcher);
            Thread.Sleep(25);
        }
        return condition();
    }

    private static void WaitTask(Task task, Dispatcher dispatcher, int timeoutMs = 15000)
    {
        var watch = Stopwatch.StartNew();
        while (!task.IsCompleted && watch.ElapsedMilliseconds < timeoutMs)
        {
            Pump(dispatcher);
            Thread.Sleep(20);
        }
        if (task.IsFaulted) throw task.Exception!;
    }

    private static void RaiseLeftClick(UIElement target)
    {
        var args = new System.Windows.Input.MouseButtonEventArgs(
            System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount,
            System.Windows.Input.MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonDownEvent,
            Source = target
        };
        target.RaiseEvent(args);
    }

    private static Rect Bounds(FrameworkElement element, Visual ancestor) =>
        element.TransformToAncestor(ancestor).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    private static T? FindVisual<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        if (root is T match && match.Name == name) return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var found = FindVisual<T>(VisualTreeHelper.GetChild(root, i), name);
            if (found != null) return found;
        }
        return null;
    }

    private static T? FindFirstVisual<T>(DependencyObject root) where T : FrameworkElement
    {
        if (root is T match) return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var found = FindFirstVisual<T>(VisualTreeHelper.GetChild(root, i));
            if (found != null) return found;
        }
        return null;
    }

    private static IEnumerable<T> FindAllVisual<T>(DependencyObject root) where T : FrameworkElement
    {
        if (root is T match) yield return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var found in FindAllVisual<T>(VisualTreeHelper.GetChild(root, i)))
                yield return found;
    }

    private static void RaiseKey(UIElement target, System.Windows.Input.Key key)
    {
        var source = PresentationSource.FromVisual(target);
        if (source == null) return;
        var args = new System.Windows.Input.KeyEventArgs(
            System.Windows.Input.Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        {
            RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent,
            Source = target
        };
        target.RaiseEvent(args);
    }

    private static void Pump(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void SetPalette(Application app, string background, string text, string dim, string accent, string card, string border)
    {
        app.Resources["ChromeBackground"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(background));
        app.Resources["ChromeText"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(text));
        app.Resources["HomeTextBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(text));
        app.Resources["ChromeHover"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(border));
        app.Resources["ChromeAccent"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accent));
        var accentColor = (Color)ColorConverter.ConvertFromString(accent);
        app.Resources["ChromeAccentText"] = new SolidColorBrush(
            (0.299 * accentColor.R + 0.587 * accentColor.G + 0.114 * accentColor.B) / 255.0 > 0.62
                ? Color.FromRgb(0x12, 0x18, 0x1F)
                : Colors.White);
        app.Resources["ChromeBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(border));
        app.Resources["HomeCardBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(card));
        app.Resources["HomeBorderBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(border));
        app.Resources["HomeDimBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dim));
        app.Resources["LogInfo"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dim));
    }

    private static void Snapshot(Window window, string path)
    {
        window.UpdateLayout();
        Pump(window.Dispatcher);
        int width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        int height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        Console.WriteLine("SNAPSHOT: " + path);
    }
}
