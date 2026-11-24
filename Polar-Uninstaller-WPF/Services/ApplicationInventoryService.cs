namespace PolarUninstallerWpf.Services;

/// <summary>
/// One authoritative installed-application inventory. Both the Home page and
/// the Deep Uninstaller read this snapshot, so their unfiltered counts can
/// never diverge: the same Windows registrations and launcher records are
/// merged, deduplicated and stripped of Polar-owned entries exactly once.
/// </summary>
public sealed record ApplicationInventory(
    IReadOnlyList<InstalledProgram> Programs,
    bool IsComplete,
    long Generation,
    DateTimeOffset CapturedAt)
{
    public int Count => Programs.Count;
}

public sealed class ApplicationInventoryService
{
    private readonly Func<CancellationToken, Task<IReadOnlyList<InstalledProgram>>> scan;
    private readonly object gate = new();
    private long generation;
    private int lastCompleteCount;
    private int lastIncompleteCount;
    private int incompleteStreak;
    private ApplicationInventory? current;
    private readonly List<(InstalledProgram Program,IReadOnlyList<string> Executables)> verifiedAbsences=new();

    public ApplicationInventoryService() { scan = ScanAsync; }

    /// <summary>
    /// Creates a service over a custom raw-record source. The merge,
    /// deduplication and Polar exclusion rules are always applied here so
    /// tests exercise the same path as the application.
    /// </summary>
    public ApplicationInventoryService(Func<CancellationToken, Task<IReadOnlyList<InstalledProgram>>> scan)
    {
        this.scan = scan ?? throw new ArgumentNullException(nameof(scan));
    }

    public ApplicationInventory? Current
    {
        get { lock (gate) return current; }
    }

    /// <summary>Raised only for the newest scan; stale results never publish.</summary>
    public event Action<ApplicationInventory>? Changed;
    public event Action<string>? Diagnostic;

    public Task<ApplicationInventory> RefreshAsync(CancellationToken token = default)
    {
        long request;
        lock (gate) request = ++generation;
        return ScanAndPublishAsync(request, token);
    }

    /// <summary>
    /// True when <paramref name="inventory"/> is still the published snapshot
    /// and no newer scan has been requested. Callers use this before applying
    /// an asynchronous result to their UI.
    /// </summary>
    public bool IsCurrent(ApplicationInventory inventory)
    {
        lock (gate) return ReferenceEquals(current, inventory) && inventory.Generation == generation;
    }

    /// <summary>
    /// Removes a verified uninstall immediately so the Home count updates
    /// without waiting for the next full scan. A fresh scan follows to confirm
    /// the authoritative state.
    /// </summary>
    public void NotifyRemoved(InstalledProgram program,IReadOnlyList<string>? capturedExecutables=null)
    {
        ApplicationInventory? updated = null;
        lock (gate)
        {
            if(capturedExecutables?.Count>0)
            {
                verifiedAbsences.RemoveAll(x=>SameProgram(x.Program,program));
                verifiedAbsences.Add((program,capturedExecutables.ToArray()));
            }
            if (current is null) return;
            var remaining = current.Programs.Where(item => !SameProgram(item, program)).ToArray();
            if (remaining.Length == current.Programs.Count) return;
            updated = current with { Programs = remaining, CapturedAt = DateTimeOffset.Now };
            current = updated;
            if (updated.IsComplete) lastCompleteCount = updated.Count;
        }
        if (updated is not null) Changed?.Invoke(updated);
    }

