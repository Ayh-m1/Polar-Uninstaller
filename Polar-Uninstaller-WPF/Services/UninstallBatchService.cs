namespace PolarUninstallerWpf.Services;

public enum BatchItemOutcome { Succeeded, Failed, Skipped, Manual }
public sealed record UninstallBatchResult(int Succeeded,int Failed,int Skipped,bool Cancelled,int Manual=0);

/// <summary>Sequential execution with per-item isolation. Selection is captured before starting.</summary>
public static class UninstallBatchService
{
    public static async Task<UninstallBatchResult> RunAsync<T>(IReadOnlyList<T> items,
        Func<T,CancellationToken,Task<BatchItemOutcome>> execute,CancellationToken token,
        Action<int,int>? progress=null,Action<Exception>? failure=null,CancellationToken stopAfterCurrent=default)
    {
        int succeeded=0,failed=0,skipped=0,manual=0;
        for(int i=0;i<items.Count;i++)
        {
            if(token.IsCancellationRequested||stopAfterCurrent.IsCancellationRequested)return new(succeeded,failed,skipped+items.Count-i,true,manual);
            progress?.Invoke(i+1,items.Count);
            try
            {
                var outcome=await execute(items[i],token);
                if(outcome==BatchItemOutcome.Succeeded)succeeded++;
                else if(outcome==BatchItemOutcome.Failed)failed++;
                else if(outcome==BatchItemOutcome.Manual)manual++;
                else skipped++;
            }
            catch(OperationCanceledException)when(token.IsCancellationRequested)
            {return new(succeeded,failed,skipped+items.Count-i,true,manual);}
            catch(Exception error){failed++;failure?.Invoke(error);}
        }
        return new(succeeded,failed,skipped,token.IsCancellationRequested,manual);
    }
}
