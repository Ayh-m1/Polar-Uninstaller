using System.IO;

namespace PolarUninstallerWpf.Services;

/// <summary>Routine removal authorization never implies consent to delete
/// profiles, save data, personal documents or application settings.</summary>
public static class RemovalDataPolicy
{
    private static readonly HashSet<string> PersonalSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "userdata", "user data", "user_data", "profiles", "profile", "saves", "save", "saved", "savedgames", "saved games",
        "documents", "screenshots", "backups", "backup", "settings", "preferences", "config", "configs", "configuration"
    };
    private static readonly HashSet<string> PersonalExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".sav", ".save", ".sqlite", ".sqlite3", ".db", ".db3", ".ini", ".cfg", ".config", ".json", ".xml",
        ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".pdf", ".odt", ".ods", ".csv", ".txt",
        ".jpg", ".jpeg", ".png", ".mp4", ".mp3", ".zip", ".7z", ".bak", ".reg"
    };

    public static bool Preserve(string path,string installationRoot)
    {
        if(string.IsNullOrWhiteSpace(installationRoot)||!Path.IsPathFullyQualified(installationRoot))return true;
        // Root ownership/security is validated by the removal engine. This
        // hot-path filter only classifies data inside that already checked root.
        string root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(installationRoot));
        string full=Path.GetFullPath(path);
        if(!full.Equals(root,StringComparison.OrdinalIgnoreCase)&&!full.StartsWith(root.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))return true;
        string relative=Path.GetRelativePath(root,full);
        return relative.Split(new[]{'\\','/'},StringSplitOptions.RemoveEmptyEntries).Any(PersonalSegments.Contains)
            ||PersonalExtensions.Contains(Path.GetExtension(full));
    }
}