    private async Task<ApplicationInventory> ScanAndPublishAsync(long request, CancellationToken token)
    {
        var raw = await scan(token).ConfigureAwait(false);
        var merged = await Task.Run(() => InventoryMerger.Merge(raw, token), token).ConfigureAwait(false);
        (InstalledProgram Program,IReadOnlyList<string> Executables)[] absences;
        lock(gate)absences=verifiedAbsences.ToArray();
        var liveSteamIds = raw.Where(program => program.Source == "Steam").Select(program => program.Identifier).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visible=await Task.Run(()=>merged.Where(candidate=>!absences.Any(absence=>
            SameProgram(absence.Program,candidate)&&absence.Program.Version==candidate.Version&&absence.Program.Uninstall==candidate.Uninstall&&
            !UninstallVerificationService.AnyExecutablePresent(absence.Executables)&&
            !UninstallVerificationService.AnyExecutablePresent((candidate.IconCandidates??Array.Empty<string>()).Prepend(candidate.IconPath)
                .Select(InstallationPathResolver.Executable).Where(p=>p.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)&&!System.IO.Path.GetFileName(p).Contains("unins",StringComparison.OrdinalIgnoreCase)))))
            .Where(candidate => !candidate.Identifier.StartsWith("steam:", StringComparison.OrdinalIgnoreCase) ||
                liveSteamIds.Contains(candidate.Identifier) || !UninstallMonitorService.Probe(candidate, null).RetainedSteamAssets)
            .ToArray(),token).ConfigureAwait(false);
        bool verifiedEmpty=merged.Count>0&&visible.Length==0;
        merged=visible;

        ApplicationInventory result;
        bool publish = false;
        lock (gate)
        {
            bool plausible = merged.Count > 0 && ExternalRemovalDetector.IsTrustworthyScan(lastCompleteCount, merged.Count);
            // A genuine bulk uninstall can legitimately drop below the trust
            // threshold. Accept the new baseline only after two consecutive
            // scans agree, so a one-off failed read is still rejected.
            bool stableDrop = merged.Count > 0 && incompleteStreak >= 1 &&
                              Math.Abs(merged.Count - lastIncompleteCount) <= Math.Max(2, lastIncompleteCount / 10);
            bool complete = plausible || stableDrop || verifiedEmpty;

            result = new ApplicationInventory(merged, complete, request, DateTimeOffset.Now);
            if (request == generation)
            {
                current = result;
                if (complete)
                {
                    lastCompleteCount = merged.Count;
                    incompleteStreak = 0;
                }
                else
                {
                    incompleteStreak++;
                    lastIncompleteCount = merged.Count;
                }
                publish = true;
            }
        }

        if (publish) Changed?.Invoke(result);
        return result;
    }

    private static bool SameProgram(InstalledProgram a, InstalledProgram b)
    {
        if (!string.IsNullOrWhiteSpace(a.Identifier) && !string.IsNullOrWhiteSpace(b.Identifier))
            return string.Equals(a.Identifier, b.Identifier, StringComparison.OrdinalIgnoreCase)&&
                (string.IsNullOrWhiteSpace(a.Location)||string.IsNullOrWhiteSpace(b.Location)||LeftoverService.EqualPath(a.Location,b.Location));
        string pathA = InventoryMerger.NormalizePath(a.Location ?? "");
        string pathB = InventoryMerger.NormalizePath(b.Location ?? "");
        if (pathA.Length > 0 && pathB.Length > 0)
            return string.Equals(pathA, pathB, StringComparison.OrdinalIgnoreCase)&&string.Equals(a.Name,b.Name,StringComparison.OrdinalIgnoreCase)&&
                (string.IsNullOrWhiteSpace(a.Publisher)||string.IsNullOrWhiteSpace(b.Publisher)||string.Equals(a.Publisher,b.Publisher,StringComparison.OrdinalIgnoreCase));
        return string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(a.Publisher, b.Publisher, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(a.Version, b.Version, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<IReadOnlyList<InstalledProgram>> ScanAsync(CancellationToken token)
    {
        var windows = new WindowsInventoryService().GetInstalledAsync(token);
        var launchers = new LauncherInventoryService(message => Diagnostic?.Invoke(message)).GetInstalledAsync(token);
        await Task.WhenAll(windows, launchers).ConfigureAwait(false);
        return windows.Result.Concat(launchers.Result).ToArray();
    }
}
