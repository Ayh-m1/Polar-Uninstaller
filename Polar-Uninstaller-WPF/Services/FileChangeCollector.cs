namespace PolarUninstallerWpf.Services;

/// <summary>Batch successful paths even when cancellation interrupts the surrounding operation.</summary>
public sealed class FileChangeCollector:IAsyncDisposable
{
    private readonly List<FileChange> changes=new();
    public void Add(string path,long? bytes=null,bool directory=false)=>changes.Add(new(path,true,bytes,directory));
    public ValueTask DisposeAsync()=>new(FileChangeHub.PublishAsync(changes));
}
