using System.IO;
using System.Runtime.InteropServices;

namespace PolarUninstallerWpf.Services;

public static class WindowsShellIconService
{
    // A distinct icon resource path avoids the previous EXE-path cache entry.
    public const string IconFileName="PolarLogo-v144-crisp.ico";
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]
    private static extern void SHChangeNotify(uint change,uint flags,string path,IntPtr other);

    public static void RefreshInstalledIcons()
    {
        var worker=new Thread(()=>
        {
            try
            {
                if(!UpdateInstallerService.IsInstalledCopy(out string? directory)||directory is null)return;
                string exe=Path.Combine(directory,"PolarUninstaller.exe");
                string icon=Path.Combine(directory,IconFileName);
                if(!File.Exists(icon))return;
                var folders=new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"Polar Uninstaller"),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),"Polar Uninstaller"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Microsoft","Internet Explorer","Quick Launch","User Pinned","TaskBar")
                };
                foreach(string folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        if(!Directory.Exists(folder))continue;
                        foreach(string shortcut in Directory.EnumerateFiles(folder,"*.lnk",SearchOption.TopDirectoryOnly))
                        {
                            try
                            {
                                if(ShellShortcutService.UpdateIconForTarget(shortcut,exe,icon))Notify(shortcut);
                            }
                            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or COMException or ArgumentException)
                            {CrashDiagnostics.Record("Refresh Polar shortcut icon",ex);}
                        }
                    }
                    catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                    {CrashDiagnostics.Record("Read Polar shortcut folder",ex);}
                }
                Notify(exe);Notify(icon);
            }
            catch(Exception ex){CrashDiagnostics.Record("Refresh installed Polar icons",ex);}
        }){IsBackground=true,Name="Polar shell icon refresh"};
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
    }
    private static void Notify(string path)=>SHChangeNotify(0x2000,0x3005,path,IntPtr.Zero);
}
