using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace PolarUninstallerWpf.Services;

/// <summary>A persistent record of one verified successful removal.</summary>
public sealed record UninstallHistoryEntry
{
    public string OperationId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string Source { get; init; } = "";
    public string Identifier { get; init; } = "";
    public string Location { get; init; } = "";
    public long? SizeBytes { get; init; }
    public DateTimeOffset RemovedAt { get; init; }
    public string IconFile { get; init; } = "";
}

/// <summary>
/// Data captured before removal. The icon and size are collected while the
/// original files still exist; the entry is committed only after verification.
/// </summary>
public sealed record UninstallHistoryCapture(
    string OperationId,
    string Name,
    string Publisher,
    string Source,
    string Identifier,
    string Location,
    long? SizeBytes,
    string IconFile);

public static class UninstallHistoryFormat
{
    public static string Size(UninstallHistoryEntry entry, bool arabic) =>
        entry.SizeBytes is long bytes ? DisplayFormat.Size(bytes, arabic) : arabic ? "غير معروف" : "Unknown";

    public static string Size(long? bytes, bool arabic) =>
        bytes is long value ? DisplayFormat.Size(value, arabic) : arabic ? "غير معروف" : "Unknown";

    public static string Date(UninstallHistoryEntry entry, bool arabic)
    {
        string text = entry.RemovedAt.ToLocalTime().ToString("g", DisplayFormat.NumericCulture);
        return arabic ? DisplayFormat.Isolate(text) : text;
    }

    public static string Source(UninstallHistoryEntry entry, bool arabic)
    {
        if (string.IsNullOrWhiteSpace(entry.Source)) return arabic ? "غير معروف" : "Unknown";
        if (string.Equals(entry.Source, "External", StringComparison.OrdinalIgnoreCase))
            return arabic ? "إزالة خارجية" : "External removal";
        return entry.Source;
    }
}

/// <summary>
/// Persists verified uninstall history under the per-user application data
/// directory. Icons are cached next to the JSON file before removal because the
/// original application icon may disappear with the installation.
/// </summary>
public sealed class UninstallHistoryService
{
    public const int DisplayLimit = 5;
    public const int StorageLimit = 50;
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMinutes(2);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private readonly string root;
    private readonly string file;
    private readonly string iconDirectory;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly SpaceFreedService freedSpace;

    /// <summary>Raised after a verified entry is persisted.</summary>
    public static event Action? HistoryChanged;

    public UninstallHistoryService(string? isolatedDirectory = null)
    {
        root = string.IsNullOrWhiteSpace(isolatedDirectory) ? DefaultRoot() : isolatedDirectory;
        file = Path.Combine(root, "history.json");
        iconDirectory = Path.Combine(root, "icons");
        freedSpace=string.IsNullOrWhiteSpace(isolatedDirectory)?SpaceFreedService.Instance:new SpaceFreedService(Path.Combine(root,"totals"));
    }

    public string StoragePath => file;
    public string IconDirectory => iconDirectory;

    public static string DefaultRoot()
    {
        foreach (var folder in new[] { Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ApplicationData })
        {
            string path = Environment.GetFolderPath(folder);
            if (!string.IsNullOrWhiteSpace(path))
                return Path.Combine(path, "PolarUninstallerWPF-Development", "uninstall-history");
        }
        return Path.Combine(Path.GetTempPath(), "PolarUninstallerWPF-Development", "uninstall-history");
    }

    /// <summary>Direct/deep removal is recorded only when registration and the folder are gone.</summary>
    public static bool ShouldRecordDirect(UninstallVerification verification) =>
        !verification.Registered && !verification.FolderRemaining;

    /// <summary>Launcher monitoring records only an explicitly confirmed probe.</summary>
    public static bool ShouldRecordMonitored(RemovalProbe probe) => probe.Confirmed;

    public Task<IReadOnlyList<UninstallHistoryEntry>> LoadAsync(CancellationToken token = default) =>
        Task.Run<IReadOnlyList<UninstallHistoryEntry>>(() => LoadCore(), token);

