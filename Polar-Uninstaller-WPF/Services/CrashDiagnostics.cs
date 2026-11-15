using System.IO;

namespace PolarUninstallerWpf.Services;

/// <summary>Bounded local diagnostics; never swallows a fatal dispatcher error.</summary>
public static class CrashDiagnostics
{
    private static readonly object Gate=new();
    public static void Record(string context,Exception error)
    {
        try
        {
            lock(Gate)
            {
                var directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PolarUninstallerWPF-Development","Diagnostics");
                Directory.CreateDirectory(directory);
                var path=Path.Combine(directory,"errors.log");
                if(File.Exists(path)&&new FileInfo(path).Length>1024*1024)File.Move(path,path+".previous",true);
                File.AppendAllText(path,$"{DateTimeOffset.Now:O} · v{AppInfo.Version} · {context}{Environment.NewLine}{error}{Environment.NewLine}");
            }
        }
        catch { /* Error reporting must not become a second crash. */ }
    }
}
