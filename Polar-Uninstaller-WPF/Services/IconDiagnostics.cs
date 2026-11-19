using System.Diagnostics;
namespace PolarUninstallerWpf.Services;

/// <summary>
/// One INFO summary per scan plus one line per failed card. Cards are counted by
/// instance, so duplicate rows with identical metadata still match the visible list.
/// Resolution stays independent of the visible log.
/// </summary>
public static class IconDiagnostics
{
    private static readonly object Gate=new();
    private static Dictionary<InstalledProgram,string> names=new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<InstalledProgram,(bool Success,string Reason,string Detail)> results=new(ReferenceEqualityComparer.Instance);
    private static readonly HashSet<InstalledProgram> reportedFailures=new(ReferenceEqualityComparer.Instance);
    private static readonly string[] Reasons={"legacy","worker_start","timeout","no_source","decode_error","unowned"};
    private static ActivityLogService? activity;private static Timer? timer;private static bool summarized;
    private static int generation;
    public static event Action<string>? Written;

    public static void Begin(IEnumerable<InstalledProgram> programs,ActivityLogService? log)
    {
        lock(Gate)
        {
            var map=new Dictionary<InstalledProgram,string>(ReferenceEqualityComparer.Instance);
            foreach(var program in programs)map[program]=program.Name;
            names=map;
            results.Clear();reportedFailures.Clear();activity=log;summarized=false;
            generation++;timer?.Dispose();timer=null;
        }
        ApplicationIconResolver.ResetFailures();
    }

    public static void Report(InstalledProgram p,bool success,string reason,string detail)
    {
        string? line=null;lock(Gate)
        {
            if(!names.TryGetValue(p,out string? name))return;
            results[p]=(success,Normalize(reason),detail);
            if(!success&&reportedFailures.Add(p))
                line=$"Icon failed: {name}; reason={Normalize(reason)}; {detail}";
            if(!summarized)
            {
                // Debounce so a scan writes a single summary once rows settle.
                timer?.Dispose();int stamp=++generation;
                timer=new Timer(_=>{lock(Gate){if(stamp!=generation||summarized)return;}Summary();},null,2500,Timeout.Infinite);
            }
        }
        if(line!=null)Write(line,"تعذر تحميل الأيقونة: "+line["Icon failed: ".Length..]);
    }

    public static void Summary()
    {
        string line,ar;lock(Gate)
        {
            if(summarized)return;summarized=true;timer?.Dispose();timer=null;
            int resolved=results.Values.Count(r=>r.Success),badges=results.Count-resolved,pending=names.Count-results.Count;
            var counts=results.Values.Where(r=>!r.Success).GroupBy(r=>r.Reason).ToDictionary(g=>g.Key,g=>g.Count());
            string failures=string.Join(", ",Reasons.Select(reason=>reason+" "+(counts.TryGetValue(reason,out int count)?count:0)));
            string worker;try{worker=IconWorkerPool.WorkerExecutable;}catch(Exception e){worker="unavailable: "+e.Message;}
            line=$"Icons: {resolved} resolved, {badges} badges. Failures: {failures}. Pending: {pending}. Worker: \"{worker}\" --polar-icon-worker.";
            ar=$"الأيقونات: المحمّلة {resolved}، الشارات {badges}. الإخفاقات: {failures}. المتبقية: {pending}. عامل الأيقونات: \"{worker}\" --polar-icon-worker.";
        }
        Write(line,ar);
    }

    private static string Normalize(string reason)
    {
        if(string.IsNullOrWhiteSpace(reason))return "no_source";
        if(reason.Contains("timeout",StringComparison.OrdinalIgnoreCase))return "timeout";
        if(reason.Contains("worker",StringComparison.OrdinalIgnoreCase)||reason.Contains("handshake",StringComparison.OrdinalIgnoreCase))return "worker_start";
        if(reason.Contains("decode",StringComparison.OrdinalIgnoreCase))return "decode_error";
        if(reason.Contains("unowned",StringComparison.OrdinalIgnoreCase)||reason.Contains("does not belong",StringComparison.OrdinalIgnoreCase))return "unowned";
        if(reason.Contains("legacy",StringComparison.OrdinalIgnoreCase))return "legacy";
        return "no_source";
    }

    private static void Write(string en,string ar)
    {
        activity?.WriteBilingual("INFO",en,ar);
        foreach(var handler in Written?.GetInvocationList()??[])try{((Action<string>)handler)(en);}catch(Exception e){Debug.WriteLine(e);}
    }
}
