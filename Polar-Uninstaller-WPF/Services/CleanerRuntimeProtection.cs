using System.IO;

namespace PolarUninstallerWpf.Services;

public static class CleanerRuntimeProtection
{
    public static IReadOnlyList<string> Directories()
    {
        var paths=new List<string>{AppContext.BaseDirectory,Path.Combine(Path.GetTempPath(),".net")};
        if(Environment.ProcessPath is string executable)paths.Add(Path.GetDirectoryName(executable)!);
        // IncludeAllContentForSelfExtract may load assemblies from the bundle
        // extraction tree, independently of the executable's original directory.
        string assembly=typeof(CleanerRuntimeProtection).Assembly.Location;
        if(!string.IsNullOrEmpty(assembly))paths.Add(Path.GetDirectoryName(assembly)!);
        string? custom=Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR");
        if(!string.IsNullOrWhiteSpace(custom)&&Path.IsPathFullyQualified(custom))paths.Add(custom);
        return paths.Where(Path.IsPathFullyQualified).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static bool IsProtected(string path,IReadOnlyList<string> protectedDirectories)
    {
        string full=Path.GetFullPath(path);
        foreach(string directory in protectedDirectories)
        {
            string root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            if(full.Equals(root,StringComparison.OrdinalIgnoreCase)||full.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))return true;
        }
        // Downloaded updates and their helper/status files are managed by the
        // updater's own stale-download policy, not the general Temp cleaner.
        return Path.GetFileName(full).StartsWith("PolarWpfUpdate-",StringComparison.OrdinalIgnoreCase);
    }
}
