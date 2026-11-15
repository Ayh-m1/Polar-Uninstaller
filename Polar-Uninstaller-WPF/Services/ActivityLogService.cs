using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace PolarUninstallerWpf.Services;

public sealed record ActivityEntry(DateTime Timestamp, string Severity, string Message)
{
    public string Display => $"[{DisplayFormat.Date(Timestamp, "HH:mm:ss")}] [{Severity}] {Message}";
}

public sealed class ActivityLogService(Dispatcher dispatcher)
{
    private readonly ConcurrentQueue<ActivityEntry> pending = new();
    private int scheduled;
    public ObservableCollection<ActivityEntry> Entries { get; } = new();
    public event Action<ActivityEntry>? EntryAdded;
    public event Action<ActivityEntry>? BatchAdded;

    public void Write(string severity, string message)
    {
        pending.Enqueue(new(DateTime.Now, severity, message));
        if (Interlocked.Exchange(ref scheduled, 1) == 0 && !dispatcher.HasShutdownStarted)
        {
            // Never modify a bound collection inside the originating click or
            // selection handler; let WPF finish its current layout transaction.
            dispatcher.BeginInvoke(Drain, DispatcherPriority.Background);
        }
    }

    private void Drain()
    {
        int count = 0;
        ActivityEntry? last = null;
        while (count++ < 100 && pending.TryDequeue(out var entry))
        {
            Entries.Add(entry);
            while (Entries.Count > 1000) Entries.RemoveAt(0);
            EntryAdded?.Invoke(entry);
            last = entry;
        }
        if (last is not null) BatchAdded?.Invoke(last);
        Interlocked.Exchange(ref scheduled, 0);
        if (!pending.IsEmpty && Interlocked.Exchange(ref scheduled, 1) == 0 && !dispatcher.HasShutdownStarted)
            dispatcher.BeginInvoke(Drain, DispatcherPriority.Background);
    }
}
