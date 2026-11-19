using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PolarUninstallerWpf.Services;

public sealed partial class DashboardStorageService
{
    private static string cacheDirectory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PolarUninstallerWPF-Development","StorageCache");
    private static readonly SemaphoreSlim DiskGate=new(1,1);
    private static bool loaded;
    public static string? CachePersistenceError {get;private set;}
    public static void UseCacheDirectory(string directory)
    {lock(CacheGate){cacheDirectory=Path.GetFullPath(directory);Cache.Clear();LastSnapshot=null;loaded=false;cacheVersion++;cacheEpoch++;RecentChanges.Clear();}}
    public static async Task LoadCacheAsync()
    {
        await DiskGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if(loaded)return;
            var snapshots=await Task.Run(()=>Directory.Exists(cacheDirectory)?Directory.EnumerateFiles(cacheDirectory,"*.json").Take(8).Select(file=>
            {try{if(new FileInfo(file).Length>64L*1024*1024)return null;return JsonSerializer.Deserialize<StorageSnapshot>(File.ReadAllText(file));}catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException){return null;}}).Where(s=>s!=null&&s.Root!=null&&Path.IsPathFullyQualified(s.Root)&&s.Largest!=null&&s.LargestFolders!=null&&s.FileIndex!=null&&s.FolderIndex!=null&&s.HiddenPaths!=null&&s.AppliedChanges!=null&&s.RemovedPaths!=null).Cast<StorageSnapshot>().ToArray():[]).ConfigureAwait(false);
            lock(CacheGate){foreach(var snapshot in snapshots.OrderByDescending(s=>s.CompletedAt).Take(4).Reverse())Cache[snapshot.Root]=snapshot;LastSnapshot=Cache.Values.OrderByDescending(s=>s.CompletedAt).FirstOrDefault();loaded=true;}
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or System.Security.SecurityException){CachePersistenceError=e.Message;}
        finally{DiskGate.Release();}
    }
    private static Task SaveCacheAsync()=>Task.Run(async()=>
    {
        await DiskGate.WaitAsync().ConfigureAwait(false);
        try
        {
            StorageSnapshot[] snapshots;lock(CacheGate)snapshots=Cache.Values.OrderByDescending(s=>s.CompletedAt).Take(4).ToArray();
            Directory.CreateDirectory(cacheDirectory);var retained=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var snapshot in snapshots)
            {
                string file=Path.Combine(cacheDirectory,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot.Root.ToUpperInvariant())))+".json");retained.Add(file);
                string temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
                try{await File.WriteAllTextAsync(temp,JsonSerializer.Serialize(snapshot)).ConfigureAwait(false);File.Move(temp,file,true);}
                finally{if(File.Exists(temp))File.Delete(temp);}
            }
            // This directory contains only our generated cache records.
            foreach(string file in Directory.EnumerateFiles(cacheDirectory,"*.json"))if(!retained.Contains(file))File.Delete(file);
            CachePersistenceError=null;
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or System.Security.SecurityException){CachePersistenceError=e.Message;CrashDiagnostics.Record("Storage cache",e);}
        finally{DiskGate.Release();}
    });
    public static async Task ApplyChangesAsync(FileChangeBatch batch)
    {
        await LoadCacheAsync().ConfigureAwait(false);
        await Task.Run(()=>
        {
            lock(CacheGate)
            {
                foreach(string root in Cache.Keys.ToArray())Cache[root]=StorageCacheUpdates.Apply(Cache[root],batch);
                if(LastSnapshot is {} last&&Cache.TryGetValue(last.Root,out var updated))LastSnapshot=updated;
                cacheVersion++;RecentChanges.Enqueue((cacheVersion,batch));while(RecentChanges.Count>1024)RecentChanges.Dequeue(); // Reconcile in-flight scans before publishing.
            }
        }).ConfigureAwait(false);
        await SaveCacheAsync().ConfigureAwait(false);
    }
    public static Task FlushCacheAsync()=>SaveCacheAsync();
}
