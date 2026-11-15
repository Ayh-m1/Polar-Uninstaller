using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PolarUninstallerWpf.Services;

namespace PolarUninstallerWpf;

public sealed class ReleaseNotesView : UserControl
{
    public ReleaseNotesView(bool arabic)
    {
        FlowDirection = arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Background = Brushes.Transparent;

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.Transparent
        };

        var panel = new StackPanel { Margin = new Thickness(4, 8, 4, 8) };

        var title = new TextBlock
        {
            Text = arabic ? "ما الجديد في Polar؟" : "What's New in Polar?",
            FontSize = 30,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "HomeTextBrush");
        panel.Children.Add(title);

        var subtitle = new TextBlock
        {
            Text = arabic ? "تعرّف على أهم الإضافات والتحسينات في هذا الإصدار." : "Discover the latest features, improvements, and fixes.",
            FontSize = 17,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        };
        subtitle.SetResourceReference(TextBlock.ForegroundProperty, "HomeDimBrush");
        panel.Children.Add(subtitle);

        // Current version indicator
        var versionBorder = new Border
        {
            Background = Brushes.Transparent,
            BorderBrush = (Brush)Application.Current.Resources["HomeBorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(0, 0, 0, 18),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var versionStack = new StackPanel();
        var versionLabel = new TextBlock
        {
            Text = arabic ? "الإصدار الحالي" : "Current Version",
            FontSize = 16,
            Margin = new Thickness(0, 0, 0, 2)
        };
        versionLabel.SetResourceReference(TextBlock.ForegroundProperty, "HomeDimBrush");
        versionStack.Children.Add(versionLabel);
        var versionValue = new TextBlock
        {
            Text = "v" + AppInfo.Version,
            FontSize = 22,
            FontWeight = FontWeights.Bold
        };
        versionValue.SetResourceReference(TextBlock.ForegroundProperty, "ChromeAccent");
        versionStack.Children.Add(versionValue);
        versionBorder.Child = versionStack;
        panel.Children.Add(versionBorder);

        // Only the installed version is shown. Older release notes remain in the
        // embedded data for version history, but they are never rendered here.
        var currentRelease = ReleaseNotesService.FindCurrent(ReleaseNotesService.Load(arabic), AppInfo.Version);
        if (currentRelease is null)
        {
            var empty = new TextBlock
            {
                Text = arabic
                    ? $"لا توجد ملاحظات إصدار متاحة للإصدار v{AppInfo.Version}."
                    : $"No release notes are available for v{AppInfo.Version}.",
                FontSize = 18,
                TextWrapping = TextWrapping.Wrap
            };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "HomeDimBrush");
            panel.Children.Add(empty);
            scroll.Content = panel;
            Content = scroll;
            return;
        }

        var versionHeading = new TextBlock
        {
            Text = arabic
                ? $"ما الجديد في v{currentRelease.Version}"
                : $"What's New in v{currentRelease.Version}",
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        versionHeading.SetResourceReference(TextBlock.ForegroundProperty, "HomeTextBrush");
        panel.Children.Add(versionHeading);

        // Show All Changes is provided by BuildReleaseContent and only ever
        // receives the currently installed version's entries.
        panel.Children.Add(BuildReleaseContent(currentRelease, arabic));

        scroll.Content = panel;
        Content = scroll;
    }

    private static FrameworkElement BuildReleaseContent(ReleaseNote release, bool arabic)
    {
        var container = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };

        var columns = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 6)
        };
        columns.SizeChanged += (_, _) => BalanceColumns(columns);

        AddCategory(columns, arabic ? "إضافات جديدة" : "New Features", release.DisplayFeatures, "NotesFeatureBrush");
        AddCategory(columns, arabic ? "تحسينات" : "Improvements", release.DisplayImprovements, "ChromeAccent");
        AddCategory(columns, arabic ? "إصلاحات" : "Bug Fixes", release.DisplayFixes, "NotesFixBrush");

        if (columns.Children.Count > 0)
            container.Children.Add(columns);

        // Show all changes
        if (release.Entries.Count > 0)
        {
            var showAll = new Expander
            {
                Header = arabic ? "عرض جميع التغييرات" : "Show All Changes",
                IsExpanded = false,
                Background = Brushes.Transparent,
                BorderBrush = (Brush)Application.Current.Resources["HomeBorderBrush"],
                BorderThickness = new Thickness(0, 1, 0, 0),
                Margin = new Thickness(0, 6, 0, 4),
                Padding = new Thickness(0, 6, 0, 6),
                Foreground = (Brush)Application.Current.Resources["HomeTextBrush"]
            };
            var allList = new StackPanel { Margin = new Thickness(0, 8, 0, 4) };
            foreach (var entry in release.Entries)
                allList.Children.Add(MakeBullet(entry, 18, (Brush)Application.Current.Resources["HomeDimBrush"]));
            showAll.Content = allList;
            container.Children.Add(showAll);
        }

        return container;
    }

    private static void AddCategory(Panel parent, string title, IReadOnlyList<string> entries, string headingResource)
    {
        if (entries.Count == 0) return;
        var column = new StackPanel
        {
            Margin = new Thickness(0, 0, 18, 12),
            MinWidth = 260,
            MaxWidth = 420
        };
        var heading = new TextBlock
        {
            Text = title,
            FontSize = 21,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = (Brush)Application.Current.Resources[headingResource]
        };
        heading.SetResourceReference(TextBlock.ForegroundProperty,headingResource);
        column.Children.Add(heading);
        foreach (var entry in entries)
            column.Children.Add(MakeBullet(entry, 18, (Brush)Application.Current.Resources["HomeDimBrush"]));
        parent.Children.Add(column);
    }

    private static void BalanceColumns(WrapPanel panel)
    {
        var children = panel.Children.OfType<FrameworkElement>().ToList();
        if (children.Count == 0) return;
        double available = panel.ActualWidth;
        if (available <= 0) return;
        double width = Math.Max(260, Math.Min(420, available / children.Count - 18));
        foreach (var child in children) child.Width = width;
    }

    private static TextBlock MakeBullet(string text, double size, Brush foreground)
    {
        return new TextBlock
        {
            Text = "•  " + ReleaseNotesService.DisplayEntry(text),
            FontSize = size,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
            LineHeight = size + 8,
            Foreground = foreground
        };
    }
}
