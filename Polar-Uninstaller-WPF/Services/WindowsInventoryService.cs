using Microsoft.Win32;

namespace PolarUninstallerWpf.Services;

public sealed record ApplicationRegistration(RegistryHive Hive, RegistryView View, string Path);

public sealed record InstalledProgram(string Name, string Publisher, string Version, string Location = "", string IconPath = "", long? SizeKb = null, string Uninstall = "", string Source = "Windows", string Identifier = "", IReadOnlyList<string>? Sources = null, IReadOnlyList<string>? IconCandidates = null,bool RequiresOfficialRemoval=false)
{
    public string LaunchUri { get; init; } = "";
    public string RegisteredIcon { get; init; } = "";
    public ApplicationRegistration? Registration { get; init; }
}

public sealed class WindowsInventoryService
{
    public Task<IReadOnlyList<InstalledProgram>> GetInstalledAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<InstalledProgram>>(() =>
        {
            var programs = new List<InstalledProgram>();
            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (uninstall is null) continue;
                    var appIcons=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                    using(var paths=baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"))
                    {
                        if(paths!=null)foreach(var executableName in paths.GetSubKeyNames())
                        {
                            using var app=paths.OpenSubKey(executableName);string target=Environment.ExpandEnvironmentVariables(Convert.ToString(app?.GetValue(""))??"").Trim('"');
                            if(System.IO.File.Exists(target))appIcons[InstallationPathResolver.NormalizeProduct(System.IO.Path.GetFileNameWithoutExtension(executableName))]=target;
                        }
                    }
                    foreach (var keyName in uninstall.GetSubKeyNames())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            using var item = uninstall.OpenSubKey(keyName);
                            var name = item?.GetValue("DisplayName") as string;
                            if (string.IsNullOrWhiteSpace(name) || Convert.ToString(item?.GetValue("SystemComponent")) == "1") continue;
                            var size = item?.GetValue("EstimatedSize");
                            long? sizeKb = long.TryParse(Convert.ToString(size), out long parsed) && parsed > 0 ? parsed : null;
                            string command = Convert.ToString(item?.GetValue("UninstallString")) ?? "";
                            var steam = System.Text.RegularExpressions.Regex.Match(keyName + " " + command, @"(?:Steam App\s+|steam://uninstall/)(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            string identifier = steam.Success ? "steam:" + steam.Groups[1].Value : "";
                            string publisher = Convert.ToString(item?.GetValue("Publisher")) ?? "";
                            var icons = new List<string>();
                            string icon = Convert.ToString(item?.GetValue("DisplayIcon")) ?? "";
                            if (icon.Length > 0) icons.Add(icon);
                            if(appIcons.TryGetValue(InstallationPathResolver.NormalizeProduct(name),out var applicationIcon))icons.Add(applicationIcon);
                            // Some installers keep their logo beside the uninstaller rather
                            // than supplying DisplayIcon. Do not execute any command here.
                            var executable = System.Text.RegularExpressions.Regex.Match(command, "^\\s*(?:\"(?<path>[^\"]+\\.exe)\"|(?<path>[^\"]+?\\.exe))(?:\\s|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (executable.Success)
                            {
                                string path = Environment.ExpandEnvironmentVariables(executable.Groups["path"].Value);
                                if (System.IO.Path.IsPathFullyQualified(path) && System.IO.File.Exists(path)) icons.Add(path);
                            }
                            string location=InstallationPathResolver.Resolve(name,Convert.ToString(item?.GetValue("InstallLocation"))??"",icon,command);
                            if(location.Length==0)foreach(var candidateIcon in icons){location=InstallationPathResolver.Resolve(name,"",candidateIcon,command);if(location.Length>0)break;}
                            // Riot Client and Riot Vanguard are shared components. Riot
                            // games are identified by their registered product id and are
                            // always routed through the official uninstaller.
                            bool riotProtected = RiotGameService.IsProtectedComponentName(name, location, publisher);
                            if (identifier.Length == 0 && !riotProtected)
                            {
                                string riotProduct = RiotGameService.ParseProductId(command);
                                if (riotProduct.Length > 0) identifier = "riot:" + riotProduct;
                            }
                            bool managed=Convert.ToString(item?.GetValue("WindowsInstaller"))=="1" || !string.IsNullOrEmpty(Convert.ToString(item?.GetValue("ReleaseType"))) || !string.IsNullOrEmpty(Convert.ToString(item?.GetValue("ParentKeyName"))) || Convert.ToString(item?.GetValue("NoRemove"))=="1" || riotProtected || identifier.StartsWith("riot:", StringComparison.OrdinalIgnoreCase);
                            programs.Add(new(name.Trim(), publisher, Convert.ToString(item?.GetValue("DisplayVersion")) ?? "", location, icon, sizeKb, command, "Windows", identifier, null, icons,managed)
                            { RegisteredIcon = icon, Registration = new(hive, view, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + keyName) });
                        }
                        catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException) { }
                    }
                }
                catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException) { }
            }
            return programs.Distinct().OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }, cancellationToken);
}
