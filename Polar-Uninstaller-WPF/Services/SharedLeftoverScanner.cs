using System.IO;
namespace PolarUninstallerWpf.Services;
public sealed record UninstallSnapshot(InstalledProgram Program,IReadOnlyList<RegistryLeftover> Registry,IReadOnlyList<ShortcutLeftover> Shortcuts,IReadOnlyList<OwnedDataFolder> Data,IReadOnlyList<string> Executables,IReadOnlyList<RegistryCleanupTarget>? Settings=null);
public sealed record LeftoverCandidate(string Category,string Path,long? Bytes,string Evidence,Func<Task<IReadOnlyList<string>>> Remove,Func<bool> Exists)
{
    public Func<DirectRemovalResult?>? FileResult { get; init; }
    public bool RequiresSeparateDataConsent { get; init; }
}
public sealed record OfficialVerification(bool Confirmed,string Code,string Detail);

/// <summary>
/// Outcome of one automatic leftover cleanup pass. Remaining paths were
/// re-checked after every removal so an incomplete cleanup is never reported as
/// complete.
/// </summary>
public sealed record LeftoverCleanupResult(
    int Removed,
    int Skipped,
    int Failed,
    int FoldersRemoved,
    int FilesRemoved,
    int RegistryRemoved,
    int RegistrySkipped,
    int RegistryFailed,
    int ShortcutsRemoved,
    long ReclaimedBytes,
    IReadOnlyList<string> RemovedPaths,
    IReadOnlyList<string> SkippedPaths,
    IReadOnlyList<string> FailedPaths,
    IReadOnlyList<string> RemainingPaths,
    IReadOnlyList<string> Messages);

