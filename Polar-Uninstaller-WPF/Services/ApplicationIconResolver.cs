using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace PolarUninstallerWpf.Services;

// Resolve once per captured identity, never while WPF measures or arranges a row.
public static class ApplicationIconResolver
{
    private static readonly ConcurrentDictionary<string,Task<ProgramIcon?>> Cache=new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim Slots=new(3);
    private static readonly ConcurrentDictionary<string, Task<ProgramIcon?>> SizedCache = new(StringComparer.OrdinalIgnoreCase);
    public static void ResetFailures()
    {
        foreach (var pair in SizedCache.Where(p => p.Value.IsCompleted))
            if (!pair.Value.IsCompletedSuccessfully || pair.Value.Result is null) SizedCache.TryRemove(pair.Key, out _);
    }
    public static Task<ProgramIcon?> ResolveAsync(InstalledProgram program, int pixels, CancellationToken token, bool priority)
    {
        pixels = Math.Clamp(pixels, 32, 256);
        string key = ProgramIconDiskCache.KeyFor(program) + "|" + pixels;
        if (SizedCache.Count > 512)
            foreach (var pair in SizedCache.Where(p => p.Value.IsCompleted).Take(64)) SizedCache.TryRemove(pair.Key, out _);
        return SizedCache.GetOrAdd(key, _ => ResolveWorkerAsync(program, pixels, priority)).WaitAsync(token);
    }
    private static async Task<ProgramIcon?> ResolveWorkerAsync(InstalledProgram program, int pixels, bool priority)
    {
        var reply = await IconWorkerPool.ResolveAsync(program, pixels, CancellationToken.None, priority).ConfigureAwait(false);
        if (reply.Png is null)
        {
            IconDiagnostics.Report(program, false, reply.Reason, reply.Detail);
            return null;
        }
        using var stream = new MemoryStream(reply.Png);
        var bitmap = new System.Windows.Media.Imaging.BitmapImage();
        bitmap.BeginInit(); bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
        IconDiagnostics.Report(program, true, reply.Method.ToString(), reply.Source);
        return new ProgramIcon(bitmap, null) { SourcePath = reply.Source, SourceIndex = reply.Index, Method = reply.Method };
    }
    public static Task<ProgramIcon?> ResolveAsync(InstalledProgram program)
    {
        if(Cache.Count>512)foreach(var entry in Cache.Where(c=>c.Value.IsCompleted).Take(64))Cache.TryRemove(entry.Key,out _);
        string key=string.Join("|",program.Name,program.Location,program.Identifier,program.IconPath,string.Join(";",program.IconCandidates??Array.Empty<string>()));
        return Cache.GetOrAdd(key,_=>ResolveAndCacheAsync(program));
    }

    /// <summary>Synchronous cache lookup used for immediate icon display.</summary>
    public static ProgramIcon? TryLoadCached(InstalledProgram program) => ProgramIconDiskCache.TryLoad(program);

