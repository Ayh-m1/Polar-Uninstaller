using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text.Json;
using System.Text.RegularExpressions;
using PolarUninstallerWpf;
using PolarUninstallerWpf.Services;

internal static partial class Parts146Checks
{
    internal static int RunLocale147()
    {
        Setup("Locale147");
        static string Rendered(TextBlock block)=>string.Concat(block.Inlines.OfType<Run>().Select(run=>run.Text));
        var scope=Path.Combine(root,"تجربة scan");
        Directory.CreateDirectory(scope);
        for(int i=0;i<250;i++) File.WriteAllBytes(Path.Combine(scope,$"{i}.bin"),[1,2,3]);

        Invoke(main,"NavigateTo","dashboard");
        var host=(ContentControl)main.FindName("PageContent");
        var dashboard=(DashboardView)host.Content;
        dashboard.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        var log=(ActivityLogService)Field(main,"activity")!;
        var session=(DashboardScanSession)Field(dashboard,"scanSession")!;
        var running=(Task)Invoke(dashboard,"RunStorageScanAsync",scope,false)!;
        Check(session.Phase==DashboardScanPhase.Running,"Owned storage scan starts through the Dashboard");
        for(int i=0;i<5;i++)
        {
            Invoke(main,"SwitchLanguage",main,new RoutedEventArgs());
            ((ComboBox)main.FindName("ThemePicker")).SelectedIndex=i;
            Check(ReferenceEquals(host.Content,dashboard)&&session.Phase==DashboardScanPhase.Running,"Language and theme keep the active Dashboard and scan");
        }
        var status=(TextBlock)dashboard.FindName("ScanStatus");
        Check(Rendered(status).Contains("جارٍ")&&status.Inlines.OfType<Run>().Any(r=>r.FlowDirection==FlowDirection.LeftToRight&&r.Text==scope),"Arabic progress keeps the full test path in one LTR run");

        dashboard.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        var replacement=new DashboardView(true,log,scanSession:session);
        host.Content=replacement;
        replacement.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Check(Rendered((TextBlock)replacement.FindName("ScanStatus")).Contains(scope),"Replacement Dashboard attaches to the existing scan");
        Wait(running);
        Pump();
        Check(session.Snapshot is {FilesScanned:250,TotalBytes:750},"Original scan completes with the correct fixture result");
        Check(Rendered((TextBlock)replacement.FindName("ScanStatus")).Contains("250"),"Replacement Dashboard receives final result");
        Check(log.Entries.Count(e=>e.Message.StartsWith("Dashboard storage scan started:"))==1&&
              log.Entries.Count(e=>e.Message.StartsWith("Dashboard storage scan completed."))==1,"One start and one completion without duplicate work");

        var duplicates=new DuplicateFilesView(true,scope,log,new ApplicationInventoryService(),new SettingsService(Path.Combine(root,"dup-settings")));
        var duplicateScan=(Task)Invoke(duplicates,"StartScanAsync")!;
        var duplicateToken=Field(duplicates,"operation");
        for(int i=0;i<5;i++)
        {
            duplicates.SetLanguage(i%2==0);
            ((ComboBox)main.FindName("ThemePicker")).SelectedIndex=i;
            Check(duplicateToken!=null&&ReferenceEquals(duplicateToken,Field(duplicates,"operation")),"Duplicate scan keeps its original cancellation token");
        }
        Wait(duplicateScan);
        Pump();
        Check(Field(duplicates,"result") is DuplicateScanResult {Groups.Count:1},"Duplicate scan completes with one verified content group");
        Check(log.Entries.Count(e=>e.Message.Contains("بدأ فحص الملفات المكررة"))==1&&
              log.Entries.Count(e=>e.Message.Contains("اكتمل فحص المكررات"))==1,"Duplicate scan logs one start and one completion");

        var inventoryGate=new TaskCompletionSource<IReadOnlyList<InstalledProgram>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inventoryCalls=0;
        var mockInventory=new ApplicationInventoryService(token=>
        {
            Interlocked.Increment(ref inventoryCalls);
            return inventoryGate.Task.WaitAsync(token);
        });
        var deep=new DeepUninstallerView(false,log,mockInventory,new SettingsService(Path.Combine(root,"deep-settings")));
        Set(main,"page","uninstall");
        host.Content=deep;
        if((bool)Field(main,"arabic")!) Invoke(main,"SwitchLanguage",main,new RoutedEventArgs());
        var inventoryTask=(Task)Invoke(deep,"Refresh")!;
        for(int i=0;i<5;i++)
        {
            Invoke(main,"SwitchLanguage",main,new RoutedEventArgs());
            ((ComboBox)main.FindName("ThemePicker")).SelectedIndex=i;
        }
        Check(ReferenceEquals(host.Content,deep)&&!inventoryTask.IsCompleted,"The active Deep inventory view stays attached");
        Check(Rendered((TextBlock)deep.FindName("Status")).Contains("جارٍ الفحص"),"Deep inventory status immediately translates");
        // An empty mock is classified as incomplete and cannot write an invented inventory baseline.
        inventoryGate.SetResult([]);
        Wait(inventoryTask);
        Pump();
        Check(inventoryCalls==1&&mockInventory.Current is {Count:0,IsComplete:false},"The incomplete mock inventory finishes once");
        Check(log.Entries.Count(e=>e.Message=="Application and launcher scan started.")==1
            &&log.Entries.Count(e=>e.Message.StartsWith("اكتمل فحص البرامج"))==1,"Deep inventory logs one start and one completion");
        host.Content=null;

        Check(DisplayFormat.BinarySize(85L<<30,true)==DisplayFormat.Isolate("85.0 GB"),"Size isolates number and unit together");
        Check(DisplayFormat.Percent(91,true)==DisplayFormat.Isolate("91%"),"Percent isolates the percent sign with the digits");
        Check(BidiText.IsolateLatin("القرص: C:")=="القرص: "+DisplayFormat.Isolate("C:"),"Drive run includes the colon");
        const string mixed="الملفات: 19,866 · الحجم: 13.6 GB · حُدّث: 11/19/2026 2:24 AM";
        var isolated=BidiText.IsolateLatin(mixed);
        Check(new[]{"19,866","13.6 GB","11/19/2026 2:24 AM"}.All(v=>isolated.Contains(DisplayFormat.Isolate(v))),"Composite Arabic status isolates grouped count, size and timestamp");
        Check(BidiText.IsolateLatin(isolated)==isolated,"Existing isolation is stable");
        var strings=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(
            Path.Combine(project,"Localization","FileBrowser.ar.json")))!;
        int audited=0;
        foreach(var (key,pattern) in strings)
        {
            if(!Regex.IsMatch(pattern,@"\{\d"))continue;
            string fixture=BrowserText.Get(key,true,866,755,644,533,422,311);
            Check(!Regex.IsMatch(Regex.Replace(fixture,@"\u2066[^\u2069]*\u2069",""),@"[0-9]"),"Arabic localization isolates formatted values: "+key);
            audited++;
        }
        Check(audited>0,"Scanned Arabic File Browser localization templates");

        var drive=new DriveSummary("C:\\",100L<<30,9L<<30){Label="Local Disk"};
        Invoke(replacement,"ShowDrive",drive);
        Snapshot(replacement,"Dashboard-ar.png");
        var browser=new FileBrowserView(true,scope,log,settings:new SettingsService(Path.Combine(root,"browser-settings")));
        Wait(browser.ShowFolderAsync(scope));
        Snapshot(browser,"FileBrowser-ar.png");
        return Finish("Locale147");

        void Snapshot(FrameworkElement element,string filename)
        {
            const int width=1280,height=800;
            var surface=new Border{Child=element};
            surface.SetResourceReference(Border.BackgroundProperty,"ChromeBackground");
            surface.Measure(new Size(width,height));
            surface.Arrange(new Rect(0,0,width,height));
            surface.UpdateLayout();
            var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
            bitmap.Render(surface);
            var encoder=new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output=File.Create(Path.Combine(root,filename));
            encoder.Save(output);
            surface.Child=null;
        }
    }
}
