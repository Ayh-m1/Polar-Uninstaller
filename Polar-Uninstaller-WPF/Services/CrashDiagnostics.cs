using System.IO;
using System.Collections.Concurrent;

namespace PolarUninstallerWpf.Services;

/// <summary>Bounded local diagnostics; never swallows a fatal dispatcher error.</summary>
public static class CrashDiagnostics
{
    private static readonly object Gate=new();
    public static string LogDirectory {get;set;}=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PolarUninstallerWPF-Development","Diagnostics");
    private static readonly ConcurrentQueue<(string Context,Exception Error)> Pending=new();
    private static int draining;
    public static void Record(string context,Exception error)
    {
        if(Pending.Count<128)Pending.Enqueue((context,error));Schedule();
    }
    private static void Schedule()
    {
        if(Interlocked.Exchange(ref draining,1)!=0)return;
        _=Task.Run(()=>{try{while(Pending.TryDequeue(out var entry))Persist(entry.Context,entry.Error);}finally{Interlocked.Exchange(ref draining,0);if(!Pending.IsEmpty)Schedule();}});
    }
    private static void Persist(string context,Exception error)
    {
        try
        {
            lock(Gate)
            {
                var directory=LogDirectory;
                Directory.CreateDirectory(directory);
                var path=Path.Combine(directory,"errors.log");
                if(File.Exists(path)&&new FileInfo(path).Length>1024*1024)File.Move(path,path+".previous",true);
                File.AppendAllText(path,$"{DateTimeOffset.Now:O} · v{AppInfo.Version} · {context}{Environment.NewLine}{error}{Environment.NewLine}");
            }
        }
        catch { /* Error reporting must not become a second crash. */ }
    }
}
