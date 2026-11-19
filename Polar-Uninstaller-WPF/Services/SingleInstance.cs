using System.Diagnostics;
using System.Runtime.InteropServices;
namespace PolarUninstallerWpf.Services;
public static class SingleInstance
{
    private static Mutex? lease;
    public static bool Acquire(string name="Local\\PolarUninstaller-WPF")
    {
        var mutex=new Mutex(true,name,out bool created);if(!created){mutex.Dispose();return false;}lease=mutex;return true;
    }
    public static void Release(){var mutex=Interlocked.Exchange(ref lease,null);if(mutex==null)return;try{mutex.ReleaseMutex();}catch(ApplicationException){}finally{mutex.Dispose();}}
    [DllImport("user32.dll")]private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]private static extern bool ShowWindow(IntPtr window,int command);
    public static void ActivateExisting()
    {
        using var self=Process.GetCurrentProcess();
        foreach(var process in Process.GetProcessesByName(self.ProcessName))using(process)
        try{if(process.Id!=self.Id&&process.MainWindowHandle!=IntPtr.Zero){ShowWindow(process.MainWindowHandle,9);SetForegroundWindow(process.MainWindowHandle);return;}}catch(InvalidOperationException){}
    }
}
