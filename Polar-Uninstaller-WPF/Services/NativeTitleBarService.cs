using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace PolarUninstallerWpf.Services;

/// <summary>Sets documented DWM caption colors without replacing the native frame.</summary>
public static class NativeTitleBarService
{
    private const int CaptionColor = 35, TextColor = 36;
    private const uint DefaultColor = 0xFFFFFFFF;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint value, int size);

    private static uint ColorRef(Color value) => (uint)(value.R | (value.G << 8) | (value.B << 16));

    public static bool Apply(Window window, Color background, Color foreground)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return false;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return false;
        try
        {
            uint caption = ColorRef(background), text = ColorRef(foreground);
            int captionResult = DwmSetWindowAttribute(handle, CaptionColor, ref caption, sizeof(uint));
            int textResult = DwmSetWindowAttribute(handle, TextColor, ref text, sizeof(uint));
            if (captionResult == 0 && textResult == 0) return true;
            uint reset = DefaultColor;
            DwmSetWindowAttribute(handle, CaptionColor, ref reset, sizeof(uint));
            DwmSetWindowAttribute(handle, TextColor, ref reset, sizeof(uint));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        return false;
    }
}
