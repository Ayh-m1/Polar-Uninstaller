using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace PolarUninstallerWpf.Services;

public sealed record UpdateLaunchResult(bool Success, string? Error = null);

/// <summary>
/// Owns the external replacement helper and the installed/portable metadata
/// rules. The helper is the new EXE itself, started with --apply-update. It
/// waits for the running instance to exit, validates the staged file against
/// the published GitHub asset, replaces the target through a same-volume
/// temporary file, keeps a backup, and rolls back if the new binary cannot be
/// verified or started.
/// </summary>
public static class UpdateInstallerService
{
    public const string ApplyUpdateArgument = "--apply-update";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\PolarUninstallerWpfRust";
    private const int WaitForParentMilliseconds = 30000;
    private const int ReadyTimeoutMilliseconds = 60000;

    public static bool IsApplyUpdateMode(string[] args) =>
        args.Any(arg => string.Equals(arg, ApplyUpdateArgument, StringComparison.OrdinalIgnoreCase));

    public static int RunApplyUpdate(string[] args)
    {
        var options = ParseArguments(args);
        string? statusPath = Value(options, "--status");
        void Status(string text)
        {
            if (string.IsNullOrWhiteSpace(statusPath)) return;
            try
            {
                string temporary = statusPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temporary, text);
                File.Move(temporary, statusPath, true);
            }
            catch { }
        }

        try
        {
            if (!TryGetRequired(options, "--target", out string targetRaw) ||
                !TryGetRequired(options, "--source", out string sourceRaw) ||
                !TryGetRequired(options, "--pid", out string pidRaw) ||
                !TryGetRequired(options, "--version", out string versionRaw) ||
                !TryGetRequired(options, "--expected-sha256", out string expectedHash))
            {
                Status("error:invalid_arguments");
                return 1;
            }

            string target = Path.GetFullPath(targetRaw);
            string source = Path.GetFullPath(sourceRaw);
            if (!IsPolarExecutableName(target) || !IsPolarExecutableName(source)) { Status("error:invalid_name"); return 1; }
            if (PathEquals(target, source)) { Status("error:same_path"); return 1; }
            if (!File.Exists(target) || !File.Exists(source)) { Status("error:missing_file"); return 1; }
            if (!int.TryParse(pidRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parentPid) || parentPid <= 0) { Status("error:invalid_pid"); return 1; }
            if (!Version.TryParse(versionRaw, out _)) { Status("error:invalid_version"); return 1; }
            if (expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit)) { Status("error:invalid_checksum"); return 1; }
            bool installed = string.Equals(Value(options, "--installed"), "1", StringComparison.Ordinal);
            bool noRestart = options.ContainsKey("--no-restart");

            // The target must be the image of the process that asked for the
            // update. This prevents the elevated helper from being used as a
            // general-purpose file replacement tool. A short retry covers the
            // moment right after the parent process starts.
            string? parentPath = null;
            for (int attempt = 0; attempt < 10 && parentPath == null; attempt++)
            {
                parentPath = TryGetProcessImagePath(parentPid);
                if (parentPath == null) Thread.Sleep(200);
            }
            if (parentPath == null || !PathEquals(parentPath, target)) { Status("error:target_mismatch"); return 1; }

            var sourceInfo = new FileInfo(source);
            var local = UpdateVerificationService.VerifyStandaloneExecutable(source, versionRaw, expectedHash, sourceInfo.Length);
            if (!local.IsValid) { Status("error:" + local.Error); return 1; }
            if (local.Signature == AuthenticodeStatus.Unsigned)
                WpfUpdateService.Log("Staged executable is unsigned; the published GitHub digest is the remaining authenticity check.");

            // Re-check the published asset independently of the command line.
            // This makes the helper useless for staging arbitrary binaries even
            // though the application runs elevated.
            WpfAsset? published;
            try { published = new WpfUpdateService().GetPublishedAsset(versionRaw, WpfUpdateService.StandaloneAssetName); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or JsonException or InvalidOperationException)
            {
                WpfUpdateService.Log("Published asset check failed: " + ex.Message);
                Status("error:published_check_failed");
                return 1;
            }
            if (published == null) { Status("error:not_published"); return 1; }
            if (published.Size != sourceInfo.Length || !DigestEquals(published.Digest, expectedHash))
            {
                Status("error:published_mismatch");
                return 1;
            }

            string? targetVersion = FileVersionInfo.GetVersionInfo(target).FileVersion;
            if (CompareVersion(targetVersion, versionRaw) > 0) { Status("error:downgrade"); return 1; }