public sealed class SharedLeftoverScanner
{
    private readonly LeftoverService registryService;
    public SharedLeftoverScanner(LeftoverService? registryService=null)=>this.registryService=registryService??new LeftoverService();
    public async Task<UninstallSnapshot> CaptureAsync(InstalledProgram program,CancellationToken token,bool includePersonalData=false)
    {
        token.ThrowIfCancellationRequested();
        var records=await registryService.CaptureAsync(program);
        var links=await ShortcutLeftoverService.ScanAsync(program,token);
        IReadOnlyList<OwnedDataFolder> data=includePersonalData?await OwnedDataService.ScanAsync(program,token):Array.Empty<OwnedDataFolder>();
        IReadOnlyList<RegistryCleanupTarget>? settings=includePersonalData?await Task.Run(()=>RegistryCleanupService.CaptureSettings(program),token):null;
        string root=InventoryMerger.NormalizePath(program.Location);
        var executables=(program.IconCandidates??Array.Empty<string>()).Prepend(program.IconPath).Select(InstallationPathResolver.Executable).Where(p=>root.Length>0&&p.StartsWith(root+"\\",StringComparison.OrdinalIgnoreCase)&&p.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)&&!System.IO.Path.GetFileName(p).Contains("unins",StringComparison.OrdinalIgnoreCase)&&!System.IO.Path.GetFileName(p).Equals("update.exe",StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if(root.Length>0&&Directory.Exists(root))
        {
            try
            {
                foreach(var exe in Directory.EnumerateFiles(root,"*.exe").Take(64))
                    if(InstallationPathResolver.NormalizeProduct(System.IO.Path.GetFileNameWithoutExtension(exe))==InstallationPathResolver.NormalizeProduct(program.Name))executables.Add(exe);
            }
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException){ }
        }
        return new(program,records,links,data,executables.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),settings);
    }
    public static bool OfficialRemovalConfirmed(UninstallSnapshot before,IEnumerable<InstalledProgram> inventory)
    {
        return Verify(before,inventory).Confirmed;
    }
    public static OfficialVerification Verify(UninstallSnapshot before,IEnumerable<InstalledProgram> inventory)
    {
        string root=InventoryMerger.NormalizePath(before.Program.Location);
        if(root.Length==0)return new(false,"missing_identity","No verified installation path was captured before uninstall.");
        if(inventory.Any(p=>UninstallVerificationService.Matches(before.Program,p)&&InventoryMerger.NormalizePath(p.Location).Length>0&&!LeftoverService.EqualPath(p.Location,root)&&Directory.Exists(p.Location)))return new(false,"installation_changed","A matching application is installed at a different live location.");
        foreach(var file in before.Executables)if(UninstallVerificationService.AnyExecutablePresent(new[]{file}))return new(false,"executable_present",file);
        if(DirectRemovalService.RunningProcesses(root).Count>0)return new(false,"process_running",root);
        bool registered=inventory.Any(p=>UninstallVerificationService.Matches(before.Program,p));
        try
        {
            foreach(var entry in before.Registry)
            {
                if(LeftoverService.RegistrationExists(entry))registered=true;
            }
            // Settings keys are leftovers, not evidence of an installed app.
            if(before.Executables.Count>0)
            {
                if(registered&&Directory.Exists(root)&&Directory.EnumerateFileSystemEntries(root).Any())
                    return new(false,"registration_and_files_present","The application registration and nonempty installation folder remain.");
                return new(true,registered?"stale_registration":"main_executable_removed",registered?"Main executable is absent; the exact captured registration can be reviewed as a leftover.":"Main executable and registration removed; remaining data is eligible for review.");
            }
            if(!Directory.Exists(root)||!Directory.EnumerateFileSystemEntries(root).Any())return new(true,registered?"stale_registration":"installation_removed",root);
            return new(false,"insufficient_evidence","Installation folder still contains files, but no primary executable identity was captured: "+root);
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException){return new(false,"verification_access_denied",ex.Message);}
    }
    public async Task<IReadOnlyList<LeftoverCandidate>> ScanAsync(UninstallSnapshot before,IReadOnlyList<InstalledProgram> inventory,IProgress<string>? progress,CancellationToken token,bool includePersonalData=false)
    {
        var result=new List<LeftoverCandidate>();var service=registryService;
        token.ThrowIfCancellationRequested();
        string installation=InventoryMerger.NormalizePath(before.Program.Location);
        bool installationPresent=InstallationStillPresent(installation);
        if(before.Executables.Count>0&&!UninstallVerificationService.AnyExecutablePresent(before.Executables)&&DirectRemovalService.RunningProcesses(installation).Count==0)
            installationPresent=false;
        bool protectedComponent=RiotGameService.IsProtectedComponent(before.Program);
        string product=InstallationPathResolver.NormalizeProduct(before.Program.Name);
        bool ambiguousProduct=inventory.Any(p=>!UninstallVerificationService.Matches(before.Program,p)&&InstallationPathResolver.NormalizeProduct(p.Name)==product&&product.Length>0);

        // Registrations and settings keys are removed only after the associated
        // application is actually gone. Logs/temporary files alone do not count
        // as a live installation because they are cleaned below.
        if(!installationPresent)
        {
            progress?.Report("Registry");
            var remaining=await service.ScanAsync(before.Program,inventory,before.Registry);
            foreach(var record in remaining.Registry)
            {
                token.ThrowIfCancellationRequested();
                result.Add(new("Registry",$"{record.Hive} [{record.View}]\\{record.Path}",null,"Exact pre-uninstall registration identity; backup required",()=>service.RemoveAsync(new LeftoverReport([],new[]{record})),()=>{using var root=Microsoft.Win32.RegistryKey.OpenBaseKey(record.Hive,record.View);using var key=root.OpenSubKey(record.Path);return key!=null;}));
            }
            if(includePersonalData&&!protectedComponent&&!ambiguousProduct&&before.Settings!=null)
                foreach(var target in before.Settings)
                {
                    token.ThrowIfCancellationRequested();
                    if(!RegistryCleanupService.Exists(target))continue;
                    if(RegistryCleanupService.IsProtectedPath(target.Path))continue;
                    result.Add(new("Registry",$"{target.Hive} [{target.View}]\\{target.Path}",null,"Application-owned settings key; backup required",async()=>{var cleanup=await RegistryCleanupService.RemoveAsync(new[]{target},null,CancellationToken.None);return cleanup.Messages;},()=>RegistryCleanupService.Exists(target)){RequiresSeparateDataConsent=true});
                }
        }

        progress?.Report("Folders");
        if(!protectedComponent)
        {
            foreach(var folder in await RemainingFoldersAsync(service,before,inventory,token))
            {
                token.ThrowIfCancellationRequested();
                if(RiotGameService.IsSharedPath(folder)||RemovalDataPolicy.Preserve(folder,before.Program.Location))continue;
                result.Add(new("Folder",folder,0,"Empty verified installation directory",()=>service.RemoveAsync(new LeftoverReport(new[]{folder},[])),()=>Directory.Exists(folder)));
            }
        }

        var discoveredData=includePersonalData?await OwnedDataService.ScanAsync(before.Program,token):Array.Empty<OwnedDataFolder>();
        if(includePersonalData&&!protectedComponent)
        foreach(var data in before.Data.Concat(discoveredData).DistinctBy(d=>d.Path,StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            if(!Directory.Exists(data.Path))continue;
            // A pre-uninstall capture is not permission to remove a directory
            // whose executable ownership evidence has since changed.
            if(!discoveredData.Any(current=>LeftoverService.EqualPath(current.Path,data.Path)))continue;
            if(RiotGameService.IsSharedPath(data.Path))continue;
            // Main installer-managed components are never directly removed here.
            // This separate directory has product+publisher ownership evidence.
            var owner=before.Program with{Location=data.Path,RequiresOfficialRemoval=false};
            try{DirectRemovalService.Validate(owner,inventory);}catch(IOException){continue;}
            DirectRemovalResult? folderResult=null;
            result.Add(new("Folder",data.Path,data.Bytes,"Matching executable product and publisher metadata",async()=>{folderResult=await new DirectRemovalService().RemoveFilesAsync(owner,inventory,null);return folderResult.Messages.Append($"Removed {folderResult.Removed}; skipped {folderResult.Skipped}: {data.Path}").ToArray();},()=>Directory.Exists(data.Path)){FileResult=()=>folderResult,RequiresSeparateDataConsent=true});
        }

        progress?.Report("Files");
        if(!protectedComponent&&installation.Length>0&&Directory.Exists(installation)&&!inventory.Any(p=>!UninstallVerificationService.Matches(before.Program,p)&&LeftoverService.EqualPath(p.Location,installation)))
        {
            try
            {
                var owner=before.Program with{RequiresOfficialRemoval=false};DirectRemovalService.Validate(owner,inventory);
                foreach(var file in Directory.EnumerateFiles(installation,"*",SearchOption.TopDirectoryOnly))
                {
                    token.ThrowIfCancellationRequested();
                    if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0||new FileInfo(file).Length>4*1024*1024||!new[]{".log",".tmp"}.Contains(System.IO.Path.GetExtension(file),StringComparer.OrdinalIgnoreCase))continue;
                    await using var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read,65536,true);
                    string hash=Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(input,token));
                    var fingerprint=new ShortcutLeftover(file,file,hash);
                    result.Add(new("File",file,new FileInfo(file).Length,"Log or temporary file inside the verified original installation; fingerprint checked before deletion",()=>Task.Run<IReadOnlyList<string>>(()=>new[]{ShortcutLeftoverService.Remove(fingerprint)}),()=>File.Exists(file)));
                }
            }
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){ }
        }

        progress?.Report("Shortcuts");
        if(!protectedComponent)
        {
            var currentLinks=await ShortcutLeftoverService.ScanAsync(before.Program,token);
            string originalRoot=InventoryMerger.NormalizePath(before.Program.Location);
            bool sharedInstallation=originalRoot.Length>0&&inventory.Any(p=>!UninstallVerificationService.Matches(before.Program,p)&&(LeftoverService.EqualPath(p.Location,originalRoot)||InventoryMerger.NormalizePath(p.Location).StartsWith(originalRoot+"\\",StringComparison.OrdinalIgnoreCase)));
            if(!sharedInstallation)foreach(var link in before.Shortcuts.Concat(currentLinks).DistinctBy(l=>l.Path,StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                if(!File.Exists(link.Path))continue;
                if(RiotGameService.IsSharedPath(link.Path))continue;
                result.Add(new("Shortcut",link.Path,new FileInfo(link.Path).Length,"Verified target / game ID; location and fingerprint revalidated; permanent shortcut-only cleanup",()=>Task.Run<IReadOnlyList<string>>(()=>new[]{ShortcutLeftoverService.Remove(link,before.Program)}),()=>File.Exists(link.Path)));
            }
        }
        return result.Where(candidate=>includePersonalData||!candidate.RequiresSeparateDataConsent).ToArray();
    }

    /// <summary>
    /// Scans, removes and re-verifies every verified leftover. The returned
    /// result distinguishes removed, skipped, failed and still-remaining items.
    /// </summary>
    public async Task<LeftoverCleanupResult> CleanupAsync(
        UninstallSnapshot before,
        IReadOnlyList<InstalledProgram> inventory,
        IProgress<string>? progress,
        CancellationToken token,IReadOnlySet<string>? authorizedTargets=null)
    {
        var candidates = await ScanAsync(before, inventory, progress, token);
        int removed = 0, skipped = 0, failed = 0;
        int folders = 0, files = 0, registry = 0, shortcuts = 0;
        int registrySkipped = 0, registryFailed = 0;
        long bytes = 0;
        var removedPaths = new List<string>();
        var skippedPaths = new List<string>();
        var failedPaths = new List<string>();
        var messages = new List<string>();

        foreach (var candidate in candidates)
        {
            token.ThrowIfCancellationRequested();
            if(authorizedTargets!=null&&!authorizedTargets.Contains(candidate.Category+"|"+candidate.Path))
            {
                skipped++;skippedPaths.Add(candidate.Path);
                if(candidate.Category=="Registry")registrySkipped++;
                messages.Add("Skipped newly discovered target outside the authorized cleanup scope: "+candidate.Path);
                continue;
            }
            try
            {
                if (!candidate.Exists())
                {
                    skipped++;
                    skippedPaths.Add(candidate.Path);
                    if (candidate.Category == "Registry") registrySkipped++;
                    continue;
                }
                var outcome = await candidate.Remove();
                messages.AddRange(outcome);
                var fileResult=candidate.FileResult?.Invoke();
                if(fileResult!=null)
                {
                    files+=fileResult.Removed;folders+=fileResult.FoldersRemoved;
                    bytes+=fileResult.Bytes;skipped+=fileResult.Skipped;
                }
                if (candidate.Exists())
                {
                    failed++;
                    failedPaths.Add(candidate.Path);
                    if (candidate.Category == "Registry") registryFailed++;
                    messages.Add("Cleanup incomplete: " + candidate.Path);
                    continue;
                }
                if(candidate.Category is "File" or "Folder" or "Shortcut")await FileChangeHub.PublishAsync(new[]{new FileChange(candidate.Path,true,candidate.Bytes,candidate.Category=="Folder")});
                removed++;
                removedPaths.Add(candidate.Path);
                if (fileResult==null&&candidate.Bytes is long size && size > 0) bytes += size;
                switch (candidate.Category)
                {
                    case "Registry": registry++; break;
                    case "Shortcut": shortcuts++; break;
                    case "File": files++; break;
                    default: if(fileResult==null)folders++; break;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                System.Security.SecurityException or ArgumentException or InvalidOperationException)
            {
                failed++;
                failedPaths.Add(candidate.Path);
                if (candidate.Category == "Registry") registryFailed++;
                messages.Add("Leftover skipped: " + candidate.Path + " (" + ex.GetType().Name + ")");
            }
        }

        bool StillExists(LeftoverCandidate candidate)
        {
            try{return candidate.Exists();}
            catch(Exception ex)when(RemovalEngine.IsItemError(ex)){return true;}
        }
        var remaining = candidates.Where(StillExists)
            .Select(candidate => candidate.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new LeftoverCleanupResult(
            removed, skipped, failed, folders, files, registry, registrySkipped, registryFailed, shortcuts, bytes,
            removedPaths, skippedPaths, failedPaths, remaining, messages);
    }

    private static async Task<IReadOnlyList<string>> RemainingFoldersAsync(LeftoverService service, UninstallSnapshot before,
        IReadOnlyList<InstalledProgram> inventory, CancellationToken token)
    {
        // LeftoverService.ScanAsync already applies empty/unshared folder rules.
        var report = await service.ScanAsync(before.Program, inventory, Array.Empty<RegistryLeftover>());
        token.ThrowIfCancellationRequested();
        return report.Folders;
    }

    private static bool InstallationStillPresent(string root)
    {
        if (root.Length == 0) return false;
        try
        {
            if((File.GetAttributes(root)&FileAttributes.Directory)==0)return true;
            foreach (var entry in new DirectoryInfo(root).EnumerateFileSystemInfos())
            {
                if (entry is DirectoryInfo) return true;
                if (!new[] { ".log", ".tmp" }.Contains(entry.Extension, StringComparer.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
        catch(FileNotFoundException){return false;}catch(DirectoryNotFoundException){return false;}
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            System.Security.SecurityException)
        {
            return true;
        }
    }
}
