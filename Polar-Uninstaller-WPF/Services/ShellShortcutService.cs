using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace PolarUninstallerWpf.Services;

// Windows' Unicode shell-link interface. Called only on the scanner's STA worker.
public static class ShellShortcutService
{
    [ComImport,Guid("00021401-0000-0000-C000-000000000046")] private class ShellLink { }
    [ComImport,Guid("000214F9-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder path,int count,IntPtr findData,uint flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder description,int count);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        void GetWorkingDirectory([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory,int count);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments,int count);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int show);
        void SetShowCmd(int show);
        void GetIconLocation([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder path,int count,out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path,int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path,uint reserved);
        void Resolve(IntPtr hwnd,uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
    public static (string Target,string Arguments,string Icon) Read(string path)
    {
        var instance=new ShellLink();
        try
        {
            ((IPersistFile)instance).Load(path,0);
            var link=(IShellLinkW)instance;var target=new StringBuilder(32768);var args=new StringBuilder(32768);var icon=new StringBuilder(32768);
            link.GetPath(target,target.Capacity,IntPtr.Zero,4); // RAWPATH: do not resolve or follow targets.
            link.GetArguments(args,args.Capacity);link.GetIconLocation(icon,icon.Capacity,out int index);
            return(Environment.ExpandEnvironmentVariables(target.ToString()),args.ToString(),icon.Length>0?icon+","+index:"");
        }
        finally{Marshal.FinalReleaseComObject(instance);}
    }
    public static void Write(string path,string target,string arguments="",string icon="",int iconIndex=0)
    {
        // Also used to create disposable shortcut fixtures; never executes the target.
        var instance=new ShellLink();
        try
        {
            var link=(IShellLinkW)instance;link.SetPath(target);link.SetArguments(arguments);
            if(icon.Length>0)link.SetIconLocation(icon,iconIndex);
            ((IPersistFile)instance).Save(path,true);
        }
        finally{Marshal.FinalReleaseComObject(instance);}
    }
    public static bool UpdateIconForTarget(string path,string expectedTarget,string icon,int iconIndex=0)
    {
        if((System.IO.File.GetAttributes(path)&System.IO.FileAttributes.ReparsePoint)!=0)return false;
        var instance=new ShellLink();
        try
        {
            var file=(IPersistFile)instance;
            file.Load(path,2); // STGM_READWRITE; retain arguments, hotkeys and shell properties.
            var link=(IShellLinkW)instance;
            var target=new StringBuilder(32768);
            link.GetPath(target,target.Capacity,IntPtr.Zero,4);
            if(!string.Equals(System.IO.Path.GetFullPath(Environment.ExpandEnvironmentVariables(target.ToString())),
                System.IO.Path.GetFullPath(expectedTarget),StringComparison.OrdinalIgnoreCase))return false;
            var previous=new StringBuilder(32768);
            link.GetIconLocation(previous,previous.Capacity,out int index);
            if(index==iconIndex&&string.Equals(previous.ToString(),icon,StringComparison.OrdinalIgnoreCase))return false;
            link.SetIconLocation(icon,iconIndex);
            file.Save(path,true);
            return true;
        }
        finally{Marshal.FinalReleaseComObject(instance);}
    }
}
