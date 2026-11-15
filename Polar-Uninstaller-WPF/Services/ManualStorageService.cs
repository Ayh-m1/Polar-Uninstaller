using System.IO;
using System.Text.Json;
using System.Diagnostics;
namespace PolarUninstallerWpf.Services;
public sealed record ManualStorageItem(string Path,long Bytes,int Files,int Skipped,DateTime? Updated);
public sealed class ManualStorageService
{
    private readonly string cache;
    public ManualStorageService(string? directory=null) => cache=Path.Combine(directory??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PolarUninstallerWPF-Development"),"dashboard-items.json");
    /// <summary>The saved scan-item file. Only Polar-generated metadata lives here;
    /// the paths inside it are never deleted by Clear Cache.</summary>
    public string CachePath=>cache;
    public Task<IReadOnlyList<ManualStorageItem>> LoadAsync()=>Task.Run<IReadOnlyList<ManualStorageItem>>(()=>File.Exists(cache)?JsonSerializer.Deserialize<ManualStorageItem[]>(File.ReadAllText(cache))??[]:[]);
    public Task ClearAsync()=>Task.Run(()=>
    {
        try { if(File.Exists(cache))File.Delete(cache); }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        // Remove abandoned atomic-write temporary files as well.
        try
        {
            string? folder=Path.GetDirectoryName(cache);
            if(folder is not null&&Directory.Exists(folder))
                foreach(string temporary in Directory.EnumerateFiles(folder,Path.GetFileName(cache)+".*.tmp"))
                    try { File.Delete(temporary); }
                    catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
    });
    public Task SaveAsync(IEnumerable<ManualStorageItem> items)
    {
        var snapshot=items.ToArray();return Task.Run(()=>{Directory.CreateDirectory(Path.GetDirectoryName(cache)!);string temp=cache+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(snapshot));File.Move(temp,cache,true);});
    }
    public Task<ManualStorageItem> ScanAsync(string path,IProgress<ManualStorageItem>? progress,CancellationToken token)=>Task.Run(()=>
    {
        path=Path.GetFullPath(path);long bytes=0;int files=0,skipped=0;var pending=new Stack<string>();pending.Push(path);var clock=Stopwatch.StartNew();
        while(pending.TryPop(out var current))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0){skipped++;continue;}
                if(File.Exists(current)){bytes+=new FileInfo(current).Length;files++;}
                else foreach(var entry in new DirectoryInfo(current).EnumerateFileSystemInfos()){token.ThrowIfCancellationRequested();pending.Push(entry.FullName);}
            }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.Security.SecurityException){skipped++;}
            if(clock.ElapsedMilliseconds>=150){progress?.Report(new(path,bytes,files,skipped,null));clock.Restart();}
        }
        return new ManualStorageItem(path,bytes,files,skipped,DateTime.Now);
    },token);
}
