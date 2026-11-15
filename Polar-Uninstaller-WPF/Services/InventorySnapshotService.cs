using System.IO;
using System.Text.Json;

namespace PolarUninstallerWpf.Services;

/// <summary>
/// A candidate removal that has not been confirmed yet. It is kept between
/// scans so a one-off partial scan cannot be mistaken for an uninstallation.
/// </summary>
public sealed record PendingRemoval(InstalledProgram Program, DateTimeOffset FirstSeen, int AbsenceCount);

/// <summary>
/// Persists a lightweight snapshot of the last successfully scanned installed
/// applications so externally removed items can be detected reliably.
/// </summary>
public static class InventorySnapshotService
{
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PolarUninstallerWPF-Development", "inventory-snapshot.json");

    public static string DefaultPendingPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PolarUninstallerWPF-Development", "inventory-pending-removals.json");

    public static IReadOnlyList<InstalledProgram> Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path)) return Array.Empty<InstalledProgram>();
            string json = File.ReadAllText(path);
            var entries = JsonSerializer.Deserialize<List<InstalledProgram>>(json);
            if (entries is null) return Array.Empty<InstalledProgram>();
            return entries.Where(p => !string.IsNullOrWhiteSpace(p.Name) && !PolarApplicationIdentity.IsPolarOwned(p)).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or System.Security.SecurityException)
        {
            return Array.Empty<InstalledProgram>();
        }
    }

    public static void Save(string? path, IEnumerable<InstalledProgram> programs)
    {
        path ??= DefaultPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(programs));
            File.Move(temporary, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or JsonException)
        {
        }
    }

    public static void Clear(string? path = null)
    {
        path ??= DefaultPath;
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
    }

    public static IReadOnlyList<PendingRemoval> LoadPending(string? path = null)
    {
        path ??= DefaultPendingPath;
        try
        {
            if (!File.Exists(path)) return Array.Empty<PendingRemoval>();
            var entries = JsonSerializer.Deserialize<List<PendingRemoval>>(File.ReadAllText(path));
            if (entries is null) return Array.Empty<PendingRemoval>();
            return entries.Where(entry => entry?.Program is not null && !string.IsNullOrWhiteSpace(entry.Program.Name)).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or System.Security.SecurityException)
        {
            return Array.Empty<PendingRemoval>();
        }
    }

    public static void SavePending(string? path, IEnumerable<PendingRemoval> pending)
    {
        path ??= DefaultPendingPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(pending));
            File.Move(temporary, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or JsonException)
        {
        }
    }

    public static void ClearPending(string? path = null)
    {
        path ??= DefaultPendingPath;
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
    }

    /// <summary>
    /// After the user clears uninstall history, capture the current inventory as
    /// the new baseline. The old snapshot can then never be replayed as a set of
    /// fresh external removals. A failed scan leaves both stores empty, which is
    /// also safe because no comparison is made against an empty baseline.
    /// </summary>
    public static async Task EstablishBaselineAsync(CancellationToken token = default)
    {
        try
        {
            var windows = await new WindowsInventoryService().GetInstalledAsync(token);
            var launchers = await new LauncherInventoryService().GetInstalledAsync(token);
            var merged = await Task.Run(() => InventoryMerger.Merge(windows.Concat(launchers), token), token);
            if (merged.Count == 0)
            {
                Clear();
                ClearPending();
                return;
            }
            Save(null, merged);
            ClearPending();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or OperationCanceledException)
        {
            Clear();
            ClearPending();
        }
    }
}
