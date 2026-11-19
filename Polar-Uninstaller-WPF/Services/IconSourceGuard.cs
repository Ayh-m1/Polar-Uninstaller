using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PolarUninstallerWpf.Services;

/// <summary>
/// Rejects an icon whose decoded source does not belong to the entry. A wrong logo
/// (another product or package) is worse than a neutral badge, so every resolver
/// result passes through this guard: the file must live inside the entry's install
/// folder, be its registered DisplayIcon/App Paths executable, its own MSI icon
/// cache, or an exactly-owned launcher executable.
/// </summary>
public static class IconSourceGuard
{
    public static bool IsOwned(InstalledProgram program, string? source)
    {
        if(string.IsNullOrWhiteSpace(source))return false;
        string path;
        try
        {
            if(!Path.IsPathFullyQualified(source))return false;
            path=Path.GetFullPath(source);
        }
        catch(Exception e)when(e is ArgumentException or NotSupportedException or PathTooLongException){return false;}

        string root=InventoryMerger.NormalizePath(program.Location);
        if(root.Length>0&&StorageCacheUpdates.Within(path,root))return true;

        if(string.Equals(path,RegisteredExecutable(program.RegisteredIcon),StringComparison.OrdinalIgnoreCase))return true;
        if(string.Equals(path,RegisteredExecutable(program.IconPath),StringComparison.OrdinalIgnoreCase))return true;
        string uninstall=InstallationPathResolver.Executable(program.Uninstall);
        if(uninstall.Length>0&&string.Equals(path,Path.GetFullPath(uninstall),StringComparison.OrdinalIgnoreCase))return true;

        // App Paths gives one exact executable per registered product name; accept it
        // only when the file name still matches this entry's own product name.
        string product=InstallationPathResolver.NormalizeProduct(program.Name);
        foreach(string candidate in program.IconCandidates??Array.Empty<string>())
        {
            string executable=InstallationPathResolver.Executable(candidate);
            if(executable.Length==0||!Path.IsPathFullyQualified(executable))continue;
            if(!string.Equals(path,Path.GetFullPath(executable),StringComparison.OrdinalIgnoreCase))continue;
            string stem=InstallationPathResolver.NormalizeProduct(Path.GetFileNameWithoutExtension(executable));
            if(product.Length>=3&&stem==product)return true;
        }

        string productCode=ProductCode(program);
        if(productCode.Length>0)
        {
            string cache=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"Installer",productCode);
            if(StorageCacheUpdates.Within(path,cache))return true;
        }

        string launcher=LauncherExecutable(program);
        if(launcher.Length>0&&string.Equals(path,launcher,StringComparison.OrdinalIgnoreCase))return true;
        return false;
    }

    private static string RegisteredExecutable(string raw)
    {
        if(string.IsNullOrWhiteSpace(raw))return "";
        string executable=InstallationPathResolver.Executable(raw);
        if(executable.Length==0)executable=raw.Trim().Trim('"');
        try{return Path.IsPathFullyQualified(executable)?Path.GetFullPath(executable):"";}catch(Exception e)when(e is ArgumentException or NotSupportedException or PathTooLongException){return "";}
    }

    private static string ProductCode(InstalledProgram program)
    {
        string value=program.Registration?.Path??program.Uninstall;
        var match=Regex.Match(value,@"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}");
        return match.Success?match.Value:"";
    }

    /// <summary>Steam's own client executable, read from Valve's exact registration.</summary>
    public static string LauncherExecutable(InstalledProgram program)
    {
        if(InstallationPathResolver.NormalizeProduct(program.Name)!="steam")return "";
        foreach(var (hive,view,keyName,field) in new[]
        {
            (RegistryHive.CurrentUser,RegistryView.Registry64,@"Software\Valve\Steam","SteamExe"),
            (RegistryHive.CurrentUser,RegistryView.Registry64,@"Software\Valve\Steam","SteamPath"),
            (RegistryHive.LocalMachine,RegistryView.Registry32,@"SOFTWARE\Valve\Steam","InstallPath")
        })
        try
        {
            using var baseKey=RegistryKey.OpenBaseKey(hive,view);using var key=baseKey.OpenSubKey(keyName);
            if(key?.GetValue(field) is not string value||string.IsNullOrWhiteSpace(value))continue;
            string candidate=Environment.ExpandEnvironmentVariables(value.Trim().Trim('"')).Replace('/',Path.DirectorySeparatorChar);
            if(Directory.Exists(candidate))candidate=Path.Combine(candidate,"steam.exe");
            if(!Path.IsPathFullyQualified(candidate))continue;
            candidate=Path.GetFullPath(candidate);
            if(File.Exists(candidate))return candidate;
        }
        catch(Exception e)when(e is UnauthorizedAccessException or System.Security.SecurityException or IOException or ArgumentException){ }
        return "";
    }
}
