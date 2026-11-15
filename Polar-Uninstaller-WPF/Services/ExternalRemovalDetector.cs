using System.IO;
using System.Text.RegularExpressions;

namespace PolarUninstallerWpf.Services;

public enum ExternalRemovalDecision
{
    /// <summary>The item is still present, cannot be verified, or is a system update.</summary>
    None,
    /// <summary>The item is absent but needs a second successful scan before recording.</summary>
    Pending,
    /// <summary>The removal is verified and may be recorded in uninstall history.</summary>
    Confirmed
}

/// <summary>
/// Decides whether an application that disappeared from a scan was actually
/// uninstalled. The rules deliberately reject Windows updates/services and
/// temporary partial scans so the Dashboard never records false removals.
/// </summary>
public static class ExternalRemovalDetector
{
    private static readonly string[] UpdatePrefixes =
    [
        "Update for ",
        "Security Update for ",
        "Cumulative Update for ",
        "Servicing Stack Update for ",
        "Hotfix for "
    ];

    /// <summary>
    /// A scan is only used for removal detection when it returned a plausible
    /// number of applications. A sudden drop usually means a launcher or registry
    /// view failed temporarily, not that everything was uninstalled.
    /// </summary>
    public static bool IsTrustworthyScan(int previousCount, int currentCount)
    {
        if (currentCount <= 0) return false;
        if (previousCount <= 5) return true;
        return currentCount >= Math.Max(3, (int)Math.Ceiling(previousCount * 0.6));
    }

    /// <summary>Windows updates and service components are not user uninstalls.</summary>
    public static bool IsSystemUpdateOrService(InstalledProgram program)
    {
        string name = program.Name?.Trim() ?? "";
        if (name.Length == 0) return true;
        if (Regex.IsMatch(name, @"^KB\d{6,}\b", RegexOptions.IgnoreCase)) return true;
        if (UpdatePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) return true;
        if (name.Equals("Xbox Service", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Xbox Service ", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.Contains("Windows Update", StringComparison.OrdinalIgnoreCase)) return true;
        string publisher = program.Publisher?.Trim() ?? "";
        if (publisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) &&
            (name.Contains(" Update", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("Service", StringComparison.OrdinalIgnoreCase)))
            return true;
        if (program.RequiresOfficialRemoval &&
            (name.Contains("Update", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("Service", StringComparison.OrdinalIgnoreCase)))
            return true;
        return false;
    }

    /// <summary>
    /// Treats a renamed/moved installation with the same identity as still
    /// present. This avoids recording launcher upgrades as removals.
    /// </summary>
    public static bool StillPresent(InstalledProgram item, InstalledProgram current)
    {
        if (!string.IsNullOrWhiteSpace(item.Identifier) &&
            string.Equals(item.Identifier, current.Identifier, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.Equals(item.Name, current.Name, StringComparison.OrdinalIgnoreCase)) return false;
        string original = InventoryMerger.NormalizePath(item.Location);
        string updated = InventoryMerger.NormalizePath(current.Location);
        if (original.Length > 0 && updated.Length > 0)
        {
            if (string.Equals(original, updated, StringComparison.OrdinalIgnoreCase)) return true;
            // Same title and publisher at a new path is an update/move, not a removal.
            return !string.IsNullOrWhiteSpace(item.Publisher) &&
                   string.Equals(item.Publisher, current.Publisher, StringComparison.OrdinalIgnoreCase);
        }
        return true;
    }

    /// <summary>
    /// Evaluates one absent item. <paramref name="priorAbsenceCount"/> is the
    /// number of earlier successful scans that already confirmed its absence.
    /// </summary>
    public static ExternalRemovalDecision Evaluate(InstalledProgram item, IReadOnlyList<InstalledProgram> current, int priorAbsenceCount)
    {
        if (IsSystemUpdateOrService(item)) return ExternalRemovalDecision.None;
        if (current.Any(candidate => StillPresent(item, candidate))) return ExternalRemovalDecision.None;

        bool hasLocation = !string.IsNullOrWhiteSpace(item.Location) && Path.IsPathFullyQualified(item.Location);
        if (hasLocation)
        {
            try
            {
                string? volume = Path.GetPathRoot(item.Location);
                // A disconnected external drive must never look like a removal.
                if (volume is not null && !Directory.Exists(volume)) return ExternalRemovalDecision.None;
                if (!Directory.Exists(item.Location)) return ExternalRemovalDecision.Confirmed;
                if (!Directory.EnumerateFileSystemEntries(item.Location).Any()) return ExternalRemovalDecision.Confirmed;
                // Files remain: this is a partial uninstall, not a verified removal.
                return ExternalRemovalDecision.None;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // Cannot verify the folder safely; never claim success.
                return ExternalRemovalDecision.None;
            }
        }

        if (!string.IsNullOrWhiteSpace(item.Identifier))
        {
            var probe = UninstallMonitorService.Probe(item, null);
            if (probe.Confirmed) return ExternalRemovalDecision.Confirmed;
            if (probe.LauncherRecordPresent || probe.InstallationFolderPresent) return ExternalRemovalDecision.None;
        }

        // Registry-only entries need a second consecutive successful scan. This
        // filters out one-off launcher/registry read failures.
        return priorAbsenceCount >= 1 ? ExternalRemovalDecision.Confirmed : ExternalRemovalDecision.Pending;
    }

    public static string IdentityKey(InstalledProgram program)
    {
        if (!string.IsNullOrWhiteSpace(program.Identifier))
            return "id:" + program.Identifier.Trim().ToLowerInvariant();
        return "name:" + (program.Name ?? "").Trim().ToLowerInvariant() + "|" + (program.Publisher ?? "").Trim().ToLowerInvariant();
    }
}
