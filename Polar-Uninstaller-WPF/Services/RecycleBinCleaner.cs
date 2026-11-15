using System.Runtime.InteropServices;

namespace PolarUninstallerWpf.Services;

public static class RecycleBinCleaner
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RecycleInfo {public uint Size;public long Bytes;public long Items;}
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
    private static extern int SHQueryRecycleBinW(string? root,ref RecycleInfo info);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
    private static extern int SHEmptyRecycleBinW(IntPtr owner,string? root,uint flags);
    private static readonly SemaphoreSlim Gate=new(1,1);
    public static Task<TempCleanerService.Result> RunAsync(bool delete,CancellationToken token)
    {
        var completion=new TaskCompletionSource<TempCleanerService.Result>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker=new Thread(()=>
        {
            bool entered=false;
            try
            {
                Gate.Wait(token);entered=true;
                completion.TrySetResult(Run(Query,()=>SHEmptyRecycleBinW(IntPtr.Zero,null,7),delete,token));
            }
            catch(OperationCanceledException){completion.TrySetCanceled(token);}
            catch(Exception ex){completion.TrySetException(ex);}
            finally{if(entered)Gate.Release();}
        }){IsBackground=true,Name="Polar Recycle Bin cleanup"};
        worker.SetApartmentState(ApartmentState.STA);worker.Start();return completion.Task;
    }
    private static (long Bytes,long Items) Query()
    {
        var info=new RecycleInfo{Size=(uint)Marshal.SizeOf<RecycleInfo>()};
        Marshal.ThrowExceptionForHR(SHQueryRecycleBinW(null,ref info));
        if(info.Bytes<0||info.Items<0)throw new System.IO.IOException("Windows returned invalid Recycle Bin statistics.");
        return(info.Bytes,info.Items);
    }
    // Kept separate from the native boundary so empty/error/partial cases can
    // be tested without emptying the user's real Recycle Bin.
    internal static TempCleanerService.Result Run(Func<(long Bytes,long Items)> query,Func<int> empty,bool delete,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var before=query();
        if(!delete)return new((int)Math.Min(before.Items,int.MaxValue),before.Bytes,0);
        if(before.Items==0&&before.Bytes==0)return new(0,0,0);
        token.ThrowIfCancellationRequested();
        int hr=empty();
        var after=query();
        long freed=Math.Max(0,before.Bytes-after.Bytes);
        int removed=(int)Math.Min(int.MaxValue,Math.Max(0,before.Items-after.Items));
        int remaining=(int)Math.Min(int.MaxValue,Math.Max(0,after.Items));
        if(hr<0&&removed==0&&freed==0)Marshal.ThrowExceptionForHR(hr);
        return new(removed,freed,remaining);
    }
}
