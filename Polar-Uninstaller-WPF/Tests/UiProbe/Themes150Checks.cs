using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PolarUninstallerWpf;
using PolarUninstallerWpf.Services;
using Path = System.IO.Path;

internal static partial class Parts146Checks
{
    internal static int RunThemes150()
    {
        Setup("Themes150");
        var added = new Dictionary<string, string>
        {
            ["Emerald"] = "الزمردي", ["Purple"] = "البنفسجي", ["Crimson"] = "القرمزي", ["Teal"] = "الفيروزي"
        };
        string[] original = ["Midnight Champagne", "Blue", "Black", "White", "Beige", "Pure Black", "Espresso", "Rose Noir"];
        var settings = (SettingsService)Field(main, "settingsService")!;
        var picker = (ComboBox)main.FindName("ThemePicker");
        var host = (ContentControl)main.FindName("PageContent");
        var sidebar = (Border)main.FindName("Side");
        var corner = (System.Windows.Shapes.Rectangle)Field(main, "backgroundTopLeft")!;
        var log = (Control)main.FindName("Log");
        var content = (FrameworkElement)main.Content;
        var size = new Size(1320, 900);
        Set(main, "currentDirectory", root);

        static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);
        static Color BrushColor(Brush value) => ((SolidColorBrush)value).Color;
        Color Resource(string key) => BrushColor((Brush)app.Resources[key]);
        picker.SelectedItem = "Blue";
        var blueLighting = (RadialGradientBrush)corner.Fill;
        picker.SelectedItem = ThemeCatalog.DefaultTheme;
        void Layout()
        {
            content.Measure(size);
            content.Arrange(new Rect(size));
            content.UpdateLayout();
        }
        void SavePreview(string name)
        {
            var image = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
            image.Render(content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var output = File.Create(Path.Combine(root, name + ".png"));
            encoder.Save(output);
        }

        foreach (bool arabic in new[] { false, true })
        {
            string locale = arabic ? "ar" : "en";
            Set(main, "arabic", arabic);
            Set(main, "page", "home");
            Invoke(main, "Render", true, true);
            Invoke(main, "ToggleThemeGallery", main, new RoutedEventArgs());
            var gallery = (ThemeGalleryView)Field(main, "themeGallery")!;
            var cards = ((Panel)gallery.FindName("Cards")).Children.OfType<Button>().ToArray();
            Check(cards.Length == 12 && cards.Select(card => ((string)card.ToolTip).Split(" · ")[0])
                .SequenceEqual(original.Concat(added.Keys)), $"{locale}: gallery displays all 12 themes in order");
            Layout();
            var scroll = (ScrollViewer)gallery.FindName("GalleryScroll");
            scroll.ScrollToBottom();
            Layout();
            SavePreview($"Gallery-{locale}");
            foreach (var (name, arabicName) in added)
            {
                if (((FrameworkElement)main.FindName("ThemeGalleryHost")).Visibility != Visibility.Visible)
                    Invoke(main, "ToggleThemeGallery", main, new RoutedEventArgs());

                var palette = ThemeCatalog.All[name];
                var card = cards.Single(button => Equals(button.ToolTip, $"{name} · {arabicName}"));
                card.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(gallery.PendingTheme == name && !Equals(picker.SelectedItem, name), $"{locale}: {name} preview is selectable");
                ((Button)gallery.FindName("ApplyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump();
                Wait(settings.FlushAsync());
                Check(Equals(picker.SelectedItem, name) && new SettingsService(Path.Combine(root, "settings")).Load().Theme == name,
                    $"{locale}: {name} applies and persists to disk");
                var lighting = (RadialGradientBrush)corner.Fill;
                Check(BrushColor(sidebar.Background).A == 0
                    && lighting.Center == blueLighting.Center && lighting.GradientOrigin == blueLighting.GradientOrigin
                    && lighting.RadiusX == blueLighting.RadiusX && lighting.RadiusY == blueLighting.RadiusY
                    && lighting.GradientStops.Select(stop => (stop.Offset, stop.Color.A))
                        .SequenceEqual(blueLighting.GradientStops.Select(stop => (stop.Offset, stop.Color.A))),
                    $"{locale}: {name} corner lighting follows Blue's falloff across the sidebar");
                Check(BrushColor(log.Background) == Parse(palette.Background) && Resource("LogInfo") == Parse(palette.Dim)
                    && Resource("ChromeAccent") == Parse(palette.Accent)
                    && Resource("ChromeSecondaryAccent") == Parse(palette.HoverAccent!)
                    && CalmThemeBrushes.Contrast(Resource("ChromeAccentText"), Resource("AccentButtonBackground")) >= 4.5
                    && CalmThemeBrushes.Contrast(Resource("CalmHoverIcon"), Resource("CalmNavHoverFill")) >= 3,
                    $"{locale}: {name} applies log, accent and legible hover resources");

                foreach (string page in new[] { "home", "dashboard", "uninstall", "cleaner", "files", "whatsnew" })
                {
                    Set(main, "page", page);
                    Invoke(main, "Render", true, true);
                    Layout();
                    var oldView = host.Content;
                    picker.SelectedItem = ThemeCatalog.DefaultTheme;
                    picker.SelectedItem = name;
                    Layout();
                    Check(ReferenceEquals(oldView, host.Content) && host.Content is FrameworkElement element && element.ActualHeight > 0
                        && host.FlowDirection == (arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight)
                        && Resource("HomeTextBrush") == Parse(palette.Text),
                        $"{locale}: {name} updates {page} without replacing its layout");
                    if (page == "home") SavePreview($"{name}-{locale}");
                }
            }
        }
        return Finish("Themes150");
    }
}