            string targetDirectory = Path.GetDirectoryName(target)!;
            long required = sourceInfo.Length + new FileInfo(target).Length + 16L * 1024 * 1024;
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(targetDirectory)!);
                if (drive.AvailableFreeSpace > 0 && drive.AvailableFreeSpace < required) { Status("error:insufficient_space"); return 1; }
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { }

            Status("ready");
            WpfUpdateService.Log($"Update helper ready for {target} ({versionRaw}); waiting for PID {parentPid}.");
            if (!WaitForProcessExit(parentPid, WaitForParentMilliseconds))
            {
                WpfUpdateService.Log("Parent process did not exit; the update was not applied.");
                return 1;
            }
            Thread.Sleep(750);

            string backup = target + ".bak";
            string staged = target + ".new";
            string marker = target + ".update.json";
            bool replaced = false;
            bool backupCreated = false;

            void Rollback()
            {
                if (RestoreBackup(target, backup)) TryDelete(backup);
                TryDelete(marker);
            }

            try
            {
                File.Copy(target, backup, true);
                backupCreated = true;
                if (File.Exists(staged)) File.Delete(staged);
                File.Copy(source, staged, true);
                if (!UpdateVerificationService.HashEquals(staged, expectedHash))
                {
                    TryDelete(staged);
                    if (backupCreated) TryDelete(backup);
                    Status("error:staged_checksum");
                    return 1;
                }

                for (int attempt = 0; attempt < 20 && !replaced; attempt++)
                {
                    try { File.Move(staged, target, true); replaced = true; }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        if (attempt == 19) throw;
                        Thread.Sleep(500);
                    }
                }

                if (!UpdateVerificationService.HashEquals(target, expectedHash))
                {
                    Rollback();
                    TryDelete(staged);
                    Status("error:replacement_checksum");
                    return 1;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                TryDelete(staged);
                if (replaced) Rollback();
                else if (backupCreated) TryDelete(backup);
                WpfUpdateService.Log("Replacement failed: " + ex.Message);
                Status("error:" + (ex is UnauthorizedAccessException ? "access_denied" : "replacement_failed"));
                return 1;
            }

            if (installed) SetInstalledVersion(targetDirectory, versionRaw);
            WriteMarker(target, backup, versionRaw);

            if (noRestart)
            {
                WpfUpdateService.Log("Update applied without restart (test mode).");
                return 0;
            }

            try
            {
                using var launched = Process.Start(new ProcessStartInfo(target)
                {
                    UseShellExecute = true,
                    WorkingDirectory = targetDirectory
                });
                Thread.Sleep(3000);
                if (launched == null || launched.HasExited && launched.ExitCode != 0)
                {
                    WpfUpdateService.Log("The updated executable did not stay running; rolling back.");
                    Rollback();
                    TryStart(target, targetDirectory);
                    return 1;
                }
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
            {
                WpfUpdateService.Log("Could not start the updated executable: " + ex.Message);
                Rollback();
                TryStart(target, targetDirectory);
                return 1;
            }

            WpfUpdateService.Log("Update applied and restarted successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            WpfUpdateService.Log("Apply update failed: " + ex.GetType().Name + ": " + ex.Message);
            Status("error:" + ErrorCode(ex));
            return 1;
        }
    }

    /// <summary>
    /// Launches the staged EXE as the external helper. The parent waits until the
    /// helper has validated the staged file and written "ready"; only then does
    /// the caller exit, so the helper can safely wait for the old process to end.
    /// </summary>
    public static async Task<UpdateLaunchResult> LaunchHelperAsync(string sourcePath, string releaseVersion, string expectedSha256, CancellationToken token)
    {
        string? targetRaw = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(targetRaw)) return new(false, "missing_target");
        string target = Path.GetFullPath(targetRaw);
        if (!IsPolarExecutableName(target)) return new(false, "invalid_target");
        string source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source)) return new(false, "missing_source");
        string? sourceDirectory = Path.GetDirectoryName(source);
        if (sourceDirectory == null) return new(false, "invalid_source");
        bool installed = IsInstalledCopy(out _);
        string status = Path.Combine(sourceDirectory, "apply-status-" + Guid.NewGuid().ToString("N") + ".txt");

        var arguments = new List<string>
        {
            ApplyUpdateArgument,
            "--target", target,
            "--source", source,
            "--pid", Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
            "--version", releaseVersion,
            "--expected-sha256", expectedSha256,
            "--installed", installed ? "1" : "0",
            "--status", status
        };

        Process? helper = null;
        try
        {
            var psi = new ProcessStartInfo(source)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = sourceDirectory
            };
            foreach (var argument in arguments) psi.ArgumentList.Add(argument);
            helper = Process.Start(psi);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 740)
        {
            try
            {
                var psi = new ProcessStartInfo(source)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = sourceDirectory,
                    Arguments = BuildQuotedArguments(arguments)
                };
                helper = Process.Start(psi);
            }
            catch (Win32Exception elevation) when (elevation.NativeErrorCode == 1223) { return new(false, "elevation_denied"); }
            catch (Exception elevation) { return new(false, "helper_start_failed:" + elevation.Message); }
        }
        catch (Exception ex) { return new(false, "helper_start_failed:" + ex.Message); }

        if (helper == null) return new(false, "helper_start_failed");
        try
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(ReadyTimeoutMilliseconds);
            while (DateTime.UtcNow < deadline)
            {
                token.ThrowIfCancellationRequested();
                if (File.Exists(status))
                {
                    string text = "";
                    try { text = File.ReadAllText(status).Trim(); } catch (IOException) { }
                    if (string.Equals(text, "ready", StringComparison.Ordinal)) { WpfUpdateService.Log("Update helper is ready."); return new(true); }
                    if (text.StartsWith("error:", StringComparison.OrdinalIgnoreCase)) { WpfUpdateService.Log("Update helper rejected the update: " + text); return new(false, text[6..]); }
                }
                if (helper.HasExited)
                {
                    WpfUpdateService.Log("Update helper exited before it was ready (code " + helper.ExitCode + ").");
                    return new(false, "helper_exit_" + helper.ExitCode);
                }
                await Task.Delay(200, token);
            }
            WpfUpdateService.Log("Timed out waiting for the update helper.");
            try { if (!helper.HasExited) helper.Kill(); } catch { }
            return new(false, "helper_timeout");
        }
        catch (OperationCanceledException)
        {
            try { if (!helper.HasExited) helper.Kill(); } catch { }
            throw;
        }
        finally { helper.Dispose(); }
    }

    public static bool IsInstalledCopy(out string? installLocation)
    {
        installLocation = FindInstallLocation(Environment.ProcessPath);
        return installLocation != null;
    }

    /// <summary>
    /// Keeps the Windows uninstall entry in sync when the application was started
    /// after a one-time manual migration or after an update whose registry write
    /// was skipped.
    /// </summary>
    public static void SynchronizeInstalledMetadata()
    {
        string? target = Environment.ProcessPath;
        string? location = FindInstallLocation(target);
        if (location == null) return;
        SetInstalledVersion(location, AppInfo.Version);
    }

    /// <summary>Deletes the previous executable after the updated app starts.</summary>
    public static void CleanupAfterSuccessfulStart()
    {
        string? targetRaw = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(targetRaw)) return;
        string target = Path.GetFullPath(targetRaw);
        string marker = target + ".update.json";
        if (!File.Exists(marker)) return;
        try
        {
            var data = JsonSerializer.Deserialize<UpdateMarker>(File.ReadAllText(marker));
            if (data == null || data.Version != AppInfo.Version) return;
            string expectedBackup = target + ".bak";
            if (!PathEquals(data.Backup, expectedBackup)) return;
            if (File.Exists(expectedBackup)) File.Delete(expectedBackup);
            File.Delete(marker);
            WpfUpdateService.Log("Removed the previous executable after a successful startup.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { WpfUpdateService.Log("Backup cleanup skipped: " + ex.Message); }
    }

    public static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    internal static Dictionary<string, string> ParseArguments(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal)) continue;
            if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
                result[argument] = args[++index];
            else
                result[argument] = "";
        }
        return result;
    }

    /// <summary>Test seam for the replacement and rollback primitives.</summary>
    internal static bool ReplaceWithBackup(string source, string target, string expectedSha256)
    {
        string backup = target + ".bak";
        string staged = target + ".new";
        try
        {
            File.Copy(target, backup, true);
            if (File.Exists(staged)) File.Delete(staged);
            File.Copy(source, staged, true);
            if (!UpdateVerificationService.HashEquals(staged, expectedSha256)) { TryDelete(staged); return false; }
            File.Move(staged, target, true);
            if (UpdateVerificationService.HashEquals(target, expectedSha256)) return true;
            RestoreBackup(target, backup);
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(staged);
            WpfUpdateService.Log("ReplaceWithBackup failed: " + ex.Message);
            return false;
        }
    }

    internal static bool RestoreBackup(string target, string backup)
    {
        if (!File.Exists(backup)) return false;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                File.Copy(backup, target, true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == 19) { WpfUpdateService.Log("RestoreBackup failed: " + ex.Message); return false; }
                Thread.Sleep(500);
            }
        }
        return false;
    }

    private static void WriteMarker(string target, string backup, string version)
    {
        try { File.WriteAllText(target + ".update.json", JsonSerializer.Serialize(new UpdateMarker { Version = version, Backup = backup })); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { WpfUpdateService.Log("Could not write the update marker: " + ex.Message); }
    }

    private static string? FindInstallLocation(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;
        string targetDirectory;
        try { targetDirectory = Path.GetDirectoryName(Path.GetFullPath(target))!; }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
        foreach (var (hive, view) in RegistryLocations())
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(UninstallKey);
                string? location = key?.GetValue("InstallLocation") as string;
                if (string.IsNullOrWhiteSpace(location)) continue;
                string normalized = Path.GetFullPath(Environment.ExpandEnvironmentVariables(location.Trim().Trim('"')));
                if (PathEquals(normalized, targetDirectory)) return normalized;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException) { }
        }
        return null;
    }

    private static void SetInstalledVersion(string installLocation, string version)
    {
        foreach (var (hive, view) in RegistryLocations())
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(UninstallKey, writable: true);
                if (key == null) continue;
                string? location = key.GetValue("InstallLocation") as string;
                if (string.IsNullOrWhiteSpace(location)) continue;
                string normalized = Path.GetFullPath(Environment.ExpandEnvironmentVariables(location.Trim().Trim('"')));
                if (!PathEquals(normalized, installLocation)) continue;
                key.SetValue("DisplayVersion", version, RegistryValueKind.String);

                WpfUpdateService.Log($"Installed DisplayVersion updated to {version}.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException) { }
        }
    }

    private static IEnumerable<(RegistryHive Hive, RegistryView View)> RegistryLocations()
    {
        yield return (RegistryHive.LocalMachine, RegistryView.Registry64);
        yield return (RegistryHive.LocalMachine, RegistryView.Registry32);
        yield return (RegistryHive.CurrentUser, RegistryView.Registry64);
        yield return (RegistryHive.CurrentUser, RegistryView.Registry32);
    }

    private static string? TryGetProcessImagePath(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException) { return null; }
    }

    private static bool WaitForProcessExit(int pid, int timeout)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.WaitForExit(timeout);
        }
        catch (ArgumentException) { return true; }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { return false; }
    }

    private static void TryStart(string target, string directory)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true, WorkingDirectory = directory }); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException) { WpfUpdateService.Log("Could not restart the previous executable: " + ex.Message); }
    }

    private static bool IsPolarExecutableName(string path) =>
        string.Equals(Path.GetFileName(path), WpfUpdateService.StandaloneAssetName, StringComparison.OrdinalIgnoreCase);

    private static bool PathEquals(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        try
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    private static int CompareVersion(string? left, string right)
    {
        if (!Version.TryParse(left, out var actual) || !Version.TryParse(right, out var expected)) return 0;
        int result = actual.Major.CompareTo(expected.Major);
        if (result != 0) return result;
        result = actual.Minor.CompareTo(expected.Minor);
        if (result != 0) return result;
        return actual.Build.CompareTo(expected.Build);
    }

    private static bool DigestEquals(string? publishedDigest, string expectedHash)
    {
        if (string.IsNullOrWhiteSpace(publishedDigest)) return false;
        string value = publishedDigest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? publishedDigest[7..] : publishedDigest;
        return value.Length == 64 && string.Equals(value, expectedHash, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetRequired(Dictionary<string, string> options, string key, out string value)
    {
        value = Value(options, key) ?? "";
        return value.Length > 0;
    }

    private static string? Value(Dictionary<string, string> options, string key) =>
        options.TryGetValue(key, out var value) ? value : null;

    private static string ErrorCode(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "access_denied",
        IOException io when io.HResult == unchecked((int)0x80070070) || io.HResult == unchecked((int)0x80070027) => "insufficient_space",
        _ => "apply_failed"
    };

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static string BuildQuotedArguments(IEnumerable<string> arguments) => string.Join(" ", arguments.Select(QuoteArgument));

    private static string QuoteArgument(string value)
    {
        if (value.Length == 0) return "\"\"";
        if (!value.Any(char.IsWhiteSpace) && !value.Contains('"')) return value;
        var builder = new StringBuilder("\"");
        int backslashes = 0;
        foreach (char character in value)
        {
            if (character == '\\') { backslashes++; continue; }
            if (character == '"')
            {
                builder.Append('\\', backslashes * 2 + 1);
                builder.Append('"');
                backslashes = 0;
                continue;
            }
            builder.Append('\\', backslashes);
            builder.Append(character);
            backslashes = 0;
        }
        builder.Append('\\', backslashes * 2);
        builder.Append('"');
        return builder.ToString();
    }

    private sealed class UpdateMarker
    {
        public string Version { get; set; } = "";
        public string Backup { get; set; } = "";
    }
}
