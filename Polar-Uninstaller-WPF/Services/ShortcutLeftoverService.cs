using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace PolarUninstallerWpf.Services;
public sealed record ShortcutLeftover(string Path, string Target, string Hash, string Arguments="", string Icon="")
{
    public string? SearchRoot { get; init; }
    internal string? StartMenuRoot { get; init; }
    internal Guid? MsiProduct { get; init; }
}
public static class ShortcutLeftoverService
{
    private const int MaxShortcutBytes = 1024 * 1024;
    private const uint ShellDelete = 0x00000004, ShellRemoveDirectory = 0x00000010, ShellPathWide = 0x0005;
    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint change, uint flags,
        [MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr unused);
    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiGetShortcutTargetW(string path, StringBuilder product, IntPtr feature, IntPtr component);
    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MsiQueryProductStateW(string product);

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
    public static Task<IReadOnlyList<ShortcutLeftover>> ScanAsync(InstalledProgram program, CancellationToken token=default, IEnumerable<string>? locations=null,bool allForIcons=false) =>
        ScanCoreAsync(program, token, locations, allForIcons, null);

    internal static Task<IReadOnlyList<ShortcutLeftover>> ScanHistoryAsync(Func<ShortcutLeftover,bool> match,
        CancellationToken token, IEnumerable<string>? locations=null) =>
        ScanCoreAsync(new InstalledProgram("", "", ""), token, locations, false, match);

