using System.IO;
using System.Runtime.InteropServices;

namespace PolarUninstallerWpf.Services;

/// <summary>
/// Keeps shortcuts created by older releases/installers aligned with the EXE's
/// embedded icon and invalidates Explorer's icon cache after an in-place update.
/// Icons always come from PolarUninstaller.exe (index 0); no separate .ico is
/// shipped or referenced by shortcuts any more.
/// </summary>
public static class WindowsShellIconService
{
    /// <summary>Standalone icon copy shipped by earlier v1.0.144 builds; removed on sight.</summary>
    public const string LegacyIconFileName="PolarLogo-v144-crisp.ico";

    private const uint ShcneUpdateItem=0x00002000;
    private const uint ShcneAssocChanged=0x08000000;
    private const uint ShcnfIdList=0x0000;
    private const uint ShcnfPathW=0x0005;
    private const uint ShcnfFlushNowAit=0x3000;

    [DllImport("shell32.dll",CharSet=CharSet.Unicode,EntryPoint="SHChangeNotify")]
    private static extern void SHChangeNotifyPath(uint change,uint flags,string path,IntPtr other);
    [DllImport("shell32.dll",EntryPoint="SHChangeNotify")]
    private static extern void SHChangeNotifyIdList(uint change,uint flags,IntPtr idList,IntPtr other);

    /// <summary>
    /// SHCNE_ASSOCCHANGED tells Explorer to rebuild its icon cache. Call this
    /// after an EXE has been installed or replaced in place.
    /// </summary>
    public static void NotifyIconCacheChanged()=>
        SHChangeNotifyIdList(ShcneAssocChanged,ShcnfIdList,IntPtr.Zero,IntPtr.Zero);

    public static void RefreshInstalledIcons()
    {
        var worker=new Thread(()=>
        {
            try
            {
                if(!UpdateInstallerService.IsInstalledCopy(out string? directory)||directory is null)return;
                string exe=Path.Combine(directory,"PolarUninstaller.exe");
                if(!File.Exists(exe))return;
                RemoveLegacyIconCopies(directory);
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
                                // The EXE owns the icon; index 0 selects its primary icon.
                                if(ShellShortcutService.UpdateIconForTarget(shortcut,exe,exe))NotifyPath(shortcut);
                            }
                            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or COMException or ArgumentException)
                            {CrashDiagnostics.Record("Refresh Polar shortcut icon",ex);}
                        }
                    }
                    catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                    {CrashDiagnostics.Record("Read Polar shortcut folder",ex);}
                }
                NotifyPath(exe);
                NotifyIconCacheChanged();
            }
            catch(Exception ex){CrashDiagnostics.Record("Refresh installed Polar icons",ex);}
        }){IsBackground=true,Name="Polar shell icon refresh"};
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
    }

    /// <summary>
    /// Earlier builds copied an .ico beside the EXE. A stale copy can survive an
    /// upgrade even when the EXE already carries the new icon, so remove it.
    /// </summary>
    private static void RemoveLegacyIconCopies(string directory)
    {
        try
        {
            foreach(string file in Directory.EnumerateFiles(directory,"PolarLogo*.ico",SearchOption.TopDirectoryOnly))
                try{File.Delete(file);}
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException){}
            string png=Path.Combine(directory,"PolarIcon.png");
            if(File.Exists(png))
                try{File.Delete(png);}
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException){}
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException){}
    }

    private static void NotifyPath(string path)=>
        SHChangeNotifyPath(ShcneUpdateItem,ShcnfPathW|ShcnfFlushNowAit,path,IntPtr.Zero);
}
