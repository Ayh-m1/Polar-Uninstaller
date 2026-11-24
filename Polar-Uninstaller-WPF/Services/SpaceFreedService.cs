using System.IO;
using System.Text.Json;

namespace PolarUninstallerWpf.Services;

/// <summary>Lifetime recorded cleanup bytes and known verified uninstall sizes.
/// This is independent of the bounded recent-history list and developer metrics.</summary>
public sealed class SpaceFreedService
{
    public static SpaceFreedService Instance {get;}=new();
    private sealed record State(long Bytes,HashSet<string> Operations);
    private readonly string file;
    private readonly SemaphoreSlim gate=new(1,1);
    private State state=new(0,new(StringComparer.Ordinal));
    private bool unreadable;
    public event Action? Changed;
    public long TotalBytes=>Interlocked.Read(ref totalBytes);
    public bool Available=>!unreadable;
    private long totalBytes;
    public SpaceFreedService(string? directory=null)
    {
        file=Path.Combine(directory??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PolarUninstallerWPF-Development"),"space-freed.json");
        try
        {
            if(File.Exists(file))
            {
                var loaded=JsonSerializer.Deserialize<State>(File.ReadAllText(file));
                if(loaded is null||loaded.Bytes<0||loaded.Operations is null)throw new JsonException("Invalid space-freed state");
                state=loaded;totalBytes=state.Bytes;
            }
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or JsonException)
        {unreadable=true;CrashDiagnostics.Record("Read space-freed total",ex);}
    }
    public Task<bool> AddAsync(long bytes,string operationId)=>Task.Run(async()=>
    {
        if(bytes<=0||string.IsNullOrWhiteSpace(operationId)||unreadable)return false;
        await gate.WaitAsync().ConfigureAwait(false);
        string? temporary=null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            using var lease=await AcquireFileLockAsync().ConfigureAwait(false);
            if(File.Exists(file))
            {
                var latest=JsonSerializer.Deserialize<State>(await File.ReadAllTextAsync(file).ConfigureAwait(false));
                if(latest is null||latest.Bytes<0||latest.Operations is null)throw new JsonException("Invalid space-freed state");
                state=latest;Interlocked.Exchange(ref totalBytes,latest.Bytes);
            }
            if(state.Operations.Contains(operationId))return false;
            var operations=new HashSet<string>(state.Operations,StringComparer.Ordinal){operationId};
            var next=new State(checked(state.Bytes+bytes),operations);
            temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";
            await File.WriteAllTextAsync(temporary,JsonSerializer.Serialize(next)).ConfigureAwait(false);
            File.Move(temporary,file,true);temporary=null;
            state=next;Interlocked.Exchange(ref totalBytes,next.Bytes);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or OverflowException or JsonException)
        {CrashDiagnostics.Record("Save space-freed total",ex);return false;}
        finally
        {
            if(temporary is not null)try{File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}
            gate.Release();
        }
        Changed?.Invoke();return true;
    });
    public Task ReloadAsync(CancellationToken token=default)=>Task.Run(async()=>
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var latest=File.Exists(file)?JsonSerializer.Deserialize<State>(await File.ReadAllTextAsync(file,token).ConfigureAwait(false)):new State(0,new(StringComparer.Ordinal));
            if(latest is null||latest.Bytes<0||latest.Operations is null)throw new JsonException("Invalid space-freed state");
            state=latest;Interlocked.Exchange(ref totalBytes,latest.Bytes);unreadable=false;
        }
        finally{gate.Release();}
    },token);
    private async Task<FileStream> AcquireFileLockAsync()
    {
        for(int attempt=0;;attempt++)
        {
            try{return new FileStream(file+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
            catch(IOException) when(attempt<20){await Task.Delay(25).ConfigureAwait(false);}
        }
    }
}
