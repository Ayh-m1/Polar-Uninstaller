using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace PolarUninstallerWpf.Services;
public sealed record ShortcutLeftover(string Path, string Target, string Hash, string Arguments="", string Icon="")
{
    public string? SearchRoot { get; init; }
}
public static class ShortcutLeftoverService
{
    private static readonly object IconIndexLock=new();
    private static Task<IReadOnlyList<ShortcutLeftover>>? iconIndex;
    private static DateTime indexedAt;
    public static async Task<IReadOnlyList<ShortcutLeftover>> ForIconsAsync(InstalledProgram program)
    {
        Task<IReadOnlyList<ShortcutLeftover>> index;
        lock(IconIndexLock)
        {
            if(iconIndex==null||iconIndex.IsFaulted||DateTime.UtcNow-indexedAt>TimeSpan.FromMinutes(2))
            {iconIndex=ScanAsync(program,allForIcons:true);indexedAt=DateTime.UtcNow;}
            index=iconIndex;
        }
        return (await index).Where(l=>Owns(program,l.Target,l.Arguments)).ToArray();
    }
    public static Task<IReadOnlyList<ShortcutLeftover>> ScanAsync(string installation) => ScanAsync(new InstalledProgram("", "", "", installation));
    public static Task<IReadOnlyList<ShortcutLeftover>> ScanAsync(InstalledProgram program, CancellationToken token=default, IEnumerable<string>? locations=null,bool allForIcons=false)
    {
        var completion = new TaskCompletionSource<IReadOnlyList<ShortcutLeftover>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var result = new List<ShortcutLeftover>();
                string root = InventoryMerger.NormalizePath(program.Location);
                if (!allForIcons && root.Length == 0 && string.IsNullOrEmpty(program.Identifier)) { completion.SetResult(result); return; }
                var roots=(locations ?? new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.CommonDesktopDirectory, Environment.SpecialFolder.StartMenu, Environment.SpecialFolder.CommonStartMenu }.Select(Environment.GetFolderPath)).Where(Directory.Exists).Select(System.IO.Path.GetFullPath).ToArray();
                var pending = new Stack<string>(roots);
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while (pending.TryPop(out var folder))
                {
                    token.ThrowIfCancellationRequested();
                    if (!visited.Add(folder)) continue;
                    try
                    {
                        if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) continue;
                        foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos())
                        {
                            token.ThrowIfCancellationRequested();
                            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                            if (entry is DirectoryInfo dir) { pending.Push(dir.FullName); continue; }
                            bool url=entry.Extension.Equals(".url",StringComparison.OrdinalIgnoreCase);
                            if (!url && !entry.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)) continue;
                            try
                            {
                                if(entry is not FileInfo shortcutFile || shortcutFile.Length>1024*1024)continue;
                                string target, arguments="", icon="";
                                if(url)
                                {
                                    var fields=File.ReadAllLines(entry.FullName);
                                    target=fields.FirstOrDefault(l=>l.StartsWith("URL=",StringComparison.OrdinalIgnoreCase))?[4..].Trim()??"";
                                    icon=fields.FirstOrDefault(l=>l.StartsWith("IconFile=",StringComparison.OrdinalIgnoreCase))?[9..].Trim()??"";
                                }
                                else
                                {
                                    var metadata=ShellShortcutService.Read(entry.FullName);
                                    target=metadata.Target;arguments=metadata.Arguments;icon=metadata.Icon;
                                }
                                if(allForIcons||Owns(program,target,arguments))result.Add(new(entry.FullName,target,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(entry.FullName))),arguments,icon){SearchRoot=roots.First(r=>entry.FullName.StartsWith(r.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))});
                            }
                            catch (Exception e) when (e is IOException or UnauthorizedAccessException or COMException or ArgumentException) { }
                        }
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
                }
                completion.SetResult(result);
            }
            catch (OperationCanceledException) { completion.TrySetCanceled(token); }
            catch (Exception e) { completion.TrySetException(e); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
    public static bool Owns(InstalledProgram program,string target,string arguments="")
    {
        if(Uri.TryCreate(target,UriKind.Absolute,out var fileUri)&&fileUri.IsFile)target=fileUri.LocalPath;
        string root=InventoryMerger.NormalizePath(program.Location);
        bool launcher=!string.IsNullOrEmpty(program.Identifier)&&new[]{"steam.exe","EpicGamesLauncher.exe","UbisoftConnect.exe","upc.exe","Launcher.exe","GalaxyClient.exe","Battle.net.exe","EADesktop.exe"}.Contains(System.IO.Path.GetFileName(target),StringComparer.OrdinalIgnoreCase);
        if(!launcher&&root.Length>0 && System.IO.Path.IsPathFullyQualified(target))
        {
            string path=System.IO.Path.GetFullPath(target);
            // A project/document/site shortcut may use the app executable but
            // belongs to the user's personal workflow. Unknown arguments are
            // not ownership authority; exact launcher IDs are handled below.
            if(path.StartsWith(root.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase)&&path.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)&&string.IsNullOrWhiteSpace(arguments)&&!RemovalDataPolicy.Preserve(path,root))return true;
        }
        // Exact launcher IDs only. A shortcut to the shared launcher without a game ID is never owned.
        string id=program.Identifier;
        if(id.StartsWith("steam:",StringComparison.OrdinalIgnoreCase)&&long.TryParse(id[6..],out long steam)&&steam>0)
            return target.Equals($"steam://rungameid/{steam}",StringComparison.OrdinalIgnoreCase)||target.Equals($"steam://run/{steam}",StringComparison.OrdinalIgnoreCase)||
                System.IO.Path.GetFileName(target).Equals("steam.exe",StringComparison.OrdinalIgnoreCase)&&System.Text.RegularExpressions.Regex.IsMatch(arguments,@"^\s*-applaunch\s+"+steam+@"(?:\s|$)",System.Text.RegularExpressions.RegexOptions.IgnoreCase)&&System.Text.RegularExpressions.Regex.Matches(arguments,@"(?:^|\s)-applaunch\b",System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count==1&&!arguments.Contains("steam://",StringComparison.OrdinalIgnoreCase);
        if(id.StartsWith("ubisoft:",StringComparison.OrdinalIgnoreCase)&&long.TryParse(id[8..],out long ubi)&&ubi>0)
            return target.Equals($"uplay://launch/{ubi}/0",StringComparison.OrdinalIgnoreCase)||target.Equals($"uplay://launch/{ubi}",StringComparison.OrdinalIgnoreCase);
        if(program.LaunchUri.Length>0&&Uri.TryCreate(program.LaunchUri,UriKind.Absolute,out var captured)&&new[]{"com.epicgames.launcher","uplay","steam","goggalaxy","riotclient"}.Contains(captured.Scheme,StringComparer.OrdinalIgnoreCase)&&Uri.TryCreate(target,UriKind.Absolute,out var actual))
        {
            // Compare the full app identity from the manifest; query order/launch options are irrelevant.
            return captured.Scheme.Equals(actual.Scheme,StringComparison.OrdinalIgnoreCase)&&captured.Host.Equals(actual.Host,StringComparison.OrdinalIgnoreCase)&&Uri.UnescapeDataString(captured.AbsolutePath).Equals(Uri.UnescapeDataString(actual.AbsolutePath),StringComparison.Ordinal);
        }
        return false;
    }
    public static string Remove(ShortcutLeftover shortcut,InstalledProgram? owner=null)
    {
        bool isShortcut=new[]{".lnk",".url"}.Contains(System.IO.Path.GetExtension(shortcut.Path),StringComparer.OrdinalIgnoreCase);
        string kind=isShortcut?"shortcut":"file";
        try
        {
            if(isShortcut)
            {
                if(owner==null||string.IsNullOrWhiteSpace(shortcut.SearchRoot)||!Owns(owner,shortcut.Target,shortcut.Arguments))return "Skipped shortcut: ownership or location was not verified.";
                string full=System.IO.Path.GetFullPath(shortcut.Path),scope=System.IO.Path.GetFullPath(shortcut.SearchRoot).TrimEnd('\\');
                if(!full.StartsWith(scope+"\\",StringComparison.OrdinalIgnoreCase))return "Skipped shortcut: outside its captured search scope.";
                for(string? current=full;current!=null;current=System.IO.Path.GetDirectoryName(current))
                    if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)return "Skipped shortcut: link/junction path was detected.";
            }
            if ((File.GetAttributes(shortcut.Path) & FileAttributes.ReparsePoint) != 0 || Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(shortcut.Path))) != shortcut.Hash)
                return "Skipped changed "+kind+": " + shortcut.Path;
            long removedBytes=new FileInfo(shortcut.Path).Length;
            File.Delete(shortcut.Path);
            BrowserOperationsService.VerifyAbsent(shortcut.Path);
            _=FileChangeHub.PublishAsync(new[]{new FileChange(shortcut.Path,true,removedBytes)});
            return isShortcut?"Removed shortcut permanently: "+System.IO.Path.GetFileName(shortcut.Path):"Removed file: "+shortcut.Path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException) { return "Skipped "+kind+": "+System.IO.Path.GetFileName(shortcut.Path)+" ("+e.GetType().Name+")"; }
    }
}
