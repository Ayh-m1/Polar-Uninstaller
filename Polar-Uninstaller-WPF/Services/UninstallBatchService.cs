namespace PolarUninstallerWpf.Services;

public enum BatchItemOutcome { Succeeded, Failed, Skipped }
public sealed record UninstallBatchResult(int Succeeded,int Failed,int Skipped,bool Cancelled);

/// <summary>Sequential execution with per-item isolation. Selection is captured before starting.</summary>
public static class UninstallBatchService
{
    public static async Task<UninstallBatchResult> RunAsync<T>(IReadOnlyList<T> items,
        Func<T,CancellationToken,Task<BatchItemOutcome>> execute,CancellationToken token,
        Action<int,int>? progress=null,Action<Exception>? failure=null)
    {
        int succeeded=0,failed=0,skipped=0;
        for(int i=0;i<items.Count;i++)
        {
            if(token.IsCancellationRequested)return new(succeeded,failed,skipped+items.Count-i,true);
            progress?.Invoke(i+1,items.Count);
            try
            {
                var outcome=await execute(items[i],token);
                if(outcome==BatchItemOutcome.Succeeded)succeeded++;
                else if(outcome==BatchItemOutcome.Failed)failed++;
                else skipped++;
            }
            catch(OperationCanceledException)when(token.IsCancellationRequested)
            {return new(succeeded,failed,skipped+items.Count-i,true);}
            catch(Exception error){failed++;failure?.Invoke(error);}
        }
        return new(succeeded,failed,skipped,token.IsCancellationRequested);
    }
}
