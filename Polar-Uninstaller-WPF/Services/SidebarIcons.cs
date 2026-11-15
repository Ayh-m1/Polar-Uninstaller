using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PolarUninstallerWpf.Services;

/// <summary>
/// Theme-aware vector icons used by the sidebar. Outline icons use a 24x24
/// coordinate space and a consistent stroke; the Discord mark is a filled
/// official logo because its silhouette is not a stroke icon.
/// </summary>
public static class SidebarIcons
{
    private static readonly IReadOnlyDictionary<string, string> Outline = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        // House
        ["home"] = "M3 9 L12 2 L21 9 L21 21 L15 21 L15 12 L9 12 L9 21 L3 21 Z",
        // Trash-2
        ["uninstall"] = "M3 6 H21 M19 6 V20 A2 2 0 0 1 17 22 H7 A2 2 0 0 1 5 20 V6 M8 6 V4 A2 2 0 0 1 10 2 H14 A2 2 0 0 1 16 4 V6 M10 11 V17 M14 11 V17",
        // Brush / cleaning
        ["cleaner"] = "M9.06 11.9 L17.13 3.84 A2.85 2.85 0 1 1 21.16 7.87 L13.1 15.95 M7.07 14.94 C5.41 14.94 4.07 16.29 4.07 17.96 C4.07 19.29 1.57 19.48 2.07 19.98 C3.15 21.08 4.56 22 6.07 22 C8.27 22 10.07 20.2 10.07 17.96 A3.01 3.01 0 0 0 7.07 14.94 Z",
        // Chart, no axes, combined
        ["dashboard"] = "M12 16 V21 M16 14 V21 M20 10 V21 M22 3 L13.5 11.5 L9.5 7.5 L2 15 M4 18 V21 M8 14 V21",
        // Folder open
        ["files"] = "M6 14 L7.5 11.1 A2 2 0 0 1 9.24 10 H20 A2 2 0 0 1 21.94 12.5 L20.4 18.5 A2 2 0 0 1 18.45 20 H4 A2 2 0 0 1 2 18 V5 A2 2 0 0 1 4 3 H7.9 A2 2 0 0 1 9.59 3.9 L10.4 5.1 A2 2 0 0 0 12.07 6 H18 A2 2 0 0 1 20 8 V10",
        // Sparkles
        ["whatsnew"] = "M12 3 L14.2 9.8 L21 12 L14.2 14.2 L12 21 L9.8 14.2 L3 12 L9.8 9.8 Z M19 3 L19.7 5.3 L22 6 L19.7 6.7 L19 9 L18.3 6.7 L16 6 L18.3 5.3 Z M5 16 L5.5 17.5 L7 18 L5.5 18.5 L5 20 L4.5 18.5 L3 18 L4.5 17.5 Z",
        // Thumbs up
        ["support"] = "M7 10 V22 M15 5.88 L14 10 H19.83 A2 2 0 0 1 21.75 12.56 L19.42 20.56 A2 2 0 0 1 17.5 22 H4 A2 2 0 0 1 2 20 V12 A2 2 0 0 1 4 10 H6.76 A2 2 0 0 0 8.55 8.89 L12 2 A3.13 3.13 0 0 1 15 5.88 Z"
    };

    // Official Discord mark (Simple Icons, 24x24 viewBox).
    private const string Discord =
        "M20.317 4.3698a19.7913 19.7913 0 00-4.8851-1.5152.0741.0741 0 00-.0785.0371c-.211.3753-.4447.8648-.6083 1.2495-1.8447-.2762-3.68-.2762-5.4868 0-.1636-.3933-.4058-.8742-.6177-1.2495a.077.077 0 00-.0785-.037 19.7363 19.7363 0 00-4.8852 1.515.0699.0699 0 00-.0321.0277C.5334 9.0458-.319 13.5799.0992 18.0578a.0824.0824 0 00.0312.0561c2.0528 1.5076 4.0413 2.4228 5.9929 3.0294a.0777.0777 0 00.0842-.0276c.4616-.6304.8731-1.2952 1.226-1.9942a.076.076 0 00-.0416-.1057c-.6528-.2476-1.2743-.5495-1.8722-.8923a.077.077 0 01-.0076-.1277c.1258-.0943.2517-.1923.3718-.2914a.0743.0743 0 01.0776-.0105c3.9278 1.7933 8.18 1.7933 12.0614 0a.0739.0739 0 01.0785.0095c.1202.099.246.1981.3728.2924a.077.077 0 01-.0066.1276 12.2986 12.2986 0 01-1.873.8914.0766.0766 0 00-.0407.1067c.3604.698.7719 1.3628 1.225 1.9932a.076.076 0 00.0842.0286c1.961-.6067 3.9495-1.5219 6.0023-3.0294a.077.077 0 00.0313-.0552c.5004-5.177-.8382-9.6739-3.5485-13.6604a.061.061 0 00-.0312-.0286zM8.02 15.3312c-1.1825 0-2.1569-1.0857-2.1569-2.419 0-1.3332.9555-2.4189 2.157-2.4189 1.2108 0 2.1757 1.0952 2.1568 2.419 0 1.3332-.9555 2.4189-2.1569 2.4189zm7.9748 0c-1.1825 0-2.1569-1.0857-2.1569-2.419 0-1.3332.9554-2.4189 2.1569-2.4189 1.2108 0 2.1757 1.0952 2.1568 2.419 0 1.3332-.946 2.4189-2.1568 2.4189z";

    public static IEnumerable<string> Keys => Outline.Keys.Concat(new[] { "discord" });

    /// <summary>Returns the parsed geometry for a sidebar key, or null when unknown.</summary>
    public static bool HasGeometry(string key) => GeometryFor(key) is not null;

    public static Geometry? GeometryFor(string key)
    {
        try
        {
            if (key.Equals("discord", StringComparison.OrdinalIgnoreCase)) return System.Windows.Media.Geometry.Parse(Discord);
            return Outline.TryGetValue(key, out string? data) ? System.Windows.Media.Geometry.Parse(data) : null;
        }
        catch (FormatException) { return null; }
    }

    /// <summary>Creates a transparent-background icon path using the supplied theme brush.</summary>
    public static Path Create(string key, Brush brush, double size = 20)
    {
        bool filled = key.Equals("discord", StringComparison.OrdinalIgnoreCase);
        var path = new Path
        {
            Data = GeometryFor(key),
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            Fill = filled ? brush : Brushes.Transparent,
            Stroke = filled ? null : brush,
            StrokeThickness = filled ? 0 : 1.6,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        RenderOptions.SetEdgeMode(path, EdgeMode.Unspecified);
        return path;
    }
}
