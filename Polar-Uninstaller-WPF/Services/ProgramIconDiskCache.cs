using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace PolarUninstallerWpf.Services;

/// <summary>Only decoded PNGs and metadata live here. Every hit revalidates the original local source.</summary>
public static class ProgramIconDiskCache
{
    private sealed record Entry(string Source,int Index,long Stamp,long Length,int Pixels,IconResolutionMethod Method,string Blob);
    // Version 4 discards cached icons whose source no longer belongs to the entry
    // (the old Steam row kept a Microsoft Teams Store logo), plus any metadata
    // written while the isolated worker could not start.
    private const string CacheVersion="4";
    private static string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PolarUninstallerWPF-Development","IconCache");
    private static readonly object Gate=new();
    private static int saves;
    private static bool versionChecked;
    public static void UseCacheDirectory(string directory){lock(Gate){root=Path.GetFullPath(directory);versionChecked=false;EnsureVersion();}}
    private static void EnsureVersion()
    {
        if(versionChecked)return;
        versionChecked=true;
        try
        {
            string marker=Path.Combine(root,"cache-version.txt");
            if(File.Exists(marker)&&File.ReadAllText(marker).Trim()==CacheVersion)return;
            if(Directory.Exists(root))
                foreach(string file in Directory.EnumerateFiles(root,"*.json").Concat(Directory.EnumerateFiles(root,"*.png")))
                    try{File.Delete(file);}catch(FileNotFoundException){}
            else Directory.CreateDirectory(root);
            File.WriteAllText(marker,CacheVersion);
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or System.Security.SecurityException){}
    }
    private static string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    public static string KeyFor(InstalledProgram p)=>Hash("legacy-first-v4|"+string.Join("|",p.Name,p.Location,p.Identifier,p.RegisteredIcon,p.IconPath,string.Join(";",p.IconCandidates??[])));
    public static string SourceKey(string source,int index,int pixels)
    {
        var stamp=Stamp(source);return Hash(string.Join("|",Path.GetFullPath(source).ToUpperInvariant(),index,stamp.Time,stamp.Length,pixels));
    }
    private static (long Time,long Length) Stamp(string file)=>File.Exists(file)?(File.GetLastWriteTimeUtc(file).Ticks,new FileInfo(file).Length):Directory.Exists(file)?(Directory.GetLastWriteTimeUtc(file).Ticks,-1):(0,0);
    public static string PathFor(InstalledProgram p)=>Path.Combine(root,KeyFor(p)+".json");
    public static ProgramIcon? TryLoad(InstalledProgram p,int pixels=64)
    {
        lock(Gate)try
        {
            EnsureVersion();
            string metadata=PathFor(p);if(!File.Exists(metadata))return null;
            var entry=JsonSerializer.Deserialize<Entry>(File.ReadAllText(metadata));if(entry==null)return null;
            var stamp=Stamp(entry.Source);
            if(stamp.Time==0||stamp.Time!=entry.Stamp||stamp.Length!=entry.Length||entry.Pixels<pixels){File.Delete(metadata);return null;}
            if(Path.GetFileName(entry.Blob)!=entry.Blob)return null;
            string blob=Path.Combine(root,entry.Blob);if(!File.Exists(blob)||new FileInfo(blob).Length>4*1024*1024)return null;
            using var stream=File.OpenRead(blob);var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();
            return new(bitmap,null){SourcePath=entry.Source,SourceIndex=entry.Index,Method=entry.Method};
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or JsonException or System.Security.SecurityException){return null;}
    }
    /// <summary>Drops one entry whose decoded source was rejected as not owned.</summary>
    public static void Remove(InstalledProgram p)
    {
        lock(Gate)try
        {
            EnsureVersion();
            string metadata=PathFor(p);if(!File.Exists(metadata))return;
            var entry=JsonSerializer.Deserialize<Entry>(File.ReadAllText(metadata));
            File.Delete(metadata);
            if(entry is {Blob:not null}&&Path.GetFileName(entry.Blob)==entry.Blob)
            {
                string blob=Path.Combine(root,entry.Blob);
                if(File.Exists(blob))File.Delete(blob);
            }
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or JsonException or System.Security.SecurityException){ }
    }
    public static void Save(InstalledProgram p,ProgramIcon icon,int pixels=64)
    {
        if(icon.Image is not BitmapSource source||string.IsNullOrEmpty(icon.SourcePath))return;
        lock(Gate)try
        {
            EnsureVersion();
            var stamp=Stamp(icon.SourcePath);if(stamp.Time==0)return;Directory.CreateDirectory(root);
            string blob=SourceKey(icon.SourcePath,icon.SourceIndex,pixels)+".png",file=Path.Combine(root,blob),temporary=file+".tmp";
            using(var stream=File.Create(temporary)){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(source));encoder.Save(stream);}
            File.Move(temporary,file,true);
            string metadata=PathFor(p);File.WriteAllText(metadata+".tmp",JsonSerializer.Serialize(new Entry(icon.SourcePath,icon.SourceIndex,stamp.Time,stamp.Length,pixels,icon.Method,blob)));File.Move(metadata+".tmp",metadata,true);
            if(++saves%16==1)Prune();
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException){ }
    }
    private static void Prune()
    {
        // Bound metadata and decoded blobs; all paths are generated under this owned cache.
        foreach(var file in new DirectoryInfo(root).EnumerateFiles("*.json").OrderByDescending(f=>f.LastWriteTimeUtc).Skip(512))file.Delete();
        var referenced=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(string file in Directory.EnumerateFiles(root,"*.json"))try{var entry=JsonSerializer.Deserialize<Entry>(File.ReadAllText(file));if(entry!=null)referenced.Add(entry.Blob);}catch(JsonException){ }
        foreach(var file in new DirectoryInfo(root).EnumerateFiles("*.png"))if(!referenced.Contains(file.Name))file.Delete();
    }
}
