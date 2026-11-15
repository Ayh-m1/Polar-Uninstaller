using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace PolarUninstallerWpf.Services;

/// <summary>
/// One verified application-owned Registry key captured before removal.
/// <see cref="Kind"/> is either "Uninstall" (a Windows uninstall registration)
/// or "Settings" (an application-specific settings key).
/// </summary>
public sealed record RegistryCleanupTarget(
    RegistryHive Hive,
    RegistryView View,
    string Path,
    string Kind,
    string Evidence,
    string Fingerprint = "");

public sealed record RegistryCleanupResult(
    int Removed,
    int Skipped,
    int Failed,
    IReadOnlyList<string> RemovedPaths,
    IReadOnlyList<string> SkippedPaths,
    IReadOnlyList<string> FailedPaths,
    IReadOnlyList<string> Messages);

/// <summary>
/// Finds and removes Registry keys whose ownership is established by the
/// verified installation path, the publisher/product key identity, or an exact
/// Windows uninstall registration. A name match alone is never enough.
/// Every deletion is backed up with reg.exe and verified afterwards.
/// </summary>
public static class RegistryCleanupService
{
    private static readonly string[] ProtectedRoots =
    {
        "Microsoft", "Classes", "Policies", "Windows", "RegisteredApplications",
        "Clients", "Wow6432Node", "WowAA32Node"
    };

    private static readonly string[] ProtectedNameFragments =
    {
        "shared", "common", "runtime", "redistributable", "anticheat", "antivirus",
        "defender", "driver", "service", "vanguard", "riotclient", "uninstaller", "updater"
    };

    private const int MaxVendors = 400;
    private const int MaxProducts = 2000;
    private const int MaxValueChecks = 4000;

    public static string DefaultBackupDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PolarUninstallerWPF-Development", "RegistryBackups");

