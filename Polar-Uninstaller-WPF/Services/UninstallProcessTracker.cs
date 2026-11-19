using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PolarUninstallerWpf.Services;

/// <summary>Tracks only the launched process and descendants born after launch, including short-lived bootstrap parents.</summary>
public static class UninstallProcessTracker
{
    public sealed record ProcessIdentity(int Id,int ParentId,DateTime StartedUtc);
    public static IReadOnlyList<ProcessIdentity> Descendants(IReadOnlyList<ProcessIdentity> processes,
        IReadOnlyDictionary<int,DateTime> parents,DateTime launchedUtc)
    {
        var known=new Dictionary<int,DateTime>(parents);var found=new List<ProcessIdentity>();bool added;
        do
        {
            added=false;
            foreach(var child in processes)
                if(!known.ContainsKey(child.Id)&&child.StartedUtc>=launchedUtc&&
                    known.TryGetValue(child.ParentId,out var parentStarted)&&child.StartedUtc>=parentStarted)
                {known[child.Id]=child.StartedUtc;found.Add(child);added=true;}
        }while(added);
        return found;
    }
    public static async Task WaitAsync(Process root,DateTime launchedUtc)
    {
        using(root)
        {
            var known=new Dictionary<int,DateTime>{{root.Id,launchedUtc}};
            DateTime? quietSince=null;
            while(true)
            {
                var snapshot=ReadProcesses(known,launchedUtc);
                foreach(var child in Descendants(snapshot,known,launchedUtc))known[child.Id]=child.StartedUtc;
                bool alive=!root.HasExited||snapshot.Any(p=>known.TryGetValue(p.Id,out var started)&&p.StartedUtc==started);
                // Keep a grace window for an exiting bootstrap that has just spawned its child.
                if(alive)quietSince=null;
                else if(quietSince==null)quietSince=DateTime.UtcNow;
                else if(DateTime.UtcNow-quietSince>=TimeSpan.FromSeconds(2))return;
                await Task.Delay(200).ConfigureAwait(false);
            }
        }
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    private struct Entry
    {
        public uint Size,Usage,Id;public UIntPtr Heap;public uint Module,Threads,Parent;public int Priority;public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string Name;
    }
    [DllImport("kernel32.dll",SetLastError=true)]private static extern IntPtr CreateToolhelp32Snapshot(uint flags,uint id);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool Process32FirstW(IntPtr snapshot,ref Entry entry);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool Process32NextW(IntPtr snapshot,ref Entry entry);
    [DllImport("kernel32.dll")]private static extern bool CloseHandle(IntPtr handle);
    private static IReadOnlyList<ProcessIdentity> ReadProcesses(IReadOnlyDictionary<int,DateTime> known,DateTime launchedUtc)
    {
        IntPtr handle=CreateToolhelp32Snapshot(2,0);if(handle==new IntPtr(-1))throw new Win32Exception();
        try
        {
            var entries=new List<ProcessIdentity>();var raw=new List<(int Id,int Parent)>();var entry=new Entry{Size=(uint)Marshal.SizeOf<Entry>(),Name=""};
            if(!Process32FirstW(handle,ref entry))throw new Win32Exception();
            do
            {
                raw.Add(((int)entry.Id,(int)entry.Parent));
            }while(Process32NextW(handle,ref entry));
            var related=known.Keys.ToHashSet();bool changed;
            do{changed=false;foreach(var p in raw)if(related.Contains(p.Parent)&&related.Add(p.Id))changed=true;}while(changed);
            foreach(var item in raw.Where(p=>related.Contains(p.Id)))
            {
                try{using var p=Process.GetProcessById(item.Id);entries.Add(new(item.Id,item.Parent,p.StartTime.ToUniversalTime()));}
                catch(Win32Exception){entries.Add(new(item.Id,item.Parent,known.TryGetValue(item.Id,out var start)?start:launchedUtc));}
                catch(Exception e)when(e is InvalidOperationException or ArgumentException){ }
            }
            return entries;
        }
        finally{CloseHandle(handle);}
    }
}
