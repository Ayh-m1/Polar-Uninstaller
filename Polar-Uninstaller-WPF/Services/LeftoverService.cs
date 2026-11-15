using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace PolarUninstallerWpf.Services;

public sealed record RegistryLeftover(RegistryHive Hive, RegistryView View, string Path, string Name, string Location,string Command="",string Version="");
public sealed record LeftoverReport(IReadOnlyList<string> Folders, IReadOnlyList<RegistryLeftover> Registry);
public sealed class LeftoverService
{
    private readonly string backupDirectory;
    public LeftoverService(string? backupDirectory=null)=>this.backupDirectory=backupDirectory??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PolarUninstallerWPF-Development","RegistryBackups");
    private const string Uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>
    /// Captures the exact Windows uninstall registrations that belong to this
    /// application. Ownership is established by the exact display name plus
    /// the registered location or command, or by a launcher-specific stable
    /// identifier (Steam App ID, Battle.net UID, Riot product ID). A name-only
    /// fuzzy match is never accepted.
    /// </summary>
    public Task<IReadOnlyList<RegistryLeftover>> CaptureAsync(InstalledProgram program) => Task.Run<IReadOnlyList<RegistryLeftover>>(() =>
    {
        var records = new List<RegistryLeftover>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view); using var parent = root.OpenSubKey(Uninstall);
                if (parent == null) continue;
                foreach (var name in parent.GetSubKeyNames())
                {
                    using var key = parent.OpenSubKey(name);
                    if (key == null) continue;
                    string title = key.GetValue("DisplayName") as string ?? "", location = key.GetValue("InstallLocation") as string ?? "";
                    string command=key.GetValue("UninstallString") as string??"";
                    if (!IdentityMatches(program, name, title, location, command)) continue;
                    records.Add(new(hive, view, Uninstall + "\\" + name, title, location,command,Convert.ToString(key.GetValue("DisplayVersion"))??""));
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException) { }
        }
        // HKCU\Software is not redirected for 32-bit callers, so both views
        // return the very same registration. HKLM genuinely has two stores.
        return records
            .GroupBy(record => (record.Hive,
                View: record.Hive == RegistryHive.CurrentUser ? "" : record.View.ToString(),
                record.Path.ToLowerInvariant()))
            .Select(group => group.First())
            .ToArray();
    });

    /// <summary>True when a captured registration still exists with the same identity.</summary>
    public static bool RegistrationExists(RegistryLeftover record)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(record.Hive, record.View);
            using var key = root.OpenSubKey(record.Path);
            return Matches(key, record);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            // Access denied must never look like a successful removal.
            return true;
        }
    }

    /// <summary>
    /// Re-reads the uninstall Registry to decide whether the exact application
    /// is still registered. Used by completion probes when no pre-uninstall
    /// snapshot is available.
    /// </summary>
    public static bool RegistrationExists(InstalledProgram program)
    {
        try
        {
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var root = RegistryKey.OpenBaseKey(hive, view); using var parent = root.OpenSubKey(Uninstall);
                if (parent == null) continue;
                foreach (var name in parent.GetSubKeyNames())
                {
                    using var key = parent.OpenSubKey(name);
                    if (key == null) continue;
                    string title = key.GetValue("DisplayName") as string ?? "";
                    string location = key.GetValue("InstallLocation") as string ?? "";
                    string command = key.GetValue("UninstallString") as string ?? "";
                    if (IdentityMatches(program, name, title, location, command)) return true;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            return true;
        }
        return false;
    }

    public static bool EqualPath(string a, string b) => !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    private static bool Matches(RegistryKey? key,RegistryLeftover record)
    {
        if (key == null) return false;
        if (!string.Equals(key.GetValue("DisplayName") as string, record.Name, StringComparison.OrdinalIgnoreCase)) return false;
        if(record.Version.Length>0&&!record.Version.Equals(Convert.ToString(key.GetValue("DisplayVersion")),StringComparison.OrdinalIgnoreCase))return false;
        string location = key.GetValue("InstallLocation") as string ?? "";
        string command = key.GetValue("UninstallString") as string ?? "";
        if (record.Location.Length > 0) return EqualPath(location, record.Location);
        if (record.Command.Length > 0) return string.Equals(command.Trim(), record.Command.Trim(), StringComparison.OrdinalIgnoreCase);
        return false;
    }
    public Task<LeftoverReport> ScanAsync(InstalledProgram program, IReadOnlyList<InstalledProgram> others, IReadOnlyList<RegistryLeftover> captured) => Task.Run(() =>
    {
        var folders = new List<string>();
        // Nonempty folders may contain user data or shared components. Only empty,
        // unshared install directories are eligible for automatic leftover removal.
        if (Directory.Exists(program.Location) && !others.Any(p => EqualPath(p.Location, program.Location)))
        {
            try { BrowserOperationsService.ValidateDeletion(program.Location); if (!Directory.EnumerateFileSystemEntries(program.Location).Any()) folders.Add(program.Location); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        var remaining = new List<RegistryLeftover>();
        foreach (var record in captured)
            if (RegistrationExists(record)) remaining.Add(record);
        return new LeftoverReport(folders, remaining);
    });
    public async Task<IReadOnlyList<string>> RemoveAsync(LeftoverReport report)
    {
        var results = new List<string>();
        foreach (var folder in report.Folders)
        {
            try { BrowserOperationsService.ValidateDeletion(folder); Directory.Delete(folder, false); results.Add("Removed empty folder: " + folder); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { results.Add("Skipped folder: " + folder); }
        }
        foreach (var record in report.Registry)
        {
            try
            {
                if(!record.Path.StartsWith(Uninstall+"\\",StringComparison.OrdinalIgnoreCase)||record.Path[(Uninstall.Length+1)..].Contains('\\'))
                    throw new IOException("Only an exact captured application uninstall key may be removed.");
                if(!RegistrationExists(record)){results.Add("Skipped already absent registration: "+record.Path);continue;}
                var backup = backupDirectory; Directory.CreateDirectory(backup);
                string file = Path.Combine(backup, Guid.NewGuid().ToString("N") + ".reg");
                string hive = record.Hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM";
                var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "reg.exe")) { UseShellExecute = false, CreateNoWindow = true };
                foreach (var arg in new[] { "export", hive + "\\" + record.Path, file, "/y", record.View == RegistryView.Registry64 ? "/reg:64" : "/reg:32" }) start.ArgumentList.Add(arg);
                using var process = Process.Start(start) ?? throw new IOException("Registry backup did not start."); await process.WaitForExitAsync();
                if (process.ExitCode != 0 || !File.Exists(file) || new FileInfo(file).Length == 0) throw new IOException("Registry backup failed.");
                using var root = RegistryKey.OpenBaseKey(record.Hive, record.View); using var key = root.OpenSubKey(record.Path);
                if (!Matches(key,record)) throw new IOException("Registration changed.");
                key!.Close(); root.DeleteSubKeyTree(record.Path); results.Add("Removed exact registration; backup: " + file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Security.SecurityException) { results.Add("Skipped registration: " + record.Path+" ("+RemovalEngine.Reason(e)+")"); }
        }
        return results;
    }

    private static bool IdentityMatches(InstalledProgram program, string keyName, string title, string location, string command)
    {
        string id = program.Identifier ?? "";
        string normalizedCommand = command ?? "";

        // Steam registrations are keyed by App ID even when the display name differs.
        if (id.StartsWith("steam:", StringComparison.OrdinalIgnoreCase))
        {
            string appId = id["steam:".Length..];
            if (appId.Length > 0 && appId.All(char.IsAsciiDigit) && appId != "0" &&
                (keyName.Equals("Steam App " + appId, StringComparison.OrdinalIgnoreCase) ||
                 normalizedCommand.Contains("steam://uninstall/" + appId, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        // Battle.net registrations use Blizzard Uninstaller.exe --uid=<product>.
        if (id.StartsWith("battlenet:", StringComparison.OrdinalIgnoreCase))
        {
            string uid = id["battlenet:".Length..];
            if (uid.Length > 0 &&
                normalizedCommand.Contains("Blizzard Uninstaller.exe", StringComparison.OrdinalIgnoreCase) &&
                BlizzardUid(normalizedCommand).Equals(uid, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Riot registrations use --uninstall-product=<product>.
        if (id.StartsWith("riot:", StringComparison.OrdinalIgnoreCase) &&
            !RiotGameService.IsProtectedComponentName(program.Name, location, program.Publisher))
        {
            string registeredProduct = RiotGameService.ParseProductId(normalizedCommand);
            if (registeredProduct.Length > 0 &&
                registeredProduct.Equals(RiotGameService.ProductIdOf(program), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (!string.Equals(title, program.Name, StringComparison.OrdinalIgnoreCase)) return false;
        if (program.Location.Length > 0 && location.Length > 0) return EqualPath(location, program.Location);
        if (program.Uninstall.Length > 0 &&
            string.Equals(normalizedCommand.Trim(), program.Uninstall.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            if(program.Location.Length==0)return true;
            string exe=InstallationPathResolver.Executable(normalizedCommand);
            string path=InventoryMerger.NormalizePath(program.Location);
            return path.Length>0&&exe.StartsWith(path.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase);
        }
        if (program.Location.Length > 0) return EqualPath(location, program.Location);
        // Registry-only entries have no location anywhere. The exact display name
        // and an equally empty registered location are the full identity.
        return false;
    }

    private static string BlizzardUid(string command)
    {
        var match = Regex.Match(command ?? "", @"--uid=(?:""([^""]+)""|([^\s""]+))", RegexOptions.IgnoreCase);
        if (!match.Success) return "";
        string value = match.Groups[1].Success && match.Groups[1].Value.Length > 0
            ? match.Groups[1].Value
            : match.Groups[2].Value;
        return value.Trim().Trim('"');
    }
}
