using System.IO;

namespace PolarUninstallerWpf.Services;

/// <summary>The four post-uninstall states shown to the user and in the log.</summary>
public enum UninstallOutcome
{
    SuccessfullyRemoved,
    RemovedWithLeftovers,
    VerificationIncomplete,
    UninstallFailed
}

/// <summary>
/// Detailed bilingual post-uninstall report. It records exactly what was
/// verified, what was removed, what was skipped and what still remains.
/// </summary>
public sealed record UninstallReport(
    UninstallOutcome Outcome,
    string Name,
    string Publisher,
    string Source,
    string Location,
    bool RegistrationPresent,
    bool FolderPresent,
    bool FilesPresent,
    bool ExecutablePresent,
    int FilesRemoved,
    int FilesSkipped,
    int RegistryRemoved,
    int RegistrySkipped,
    int RegistryFailed,
    int FoldersRemoved,
    int ShortcutsRemoved,
    long ReclaimedBytes,
    IReadOnlyList<string> RemainingPaths,
    IReadOnlyList<string> Messages,
    string Detail)
{
    public string Title(bool arabic) => Outcome switch
    {
        UninstallOutcome.SuccessfullyRemoved => arabic ? "أُزيل بنجاح" : "Successfully Removed",
        UninstallOutcome.RemovedWithLeftovers => arabic ? "أُزيل مع بقايا" : "Removed with Leftovers",
        UninstallOutcome.VerificationIncomplete => arabic ? "التحقق غير مكتمل" : "Verification Incomplete",
        _ => arabic ? "فشلت الإزالة" : "Uninstall Failed"
    };

    public string Severity => Outcome switch
    {
        UninstallOutcome.SuccessfullyRemoved => "SUCCESS",
        UninstallOutcome.RemovedWithLeftovers => "WARNING",
        UninstallOutcome.VerificationIncomplete => "WARNING",
        _ => "ERROR"
    };

    public string Summary(bool arabic)
    {
        string size = DriveSummary.Format(ReclaimedBytes);
        return Outcome switch
        {
            UninstallOutcome.SuccessfullyRemoved => arabic
                ? $"أُزيل بنجاح · {DisplayFormat.Isolate(size)} مستردة"
                : $"Successfully Removed · {size} reclaimed",
            UninstallOutcome.RemovedWithLeftovers => arabic
                ? $"أُزيل مع بقايا · {DisplayFormat.Isolate(size)} مستردة · {DisplayFormat.Isolate(RemainingPaths.Count.ToString())} بقايا"
                : $"Removed with Leftovers · {size} reclaimed · {RemainingPaths.Count} leftover(s)",
            UninstallOutcome.VerificationIncomplete => arabic
                ? "التحقق غير مكتمل · راجع التفاصيل"
                : "Verification Incomplete · review the details",
            _ => arabic
                ? "فشلت الإزالة · ما زال التطبيق مثبتًا"
                : "Uninstall Failed · the application is still installed"
        };
    }

    public IReadOnlyList<string> LogLines(bool arabic)
    {
        var lines = new List<string>();
        void Add(string english, string arabicText) =>
            lines.Add(arabic ? "\u200F" + BidiText.IsolateLatin(arabicText) : english);

        Add($"=== Uninstall report: {Name} ===", $"=== تقرير الإزالة: {Name} ===");
        Add($"Result: {Title(false)}", $"النتيجة: {Title(true)}");
        Add($"Publisher: {Value(Publisher)} · Source: {Value(Source)}",
            $"الناشر: {Value(Publisher)} · المصدر: {Value(Source)}");
        Add($"Location: {Value(Location)}", $"الموقع: {Value(Location)}");
        Add($"State — registration: {State(RegistrationPresent)} · folder: {State(FolderPresent)} · files: {State(FilesPresent)} · executable: {State(ExecutablePresent)}",
            $"الحالة — التسجيل: {ArabicState(RegistrationPresent)} · المجلد: {ArabicState(FolderPresent)} · الملفات: {ArabicState(FilesPresent)} · الملف التنفيذي: {ArabicState(ExecutablePresent)}");
        Add($"Removed — files: {DisplayFormat.Number(FilesRemoved)} · folders: {DisplayFormat.Number(FoldersRemoved)} · shortcuts: {DisplayFormat.Number(ShortcutsRemoved)} · registry: {DisplayFormat.Number(RegistryRemoved)}",
            $"حُذف — الملفات: {DisplayFormat.Isolate(DisplayFormat.Number(FilesRemoved))} · المجلدات: {DisplayFormat.Isolate(DisplayFormat.Number(FoldersRemoved))} · الاختصارات: {DisplayFormat.Isolate(DisplayFormat.Number(ShortcutsRemoved))} · الريجستري: {DisplayFormat.Isolate(DisplayFormat.Number(RegistryRemoved))}");
        Add($"Skipped — files: {DisplayFormat.Number(FilesSkipped)} · registry: {DisplayFormat.Number(RegistrySkipped)} · failed registry: {DisplayFormat.Number(RegistryFailed)}",
            $"تُجاوز — الملفات: {DisplayFormat.Isolate(DisplayFormat.Number(FilesSkipped))} · الريجستري: {DisplayFormat.Isolate(DisplayFormat.Number(RegistrySkipped))} · فشل الريجستري: {DisplayFormat.Isolate(DisplayFormat.Number(RegistryFailed))}");
        Add($"Reclaimed: {DriveSummary.Format(ReclaimedBytes)}", $"المسترد: {DisplayFormat.Isolate(DriveSummary.Format(ReclaimedBytes))}");
        if (!string.IsNullOrWhiteSpace(Detail))
            Add($"Detail: {Detail}", $"التفاصيل: {ActivityText.Localize(Detail,true)}");
        if (RemainingPaths.Count > 0)
        {
            Add($"Remaining leftovers ({RemainingPaths.Count}):", $"البقايا المتبقية ({DisplayFormat.Isolate(RemainingPaths.Count.ToString())}):");
            foreach (string path in RemainingPaths.Take(25))
                Add("  · " + path, "  · " + DisplayFormat.Isolate(path));
            if (RemainingPaths.Count > 25)
                Add($"  … and {RemainingPaths.Count - 25} more", $"  … و{DisplayFormat.Isolate((RemainingPaths.Count - 25).ToString())} أخرى");
        }
        if (Messages.Count > 0)
        {
            Add("Notes:", "ملاحظات:");
            foreach (string message in Messages.Take(25))
                Add("  · " + message, "  · " + ActivityText.Localize(message,true));
        }
        return lines;
    }

    private static string Value(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
    private static string State(bool present) => present ? "present" : "absent";
    private static string ArabicState(bool present) => present ? "موجود" : "غير موجود";
}

public static class UninstallReportService
{
    /// <summary>
    /// Classifies a direct removal. "Uninstall Failed" is reserved for an
    /// executable or a registration+folder that still show an installed app;
    /// stale registrations, empty folders and retained data are leftovers.
    /// </summary>
    public static UninstallReport Evaluate(
        InstalledProgram program,
        UninstallVerification verification,
        LeftoverCleanupResult? cleanup,
        IReadOnlyList<string>? remaining,
        int removedFiles,
        int skippedFiles,
        long reclaimedBytes,
        string detail,
        bool executablePresent = false,
        string incompleteReason = "")
    {
        var remainingPaths = (remaining ?? cleanup?.RemainingPaths ?? Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        bool leftovers = remainingPaths.Length > 0 ||
                         (cleanup != null && cleanup.Failed > 0);

        UninstallOutcome outcome;
        if (!string.IsNullOrWhiteSpace(incompleteReason) || verification.Incomplete)
            outcome = UninstallOutcome.VerificationIncomplete;
        else if (executablePresent || (verification.Registered && verification.FolderRemaining)) outcome = UninstallOutcome.UninstallFailed;
        else if (verification.Registered || verification.FolderRemaining || verification.FilesRemaining || leftovers)
            outcome = UninstallOutcome.RemovedWithLeftovers;
        else
            outcome = UninstallOutcome.SuccessfullyRemoved;

        return Build(program, outcome, verification.Registered, verification.FolderRemaining,
            verification.FilesRemaining, executablePresent, removedFiles, skippedFiles,
            cleanup?.RegistryRemoved ?? 0, cleanup?.RegistrySkipped ?? 0, cleanup?.RegistryFailed ?? 0,
            cleanup?.FoldersRemoved ?? 0, cleanup?.ShortcutsRemoved ?? 0,
            reclaimedBytes, remainingPaths, cleanup?.Messages ?? Array.Empty<string>(), detail);
    }

    /// <summary>Classifies a monitored official removal from its final probe.</summary>
    public static UninstallReport FromProbe(
        InstalledProgram program,
        RemovalProbe probe,
        LeftoverCleanupResult? cleanup,
        string detail,
        bool cleanupUnverified = false)
    {
        var remainingPaths = (cleanup?.RemainingPaths ?? Array.Empty<string>())
            .Concat(probe.RetainedSteamAssets || probe.SteamPayloadTimedOut ? new[] { program.Location } : Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        bool registered = probe.LauncherRecordPresent;
        bool folder = probe.InstallationFolderPresent;
        bool files = folder;
        bool executable = probe.MainExecutablePresent;
        bool leftovers = remainingPaths.Length > 0 ||
                         (cleanup != null && cleanup.Failed > 0);

        UninstallOutcome outcome;
        // A confirmed official removal is only complete success when the
        // verified leftover pass also succeeded. Any remaining or failed
        // leftover keeps the report at "Removed with Leftovers", and a cleanup
        // that could not run at all is reported as unverified instead of success.
        if(probe.SteamPayloadTimedOut || probe.Detail.StartsWith("Probe error:",StringComparison.Ordinal)) outcome=UninstallOutcome.VerificationIncomplete;
        else if (executable) outcome = UninstallOutcome.UninstallFailed;
        else if (probe.Confirmed)
            outcome = leftovers ? UninstallOutcome.RemovedWithLeftovers
                : cleanupUnverified ? UninstallOutcome.VerificationIncomplete
                : UninstallOutcome.SuccessfullyRemoved;
        else if (registered && folder) outcome = UninstallOutcome.UninstallFailed;
        else outcome = UninstallOutcome.VerificationIncomplete;

        return Build(program, outcome, registered, folder, files, executable,
            cleanup?.FilesRemoved ?? 0, cleanup?.Skipped ?? 0,
            cleanup?.RegistryRemoved ?? 0, cleanup?.RegistrySkipped ?? 0, cleanup?.RegistryFailed ?? 0,
            cleanup?.FoldersRemoved ?? 0, cleanup?.ShortcutsRemoved ?? 0,
            cleanup?.ReclaimedBytes ?? 0, remainingPaths,
            cleanup?.Messages ?? Array.Empty<string>(), detail);
    }

    private static UninstallReport Build(
        InstalledProgram program,
        UninstallOutcome outcome,
        bool registration,
        bool folder,
        bool files,
        bool executable,
        int filesRemoved,
        int filesSkipped,
        int registryRemoved,
        int registrySkipped,
        int registryFailed,
        int foldersRemoved,
        int shortcutsRemoved,
        long reclaimedBytes,
        IReadOnlyList<string> remainingPaths,
        IReadOnlyList<string> messages,
        string detail) =>
        new(outcome, program.Name, program.Publisher, program.Source, program.Location,
            registration, folder, files, executable,
            filesRemoved, filesSkipped,
            registryRemoved, registrySkipped, registryFailed,
            foldersRemoved, shortcutsRemoved, reclaimedBytes,
            remainingPaths, messages, detail);
}