    /// <summary>
    /// Captures the icon and pre-removal size. This never throws; missing values
    /// are represented as an empty icon file and a null size.
    /// </summary>
    public async Task<UninstallHistoryCapture> CaptureAsync(
        InstalledProgram program,
        string? operationId = null,
        bool includeIcon = true,
        CancellationToken token = default)
    {
        string id = string.IsNullOrWhiteSpace(operationId) ? Guid.NewGuid().ToString("N") : operationId;
        long? size = null;
        string iconFile = "";
        try
        {
            var sizeTask = TryMeasureSizeAsync(program, token);
            var iconTask = includeIcon ? SaveIconAsync(program, token) : Task.FromResult("");
            await Task.WhenAll(sizeTask, iconTask);
            size = sizeTask.Result;
            iconFile = iconTask.Result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or OperationCanceledException or NotSupportedException)
        {
            // Unknown metadata is valid; never block the removal workflow.
        }

        return new(
            id,
            program.Name?.Trim() ?? "",
            program.Publisher?.Trim() ?? "",
            SourceOf(program),
            program.Identifier ?? "",
            program.Location ?? "",
            size,
            iconFile);
    }

    /// <summary>
    /// Records an entry at most once per operation and suppresses an immediate
    /// duplicate of the same identity (for example a retried completion signal).
    /// </summary>
    public async Task<bool> RecordAsync(UninstallHistoryCapture capture, DateTimeOffset? removedAt = null)
    {
        if (capture is null || string.IsNullOrWhiteSpace(capture.Name)) return false;
        await gate.WaitAsync();
        try
        {
            var entries = LoadCore().ToList();
            var at = removedAt ?? DateTimeOffset.Now;
            if (!string.IsNullOrWhiteSpace(capture.OperationId) &&
                entries.Any(entry => string.Equals(entry.OperationId, capture.OperationId, StringComparison.Ordinal)))
                return false;

            if (entries.Any(entry => SameIdentity(entry, capture) && Math.Abs((entry.RemovedAt - at).TotalSeconds) <= DuplicateWindow.TotalSeconds))
                return false;

            entries.Add(new UninstallHistoryEntry
            {
                OperationId = capture.OperationId,
                Name = capture.Name,
                Publisher = capture.Publisher,
                Source = capture.Source,
                Identifier = capture.Identifier,
                Location = capture.Location,
                SizeBytes = capture.SizeBytes,
                RemovedAt = at,
                IconFile = capture.IconFile
            });

            entries = entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                .OrderByDescending(entry => entry.RemovedAt)
                .Take(StorageLimit)
                .ToList();

            SaveCore(entries);
            if(capture.SizeBytes is >0&&!string.IsNullOrWhiteSpace(capture.OperationId))
                await freedSpace.AddAsync(capture.SizeBytes.Value,"uninstall:"+capture.OperationId);
            HistoryChanged?.Invoke();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or JsonException or NotSupportedException)
        {
            return false;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Permanently removes the persisted uninstall history and its cached icons.
    /// The next verified removal starts a new history from scratch.
    /// </summary>
    public async Task ClearAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (File.Exists(file)) File.Delete(file);
            if (Directory.Exists(iconDirectory))
                foreach (string icon in Directory.EnumerateFiles(iconDirectory))
                {
                    try { File.Delete(icon); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
                }
            if (Directory.Exists(root))
                foreach (string temporary in Directory.EnumerateFiles(root, "history.json.*.tmp"))
                {
                    try { File.Delete(temporary); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
                }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        finally
        {
            gate.Release();
            HistoryChanged?.Invoke();
        }
    }

    /// <summary>Returns the cached icon path for a history entry, or null when it is unavailable.</summary>
    public string? ResolveIconPath(UninstallHistoryEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.IconFile)) return null;
        try
        {
            string name = Path.GetFileName(entry.IconFile);
            if (name.Length == 0) return null;
            string path = Path.Combine(iconDirectory, name);
            return File.Exists(path) ? path : null;
        }
        catch (ArgumentException) { return null; }
    }

    /// <summary>True when a history entry refers to the same installed product.</summary>
    public static bool Matches(UninstallHistoryEntry entry, InstalledProgram program)
    {
        if (!string.IsNullOrWhiteSpace(entry.Identifier) && !string.IsNullOrWhiteSpace(program.Identifier) &&
            string.Equals(entry.Identifier, program.Identifier, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.Equals(entry.Name, program.Name, StringComparison.OrdinalIgnoreCase)) return false;
        if (string.IsNullOrWhiteSpace(entry.Location) || string.IsNullOrWhiteSpace(program.Location)) return true;
        try { return LeftoverService.EqualPath(entry.Location, program.Location); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return true; }
    }

    public static string SourceOf(InstalledProgram program)
    {
        if (!string.IsNullOrWhiteSpace(program.Source)) return program.Source.Trim();
        string? source = program.Sources?.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return string.IsNullOrWhiteSpace(source) ? "Windows" : source.Trim();
    }

    /// <summary>
    /// Prefers launcher/registry metadata and falls back to a bounded folder
    /// measurement. Returns null (shown as Unknown) when no reliable value exists.
    /// </summary>
    public static async Task<long?> TryMeasureSizeAsync(InstalledProgram program, CancellationToken token = default)
    {
        if (program.SizeKb is long kilobytes && kilobytes > 0)
            return kilobytes * 1024L;
        if (string.IsNullOrWhiteSpace(program.Location)) return null;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            return await new BrowserOperationsService().TrySizeAsync(program.Location, timeout.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException) { return null; }
    }

    private IReadOnlyList<UninstallHistoryEntry> LoadCore()
    {
        try
        {
            if (!File.Exists(file)) return Array.Empty<UninstallHistoryEntry>();
            string json = File.ReadAllText(file);
            var entries = JsonSerializer.Deserialize<List<UninstallHistoryEntry>>(json, JsonOptions);
            if (entries is null) return Array.Empty<UninstallHistoryEntry>();
            return entries
                .Where(entry => entry is not null && !string.IsNullOrWhiteSpace(entry.Name))
                .OrderByDescending(entry => entry.RemovedAt)
                .Take(StorageLimit)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or System.Security.SecurityException)
        {
            // Corrupted or older history must not prevent the application from starting.
            return Array.Empty<UninstallHistoryEntry>();
        }
    }

    private void SaveCore(IReadOnlyList<UninstallHistoryEntry> entries)
    {
        Directory.CreateDirectory(root);
        string temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(entries, JsonOptions), Encoding.UTF8);
        File.Move(temporary, file, true);
    }

    private static bool SameIdentity(UninstallHistoryEntry entry, UninstallHistoryCapture capture)
    {
        if (!string.IsNullOrWhiteSpace(capture.Identifier) &&
            string.Equals(entry.Identifier, capture.Identifier, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.IsNullOrWhiteSpace(capture.Location) &&
            string.Equals(entry.Location, capture.Location, StringComparison.OrdinalIgnoreCase))
            return true;
        return string.Equals(entry.Name, capture.Name, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> SaveIconAsync(InstalledProgram program, CancellationToken token)
    {
        try
        {
            var icon = await ApplicationIconResolver.ResolveAsync(program);
            if (icon?.Image is not BitmapSource source) return "";
            string name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                string.Join("|", program.Name, program.Location, program.Identifier, program.IconPath)))).ToLowerInvariant()[..24] + ".png";
            string path = Path.Combine(iconDirectory, name);
            await Task.Run(() =>
            {
                Directory.CreateDirectory(iconDirectory);
                string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                using (var stream = File.Create(temporary))
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(source));
                    encoder.Save(stream);
                }
                File.Move(temporary, path, true);
            }, token);
            return name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException or OperationCanceledException or InvalidOperationException)
        {
            return "";
        }
    }
}
