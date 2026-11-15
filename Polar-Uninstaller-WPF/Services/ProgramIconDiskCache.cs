using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PolarUninstallerWpf.Services;

/// <summary>
/// Persistent disk cache for resolved application/game icons. Cache keys are
/// based on the full installation identity, so stale icons are not reused when
/// an application moves or is replaced.
/// </summary>
public static class ProgramIconDiskCache
{
    private static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PolarUninstallerWPF-Development", "icon-cache");

    public static string KeyFor(InstalledProgram program) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("|", program.Name, program.Location, program.Identifier,
                program.IconPath, string.Join(";", program.IconCandidates ?? Array.Empty<string>()))))).ToLowerInvariant();

    public static string PathFor(InstalledProgram program) =>
        Path.Combine(Root, KeyFor(program) + ".png");

    public static ProgramIcon? TryLoad(InstalledProgram program)
    {
        string path = PathFor(program);
        if (!File.Exists(path)) return null;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 64;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return new ProgramIcon(bitmap, null) { SourcePath = path };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UriFormatException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    public static void Save(InstalledProgram program, ProgramIcon icon)
    {
        if (icon.Image is not BitmapSource source) return;
        try
        {
            string path = PathFor(program);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var stream = File.Create(temporary))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(source));
                encoder.Save(stream);
            }
            File.Move(temporary, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
        }
    }
}
