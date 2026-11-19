using System.Diagnostics;

namespace PolarUninstallerWpf.Services;

public enum FileChangeKind { Deleted,Recycled,HiddenFromDashboard }
public sealed record FileChange(string Path,bool Success,long? Bytes=null,bool IsDirectory=false);
public sealed record FileChangeBatch(Guid Id,DateTimeOffset At,FileChangeKind Kind,IReadOnlyList<FileChange> Changes);
public static class FileChangeHub
{
    public static event Action<FileChangeBatch>? Changed;
    public static async Task PublishAsync(IEnumerable<FileChange> changes,FileChangeKind kind=FileChangeKind.Deleted)
    {
        var batch=new FileChangeBatch(Guid.NewGuid(),DateTimeOffset.UtcNow,kind,changes.ToArray());

        // Cache correctness must not depend on whether a Dashboard view exists.
        try{if(batch.Changes.Count>0)await DashboardStorageService.ApplyChangesAsync(batch).ConfigureAwait(false);}
        catch(Exception error)when(error is not OutOfMemoryException and not StackOverflowException){CrashDiagnostics.Record("Deletion cache notification",error);}
        foreach(var handler in Changed?.GetInvocationList()??Array.Empty<Delegate>())
            try{((Action<FileChangeBatch>)handler)(batch);}catch(Exception error){Debug.WriteLine(error);}
    }
}
