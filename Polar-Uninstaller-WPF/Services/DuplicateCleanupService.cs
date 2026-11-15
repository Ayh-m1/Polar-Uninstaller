using System.IO;
using System.Security.Cryptography;

namespace PolarUninstallerWpf.Services;

public sealed record DuplicateCleanupItem(string Path,bool Removed,string Code,string Detail="");
public sealed record DuplicateCleanupResult(IReadOnlyList<DuplicateCleanupItem> Items,long Reclaimed,IReadOnlyList<DuplicateGroup> Groups);

/// <summary>Only scanned members are eligible. A live SHA-256-verified keeper
/// is held read-only (no writes/deletion) throughout each group's cleanup.</summary>
public sealed class DuplicateCleanupService
{
    public Task<DuplicateCleanupResult> CleanAsync(DuplicateScanResult scan,IEnumerable<string> selected,
        bool permanent,IReadOnlyList<string> installationRoots,IProgress<DuplicateCleanupItem>? progress,CancellationToken token)=>Task.Run(async()=>
    {
        if(!Path.IsPathFullyQualified(scan.Root))throw new ArgumentException("The scan has no verified physical scope.");
        string root=Path.GetFullPath(scan.Root);
        var selection=selected.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var outcomes=new List<DuplicateCleanupItem>();long reclaimed=0;
        var operations=new BrowserOperationsService();
        var known=scan.Groups.SelectMany(g=>g.Files).Select(f=>f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        void Add(DuplicateCleanupItem item){outcomes.Add(item);progress?.Report(item);}
        foreach(string unknown in selection.Where(p=>!known.Contains(p)))Add(new(unknown,false,"not_scanned"));
        foreach(var group in scan.Groups)
        {
            token.ThrowIfCancellationRequested();
            var requested=group.Files.Where(f=>selection.Contains(f.Path)).ToArray();
            if(requested.Length==0)continue;
            DuplicateFile? keeper=null;FileStream? keepStream=null;
            // Prefer an unselected copy. If every copy was selected, retain one
            // selected copy rather than trusting checkbox state as permission to
            // destroy the entire content group.
            foreach(var candidate in group.Files.OrderBy(f=>selection.Contains(f.Path)))
            {
                try
                {
                    ValidateScope(candidate.Path,root);
                    keepStream=await OpenVerifiedAsync(candidate,group.Hash,token);
                    keeper=candidate;break;
                }
                catch(Exception ex)when(ItemError(ex)){keepStream?.Dispose();keepStream=null;}
            }
            if(keeper==null||keepStream==null)
            {foreach(var file in requested)Add(new(file.Path,false,"no_verified_keeper"));continue;}
            await using(keepStream)
            {
                foreach(var file in requested)
                {
                    token.ThrowIfCancellationRequested();
                    if(file.Path.Equals(keeper.Path,StringComparison.OrdinalIgnoreCase)){Add(new(file.Path,false,"last_copy"));continue;}
                    try
                    {
                        ValidateTarget(file.Path,root,installationRoots);
                        await using(var input=await OpenVerifiedAsync(file,group.Hash,token)){ }
                        // Metadata and protection are checked again immediately
                        // before calling the existing Windows deletion service.
                        ValidateTarget(file.Path,root,installationRoots);
                        var info=new FileInfo(file.Path);
                        if(info.Length!=file.Size||info.LastWriteTimeUtc!=file.Modified)throw new IOException("The file changed after scanning.");
                        var deleted=(await operations.DeleteAsync(new[]{file.Path},permanent,token)).Single();
                        bool removed=deleted.Success&&!File.Exists(file.Path);
                        Add(new(file.Path,removed,removed?"removed":"delete_failed",deleted.Error??""));
                        if(removed)reclaimed+=file.Size;
                    }
                    catch(FileNotFoundException){Add(new(file.Path,false,"missing"));}
                    catch(DirectoryNotFoundException){Add(new(file.Path,false,"missing"));}
                    catch(DeletionProtectionException ex){Add(new(file.Path,false,"protected",ex.Message));}
                    catch(Exception ex)when(ItemError(ex)){Add(new(file.Path,false,"changed_or_locked",ex.Message));}
                }
            }
        }
        var remaining=new List<DuplicateGroup>();
        foreach(var group in scan.Groups)
        {
            if(!group.Files.Any(f=>selection.Contains(f.Path))){remaining.Add(group);continue;}
            var valid=new List<DuplicateFile>();
            foreach(var file in group.Files)
            {
                token.ThrowIfCancellationRequested();
                try{ValidateScope(file.Path,root);await using var stream=await OpenVerifiedAsync(file,group.Hash,token);valid.Add(file);}
                catch(Exception ex)when(ItemError(ex)){ }
            }
            if(valid.Count>1)remaining.Add(group with{Files=valid});
        }
        return new DuplicateCleanupResult(outcomes,reclaimed,remaining);
    },token);

    private static async Task<FileStream> OpenVerifiedAsync(DuplicateFile file,string hash,CancellationToken token)
    {
        var info=new FileInfo(file.Path);
        if(info.Length!=file.Size||info.LastWriteTimeUtc!=file.Modified)throw new IOException("The file changed after scanning.");
        var stream=new FileStream(file.Path,FileMode.Open,FileAccess.Read,FileShare.Read,131072,FileOptions.Asynchronous|FileOptions.SequentialScan);
        try
        {
            string actual=Convert.ToHexString(await SHA256.HashDataAsync(stream,token));
            info.Refresh();
            if(!hash.Equals(actual,StringComparison.OrdinalIgnoreCase)||info.Length!=file.Size||info.LastWriteTimeUtc!=file.Modified)
                throw new IOException("The contents changed after scanning.");
            return stream;
        }
        catch{await stream.DisposeAsync();throw;}
    }
    internal static void ValidateScope(string path,string root)
    {
        string full=Path.GetFullPath(path),prefix=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if(!full.StartsWith(prefix.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))throw new DeletionProtectionException("Outside the selected scan folder.");
        for(string? p=full;p!=null;p=Path.GetDirectoryName(p))
            if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new DeletionProtectionException("Links and junctions are protected.");
    }
    private static void ValidateTarget(string path,string root,IReadOnlyList<string> installations)
    {
        ValidateScope(path,root);BrowserOperationsService.ValidateDeletion(path);
        foreach(string installation in installations.Append(AppContext.BaseDirectory).Append(Path.GetDirectoryName(Environment.ProcessPath)??""))
        {
            if(string.IsNullOrWhiteSpace(installation))continue;
            string folder=Path.GetFullPath(installation).TrimEnd('\\');
            if(path.StartsWith(folder+"\\",StringComparison.OrdinalIgnoreCase))throw new DeletionProtectionException("An application installation is protected.");
        }
        if(new[]{".exe",".dll",".sys",".msi",".msix",".lnk",".url"}.Contains(Path.GetExtension(path),StringComparer.OrdinalIgnoreCase))
            throw new DeletionProtectionException("Application binaries, packages and shortcuts are protected from duplicate cleanup.");
    }
    private static bool ItemError(Exception ex)=>ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException;
}
