namespace PolarUninstallerWpf.Services;
public static class SafeTasks
{
    public static async Task Run(Func<Task> work,string context)
    {
        try{await work().ConfigureAwait(false);}
        catch(OperationCanceledException){}
        catch(Exception error)when(error is not OutOfMemoryException and not StackOverflowException){AppSafety.Report(context,error);}
    }
}
