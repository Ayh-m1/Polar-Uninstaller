using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace PolarUninstallerWpf;

public partial class MainWindow
{
    private const int HitTest = 0x0084, NonClientMouseMove = 0x00A0, NonClientMouseLeave = 0x02A2;
    private const int ClientMouseMove = 0x0200, ActivateMessage = 0x0006, MaximizeHit = 9;
    private static readonly Geometry MaximizeGlyph = Geometry.Parse("M 0,0 H 10 V 10 H 0 Z");
    private static readonly Geometry RestoreGlyph = Geometry.Parse("M 0,3 H 7 V 10 H 0 Z M 3,3 V 0 H 10 V 7 H 7");
    private HwndSource? captionSource;
    private bool nativeMaximizeHover;

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseTracking
    {
        internal uint Size, Flags;
        internal IntPtr Window;
        internal uint HoverTime;
    }

    [DllImport("user32.dll")]
    private static extern bool TrackMouseEvent(ref MouseTracking tracking);

    private void InitializeCaption()
    {
        SourceInitialized += (_, _) =>
        {
            captionSource = PresentationSource.FromVisual(this) as HwndSource;
            captionSource?.AddHook(CaptionMessage);
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 36,
                ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false
            });
        };
        Closed += (_, _) =>
        {
            if (captionSource is { IsDisposed: false }) captionSource.RemoveHook(CaptionMessage);
            captionSource = null;
        };
        StateChanged += (_, _) => RefreshCaptionLabels();
    }

    private IntPtr CaptionMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == HitTest && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) &&
            CaptionMaximize.IsVisible && CaptionMaximize.IsEnabled &&
            ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip && WindowState != WindowState.Minimized)
        {
            long packed = lParam.ToInt64();
            var screen = new Point(unchecked((short)packed), unchecked((short)(packed >> 16)));
            var point = CaptionMaximize.PointFromScreen(screen);
            if (point.Y >= 1 && new Rect(CaptionMaximize.RenderSize).Contains(point))
            {
                handled = true;
                return new IntPtr(MaximizeHit);
            }
        }
        else if (message == NonClientMouseMove && wParam == new IntPtr(MaximizeHit))
        {
            if (!nativeMaximizeHover)
            {
                SetNativeCaptionHover(true);
                var tracking = new MouseTracking { Size = (uint)Marshal.SizeOf<MouseTracking>(), Flags = 0x12, Window = hwnd };
                TrackMouseEvent(ref tracking);
            }
        }
        else if (message is NonClientMouseMove or NonClientMouseLeave or ClientMouseMove ||
                 (message == ActivateMessage && (wParam.ToInt64() & 0xFFFF) == 0))
            SetNativeCaptionHover(false);
        return IntPtr.Zero;
    }

    private void SetNativeCaptionHover(bool active)
    {
        if (nativeMaximizeHover == active) return;
        nativeMaximizeHover = active;
        CaptionMaximize.Tag = active ? "NativeHover" : null;
    }

    private void RefreshCaptionLabels()
    {
        HeaderCommands.FlowDirection = arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        bool maximized = WindowState == WindowState.Maximized;
        CaptionMaximizeGlyph.Data = maximized ? RestoreGlyph : MaximizeGlyph;
        SetLabel(CaptionMenu, L("Window menu", "قائمة النافذة"));
        SetLabel(CaptionMinimize, L("Minimize", "تصغير"));
        SetLabel(CaptionMaximize, maximized ? L("Restore", "استعادة الحجم") : L("Maximize", "تكبير"));
        SetLabel(CaptionClose, L("Close", "إغلاق"));
        static void SetLabel(Button button, string label)
        {
            button.ToolTip = label;
            AutomationProperties.SetName(button, label);
        }
    }

    private void CaptionMenuClick(object sender, RoutedEventArgs e)
    {
        var screen = CaptionMenu.PointToScreen(new Point(0, CaptionMenu.ActualHeight));
        var target = PresentationSource.FromVisual(this)?.CompositionTarget;
        if (target != null) SystemCommands.ShowSystemMenu(this, target.TransformFromDevice.Transform(screen));
    }

    private void CaptionMinimizeClick(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void CaptionMaximizeClick(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }
    private void CaptionCloseClick(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);
}
