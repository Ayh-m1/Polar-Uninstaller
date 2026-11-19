using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
namespace PolarUninstallerWpf.Services;

/// <summary>Errors remain local. Recoverable I/O can continue; corrupted program state never does.</summary>
public static class AppSafety
{
    private static ActivityLogService? log;private static Func<bool> arabic=()=>false;
    private static readonly ConcurrentQueue<string> recent=new();
    private static readonly ConcurrentDictionary<string,long> notices=new();
    private static int installed,fatal;private static long lastDialog;
    public static string LogDirectory {get;set;}=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PolarUninstallerWPF-Development","Diagnostics");
    public static bool IsRecoverable(Exception error)=>error is IOException or UnauthorizedAccessException or OperationCanceledException or JsonException or System.Security.SecurityException||error is Win32Exception w&&w.NativeErrorCode is 1223 or 740 or 5 or 2||error is COMException com&&com.HResult is unchecked((int)0x80004005) or unchecked((int)0x80070005) or unchecked((int)0x80010001) or unchecked((int)0x8001010A);
    public static void AttachLog(ActivityLogService activity,Func<bool> language){if(log!=null)log.EntryAdded-=Capture;log=activity;arabic=language;log.EntryAdded+=Capture;}
    private static void Capture(ActivityEntry entry){recent.Enqueue(entry.Display);while(recent.Count>60)recent.TryDequeue(out _);}
    public static void Install(Application app,bool helper)
    {
        if(Interlocked.Exchange(ref installed,1)!=0)return;
        app.DispatcherUnhandledException+=(_,e)=>
        {
            if(IsRecoverable(e.Exception)){Report("UI action",e.Exception);e.Handled=true;long now=Environment.TickCount64;if(!helper&&now-Interlocked.Read(ref lastDialog)>30000){Interlocked.Exchange(ref lastDialog,now);MessageBox.Show(arabic()?"تعذر إكمال العملية. أعد المحاولة أو تحقق من صلاحية الوصول.":"The operation could not finish. Retry or check file access.","Polar Uninstaller",MessageBoxButton.OK,MessageBoxImage.Information);}}
            else
            {
                string? file=WriteCrash("Fatal UI error",e.Exception);e.Handled=true;
                if(!helper&&Interlocked.Exchange(ref fatal,1)==0)new RecoveryWindow(arabic(),file,e.Exception.ToString()).ShowDialog();
                app.Shutdown(1);
            }
        };
        AppDomain.CurrentDomain.UnhandledException+=(_,e)=>{if(e.ExceptionObject is Exception error)WriteCrash("Fatal process error",error);};
        TaskScheduler.UnobservedTaskException+=(_,e)=>{Report("Background operation",e.Exception);e.SetObserved();};
    }
    public static void Report(string context,Exception error)
    {
        CrashDiagnostics.Record(context,error);
        string key=context+"|"+error.GetType().FullName+"|"+error.Message;long now=Environment.TickCount64;
        if(notices.TryGetValue(key,out long last)&&now-last<10000)return;
        if(notices.Count>128)notices.Clear();notices[key]=now;
        log?.WriteBilingual("ERROR","The operation could not finish. Retry or check file access. ("+context+")","تعذر إكمال العملية. أعد المحاولة أو تحقق من صلاحية الوصول. ("+context+")");
    }
    public static string? WriteCrash(string context,Exception error)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);string file=Path.Combine(LogDirectory,"crash-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")+".txt");
            File.WriteAllText(file,string.Join(Environment.NewLine,new[]{DateTimeOffset.Now.ToString("O"),"Version: "+AppInfo.Version,"Windows: "+Environment.OSVersion,"Culture: "+CultureInfo.CurrentCulture.Name,context,error.ToString(),"Performance: "+JsonSerializer.Serialize(PerformanceTrace.Snapshot()),"Recent Activity:",string.Join(Environment.NewLine,recent)}));
            foreach(var old in new DirectoryInfo(LogDirectory).EnumerateFiles("crash-*.txt").OrderByDescending(f=>f.LastWriteTimeUtc).Skip(10))old.Delete();return file;
        }
        catch(Exception){return null;}
    }
}
