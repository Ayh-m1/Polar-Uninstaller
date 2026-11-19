using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text;
using System.Windows.Media.Imaging;

namespace PolarUninstallerWpf.Services;

public sealed record IconWorkerRequest(InstalledProgram Program,int Pixels);
public sealed record IconWorkerReply(IconResolutionMethod Method,string Source,int Index,byte[]? Png,bool TimedOut=false,bool Retire=false,string Reason="",string Detail="",string Tried="");
/// <summary>Four persistent, disposable icon-only processes. A stalled decoder is terminated, never left in the UI process.</summary>
public static class IconWorkerPool
{
    private sealed record Job(IconWorkerRequest Request,CancellationToken Token,TaskCompletionSource<IconWorkerReply> Completion);
    private static readonly object Gate=new();
    private static readonly PriorityQueue<Job,long> Queue=new();
    private static readonly SemaphoreSlim Signal=new(0);
    private static readonly List<Process> Children=new();
    private static bool started,stopping;private static long sequence;
    private static string? executable;
    /// <summary>
    /// Chooses the process that hosts the icon worker. A Single-File publish has an
    /// empty <see cref="System.Reflection.Assembly.Location"/>, so the running host
    /// (<see cref="Environment.ProcessPath"/>) or the application base directory must
    /// win. Assembly.Location is only a last-resort developer fallback and is never
    /// allowed to produce a relative path.
    /// </summary>
    public static string SelectExecutable(string? processPath,string assemblyLocation,string baseDirectory,bool applicationEntry)
    {
        // The normal installed/portable app always knows its own executable, even
        // when that path contains spaces or non-ASCII (Arabic) characters.
        if(applicationEntry&&IsHostExecutable(processPath))return Path.GetFullPath(processPath!);
        string sibling=Path.Combine(baseDirectory,"PolarUninstaller.exe");
        if(File.Exists(sibling))return Path.GetFullPath(sibling);
        if(IsHostExecutable(processPath))return Path.GetFullPath(processPath!);
        if(!string.IsNullOrWhiteSpace(assemblyLocation))
        {
            string? folder=Path.GetDirectoryName(assemblyLocation);
            if(!string.IsNullOrEmpty(folder))
            {
                string developer=Path.Combine(folder,"PolarUninstaller.exe");
                if(File.Exists(developer))return Path.GetFullPath(developer);
            }
        }
        throw new FileNotFoundException(
            $"Icon worker executable was not found. ProcessPath='{processPath}'; Assembly.Location='{assemblyLocation}'; base='{baseDirectory}'.",
            sibling);
    }
    private static bool IsHostExecutable(string? path)
    {
        if(string.IsNullOrWhiteSpace(path)||!Path.IsPathFullyQualified(path))return false;
        if(!Path.GetExtension(path).Equals(".exe",StringComparison.OrdinalIgnoreCase))return false;
        if(Path.GetFileNameWithoutExtension(path).Equals("dotnet",StringComparison.OrdinalIgnoreCase))return false;
        return File.Exists(path);
    }
    private static string SafeAssemblyLocation()
    {
        try{return typeof(App).Assembly.Location??"";}catch(Exception e)when(e is NotSupportedException or System.IO.IOException){return "";}
    }
    private static bool IsApplicationEntry
    {
        get
        {
            try{return ReferenceEquals(System.Reflection.Assembly.GetEntryAssembly(),typeof(App).Assembly);}
            catch(Exception e)when(e is NotSupportedException or System.IO.IOException){return false;}
        }
    }
    public static string WorkerExecutable=>executable??SelectExecutable(Environment.ProcessPath,SafeAssemblyLocation(),AppContext.BaseDirectory,IsApplicationEntry);
    public static void ConfigureExecutable(string path){lock(Gate)executable=Path.GetFullPath(path);}
    /// <summary>Builds the exact worker command line. ArgumentList quotes Arabic/space paths safely.</summary>
    public static ProcessStartInfo BuildStartInfo(string host)
    {
        host=Path.GetFullPath(host);
        bool managed=Path.GetExtension(host).Equals(".dll",StringComparison.OrdinalIgnoreCase);
        string folder=Path.GetDirectoryName(host) is {Length:>0} directory?directory:Environment.CurrentDirectory;
        var start=new ProcessStartInfo(managed?"dotnet":host)
        {
            UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,
            RedirectStandardOutput=true,RedirectStandardError=true,
            StandardErrorEncoding=new UTF8Encoding(false),StandardInputEncoding=new UTF8Encoding(false),
            StandardOutputEncoding=new UTF8Encoding(false),WorkingDirectory=folder
        };
        if(managed)start.ArgumentList.Add(host);
        start.ArgumentList.Add("--polar-icon-worker");
        return start;
    }
    public static Task<IconWorkerReply> ResolveAsync(InstalledProgram program,int pixels,CancellationToken token,bool priority)
    {
        var completion=new TaskCompletionSource<IconWorkerReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock(Gate)
        {
            if(stopping)return Task.FromCanceled<IconWorkerReply>(new CancellationToken(true));
            if(!started){started=true;for(int i=0;i<4;i++)_=SafeTasks.Run(Loop,"Icon worker loop");}
            if(Queue.Count>=256){var kept=Queue.UnorderedItems.Where(x=>!x.Element.Token.IsCancellationRequested).ToArray();Queue.Clear();foreach(var item in kept)Queue.Enqueue(item.Element,item.Priority);if(Queue.Count>=256)return Task.FromResult(new IconWorkerReply(IconResolutionMethod.Badge,"",0,null,Reason:"queue_busy",Detail:"Visible icon queue is busy; retry on refresh."));}
            Queue.Enqueue(new(new(program,pixels),token,completion),priority?-++sequence:long.MaxValue/2+sequence++);
        }
        Signal.Release();return completion.Task.WaitAsync(token);
    }
    private static async Task Loop()
    {
        Process? process=null;Task<string>? stderr=null;
        while(true)
        {
            if(!await Signal.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false)){Kill(process);process=null;continue;}Job job;
            lock(Gate){if(stopping)break;if(!Queue.TryDequeue(out job!,out _))continue;}
            if(job.Token.IsCancellationRequested){job.Completion.TrySetCanceled(job.Token);continue;}
            string host="",stage="worker_start";
            try
            {
                if(process==null||process.HasExited)
                {
                    host=WorkerExecutable;
                    var start=BuildStartInfo(host);
                    process=Process.Start(start)??throw new IOException("Icon worker did not start");lock(Gate)Children.Add(process);
                    stderr=process.StandardError.ReadToEndAsync();stage="startup_timeout";
                    string? ready=await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15),job.Token).ConfigureAwait(false);if(ready!="POLAR-ICON-READY")throw new IOException("Invalid icon worker handshake");
                }
                stage="resolve_timeout";
                await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(job.Request)).ConfigureAwait(false);
                await process.StandardInput.FlushAsync().ConfigureAwait(false);
                string? line=await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5),job.Token).ConfigureAwait(false);
                var result=line==null?throw new IOException("Icon worker closed"):JsonSerializer.Deserialize<IconWorkerReply>(line)??throw new IOException("Invalid icon worker result");
                if(result.Retire){Kill(process);process=null;}
                job.Completion.TrySetResult(result);
            }
            catch(Exception e)when(e is IOException or System.ComponentModel.Win32Exception or InvalidOperationException or TimeoutException or OperationCanceledException or JsonException)
            {
                int? exit=null;try{if(process?.HasExited==true)exit=process.ExitCode;}catch(InvalidOperationException){}
                Kill(process);process=null;
                string errorText="";if(stderr!=null)try{errorText=await stderr.WaitAsync(TimeSpan.FromMilliseconds(300)).ConfigureAwait(false);}catch(Exception){}
                string reason=e is TimeoutException?stage:e is System.ComponentModel.Win32Exception or FileNotFoundException?"worker_start":stage=="startup_timeout"?"worker_handshake":"worker_io";
                string detail=$"path={host}; args=--polar-icon-worker; exit={(exit?.ToString()??"not started/terminated")}; native={(e as System.ComponentModel.Win32Exception)?.NativeErrorCode}; stderr={errorText[..Math.Min(errorText.Length,2048)]}; error={e.Message}";
                if(job.Token.IsCancellationRequested)job.Completion.TrySetCanceled(job.Token);
                else job.Completion.TrySetResult(new(LocalIconSources.Component(job.Request.Program)?IconResolutionMethod.Component:IconResolutionMethod.Badge,"",0,null,e is TimeoutException,Reason:reason,Detail:detail));
            }
        }
        Kill(process);
    }
    private static void Kill(Process? process)
    {
        if(process==null)return;
        try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}
        lock(Gate)Children.Remove(process);process.Dispose();
    }
    public static void StopAll()
    {
        Process[] children;lock(Gate){stopping=true;children=Children.ToArray();while(Queue.TryDequeue(out var job,out _))job.Completion.TrySetCanceled();}
        foreach(var child in children)Kill(child);Signal.Release(4);
    }
    public static void RunConsoleWorker()
    {
        Console.InputEncoding=new UTF8Encoding(false);Console.OutputEncoding=new UTF8Encoding(false);
        Console.WriteLine("POLAR-ICON-READY");Console.Out.Flush();string? line;
        while((line=Console.ReadLine())!=null)
        {
            IconWorkerReply reply=new(IconResolutionMethod.Badge,"",0,null);bool retire=false;var clock=Stopwatch.StartNew();var tried=new List<string>();
            try
            {
                if(line.Length>262144)throw new JsonException();var request=JsonSerializer.Deserialize<IconWorkerRequest>(line)??throw new JsonException();
                reply=new(LocalIconSources.Component(request.Program)?IconResolutionMethod.Component:IconResolutionMethod.Badge,"",0,null);
                foreach(var candidate in LocalIconSources.Candidates(request.Program,request.Pixels))
                {
                    if(clock.Elapsed>TimeSpan.FromMilliseconds(4000))break;
                    tried.Add(candidate.Method+":"+candidate.Path);
                    var decoding=Task.Run(()=>LocalIconDecoder.Load(candidate,request.Pixels));
                    if(!decoding.Wait(TimeSpan.FromMilliseconds(700))){retire=true;continue;}
                    var image=decoding.GetAwaiter().GetResult();if(image==null)continue;
                    using var stream=new MemoryStream();var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));encoder.Save(stream);
                    reply=new(candidate.Method,candidate.Path,candidate.Index,stream.ToArray());break;
                }
            }
            catch(Exception e)when(e is not OutOfMemoryException and not StackOverflowException){reply=reply with{Reason="decode_error",Detail=e.GetType().Name+": "+e.Message};}
            Console.WriteLine(JsonSerializer.Serialize(reply with{Retire=retire,Reason=reply.Png==null?(reply.Reason.Length>0?reply.Reason:retire?"candidate_timeout":"no_usable_source"):"",Tried=string.Join(" | ",tried.Take(8))}));Console.Out.Flush();if(retire)break;
        }
    }
}
