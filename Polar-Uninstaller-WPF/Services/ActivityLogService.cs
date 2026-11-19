using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace PolarUninstallerWpf.Services;

public sealed record ActivityEntry(DateTime Timestamp, string Severity, string Message)
{
    public bool Arabic {get;init;}
    public string? ArabicMessage {get;init;}
    public string Display => $"[{DisplayFormat.Date(Timestamp, "HH:mm:ss")}] [{Severity}] {(Arabic?BidiText.IsolateLatin(ArabicMessage??ActivityText.Localize(Message,true)):Message)}";
}

public sealed class ActivityLogService(Dispatcher dispatcher)
{
    private readonly ConcurrentQueue<ActivityEntry> pending = new();
    private int scheduled;
    private bool arabic;
    public ObservableCollection<ActivityEntry> Entries { get; } = new();
    public event Action<ActivityEntry>? EntryAdded;
    public event Action<ActivityEntry>? BatchAdded;
    public void Clear(){pending.Clear();Entries.Clear();}

    public void SetLanguage(bool value)
    {
        if(arabic==value)return;
        arabic=value;
        for(int i=0;i<Entries.Count;i++)Entries[i]=Entries[i] with{Arabic=value};
    }

    public void Write(string severity, string message)
    {
        Queue(new(DateTime.Now,severity,message));
    }
    public void WriteBilingual(string severity,string english,string arabicMessage)=>Queue(new(DateTime.Now,severity,english){ArabicMessage=arabicMessage});
    private void Queue(ActivityEntry entry)
    {
        pending.Enqueue(entry);
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
            entry=entry with{Arabic=arabic};Entries.Add(entry);
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
