using System.Collections.Concurrent;
using System.Diagnostics;
namespace PolarUninstallerWpf.Services;
public static class PerformanceTrace
{
    private static readonly ConcurrentDictionary<string,(long Count,double Max,double Total)> Samples=new();
    public static void Record(string name,double ms)=>Samples.AddOrUpdate(name,(1,ms,ms),(_,s)=>(s.Count+1,Math.Max(s.Max,ms),s.Total+ms));
    public static IDisposable Measure(string name)=>new Scope(name);
    private sealed class Scope(string name):IDisposable
    {
        private readonly Stopwatch clock=Stopwatch.StartNew();private int done;
        public void Dispose(){if(Interlocked.Exchange(ref done,1)!=0)return;double ms=clock.Elapsed.TotalMilliseconds;Record(name,ms);}
    }
    public static IReadOnlyDictionary<string,object> Snapshot()=>Samples.ToDictionary(k=>k.Key,k=>(object)new{count=k.Value.Count,maxMs=k.Value.Max,averageMs=k.Value.Total/k.Value.Count});
}
