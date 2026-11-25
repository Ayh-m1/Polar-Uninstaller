using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using PolarUninstallerWpf;
using PolarUninstallerWpf.Services;

internal static partial class Parts146Checks
{
    internal static int RunCaption152()
    {
        Setup("Caption152");
        var root = (FrameworkElement)main.Content;
        var header = (Grid)main.FindName("Header");
        var dock = (FrameworkElement)main.FindName("Root");
        var picker = (ComboBox)main.FindName("ThemePicker");
        var commands = (StackPanel)main.FindName("HeaderCommands");
        var maximize = (Button)main.FindName("CaptionMaximize");
        var background = (FrameworkElement)Field(main, "backgroundBase")!;
        var handle = new WindowInteropHelper(main).EnsureHandle();
        var chrome = WindowChrome.GetWindowChrome(main);
        Check(handle != IntPtr.Zero && chrome is { CaptionHeight: 36, UseAeroCaptionButtons: false }
            && chrome.ResizeBorderThickness.Left > 0 && main.ShowInTaskbar && !main.AllowsTransparency,
            "WindowChrome attaches to a native resizable window without a layered window");
        Check(new[] { "CaptionMinimize", "CaptionMaximize", "CaptionClose", "ThemeButton", "UpdateButton", "LangButton" }
            .Select(name => (Button)main.FindName(name)).All(WindowChrome.GetIsHitTestVisibleInChrome),
            "Window controls and existing header actions are excluded from caption dragging");
        var size = new Size(850, 620);
        foreach (bool rtl in new[] { false, true })
        {
            Set(main, "arabic", rtl);
            Set(main, "page", "cleaner");
            Invoke(main, "Render", true, true);
            root.Measure(size);
            root.Arrange(new Rect(size));
            root.UpdateLayout();
            Check(dock.TransformToAncestor(root).Transform(new Point()).Y == header.ActualHeight &&
                commands.FlowDirection == (rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight),
                $"{rtl}: unified header has no second header row and language controls follow direction");
            var commandBounds = commands.TransformToAncestor(header).TransformBounds(new Rect(commands.RenderSize));
            var maxBounds = maximize.TransformToAncestor(header).TransformBounds(new Rect(maximize.RenderSize));
            Check(commandBounds.Right < maxBounds.Left && maxBounds.Right <= header.ActualWidth,
                $"{rtl}: header actions and caption buttons fit at minimum width");
            var checks = FindChecks((DependencyObject)((ContentControl)main.FindName("PageContent")).Content).ToArray();
            Check(checks.Length == 6 && checks.Count(check => check.IsChecked == true) == 2,
                $"{rtl}: six cleanup options fit the existing layout with safe defaults");
            var backgroundBounds = background.TransformToAncestor(root).TransformBounds(new Rect(background.RenderSize));
            var headerBounds = header.TransformToAncestor(root).TransformBounds(new Rect(header.RenderSize));
            Check(header.Background is SolidColorBrush { Color.A: 0 } && backgroundBounds.Contains(headerBounds),
                $"{rtl}: the theme background covers the title bar under its transparent surface");
            Check(AutomationProperties.GetName(maximize) == (rtl ? "تكبير" : "Maximize"),
                $"{rtl}: maximize control has a localized accessible name");
        }
        foreach (var (theme, palette) in ThemeCatalog.All)
        {
            picker.SelectedItem = theme;
            Check(maximize.Foreground is SolidColorBrush brush && brush.Color == (Color)ColorConverter.ConvertFromString(palette.Text),
                $"{theme}: caption text follows the live theme");
        }
        return Finish("Caption152");

        static IEnumerable<CheckBox> FindChecks(DependencyObject node)
        {
            if (node is CheckBox check) yield return check;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                foreach (var child in FindChecks(VisualTreeHelper.GetChild(node, i))) yield return child;
        }
    }
}