    private static Task<IReadOnlyList<ShortcutLeftover>> ScanCoreAsync(InstalledProgram program,
        CancellationToken token, IEnumerable<string>? locations, bool allForIcons, Func<ShortcutLeftover,bool>? historyMatch)
    {
        var completion = new TaskCompletionSource<IReadOnlyList<ShortcutLeftover>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var result = new List<ShortcutLeftover>();
                string root = InventoryMerger.NormalizePath(program.Location);
                bool application = historyMatch != null || (!allForIcons && UsesApplicationChecks(program));
                Guid? product = application ? MsiProductCode(program) : null;
                if (historyMatch == null && !allForIcons && root.Length == 0 && string.IsNullOrEmpty(program.Identifier) && product == null) { completion.SetResult(result); return; }
                var startMenus = locations == null && application
                    ? KnownRoots(Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms) : [];
                var roots = (locations ?? startMenus.Concat(KnownRoots(Environment.SpecialFolder.DesktopDirectory,
                    Environment.SpecialFolder.CommonDesktopDirectory, Environment.SpecialFolder.StartMenu, Environment.SpecialFolder.CommonStartMenu)))
                    .Where(Directory.Exists).Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)))
                    .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path.Length).ToArray();
                bool safeRoot = application && IsSafeInstallationRoot(root);
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
                                if(entry is not FileInfo shortcutFile || shortcutFile.Length>MaxShortcutBytes)continue;
                                using var input = new FileStream(entry.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
                                if (input.Length > MaxShortcutBytes) continue;
                                string target, arguments="", icon="";
                                Guid? shortcutProduct = null;
                                if(url)
                                {
                                    using var reader = new StreamReader(input, leaveOpen: true);
                                    string[] fields = reader.ReadToEnd().Split('\n');
                                    if (application)
                                    {
                                        fields = fields.Select(line => line.Trim()).ToArray();
                                        fields = fields.SkipWhile(line => !line.Equals("[InternetShortcut]", StringComparison.OrdinalIgnoreCase))
                                            .Skip(1).TakeWhile(line => !line.StartsWith('[')).ToArray();
                                        if (fields.Count(line => line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase)) != 1) continue;
                                    }
                                    target=fields.FirstOrDefault(l=>l.StartsWith("URL=",StringComparison.OrdinalIgnoreCase))?[4..].Trim()??"";
                                    icon=fields.FirstOrDefault(l=>l.StartsWith("IconFile=",StringComparison.OrdinalIgnoreCase))?[9..].Trim()??"";
                                    input.Position = 0;
                                }
                                else
                                {
                                    var metadata=ShellShortcutService.Read(entry.FullName);
                                    target=metadata.Target;arguments=metadata.Arguments;icon=metadata.Icon;
                                    if (application)
                                    {
                                        var code = new StringBuilder(39);
                                        if (MsiGetShortcutTargetW(entry.FullName, code, IntPtr.Zero, IntPtr.Zero) == 0 &&
                                            Guid.TryParseExact(code.ToString(), "B", out var msiCode))
                                            shortcutProduct = msiCode;
                                    }
                                }
                                var candidate = new ShortcutLeftover(entry.FullName,target,"",arguments,icon)
                                {
                                    SearchRoot = roots.Last(r => IsUnder(entry.FullName, r)),
                                    StartMenuRoot = startMenus.FirstOrDefault(r => IsUnder(entry.FullName, r)),
                                    MsiProduct = shortcutProduct
                                };
                                bool owned = historyMatch != null ? historyMatch(candidate) : application
                                    ? shortcutProduct is Guid advertised ? advertised == product : safeRoot && Owns(program,target,arguments)
                                    : allForIcons || Owns(program,target,arguments);
                                if (owned) result.Add(candidate with { Hash = Convert.ToHexString(SHA256.HashData(input)) });
                            }
                            catch (Exception e) when (e is IOException or UnauthorizedAccessException or COMException or ArgumentException or System.Security.SecurityException) { }
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

    private static string[] KnownRoots(params Environment.SpecialFolder[] folders) =>
        folders.Select(Environment.GetFolderPath).Where(Path.IsPathFullyQualified)
            .Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))).ToArray();

    private static bool IsUnder(string path, string directory) =>
        path.StartsWith(Path.EndsInDirectorySeparator(directory) ? directory : directory + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    internal static bool UsesApplicationChecks(InstalledProgram program) =>
        string.IsNullOrWhiteSpace(program.Identifier) && !GameClassificationService.IsGame(program);

    internal static Guid? MsiProductCode(InstalledProgram program)
    {
        if (!UninstallCommandService.TryParse(program.Uninstall, out var command, out var arguments) ||
            !LeftoverService.EqualPath(command, Path.Combine(Environment.SystemDirectory, "msiexec.exe"))) return null;
        Guid? result = null;
        for (int i = 0; i < arguments.Length; i++)
        {
            var arg = arguments[i];
            if (!arg.StartsWith("/x", StringComparison.OrdinalIgnoreCase)) continue;
            string value = arg.Length == 2 && i + 1 < arguments.Length ? arguments[++i] : arg[2..];
            if (!Guid.TryParseExact(value, "B", out var product) || product == Guid.Empty || result.HasValue) return null;
            result = product;
        }
        return result;
    }

    internal static bool IsSafeInstallationRoot(string root)
    {
        if (root.Length == 0) return false;
        try { DirectRemovalService.ValidateRoot(root); return true; }
        catch (Exception e) when (RemovalEngine.IsItemError(e)) { return false; }
    }

    internal static bool OwnsShortcut(InstalledProgram program, ShortcutLeftover shortcut) =>
        UsesApplicationChecks(program)
            ? shortcut.MsiProduct is Guid product ? MsiProductCode(program) == product
                : IsSafeInstallationRoot(InventoryMerger.NormalizePath(program.Location)) && Owns(program, shortcut.Target, shortcut.Arguments)
            : Owns(program, shortcut.Target, shortcut.Arguments);

    internal static bool IsAbsent(string path)
    {
        try { File.GetAttributes(path); return false; }
        catch (FileNotFoundException) { return true; }
        catch (DirectoryNotFoundException) { return true; }
    }

    internal static bool IsStaleApplicationShortcut(ShortcutLeftover shortcut)
    {
        if (shortcut.MsiProduct is Guid product)
            return MsiQueryProductStateW(product.ToString("B")) == -1; // INSTALLSTATE_UNKNOWN
        string target = shortcut.Target;
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.IsFile) target = uri.LocalPath;
        return Path.IsPathFullyQualified(target) && IsAbsent(target);
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
    public static string Remove(ShortcutLeftover shortcut,InstalledProgram? owner=null) =>
        RemoveCore(shortcut, owner, null);

    internal static IReadOnlyList<string> RemoveForCleanup(ShortcutLeftover shortcut, InstalledProgram owner)
    {
        var folders = new List<string>();
        string result = RemoveCore(shortcut, owner, folders);
        folders.Insert(0, result);
        return folders;
    }

    private static string RemoveCore(ShortcutLeftover shortcut, InstalledProgram? owner, List<string>? folderMessages)
    {
        bool isShortcut=new[]{".lnk",".url"}.Contains(System.IO.Path.GetExtension(shortcut.Path),StringComparer.OrdinalIgnoreCase);
        string kind=isShortcut?"shortcut":"file";
        try
        {
            if(isShortcut)
            {
                if(owner==null||string.IsNullOrWhiteSpace(shortcut.SearchRoot)||!OwnsShortcut(owner,shortcut))return "Skipped shortcut: ownership or location was not verified.";
                if(folderMessages!=null && UsesApplicationChecks(owner) && !IsStaleApplicationShortcut(shortcut))
                    return "Skipped shortcut: the registered product or target still exists.";
                string full=Path.GetFullPath(shortcut.Path),scope=Path.GetFullPath(shortcut.SearchRoot);
                if(!IsUnder(full,scope))return "Skipped shortcut: outside its captured search scope.";
                for(string? current=full;current!=null;current=System.IO.Path.GetDirectoryName(current))
                    if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)return "Skipped shortcut: link/junction path was detected.";
            }
            long removedBytes;
            using (var source = new FileStream(shortcut.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                removedBytes = source.Length;
                if ((File.GetAttributes(shortcut.Path) & FileAttributes.ReparsePoint) != 0 ||
                    (isShortcut && removedBytes > MaxShortcutBytes) || Convert.ToHexString(SHA256.HashData(source)) != shortcut.Hash)
                    return "Skipped changed "+kind+": " + shortcut.Path;
            }
            File.Delete(shortcut.Path);
            BrowserOperationsService.VerifyAbsent(shortcut.Path);
            _=FileChangeHub.PublishAsync(new[]{new FileChange(shortcut.Path,true,removedBytes)});
            if (isShortcut)
            {
                SHChangeNotify(ShellDelete, ShellPathWide, shortcut.Path, IntPtr.Zero);
                if (folderMessages != null && owner != null && UsesApplicationChecks(owner))
                    RemoveEmptyStartMenuFolders(shortcut, owner, folderMessages);
            }
            return isShortcut?"Removed shortcut permanently: "+System.IO.Path.GetFileName(shortcut.Path):"Removed file: "+shortcut.Path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException) { return "Skipped "+kind+": "+System.IO.Path.GetFileName(shortcut.Path)+" ("+e.GetType().Name+")"; }
    }

    private static void RemoveEmptyStartMenuFolders(ShortcutLeftover shortcut, InstalledProgram owner, List<string> messages)
    {
        if (shortcut.StartMenuRoot is not string root || !IsUnder(shortcut.Path, root)) return;
        string product = InstallationPathResolver.NormalizeProduct(owner.Name);
        if (product.Length == 0) return;
        var parents = new List<string>();
        for (string? folder = Path.GetDirectoryName(shortcut.Path); folder != null && IsUnder(folder, root); folder = Path.GetDirectoryName(folder))
        {
            parents.Add(folder);
            if (InstallationPathResolver.NormalizeProduct(Path.GetFileName(folder)) == product) break;
        }
        if (parents.Count == 0 ||
            InstallationPathResolver.NormalizeProduct(Path.GetFileName(parents[^1])) != product) return;
        foreach (string folder in parents)
        {
            try
            {
                if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0 || Directory.EnumerateFileSystemEntries(folder).Any()) break;
                Directory.Delete(folder, recursive: false);
                BrowserOperationsService.VerifyAbsent(folder);
                SHChangeNotify(ShellRemoveDirectory, ShellPathWide, folder, IntPtr.Zero);
                _ = FileChangeHub.PublishAsync(new[] { new FileChange(folder, true, 0, true) });
                messages.Add("Removed empty Start Menu folder: " + folder);
            }
            catch (Exception e) when (RemovalEngine.IsItemError(e))
            {
                messages.Add("Skipped Start Menu folder: " + folder + " (" + e.GetType().Name + ")");
                break;
            }
        }
    }
}