    private static async Task<ProgramIcon?> ResolveAndCacheAsync(InstalledProgram program)
    {
        var cached = ProgramIconDiskCache.TryLoad(program);
        if (cached is not null) return cached;
        var icon = await ResolveCore(program);
        if (icon is not null) ProgramIconDiskCache.Save(program, icon);
        return icon;
    }
    private static async Task<ProgramIcon?> ResolveCore(InstalledProgram p)
    {
        await Slots.WaitAsync();
        try
        {
            bool game=!string.IsNullOrEmpty(p.Identifier)||new[]{"Steam","Epic Games","Ubisoft","Rockstar Games","EA","GOG.com","Blizzard Entertainment","Battle.net"}.Contains(p.Source,StringComparer.OrdinalIgnoreCase);
            var registered=(p.IconCandidates??Array.Empty<string>()).Prepend(p.IconPath).Where(s=>!string.IsNullOrWhiteSpace(s)).ToArray();
            bool Valid(string path)
            {
                string name=Path.GetFileNameWithoutExtension(InstallationPathResolver.Executable(path));
                if(name.Length==0)name=Path.GetFileNameWithoutExtension(path);
                return !name.StartsWith("unins",StringComparison.OrdinalIgnoreCase)&&!new[]{"update","msiexec","crashreporter","crashreportclient","UnityCrashHandler64","vc_redist.x64"}.Contains(name,StringComparer.OrdinalIgnoreCase)
                    &&(!game||!new[]{"steam","EpicGamesLauncher","UbisoftConnect","upc","Launcher","GalaxyClient","Battle.net","EADesktop","SocialClubHelper"}.Contains(name,StringComparer.OrdinalIgnoreCase));
            }
            var metadata=await Task.Run(()=>LauncherArtworkService.Candidates(p));
            if(game)foreach(string path in metadata)
                if(Valid(path)&&await ProgramIconService.LoadAsync(path) is {} art)return art;
            // Launcher inventory already supplies ID-specific artwork. Keep it ahead of executable icons.
            foreach(string path in registered.Where(Valid).OrderByDescending(s=>game&&new[]{".png",".jpg",".jpeg",".ico"}.Contains(Path.GetExtension(s),StringComparer.OrdinalIgnoreCase)))
                if(await ProgramIconService.LoadAsync(path) is {} image)return image;
            var local=await Task.Run(()=>LocalCandidates(p,game));
            foreach(string path in local.Where(Valid))if(await ProgramIconService.LoadAsync(path) is {} image)return image;
            foreach(var shortcut in await ShortcutLeftoverService.ForIconsAsync(p))
            {
                foreach(string path in new[]{shortcut.Icon,shortcut.Target}.Where(s=>!string.IsNullOrWhiteSpace(s)&&Valid(s)))
                    if(await ProgramIconService.LoadAsync(path) is {} image)return image;
            }
            if(!game)foreach(string path in metadata.Where(Valid))if(await ProgramIconService.LoadAsync(path) is {} image)return image;
            return null;
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or System.Runtime.InteropServices.COMException){return null;}
        finally{Slots.Release();}
    }
    private static IReadOnlyList<string> LocalCandidates(InstalledProgram p,bool game)
    {
        var paths=new List<string>();string root=InventoryMerger.NormalizePath(p.Location);
        if(root.Length==0||!Directory.Exists(root))return paths;
        var pending=new Queue<(string Path,int Depth)>();pending.Enqueue((root,0));int visited=0;
        while(pending.TryDequeue(out var item)&&visited++<200)
        {
            try
            {
                if((File.GetAttributes(item.Path)&FileAttributes.ReparsePoint)!=0)continue;
                foreach(string file in Directory.EnumerateFiles(item.Path).Take(320))
                {
                    string ext=Path.GetExtension(file);string stem=Path.GetFileNameWithoutExtension(file);
                    bool exact=InstallationPathResolver.NormalizeProduct(stem)==InstallationPathResolver.NormalizeProduct(p.Name);
                    if(ext.Equals(".exe",StringComparison.OrdinalIgnoreCase))
                    {
                        var version=FileVersionInfo.GetVersionInfo(file);
                        bool metadata=InstallationPathResolver.NormalizeProduct(version.ProductName??"")==InstallationPathResolver.NormalizeProduct(p.Name);
                        if(exact||metadata||game)paths.Add(file);
                    }
                    // Do not select arbitrary JPG/PNG files or unrelated cover art.
                    else if(new[]{".ico",".png"}.Contains(ext,StringComparer.OrdinalIgnoreCase)&&(exact||game&&new[]{"icon","gameicon"}.Contains(stem,StringComparer.OrdinalIgnoreCase)))paths.Add(file);
                }
                if(item.Depth<4)foreach(string child in Directory.EnumerateDirectories(item.Path).Take(40))pending.Enqueue((child,item.Depth+1));
            }
            catch(Exception e)when(e is IOException or UnauthorizedAccessException or System.Security.SecurityException){ }
        }
        return paths.OrderByDescending(f=>InstallationPathResolver.NormalizeProduct(Path.GetFileNameWithoutExtension(f))==InstallationPathResolver.NormalizeProduct(p.Name)).ThenBy(f=>f.Length).ToArray();
    }
}
