using System.IO;

namespace PolarUninstallerWpf.Services;

public enum DashboardScanPhase { None, Running, Completed, Cancelled, Failed }

/// <summary>Dispatcher-owned scan state shared by successive Dashboard views.</summary>
public sealed class DashboardScanSession(ActivityLogService log)
{
    private CancellationTokenSource? running;
    private int generation;
    public DashboardScanPhase Phase { get; private set; }
    public string Root { get; private set; } = "";
    public bool IsDrive { get; private set; }
    public int Files { get; private set; }
    public StorageSnapshot? Snapshot { get; private set; }
    public event Action? Changed;

    public async Task RunAsync(DashboardStorageService storage, string root, bool drive)
    {
        if (running != null) return;
        using var cancellation = new CancellationTokenSource();
        running = cancellation;
        int request = ++generation;
        Root = root;
        IsDrive = drive;
        Files = 0;
        Snapshot = null;
        Phase = DashboardScanPhase.Running;
        log.WriteBilingual("INFO", $"Dashboard storage scan started: {(drive ? "drive" : "folder")} {root}", $"بدأ فحص التخزين: {DisplayFormat.Isolate(root)}");
        try
        {
            Changed?.Invoke();
            var progress = new Progress<int>(count =>
            {
                if (running != cancellation || request != generation || cancellation.IsCancellationRequested) return;
                Files = count;
                Changed?.Invoke();
            });
            var partial = new Progress<StorageSnapshot>(snapshot =>
            {
                if (running != cancellation || request != generation || cancellation.IsCancellationRequested) return;
                Snapshot = snapshot;
                Changed?.Invoke();
            });

            var result = await storage.ScanAsync(root, cancellation.Token, progress, partial);
            cancellation.Token.ThrowIfCancellationRequested();
            if (request != generation) return;
            Snapshot = result;
            Phase = DashboardScanPhase.Completed;
            log.WriteBilingual(result.Limited ? "WARNING" : "INFO",
                result.Limited ? "Dashboard storage scan completed. Some locations were inaccessible." : "Dashboard storage scan completed.",
                result.Limited ? "اكتمل فحص التخزين. تعذر الوصول إلى بعض المواقع." : "اكتمل فحص التخزين.");
        }
        catch (OperationCanceledException)
        {
            if (request == generation) Phase = DashboardScanPhase.Cancelled;
            log.WriteBilingual("INFO", "Dashboard storage scan cancelled.", "أُلغي فحص التخزين.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            if (request == generation) Phase = DashboardScanPhase.Failed;
            log.WriteBilingual("WARNING", "Dashboard storage scan failed.", "تعذر إكمال فحص التخزين.");
            CrashDiagnostics.Record("Dashboard storage scan", error);
        }
        finally
        {
            if (running == cancellation) running = null;
            if (request == generation) Changed?.Invoke();
        }
    }

    public void Cancel() => running?.Cancel();

    public void Reset()
    {
        Cancel();
        generation++;
        Phase = DashboardScanPhase.None;
        Snapshot = null;
    }
}
