using System.IO;

namespace PolarUninstallerWpf.Services;

public sealed record PlannedFile(string Path, long Bytes, DateTime Written);
public sealed record RemovalPlan(UninstallSnapshot Snapshot, string? Root,
    IReadOnlyList<PlannedFile> Files, IReadOnlyList<string> Directories, IReadOnlyList<string> Notes);
public sealed record RemovalPass(RemovalPlan Plan, LeftoverCleanupResult Cleanup,
    IReadOnlyList<LeftoverCandidate> Remaining);

/// <summary>
/// One engine for direct removal and its explicit second-stage retry. Identity
/// is captured BEFORE uninstall, not reconstructed from a stale inventory row.
/// Official completion calls InspectAsync only; it never executes this plan.
/// </summary>
public sealed class RemovalEngine
{
    private readonly SharedLeftoverScanner scanner;
    public RemovalEngine(SharedLeftoverScanner? scanner=null)=>this.scanner=scanner??new();

    public async Task<RemovalPlan> PlanAsync(UninstallSnapshot snapshot,
        IReadOnlyList<InstalledProgram> inventory, bool direct, CancellationToken token)
    {
        string? root=null;
        if(direct) root=DirectRemovalService.Validate(snapshot.Program,inventory);
        else
        {
            // Never weaken the policy for MSI, games, shared/system components
            // simply because their official uninstaller has already run.
            try { root=DirectRemovalService.Validate(snapshot.Program,inventory); }
            catch(IOException) { }
        }
        var files=new List<PlannedFile>();var directories=new List<string>();var notes=new List<string>();
        if(root!=null) await Task.Run(()=>
        {
            var pending=new Stack<string>();if(Directory.Exists(root))pending.Push(root);
            while(pending.TryPop(out var path))
            {
                token.ThrowIfCancellationRequested();
                if(RemovalDataPolicy.Preserve(path,root))continue;
                try
                {
                    EnsurePath(path,root);
                    directories.Add(path);
                    foreach(var entry in new DirectoryInfo(path).EnumerateFileSystemInfos())
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            EnsurePath(entry.FullName,root);
                            if(entry is DirectoryInfo child)pending.Push(child.FullName);
                            else if(entry is FileInfo file&&!RemovalDataPolicy.Preserve(file.FullName,root))files.Add(new(file.FullName,file.Length,file.LastWriteTimeUtc));
                        }
                        catch(Exception ex) when(IsItemError(ex)){notes.Add("Skipped: "+entry.FullName+" ("+Reason(ex)+")");}
                    }
                }
                catch(Exception ex) when(IsItemError(ex)){notes.Add("Skipped folder: "+path+" ("+Reason(ex)+")");}
            }
        },token);
        return new(snapshot,root,files,directories,notes);
    }

    public Task<IReadOnlyList<LeftoverCandidate>> InspectAsync(RemovalPlan plan,
        IReadOnlyList<InstalledProgram> inventory, CancellationToken token)=>
        Task.Run(()=>InspectCoreAsync(plan,inventory,token),token);

    private async Task<IReadOnlyList<LeftoverCandidate>> InspectCoreAsync(RemovalPlan plan,
        IReadOnlyList<InstalledProgram> inventory, CancellationToken token)
    {
        var result=(await scanner.ScanAsync(plan.Snapshot,inventory,null,token)).ToList();
        if(plan.Root!=null)
        {
            // Revalidate against the CURRENT inventory. Do not delete a new
            // application that reused the original directory.
            try
            {
                var root=DirectRemovalService.Validate(plan.Snapshot.Program,inventory);
                foreach(var file in plan.Files)
                {
                    token.ThrowIfCancellationRequested();
                    if(RemovalDataPolicy.Preserve(file.Path,root))continue;
                    try
                    {
                        EnsurePath(file.Path,root);
                        var info=new FileInfo(file.Path);
                        if(info.Exists&&info.Length==file.Bytes&&info.LastWriteTimeUtc==file.Written)
                            result.Add(new("File",file.Path,file.Bytes,"Captured verified installation file",()=>throw new InvalidOperationException("Execute the removal plan, not an inspection candidate."),()=>File.Exists(file.Path)));
                    }
                    catch(Exception ex)when(IsItemError(ex)){ }
                }
                foreach(var directory in plan.Directories.Where(Directory.Exists))
                {
                    if(RemovalDataPolicy.Preserve(directory,root))continue;
                    try
                    {
                        EnsurePath(directory,root);
                        if(!Directory.EnumerateFileSystemEntries(directory).Any())result.Add(new("Folder",directory,0,"Empty captured installation folder",()=>throw new InvalidOperationException("Execute the removal plan."),()=>Directory.Exists(directory)));
                    }
                    catch(Exception ex)when(IsItemError(ex)){ }
                }
            }
            catch(Exception ex)when(IsItemError(ex)){ }
        }
        return result.DistinctBy(c=>c.Category+"|"+c.Path,StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<RemovalPass> ExecuteAsync(RemovalPlan plan,IReadOnlyList<InstalledProgram> inventory,
        IProgress<string>? progress,CancellationToken token,IReadOnlySet<string>? authorizedCleanup=null)
    {
        var removedPaths=new List<string>();var failedPaths=new List<string>();var skippedPaths=new List<string>();
        var messages=new List<string>(plan.Notes);long bytes=0;int files=0,folders=0;
        if(plan.Root!=null)
        {
            var root=DirectRemovalService.Validate(plan.Snapshot.Program,inventory);
            await Task.Run(async()=>
            {
                var changes=new List<FileChange>();
                try
                {
                var clock=System.Diagnostics.Stopwatch.StartNew();
                foreach(var file in plan.Files)
                {
                    token.ThrowIfCancellationRequested();
                    if(RemovalDataPolicy.Preserve(file.Path,root)){messages.Add("Preserved personal/settings data outside routine removal scope: "+file.Path);continue;}
                    try
                    {
                        EnsurePath(file.Path,root);
                        var info=new FileInfo(file.Path);
                        if(!info.Exists)continue;
                        if(info.Length!=file.Bytes||info.LastWriteTimeUtc!=file.Written)
                        {skippedPaths.Add(file.Path);messages.Add("Skipped changed file: "+file.Path);continue;}
                        File.Delete(file.Path);
                        if(File.Exists(file.Path))throw new IOException("File remains after deletion.");
                        changes.Add(new(file.Path,true,file.Bytes));files++;bytes+=file.Bytes;removedPaths.Add(file.Path);messages.Add("Removed file: "+file.Path);
                    }
                    catch(Exception ex) when(IsItemError(ex)){failedPaths.Add(file.Path);messages.Add("Failed file: "+file.Path+" ("+Reason(ex)+")");}
                    if(clock.ElapsedMilliseconds>150){progress?.Report(files.ToString("N0"));clock.Restart();}
                }
                foreach(var directory in plan.Directories.OrderByDescending(d=>d.Length))
                {
                    token.ThrowIfCancellationRequested();
                    if(RemovalDataPolicy.Preserve(directory,root))continue;
                    try
                    {
                        EnsurePath(directory,root);
                        if(!Directory.Exists(directory))continue;
                        // Retained personal data or uncertain new content does not
                        // authorize recursive folder deletion or a false cleanup failure.
                        if(Directory.EnumerateFileSystemEntries(directory).Any())continue;
                        Directory.Delete(directory,false);changes.Add(new(directory,true,0,true));folders++;removedPaths.Add(directory);messages.Add("Removed folder: "+directory);
                    }
                    catch(Exception ex) when(IsItemError(ex)){failedPaths.Add(directory);messages.Add("Retained folder: "+directory+" ("+Reason(ex)+")");}
                }
                }finally{await FileChangeHub.PublishAsync(changes).ConfigureAwait(false);}
            },token);
        }
        // Use the ORIGINAL registry/shortcut capture, even after files and the
        // uninstall registration disappeared. Each remover revalidates its target.
        var cleanup=await Task.Run(()=>scanner.CleanupAsync(plan.Snapshot,inventory,null,token,authorizedCleanup),token);
        var remaining=await InspectAsync(plan,inventory,token);
        bool Retained(string path)
        {
            try{File.GetAttributes(path);return true;}
            catch(FileNotFoundException){return false;}catch(DirectoryNotFoundException){return false;}
            catch(Exception ex)when(IsItemError(ex)){return true;}
        }
        failedPaths=failedPaths.Where(Retained).ToList();
        skippedPaths=skippedPaths.Where(Retained).ToList();
        var remainingPaths=remaining.Select(c=>c.Path).Concat(failedPaths).Concat(skippedPaths)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var total=cleanup with
        {
            Removed=cleanup.Removed+files+folders, FilesRemoved=cleanup.FilesRemoved+files,
            FoldersRemoved=cleanup.FoldersRemoved+folders, Failed=cleanup.Failed+failedPaths.Count,
            Skipped=cleanup.Skipped+skippedPaths.Count+plan.Notes.Count,ReclaimedBytes=cleanup.ReclaimedBytes+bytes,
            RemovedPaths=cleanup.RemovedPaths.Concat(removedPaths).ToArray(),
            FailedPaths=cleanup.FailedPaths.Concat(failedPaths).ToArray(),SkippedPaths=cleanup.SkippedPaths.Concat(skippedPaths).ToArray(),
            RemainingPaths=cleanup.RemainingPaths.Concat(remainingPaths).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Messages=messages.Concat(cleanup.Messages).ToArray()
        };
        return new(plan,total,remaining);
    }

    internal static void EnsurePath(string path,string root)
    {
        DirectRemovalService.ValidateRoot(root);
        string full=Path.GetFullPath(path), parent=Path.GetFullPath(root).TrimEnd('\\');
        if(!full.Equals(parent,StringComparison.OrdinalIgnoreCase)&&!full.StartsWith(parent+"\\",StringComparison.OrdinalIgnoreCase))
            throw new IOException("Target is outside the verified installation.");
        for(string? p=full;p!=null;p=Path.GetDirectoryName(p))
        {
            // File.Exists hides access-denied: GetAttributes does not.
            try{if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new IOException("Symbolic link or junction retained.");}
            catch(FileNotFoundException){ }catch(DirectoryNotFoundException){ }
            if(p.Equals(parent,StringComparison.OrdinalIgnoreCase))break;
        }
    }
    internal static bool IsItemError(Exception ex)=>ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or InvalidOperationException;
    internal static string Reason(Exception ex)=>ex is UnauthorizedAccessException or System.Security.SecurityException?"access denied; administrator permission may be required":ex is IOException&&((ex.HResult&0xffff)==32||(ex.HResult&0xffff)==33)?"locked by a process":ex is FileNotFoundException or DirectoryNotFoundException?"already absent":ex.Message;
}
