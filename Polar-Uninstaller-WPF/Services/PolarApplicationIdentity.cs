using System.IO;

namespace PolarUninstallerWpf.Services;

/// <summary>
/// Identifies Polar Uninstaller's own installation records so the application
/// never lists, tracks, or offers to uninstall itself.
/// </summary>
public static class PolarApplicationIdentity
{
    public static bool IsPolarOwned(InstalledProgram program)
    {
        if (program is null) return false;

        string name = program.Name?.Trim() ?? "";
        string publisher = program.Publisher?.Trim() ?? "";
        string source = program.Source?.Trim() ?? "";
        string location = InventoryMerger.NormalizePath(program.Location ?? "");
        string identifier = program.Identifier?.Trim() ?? "";
        string iconPath = program.IconPath?.Trim() ?? "";
        var iconCandidates = program.IconCandidates ?? Array.Empty<string>();

        bool nameMatch =
            name.Equals("Polar Uninstaller", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Polar Uninstaller (WPF)", StringComparison.OrdinalIgnoreCase);

        bool publisherMatch =
            publisher.Contains("Polar", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("Polar", StringComparison.OrdinalIgnoreCase);

        bool pathMatch =
            location.Contains("Polar Uninstaller", StringComparison.OrdinalIgnoreCase) ||
            location.Contains("PolarUninstaller", StringComparison.OrdinalIgnoreCase);

        bool executableMatch =
            iconPath.Contains("PolarUninstaller", StringComparison.OrdinalIgnoreCase) ||
            iconCandidates.Any(path => (path ?? "").Contains("PolarUninstaller", StringComparison.OrdinalIgnoreCase));

        bool identifierMatch =
            identifier.Contains("PolarUninstaller", StringComparison.OrdinalIgnoreCase);

        // Require a name match plus at least one reliable identity signal so an
        // unrelated product named "Polar..." is not excluded.
        if (nameMatch && (publisherMatch || pathMatch || executableMatch || identifierMatch))
            return true;

        // A Polar installation path/executable is definitive even if the
        // registered display name differs slightly.
        if (pathMatch && (publisherMatch || executableMatch))
            return true;

        if (executableMatch && publisherMatch)
            return true;

        return false;
    }
}
