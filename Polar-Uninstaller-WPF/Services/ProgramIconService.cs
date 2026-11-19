using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PolarUninstallerWpf.Services;

public sealed record ProgramIcon(ImageSource Image, Color? Accent)
{
    public string SourcePath { get; init; } = "";
    public int SourceIndex { get; init; }
    public IconResolutionMethod Method { get; init; } = IconResolutionMethod.Legacy;
}
public static class ProgramIconService
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct ShellInfo { public IntPtr Icon; public int Index; public uint Attributes; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SHGetFileInfo(string path, uint attributes, out ShellInfo info, uint size, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint ExtractIconEx(string path, int index, out IntPtr large, IntPtr small, uint count);
    private static readonly ConcurrentDictionary<string, Task<ProgramIcon?>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim Slots = new(4);
    public static Task<ProgramIcon?> LoadAsync(string input)
    {
        // Bound retained decoded images while keeping in-flight extraction shared.
        if(Cache.Count>1024)foreach(var item in Cache.Where(c=>c.Value.IsCompleted).Take(128))Cache.TryRemove(item.Key,out _);
        return Cache.GetOrAdd(input, async key =>
        {
            await Slots.WaitAsync();
            try { return await Task.Run(() => Load(key)); }
            finally { Slots.Release(); }
        });
    }
    private static ProgramIcon? Load(string input)
    {
        try
        {
            string path = Environment.ExpandEnvironmentVariables(input.Trim().Trim('"'));
            int? iconIndex = null;
            int comma = path.LastIndexOf(','); if (comma > 0 && int.TryParse(path[(comma + 1)..], out int parsedIndex)) { iconIndex = parsedIndex; path = path[..comma].Trim().Trim('"'); }
            if (!Path.IsPathFullyQualified(path) || !File.Exists(path)&&!Directory.Exists(path)) return null;
            string ext = Path.GetExtension(path);
            BitmapSource source;
            if (new[] { ".png", ".jpg", ".jpeg", ".ico" }.Contains(ext, StringComparer.OrdinalIgnoreCase))
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if(ext.Equals(".ico",StringComparison.OrdinalIgnoreCase))
                {var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);source=decoder.Frames.OrderByDescending(x=>x.PixelWidth).First();source.Freeze();}
                else
                {var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=128;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();source=bitmap;}
            }
            else
            {
                IntPtr handle;
                if (File.Exists(path) && new[]{".exe",".dll"}.Contains(ext,StringComparer.OrdinalIgnoreCase) && ExtractIconEx(path, iconIndex ?? 0, out var extracted, IntPtr.Zero, 1) > 0) handle = extracted;
                else if (SHGetFileInfo(path, 0, out var info, (uint)Marshal.SizeOf<ShellInfo>(), 0x100) != IntPtr.Zero) handle = info.Icon;
                else return null;
                if (handle == IntPtr.Zero) return null;
                try { source = Imaging.CreateBitmapSourceFromHIcon(handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32)); source.Freeze(); }
                finally { DestroyIcon(handle); }
            }
            try
            {
                double scale=Math.Min(1,Math.Min(64.0/source.PixelWidth,64.0/source.PixelHeight));
                var scaled = new TransformedBitmap(source, new ScaleTransform(scale,scale));
                var bitmap = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0); bitmap.Freeze();
                byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                int best = 0; long rr = 0, gg = 0, bb = 0, count = 0;
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    int b = pixels[i], g = pixels[i + 1], r = pixels[i + 2], alpha = pixels[i + 3];
                    int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
                    if (alpha < 160 || max < 70 || min > 235 || max - min < 40) continue;
                    int score = max - min; if (score > best) best = score;
                    rr += r * score; gg += g * score; bb += b * score; count += score;
                }
                bool visible=pixels.Where((_,i)=>i%4==3).Any(a=>a>0);
                return visible ? new ProgramIcon(bitmap, best > 0 && count > 0 ? Color.FromRgb((byte)(rr / count), (byte)(gg / count), (byte)(bb / count)) : null){SourcePath=path} : null;
            }
            finally { }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException or NotSupportedException or FormatException or InvalidOperationException or COMException) { return null; }
    }
}