    /// <summary>
    /// Captures application-specific settings keys. Requires a verified
    /// installation path; a product name alone is never sufficient.
    /// </summary>
    public static IReadOnlyList<RegistryCleanupTarget> CaptureSettings(
        InstalledProgram program,
        IReadOnlyList<InstalledProgram>? inventory = null)
    {
        var results = new List<RegistryCleanupTarget>();
        string location = InventoryMerger.NormalizePath(program.Location);
        if (location.Length == 0) return results;

        string product = InstallationPathResolver.NormalizeProduct(program.Name);
        if (product.Length < 3) return results;
        string publisher = InstallationPathResolver.NormalizeProduct(program.Publisher);
        var executables = CollectExecutables(program, location);

        // When another live application has the same normalized product name the
        // ownership of a settings key is ambiguous, so nothing is captured.
        if (inventory != null &&
            inventory.Any(other => !UninstallVerificationService.Matches(program, other) &&
                                   InstallationPathResolver.NormalizeProduct(other.Name) == product))
            return results;

        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var software = baseKey.OpenSubKey("Software");
                if (software == null) continue;

                int vendors = 0, products = 0, valueChecks = 0;

                // Direct children such as HKCU\Software\<Product> are only
                // eligible when a value references the verified installation.
                foreach (string name in SafeNames(software))
                {
                    if (++vendors > MaxVendors) break;
                    if (ProtectedRoots.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                    if (IsProtectedName(name)) continue;
                    if (InstallationPathResolver.NormalizeProduct(name) != product) continue;
                    using var key = SafeOpen(software, name);
                    if (key == null) continue;
                    if (!ValuesReference(key, location, executables, 0, ref valueChecks)) continue;
                    Add(results, hive, view, "Software\\" + name, "Settings",
                        "Registry values reference the verified installation path");
                }

                vendors = 0;
                // Vendor\Product keys are eligible with publisher/product identity
                // or with a value that references the verified installation.
                foreach (string vendorName in SafeNames(software))
                {
                    if (++vendors > MaxVendors) break;
                    if (ProtectedRoots.Contains(vendorName, StringComparer.OrdinalIgnoreCase)) continue;
                    if (IsProtectedName(vendorName)) continue;
                    using var vendor = SafeOpen(software, vendorName);
                    if (vendor == null) continue;
                    string vendorProduct = InstallationPathResolver.NormalizeProduct(vendorName);
                    foreach (string productName in SafeNames(vendor))
                    {
                        if (++products > MaxProducts) break;
                        if (ProtectedRoots.Contains(productName, StringComparer.OrdinalIgnoreCase)) continue;
                        if (IsProtectedName(productName) || IsProtectedName(vendorName)) continue;
                        if (InstallationPathResolver.NormalizeProduct(productName) != product) continue;
                        using var key = SafeOpen(vendor, productName);
                        if (key == null) continue;
                        bool publisherEvidence = publisher.Length >= 3 && vendorProduct == publisher;
                        bool valueEvidence = ValuesReference(key, location, executables, 0, ref valueChecks);
                        if (!publisherEvidence && !valueEvidence) continue;
                        Add(results, hive, view, "Software\\" + vendorName + "\\" + productName, "Settings",
                            publisherEvidence
                                ? "Publisher and product key identity match the verified application"
                                : "Registry values reference the verified installation path");
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                System.Security.SecurityException or ArgumentException)
            {
                // An unreadable view is skipped; it is never treated as removed.
            }
        }

        return results
            .GroupBy(target => (target.Hive, target.View, target.Path.ToLowerInvariant()))
            .Select(group => group.First())
            .OrderByDescending(target => target.Path.Length)
            .ToArray();
    }

    public static bool Exists(RegistryCleanupTarget target)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(target.Hive, target.View);
            using var key = baseKey.OpenSubKey(target.Path);
            return key != null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            System.Security.SecurityException or ArgumentException)
        {
            // Access denied must never look like a successful deletion.
            return true;
        }
    }

    /// <summary>
    /// Backs up and deletes each target, then verifies the deletion. Protected
    /// or ambiguous keys are skipped with a reason.
    /// </summary>
    public static Task<RegistryCleanupResult> RemoveAsync(
        IEnumerable<RegistryCleanupTarget> targets,
        string? backupDirectory = null,
        CancellationToken token = default) => Task.Run(() =>
    {
        int removed = 0, skipped = 0, failed = 0;
        var removedPaths = new List<string>();
        var skippedPaths = new List<string>();
        var failedPaths = new List<string>();
        var messages = new List<string>();
        string backup = string.IsNullOrWhiteSpace(backupDirectory) ? DefaultBackupDirectory : backupDirectory;

        foreach (var target in targets)
        {
            token.ThrowIfCancellationRequested();
            string display = target.Hive + " [" + target.View + "]\\" + target.Path;
            if (IsProtectedPath(target.Path))
            {
                skipped++;
                skippedPaths.Add(display);
                messages.Add("Skipped protected Registry key: " + display);
                continue;
            }
            if (!Exists(target))
            {
                skipped++;
                skippedPaths.Add(display);
                continue;
            }
            try
            {
                if(target.Fingerprint.Length==0||!Fingerprint(target.Hive,target.View,target.Path).Equals(target.Fingerprint,StringComparison.Ordinal))
                    throw new IOException("Registry ownership snapshot is missing or has changed.");
                Directory.CreateDirectory(backup);
                string file = Path.Combine(backup, Guid.NewGuid().ToString("N") + ".reg");
                string hive = target.Hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM";
                var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "reg.exe"))
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                foreach (var argument in new[]
                {
                    "export", hive + "\\" + target.Path, file, "/y",
                    target.View == RegistryView.Registry64 ? "/reg:64" : "/reg:32"
                })
                    start.ArgumentList.Add(argument);

                using (var process = Process.Start(start) ?? throw new IOException("Registry backup did not start."))
                {
                    process.WaitForExit();
                    if (process.ExitCode != 0 || !File.Exists(file) || new FileInfo(file).Length == 0)
                        throw new IOException("Registry backup failed.");
                }

                using (var baseKey = RegistryKey.OpenBaseKey(target.Hive, target.View))
                {
                    using var key = baseKey.OpenSubKey(target.Path);
                    if (key == null) throw new IOException("The Registry key changed before removal.");
                    if(!Fingerprint(target.Hive,target.View,target.Path).Equals(target.Fingerprint,StringComparison.Ordinal))
                        throw new IOException("The Registry key changed during backup.");
                    key.Close();
                    baseKey.DeleteSubKeyTree(target.Path, throwOnMissingSubKey: false);
                }

                if (Exists(target)) throw new IOException("The Registry key still exists after removal.");

                removed++;
                removedPaths.Add(display);
                messages.Add("Removed Registry key (backup: " + file + "): " + display);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                System.Security.SecurityException or System.ComponentModel.Win32Exception or ArgumentException)
            {
                failed++;
                failedPaths.Add(display);
                messages.Add("Could not remove Registry key: " + display + " (" + ex.GetType().Name + ")");
            }
        }

        return new RegistryCleanupResult(removed, skipped, failed, removedPaths, skippedPaths, failedPaths, messages);
    }, token);

    /// <summary>
    /// Shared Windows and launcher locations that must never be removed as an
    /// application-owned settings key.
    /// </summary>
    public static bool IsProtectedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return true;
        string normalized = path.Replace('/', '\\').Trim('\\');
        if (normalized.StartsWith("Software\\", StringComparison.OrdinalIgnoreCase))
        {
            string rest = normalized["Software\\".Length..];
            foreach (string root in ProtectedRoots)
                if (rest.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                    rest.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase))
                    return true;
            if (rest.Equals("Riot Games", StringComparison.OrdinalIgnoreCase)) return true;
            if (rest.StartsWith("Riot Games\\Riot Client", StringComparison.OrdinalIgnoreCase)) return true;
            if (rest.StartsWith("Riot Games\\Riot Vanguard", StringComparison.OrdinalIgnoreCase)) return true;
        }
        if (normalized.Contains("\\Riot Games\\Riot Client", StringComparison.OrdinalIgnoreCase)) return true;
        if (normalized.Contains("\\Riot Vanguard", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static void Add(List<RegistryCleanupTarget> results, RegistryHive hive, RegistryView view,
        string path, string kind, string evidence)
    {
        if (IsProtectedPath(path)) return;
        string fingerprint=Fingerprint(hive,view,path);
        if(fingerprint.Length>0)results.Add(new RegistryCleanupTarget(hive, view, path, kind, evidence,fingerprint));
    }

    private static string Fingerprint(RegistryHive hive,RegistryView view,string path)
    {
        try
        {
            using var root=RegistryKey.OpenBaseKey(hive,view);using var key=root.OpenSubKey(path);
            if(key==null)return "";
            var text=new System.Text.StringBuilder();int count=0;
            void Visit(RegistryKey current,int depth)
            {
                if(depth>16||++count>4096)throw new IOException("Registry subtree is too large to verify.");
                text.Append(current.Name).Append('\n');
                foreach(var name in current.GetValueNames().Order(StringComparer.Ordinal))
                {
                    var value=current.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames);
                    text.Append(name).Append('|').Append(current.GetValueKind(name)).Append('|');
                    text.Append(value is byte[] bytes?Convert.ToBase64String(bytes):value is string[] values?System.Text.Json.JsonSerializer.Serialize(values):Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
                }
                foreach(var name in current.GetSubKeyNames().Order(StringComparer.Ordinal))
                {using var child=current.OpenSubKey(name)??throw new IOException("Registry subtree changed.");Visit(child,depth+1);}
            }
            Visit(key,0);
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text.ToString())));
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException){return "";}
    }

    private static bool IsProtectedName(string name)
    {
        string normalized = InstallationPathResolver.NormalizeProduct(name);
        if (normalized is "riotclient" or "riotvanguard" or "vanguard") return true;
        return ProtectedNameFragments.Any(fragment => normalized.Contains(fragment, StringComparison.Ordinal));
    }

    private static IEnumerable<string> SafeNames(RegistryKey key)
    {
        string[] names;
        try { names = key.GetSubKeyNames(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Array.Empty<string>();
        }
        return names;
    }

    private static RegistryKey? SafeOpen(RegistryKey parent, string name)
    {
        try { return parent.OpenSubKey(name); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> CollectExecutables(InstalledProgram program, string location)
    {
        var executables = new List<string>();
        foreach (string candidate in (program.IconCandidates ?? Array.Empty<string>()).Prepend(program.IconPath))
        {
            string executable = InstallationPathResolver.Executable(candidate);
            if (executable.Length == 0) continue;
            try
            {
                string full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(executable));
                if (IsWithin(full, location)) executables.Add(full);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }
        }
        if (Directory.Exists(location))
        {
            try
            {
                executables.AddRange(Directory.EnumerateFiles(location, "*.exe", SearchOption.TopDirectoryOnly).Take(32));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }
        return executables.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool ValuesReference(RegistryKey key, string location, IReadOnlyList<string> executables,
        int depth, ref int checkedValues)
    {
        if (depth > 2 || checkedValues >= MaxValueChecks) return false;
        string[] names;
        try { names = key.GetValueNames(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
        foreach (string name in names)
        {
            if (++checkedValues >= MaxValueChecks) break;
            string raw;
            try { raw = Convert.ToString(key.GetValue(name)) ?? ""; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                continue;
            }
            if (raw.Length == 0 || raw.Length > 4096) continue;
            string expanded;
            try { expanded = Environment.ExpandEnvironmentVariables(raw).Trim().Trim('"'); }
            catch (ArgumentException) { continue; }
            if (expanded.Length == 0) continue;
            if (IsWithin(expanded, location)) return true;
            foreach (string executable in executables)
                if (expanded.Contains(executable, StringComparison.OrdinalIgnoreCase)) return true;
            if (expanded.StartsWith(location, StringComparison.OrdinalIgnoreCase) &&
                (expanded.Length == location.Length ||
                 expanded[location.Length] is '\\' or ' ' or '"' or '/' or ','))
                return true;
        }
        string[] subKeys;
        try { subKeys = key.GetSubKeyNames(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
        foreach (string subKey in subKeys.Take(8))
        {
            using var child = SafeOpen(key, subKey);
            if (child != null && ValuesReference(child, location, executables, depth + 1, ref checkedValues))
                return true;
        }
        return false;
    }

    private static bool IsWithin(string path, string root)
    {
        try
        {
            string full = Path.GetFullPath(path).TrimEnd('\\');
            string parent = Path.GetFullPath(root).TrimEnd('\\');
            return full.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
                   full.StartsWith(parent + "\\", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
