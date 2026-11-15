using System.IO;
using System.Text.Json;

namespace PolarUninstallerWpf.Services;
public sealed record ActivityDay(string Date, long Opens, long Clicks);
public sealed record ActivityMetrics(long Opens, long Clicks, IReadOnlyList<ActivityDay> Days);
public sealed class ActivityMetricsService
{
    public static ActivityMetricsService Instance { get; } = new();
    private readonly object gate = new();
    private readonly object persistenceGate = new();
    private readonly string file;
    private ActivityMetrics metrics = new(0, 0, []);
    private bool saving, dirty;
    public string? PersistenceError { get; private set; }
    public event Action? Changed;
    public ActivityMetricsService(string? directory = null)
    {
        file = Path.Combine(directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PolarUninstallerWPF-Development"), "activity-metrics.json");
        try { if(File.Exists(file)) metrics = JsonSerializer.Deserialize<ActivityMetrics>(File.ReadAllText(file)) ?? metrics; }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException) { PersistenceError=e.GetType().Name; }
    }
    public ActivityMetrics Snapshot() { lock(gate) return metrics; }
    public void Record(bool launch)
    {
        lock(gate)
        {
            string today=DateTime.Now.ToString("yyyy-MM-dd"); var days=metrics.Days.ToList(); var previous=days.FirstOrDefault(d=>d.Date==today) ?? new ActivityDay(today,0,0);
            days.RemoveAll(d=>d.Date==today); days.Add(previous with { Opens=previous.Opens+(launch?1:0), Clicks=previous.Clicks+(launch?0:1) });
            metrics=new(metrics.Opens+(launch?1:0),metrics.Clicks+(launch?0:1),days.OrderBy(d=>d.Date).TakeLast(90).ToArray());
            dirty=true; if(!saving) { saving=true; _=Task.Run(Save); }
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// Resets the persisted launch/click counters to zero. The write happens
    /// under the same lock used by the background saver, so a queued save can
    /// never put the old values back after a reset.
    /// </summary>
    public Task ResetAsync() => Task.Run(() =>
    {
        lock(persistenceGate)
        {
            ActivityMetrics snapshot;
            lock(gate) { metrics = new(0, 0, []);dirty = false;snapshot=metrics; }
            try { Persist(snapshot); PersistenceError = null; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { PersistenceError = e.GetType().Name; }
        }
        Changed?.Invoke();
    });

    private void Save()
    {
        // UI click accounting only holds gate for memory updates. Disk I/O
        // uses a separate lock so a slow disk cannot block every mouse click.
        while(true)
        {
            lock(persistenceGate)
            {
                ActivityMetrics snapshot;
                lock(gate)
                {
                    if(!dirty){saving=false;return;}
                    snapshot=metrics;dirty=false;
                }
                try { Persist(snapshot); PersistenceError = null; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { PersistenceError = e.GetType().Name; }
            }
            lock(gate) { if(!dirty){saving=false;return;} }
        }
    }

    private void Persist(ActivityMetrics snapshot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string temp = file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(snapshot));
        File.Move(temp, file, true);
    }
}
