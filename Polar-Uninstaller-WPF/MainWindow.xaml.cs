using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using System.IO;
using System.Diagnostics;
using PolarUninstallerWpf.Services;

namespace PolarUninstallerWpf;

public partial class MainWindow : Window
{
    private readonly string[] keys = ["home", "uninstall", "cleaner", "dashboard", "files", "whatsnew"];
    private readonly Dictionary<string, (string En, string Ar)> labels = new()
    {
        ["home"] = ("Home", "الرئيسية"), ["uninstall"] = ("Deep Uninstaller", "الإزالة العميقة"),
        ["cleaner"] = ("Quick Cleaner", "التنظيف السريع"), ["dashboard"] = ("Dashboard", "لوحة المعلومات"),
        ["files"] = ("File Browser", "متصفح الملفات"), ["whatsnew"] = ("What's New", "ما الجديد")
    };
    private readonly IReadOnlyDictionary<string,ThemePalette> palettes=ThemeCatalog.All;
    private string page = "home";
    private bool arabic, collapsed, ready;
    private ThemePalette palette;
    private ThemeGalleryView? themeGallery;
    private readonly System.Windows.Shapes.Rectangle backgroundBase = new() { IsHitTestVisible = false };
    private readonly System.Windows.Shapes.Rectangle backgroundTopLeft = new() { IsHitTestVisible = false };
    private readonly System.Windows.Shapes.Rectangle backgroundCenter = new() { IsHitTestVisible = false };
    private readonly System.Windows.Shapes.Rectangle backgroundBottomRight = new() { IsHitTestVisible = false };
    private readonly WindowsInventoryService inventory = new();
    private readonly ApplicationInventoryService applicationInventory = new();
    private readonly FileBrowserService browser = new();
    private readonly SettingsService settingsService;
    private readonly ActivityMetricsService metrics;
    private string currentDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private string? pendingFileReveal;
    private FileBrowserView? fileBrowserPage;
    private CancellationTokenSource? pageLoad;
    private bool cleanerOperationActive;
    private readonly HashSet<TempCleanerService.Category> cleanerCategories = new();
    private readonly ActivityLogService activity;
    private bool followLog = true, settingsErrorReported, logCollapsed;
    private readonly WpfUpdateService updates = new();
    private UpdateResult updateResult = new(UpdateState.Checking);
    private UpdateCenterWindow? updateWindow;
    private bool autoUpdatePromptShown;

    public MainWindow():this(new SettingsService(),ActivityMetricsService.Instance) { }
    internal MainWindow(SettingsService settingsService,ActivityMetricsService metrics)
    {
        this.settingsService=settingsService;this.metrics=metrics;
        InitializeComponent();
        LogClearButton.Background = Brushes.Transparent; LogClearButton.BorderBrush = Brushes.Transparent;
        LogToggleButton.Background = Brushes.Transparent; LogToggleButton.BorderBrush = Brushes.Transparent;
        Log.ItemContainerStyle = (Style)Application.Current.Resources["LogEntryContainer"];
        activity = new ActivityLogService(Dispatcher);
        applicationInventory.Diagnostic += message => activity.Write("INFO", message);
        metrics.Record(true);
        PreviewMouseDown += (_, _) => metrics.Record(false);
        Log.ItemsSource = activity.Entries;
        Log.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, e) =>
        {
            if (e.ExtentHeightChange == 0 && e.OriginalSource is ScrollViewer scroller)
                followLog = scroller.VerticalOffset >= scroller.ScrollableHeight - 2;
        }));
        activity.BatchAdded += entry => { if (followLog && !logCollapsed) Log.ScrollIntoView(entry); };
        var settings = settingsService.Load();
        var selectedTheme = ThemeCatalog.Resolve(settings.Theme);
        palette = palettes[selectedTheme];
        arabic = settings.Arabic;
        collapsed = settings.SidebarCollapsed;
        foreach (var category in CleanerSelection.Parse(settings.CleanerCategories)) cleanerCategories.Add(category);
        ThemePicker.ItemsSource = palettes.Keys;
        ThemePicker.SelectedItem = selectedTheme;
        ready = true;
        SourceInitialized += (_, _) => ApplyNativeTitleBar();
        var backgroundLayer = new Grid { IsHitTestVisible = false };
        Grid.SetColumnSpan(backgroundLayer, 2);
        backgroundLayer.Children.Add(backgroundBase);
        backgroundLayer.Children.Add(backgroundCenter);
        backgroundLayer.Children.Add(backgroundTopLeft);
        backgroundLayer.Children.Add(backgroundBottomRight);
        Root.Children.Insert(0, backgroundLayer);
        Render();
        ContentRendered += async (_, _) => await StartupUpdateCheckAsync();
        activity.Write("INFO", AppInfo.Name+" v"+AppInfo.Version+" started.");
        if (settingsService.LoadError is string loadError) activity.Write("WARNING", "Settings could not be loaded: " + loadError);
        Closing += (_, _) => activity.Write("INFO", "Application shutting down.");
    }

    private string L(string en, string ar) => arabic ? ar : en;
    private static Brush B(string color) => (Brush)new BrushConverter().ConvertFromString(color)!;
    private static Color ContrastText(Color accent) =>
        (0.299 * accent.R + 0.587 * accent.G + 0.114 * accent.B) / 255.0 > 0.62
            ? Color.FromRgb(0x12, 0x18, 0x1F)
            : Colors.White;
    private void Render(bool rebuildPage=true, bool? rebuildNavigation=null)
    {
        if(rebuildPage)pageLoad?.Cancel();
        var bg = B(palette.Background); var text = B(palette.Text); var dim = B(palette.Dim); var accent = B(palette.Accent);
        Background=bg;
        bool isNavy=ThemePicker.SelectedItem as string==ThemeCatalog.DefaultTheme;
        var cardColor = (Color)ColorConverter.ConvertFromString(palette.Card); cardColor.A = (byte)(isNavy?255:185);
        var borderColor = (Color)ColorConverter.ConvertFromString(palette.Border); borderColor.A = (byte)120;
        var card = new SolidColorBrush(cardColor); var border = new SolidColorBrush(borderColor);
        var resources = Application.Current.Resources;
        BrandingAssets.Apply(resources,ThemePicker.SelectedItem as string??ThemeCatalog.DefaultTheme);
        DialogThemeResources.Apply(resources,palette.Background,palette.Card,palette.Border,palette.Accent,palette.Text,palette.Dim);
        bool isBeige = ThemePicker.SelectedItem as string == "Beige";
        resources["InterfaceFont"] = resources[arabic ? "TajawalFont" : "InterFont"];
        SetThemeBrush(resources,"HomeCardBrush",card); SetThemeBrush(resources,"HomeBorderBrush",border);
        SetThemeBrush(resources,"HomeTextBrush",text); SetThemeBrush(resources,"HomeDimBrush",dim); SetThemeBrush(resources,"HomeAccentBrush",accent);
        SetThemeBrush(resources,"ChromeBackground",card); SetThemeBrush(resources,"ChromeBorder",border);
        SetThemeBrush(resources,"ChromeText",text); SetThemeBrush(resources,"ChromeHover",B(palette.Glow)); SetThemeBrush(resources,"ChromeAccent",accent);
        SetThemeBrush(resources,"ChromeAccentText",new SolidColorBrush(ContrastText((Color)ColorConverter.ConvertFromString(palette.Accent))));
        SetThemeBrush(resources,"SidebarSelected",card); SetThemeBrush(resources,"SidebarHover",border);
        SetThemeBrush(resources,"SidebarSelectedHover",border);
        SetThemeBrush(resources,"SidebarActiveText",text);
        SetThemeBrush(resources,"ChromeSecondaryBackground",B(palette.SecondaryBackground??palette.Background));
        SetThemeBrush(resources,"ChromeSecondaryAccent",B(palette.SecondaryAccent??palette.Accent));
        var hoverHome = (Color)ColorConverter.ConvertFromString("#EBD6B9"); hoverHome.A = 210;
        SetThemeBrush(resources,"HomeHoverBrush",isBeige ? new SolidColorBrush(hoverHome) : card);
        if (isBeige)
        {
            var hoverCard = (Color)ColorConverter.ConvertFromString("#EBD7BC"); hoverCard.A = 200;
            var selectedCard = (Color)ColorConverter.ConvertFromString("#F0DFCA"); selectedCard.A = 215;
            var hoverBorder = (Color)ColorConverter.ConvertFromString("#DBC0A0"); hoverBorder.A = 170;
            SetThemeBrush(resources,"ChromeHover",new SolidColorBrush(hoverCard));
            SetThemeBrush(resources,"SidebarSelected",new SolidColorBrush(selectedCard));
            SetThemeBrush(resources,"SidebarHover",new SolidColorBrush(hoverBorder));
        }
        resources["LogoGlowColor"] = (Color)ColorConverter.ConvertFromString(isBeige ? "#C6A36D" : palette.Accent);
        SetThemeBrush(resources,"LogInfo",dim); SetThemeBrush(resources,"LogSuccess",B(palette.Accent));
        SetThemeBrush(resources,"LogWarning",B(ThemePicker.SelectedItem as string is "White" or "Beige" ? "#956014" : "#F0C274"));
        SetThemeBrush(resources,"LogError",B(ThemePicker.SelectedItem as string is "White" or "Beige" ? "#A22C2C" : "#FF8B88"));
        ApplyNativeTitleBar();
        foreach(Window child in Application.Current.Windows)
            if(!ReferenceEquals(child,this))NativeTitleBarService.Apply(child,(Color)ColorConverter.ConvertFromString(palette.Card),(Color)ColorConverter.ConvertFromString(palette.Text));
        Root.Background = Brushes.Transparent;
        // All themes share the same window-wide lighting layer. An opaque
        // sidebar would cut the corner gradient into a separate rectangle.
        Side.Background = Brushes.Transparent;
        backgroundBase.Fill = B(palette.Background);
        if ((ThemePicker.SelectedItem as string) == "Black")
            backgroundBase.Fill = B("#050505");
        if ((ThemePicker.SelectedItem as string) == "Beige")
            backgroundBase.Fill = B("#E5D1B4");

        string glowHexTop;
        string glowHexBottom;
        byte glowAlphaTop;
        byte glowAlphaBottom;
        if ((ThemePicker.SelectedItem as string) == "Beige")
        {
            glowHexTop = "#F4E8D7";
            glowHexBottom = "#C8A884";
            glowAlphaTop = 44;
            glowAlphaBottom = 34;
        }
        else if ((ThemePicker.SelectedItem as string) == "Black")
        {
            glowHexTop = "#FFFFFF";
            glowHexBottom = "#FFFFFF";
            glowAlphaTop = 46;
            glowAlphaBottom = 46;
        }
        else
        {
            glowHexTop = isNavy?palette.Glow:palette.Accent;
            glowHexBottom = isNavy?palette.Glow:palette.Accent;
            glowAlphaTop = (byte)(isNavy?24:40);
            glowAlphaBottom = (byte)(isNavy?18:40);
        }
        Color glowColorTop = (Color)ColorConverter.ConvertFromString(glowHexTop);
        Color glowColorBottom = (Color)ColorConverter.ConvertFromString(glowHexBottom);

        RadialGradientBrush MakeGlow(Color color, byte alpha, Point center)
        {
            return new RadialGradientBrush
            {
                Center = center,
                GradientOrigin = center,
                RadiusX = 0.42,
                RadiusY = 0.42,
                GradientStops = new GradientStopCollection
                {
                    new(Color.FromArgb(alpha, color.R, color.G, color.B), 0.0),
                    new(Color.FromArgb((byte)(alpha / 2), color.R, color.G, color.B), 0.35),
                    new(Color.FromArgb(0, color.R, color.G, color.B), 1.0)
                }
            };
        }

        backgroundTopLeft.Fill = isNavy?ChampagneLighting.UpperLeft:MakeGlow(glowColorTop, glowAlphaTop, new Point(0, 0));
        backgroundCenter.Fill=isNavy?ChampagneLighting.Center:Brushes.Transparent;
        backgroundBottomRight.Fill = isNavy?ChampagneLighting.LowerRight:MakeGlow(glowColorBottom, glowAlphaBottom, new Point(1, 1));
        if((ThemePicker.SelectedItem as string)=="Pure Black")
        {
            backgroundTopLeft.Fill=backgroundBottomRight.Fill=Brushes.Transparent;
            resources["LogoGlowOpacity"]=0d;
            SetThemeBrush(resources,"HomeCardBrush",B(palette.Card));
            SetThemeBrush(resources,"HomeHoverBrush",B("#111111"));
            SetThemeBrush(resources,"ChromeBackground",B(palette.Card));
            SetThemeBrush(resources,"ChromeBorder",B("#727272"));
            SetThemeBrush(resources,"SidebarSelected",B("#151515"));
            SetThemeBrush(resources,"SidebarHover",B("#101010"));
            SetThemeBrush(resources,"LogWarning",B("#CECECE"));SetThemeBrush(resources,"LogError",B("#FFFFFF"));
            SetThemeBrush(resources,"AccentButtonBackground",B("#111111"));SetThemeBrush(resources,"ChromeAccentText",B("#FFFFFF"));
            SetThemeBrush(resources,"NotesFeatureBrush",B("#F4F4F4"));SetThemeBrush(resources,"NotesFixBrush",B("#CECECE"));
        }
        else
        {
            resources["LogoGlowOpacity"]=.65d;
            SetThemeBrush(resources,"AccentButtonBackground",accent);
            SetThemeBrush(resources,"NotesFeatureBrush",B((ThemePicker.SelectedItem as string)=="Rose Noir"?palette.Accent:"#7FD6A3"));
            SetThemeBrush(resources,"NotesFixBrush",B((ThemePicker.SelectedItem as string)=="Rose Noir"?"#EBAAC8":"#F08B88"));
            if((ThemePicker.SelectedItem as string)=="Rose Noir")
            {
                backgroundTopLeft.Fill=MakeGlow((Color)ColorConverter.ConvertFromString(palette.Accent),88,new Point(0,0));
                backgroundBottomRight.Fill=MakeGlow((Color)ColorConverter.ConvertFromString(palette.Accent),68,new Point(1,1));
                SetThemeBrush(resources,"SidebarSelected",B("#371527"));
                SetThemeBrush(resources,"HomeHoverBrush",B("#382034"));
            }
        }
        SetThemeBrush(resources,"SidebarSelectedHover",(Brush)resources["SidebarHover"]);
        if(isNavy)
        {
            // Static native brushes: WPF stretches them during resize without
            // image generation, new timers or size-dependent callbacks.
            backgroundBase.Fill=ChampagneLighting.Background;
            backgroundTopLeft.Fill=ChampagneLighting.UpperLeft;
            backgroundCenter.Fill=ChampagneLighting.Center;
            backgroundBottomRight.Fill=ChampagneLighting.LowerRight;
            SetThemeBrush(resources,"SidebarSelected",accent);
            SetThemeBrush(resources,"SidebarSelectedHover",B(palette.SecondaryAccent!));
            SetThemeBrush(resources,"SidebarActiveText",B(palette.Sidebar!));
            SetThemeBrush(resources,"SidebarHover",B(palette.Hover!));
            SetThemeBrush(resources,"ChromeHover",B(palette.Hover!));
            SetThemeBrush(resources,"HomeHoverBrush",B(palette.Hover!));
            SetThemeBrush(resources,"NotesFeatureBrush",accent);
            SetThemeBrush(resources,"NotesFixBrush",B(palette.SecondaryAccent!));
            resources["LogoGlowOpacity"]=.20d;
        }
        Side.FlowDirection = arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Side.BorderBrush = border; Brand.Foreground = text; PreviewLabel.Foreground = dim;
        Brand.Text = "Polar Uninstaller"; Brand.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        Toggle.Content = collapsed ? (arabic ? "‹" : "›") : (arabic ? "›" : "‹");
        Toggle.ToolTip = collapsed ? L("Expand sidebar", "توسيع الشريط") : L("Collapse sidebar", "طي الشريط");
        PreviewLabel.Text="v"+AppInfo.Version+" · "+AppInfo.Publisher; PreviewLabel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        LangButton.Content = arabic ? "English" : "العربية";
        ThemeButton.Content = L("◉  Theme  ▾", "◉  المظهر  ▾");
        LogTitle.Text = L("Activity Log", "سجل النشاط");
        LogClearButton.Content = L("Clear", "مسح");
        LogToggleButton.ToolTip = logCollapsed ? L("Expand log", "توسيع السجل") : L("Collapse log", "طي السجل");
        UpdateText.Text = UpdateStatusText(updateResult);
        UpdateText.Foreground = accent; LogTitle.Foreground = accent; LogBorder.Background = card; LogBorder.BorderBrush = border;
        Log.Background = bg; Log.Foreground = dim; PageContent.FlowDirection = arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        if(rebuildNavigation ?? rebuildPage)
        {
        Root.ColumnDefinitions.Clear();
        if (arabic) { Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); Root.ColumnDefinitions.Add(SideColumn); Grid.SetColumn(Side, 1); Grid.SetColumn(ContentArea, 0); }
        else { Root.ColumnDefinitions.Add(SideColumn); Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); Grid.SetColumn(Side, 0); Grid.SetColumn(ContentArea, 1); }
        SideColumn.Width = new GridLength(collapsed ? 100 : 244);
        Nav.Children.Clear();
        for (int i = 0; i < keys.Length; i++)
        {
            string key = keys[i];
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            var navForeground=(Brush)resources[key==page?"SidebarActiveText":"HomeDimBrush"];
            var navigationIcon=SidebarIcons.Create(key,navForeground,20);
            navigationIcon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,key==page?"SidebarActiveText":"HomeDimBrush");
            content.Children.Add(navigationIcon);
            if (!collapsed) content.Children.Add(new TextBlock { Text = L(labels[key].En, labels[key].Ar), FontSize = 14, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            // Tag is object-typed; match the string values in the XAML triggers
            // explicitly so selected cream surfaces do not keep a dark background.
            var button = new Button { Style = (Style)resources["SidebarNavButton"], Content = content, Tag = (key == page).ToString(), HorizontalContentAlignment = collapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left, Height = 48, Margin = new Thickness(0, 3, 0, 3), Padding = new Thickness(12, 0, 12, 0), Foreground = navForeground };
            button.SetResourceReference(Control.ForegroundProperty,key==page?"SidebarActiveText":"HomeDimBrush");
            button.Click += (_, _) => NavigateTo(key);
            button.ToolTip = L(labels[key].En, labels[key].Ar); Nav.Children.Add(button);
        }
        CommunityNav.Children.Clear();
        AddCommunityButton("discord", L("Discord", "ديسكورد"), () => OpenCommunityLink("https://discord.gg/plr"));
        AddCommunityButton("support", L("Support & Donate", "الدعم والتبرع"), () => OpenCommunityLink("https://polarwolves.com/supportus/p1682342912"));
        AddCommunityButton("feedback", L("Feedback & Reports", "الملاحظات والبلاغات"), OpenFeedback);
        }
        if(rebuildPage) PageContent.Content = BuildPage();
    }

    private void AddCommunityButton(string key, string label, Action action)    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        if (key == "feedback")
            content.Children.Add(new TextBlock { Text = "\uE715", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 17, Width = 20, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        else
        {
            var icon=SidebarIcons.Create(key,(Brush)Application.Current.Resources["HomeDimBrush"],20);
            icon.SetResourceReference(icon.Stroke==null?System.Windows.Shapes.Shape.FillProperty:System.Windows.Shapes.Shape.StrokeProperty,"HomeDimBrush");
            content.Children.Add(icon);
        }
        if (!collapsed) content.Children.Add(new TextBlock { Text = label, FontSize = 13, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Style = (Style)Application.Current.Resources["SidebarNavButton"], Content = content, ToolTip = label, Height = 42, Padding = new Thickness(12, 0, 12, 0), Foreground = (Brush)Application.Current.Resources["HomeDimBrush"], HorizontalContentAlignment = collapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left };
        button.SetResourceReference(Control.ForegroundProperty,"HomeDimBrush");
        button.Click += (_, _) => action();
        CommunityNav.Children.Add(button);
    }

    private void OpenCommunityLink(string url)
    {
        try
        {
            // Ask the interactive Windows shell to open the HTTPS URL with its default handler.
            var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            start.ArgumentList.Add(url);
            Process.Start(start);
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or System.Security.SecurityException)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception fallbackError) when (fallbackError is System.ComponentModel.Win32Exception or InvalidOperationException or System.Security.SecurityException)
            {
                activity.Write("WARNING", "Could not open the community link.");
                MessageBox.Show(this, L("Could not open the link in your browser. You can open it manually:\n", "تعذر فتح الرابط في المتصفح. يمكنك فتحه يدويًا:\n") + url, "Polar Uninstaller", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void OpenFeedback()
    {
        activity.Write("INFO", "Feedback window opened.");
        var dialog = new FeedbackWindow(arabic, activity) { Owner = this, Icon = Icon };
        dialog.ShowDialog();
    }
    private string UpdateStatusText(UpdateResult result) => result.State switch
    {
        UpdateState.Checking => L("◌  Checking…", "◌  جارٍ التحقق…"),
        UpdateState.Available => L($"↓  Update available · {result.Release?.Version}", $"↓  تحديث متاح · {result.Release?.Version}"),
        UpdateState.UpToDate => L("✓  Up to date", "✓  أحدث إصدار"),
        UpdateState.NoCompatibleRelease => L("!  No compatible update", "!  لا يوجد تحديث متوافق"),
        _ => L("⚠  Check failed", "⚠  تعذر التحقق")
    };
    private async Task CheckUpdates(bool force = false)
    {
        updateResult = new(UpdateState.Checking); UpdateText.Text = UpdateStatusText(updateResult); updateWindow?.ShowResult(updateResult);
        activity.Write("INFO", "Checking compatible Polar releases.");
        updateResult = await updates.CheckAsync(force);
        UpdateText.Text = UpdateStatusText(updateResult); updateWindow?.ShowResult(updateResult);
        activity.Write(updateResult.State == UpdateState.Failed ? "WARNING" : "INFO", "Update check: " + updateResult.State + (updateResult.Error == null ? "" : " (" + updateResult.Error + ")"));
    }
    private async Task StartupUpdateCheckAsync()
    {
        await CheckUpdates();
        if (updateResult.State == UpdateState.Available && !autoUpdatePromptShown)
        {
            // The automatic notification appears at most once per session.
            // Choosing Update Later or closing the window will not make it
            // reappear until the next launch.
            autoUpdatePromptShown = true;
            activity.Write("INFO", "New version " + updateResult.Release?.Version + " available; opening the update window.");
            ShowUpdateWindow();
        }
    }
    private async void OpenUpdateCenter(object sender, RoutedEventArgs e)
    {
        if (updateResult.State is UpdateState.UpToDate or UpdateState.Checking) return;
        if (updateResult.State != UpdateState.Available)
        {
            await CheckUpdates(true);
            if (updateResult.State != UpdateState.Available) return;
        }
        ShowUpdateWindow();
    }
    private void ShowUpdateWindow()
    {
        if (updateWindow != null) { updateWindow.Activate(); return; }
        updateWindow = new UpdateCenterWindow(arabic, updateResult) { Owner = this, Icon = Icon };
        updateWindow.Closed += (_, _) => updateWindow = null;
        updateWindow.Show();
        updateWindow.Activate();
    }
    private UIElement BuildPage()
    {
        if (page == "home")
        {
            var home = new HomeView(arabic, applicationInventory);
            home.NavigationRequested += NavigateTo;
            return home;
        }
        if (page == "whatsnew") return new ReleaseNotesView(arabic);
        if (page == "uninstall") return new DeepUninstallerView(arabic, activity, applicationInventory);
        if (page == "dashboard")
        {
            var dashboard=new DashboardView(arabic,activity,OpenStorageItem,applicationInventory);
            dashboard.NavigationRequested+=NavigateTo;return dashboard;
        }
        if (page == "files")
        {
            string? reveal = pendingFileReveal;
            pendingFileReveal = null;
            if(fileBrowserPage==null)
            {
                fileBrowserPage=new FileBrowserView(arabic,currentDirectory,activity,reveal,settingsService,applicationInventory);
                fileBrowserPage.LocationChanged+=path=>currentDirectory=path;
            }
            else
            {
                fileBrowserPage.SetLanguage(arabic);
                if(reveal!=null||!string.Equals(fileBrowserPage.CurrentDirectory,currentDirectory,StringComparison.OrdinalIgnoreCase))
                    _=fileBrowserPage.ShowFolderAsync(currentDirectory,reveal);
            }
            return fileBrowserPage;
        }
        var body = new Grid(); body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = new StackPanel { Margin = new Thickness(0, 6, 0, 16) };
        header.Children.Add(Text(L(labels[page].En, labels[page].Ar), 25, palette.Text, true));

        body.Children.Add(header);
        UIElement content = Cleaner();
        Grid.SetRow(content, 1); body.Children.Add(content); return body;
    }
    private TextBlock Text(string value, double size, string color, bool bold = false)
    {
        var block=new TextBlock{Text=value,FontSize=size,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,TextWrapping=TextWrapping.Wrap};
        block.SetResourceReference(TextBlock.ForegroundProperty,color==palette.Accent?"ChromeAccent":color==palette.Dim?"HomeDimBrush":"HomeTextBrush");
        return block;
    }
    private Border Card(UIElement content) => new() { Background = B(palette.Card), BorderBrush = B(palette.Border), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(18), Margin = new Thickness(5), Child = content };
    private UIElement Items(string kind)
    {
        pageLoad?.Cancel();
        var source = pageLoad = new CancellationTokenSource();
        var panel = new DockPanel();
        var input = new TextBox { Text = kind == "files" ? currentDirectory : "", Margin = new Thickness(5, 0, 5, 8), Padding = new Thickness(8), Background = B(palette.Card), Foreground = B(palette.Text), BorderBrush = B(palette.Border), ToolTip = kind == "files" ? L("Enter a folder path", "أدخل مسار مجلد") : L("Filter applications", "تصفية البرامج") };
        DockPanel.SetDock(input, Dock.Top); panel.Children.Add(input);
        var status = Text(L("Loading…", "جارٍ التحميل…"), 12, palette.Dim); DockPanel.SetDock(status, Dock.Top); panel.Children.Add(status);
        var list = new ListBox { Background = B(palette.Card), Foreground = B(palette.Text), BorderBrush = B(palette.Border), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        VirtualizingPanel.SetIsVirtualizing(list, true); VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling); ScrollViewer.SetCanContentScroll(list, true); panel.Children.Add(list);
        if (kind == "uninstall")
        {
            IReadOnlyList<InstalledProgram> items = Array.Empty<InstalledProgram>();
            void Filter() => list.ItemsSource = items.Where(x => x.Name.Contains(input.Text, StringComparison.CurrentCultureIgnoreCase)).Select(x => $"{x.Name}    {x.Version}    {x.Publisher}").ToArray();
            input.TextChanged += (_, _) => Filter();
            async void Load()
            {
                activity.Write("INFO", "Reading registered Windows applications.");
                try { items = await inventory.GetInstalledAsync(source.Token); if (source.IsCancellationRequested) return; Filter(); status.Text = L($"{items.Count} registered applications · uninstall is not implemented", $"{items.Count} برنامج مسجل · الإزالة غير متاحة بعد"); activity.Write("SUCCESS", $"Found {items.Count} registered applications."); }
                catch (OperationCanceledException) { }
                catch (Exception error) { status.Text = L("Could not read applications: ", "تعذرت قراءة البرامج: ") + error.Message; activity.Write("ERROR", "Application inventory failed: " + error.Message); }
            }
            Load();
        }
        else
        {
            IReadOnlyList<BrowserEntry> entries = Array.Empty<BrowserEntry>();
            int navigationGeneration = 0;
            async void Navigate(string path)
            {
                int request = ++navigationGeneration;
                try { status.Text = L("Loading…", "جارٍ التحميل…"); var result = await browser.ListAsync(path, source.Token); if (source.IsCancellationRequested || request != navigationGeneration) return; entries = result; currentDirectory = path; input.Text = path; list.ItemsSource = entries.Select(x => (x.IsDirectory ? "📁  " : "📄  ") + x.Name + (x.Size is long size ? $"    {size:N0} B" : "")).ToArray(); status.Text = L($"{entries.Count} items · double-click a folder to open", $"{entries.Count} عنصر · انقر مرتين لفتح مجلد"); activity.Write("INFO", $"Opened folder: {path}"); }
                catch (OperationCanceledException) { }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException) { if (!source.IsCancellationRequested && request == navigationGeneration) { status.Text = L("Cannot open folder: ", "تعذر فتح المجلد: ") + error.Message; activity.Write("WARNING", "Folder could not be opened: " + error.Message); } }
            }
            input.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Navigate(input.Text); };
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5, 0, 5, 8) };
            void AddAction(string label, Action action) { var button = new Button { Content = label, Style = (Style)Application.Current.Resources["ChromeButton"], Margin = new Thickness(0, 0, 6, 0) }; button.Click += (_, _) => action(); toolbar.Children.Add(button); }
            AddAction(L("Up", "للأعلى"), () => { var parent = Directory.GetParent(currentDirectory); if (parent != null) Navigate(parent.FullName); });
            AddAction(L("Refresh", "تحديث"), () => Navigate(currentDirectory));
            AddAction(L("Desktop", "سطح المكتب"), () => Navigate(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)));
            AddAction(L("Documents", "المستندات"), () => Navigate(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)));
            DockPanel.SetDock(toolbar, Dock.Top); panel.Children.Insert(0, toolbar);
            list.MouseDoubleClick += (_, _) => { var index = list.SelectedIndex; if (index >= 0 && index < entries.Count && entries[index].IsDirectory) Navigate(entries[index].FullPath); };
            Navigate(currentDirectory);
        }
        return panel;
    }
    private UIElement Cleaner()
    {
        var outer = new StackPanel();
        outer.Children.Add(Text(L("Scans temporary files of all ages. Locked and inaccessible items are skipped. Prefetch cleanup is optional and generally unnecessary.", "يشمل الفحص الملفات المؤقتة بجميع أعمارها، مع تجاوز الملفات المقفلة أو التي يتعذر الوصول إليها. تنظيف Prefetch اختياري وغير ضروري عادةً."), 13, palette.Dim));
        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 8, 0, 8) };
        var categories = new[] { (TempCleanerService.Category.UserTemp, "User Temp", "ملفات المستخدم المؤقتة", "◈"), (TempCleanerService.Category.SystemTemp, "System Temp", "ملفات النظام المؤقتة", "⚙"), (TempCleanerService.Category.Prefetch, "Windows Prefetch", "ملفات Prefetch", "✦"), (TempCleanerService.Category.RecycleBin, "Recycle Bin", "سلة المحذوفات", "♲") };
        var controls = new Dictionary<TempCleanerService.Category, (CheckBox Check, TextBlock Summary)>();
        foreach (var (kind, en, ar, icon) in categories)
        {
            var content = new StackPanel(); var check = new CheckBox { Style = (Style)Application.Current.Resources["CleanerCheckBox"], Content = $"{icon}   {L(en, ar)}", FontSize = 15, IsChecked = cleanerCategories.Contains(kind) };
            if(kind==TempCleanerService.Category.RecycleBin)check.ToolTip=L("Emptying the Recycle Bin permanently deletes its contents.","إفراغ سلة المحذوفات يحذف محتوياتها نهائيًا.");
            var summary = Text(L("Not scanned", "لم يُفحص"), 12, palette.Dim); summary.Margin = new Thickness(0, 12, 0, 2);
            content.Children.Add(check); content.Children.Add(summary); grid.Children.Add(CleanerCard(content)); controls[kind] = (check, summary);
        }
        outer.Children.Add(grid);
        var status = Text(L("Scanning…", "جارٍ الفحص…"), 13, palette.Dim); status.TextWrapping = TextWrapping.Wrap;
        var cleanButton = new Button { Content = L("✦   One-Click Quick Clean   ✦", "✦   التنظيف السريع   ✦"), Style = (Style)Application.Current.Resources["ChromeButton"], IsEnabled = false, Height = 52, FontSize = 16, FontWeight = FontWeights.SemiBold, HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(5, 10, 5, 4) };
        cleanButton.SetResourceReference(Control.BackgroundProperty, "HomeCardBrush"); cleanButton.SetResourceReference(Control.BorderBrushProperty, "HomeBorderBrush");
        outer.Children.Add(cleanButton); outer.Children.Add(status);
        var service = new TempCleanerService(); CancellationTokenSource? running = null;
        var scanned = new HashSet<TempCleanerService.Category>();
        var estimatedBytes = new Dictionary<TempCleanerService.Category,long>();
        bool scanComplete = false;
        void PersistSelection()
        {
            cleanerCategories.Clear();
            foreach (var pair in controls) if (pair.Value.Check.IsChecked == true) cleanerCategories.Add(pair.Key);
            SaveSettings();
        }
        void UpdateCleanEnabled()
        {
            cleanButton.IsEnabled = running is null && scanComplete && controls.Any(c => c.Value.Check.IsChecked == true);
            long estimate=CleanerSelection.EstimatedBytes(controls.Where(c=>c.Value.Check.IsChecked==true).Select(c=>c.Key),estimatedBytes);
            cleanButton.Content=L("✦   One-Click Quick Clean   ✦","✦   التنظيف السريع   ✦")+
                (scanComplete?L("  ·  Estimated: ","  ·  المتوقع: ")+DisplayFormat.Size(estimate,arabic):"");
        }
        foreach (var entry in controls.Values)
        {
            entry.Check.Checked += (_, _) => { PersistSelection(); UpdateCleanEnabled(); };
            entry.Check.Unchecked += (_, _) => { PersistSelection(); UpdateCleanEnabled(); };
        }
        async Task Run(bool delete, bool silent = false)
        {
            if (running != null || cleanerOperationActive) { if (!silent) status.Text = L("A cleanup operation is already running.", "توجد عملية تنظيف جارية بالفعل."); return; }
            var selected = controls.Where(pair => !delete || pair.Value.Check.IsChecked == true).Select(pair => pair.Key).ToArray();
            if (selected.Length == 0) { if (!silent) status.Text = L("Select at least one category.", "اختر فئة واحدة على الأقل."); return; }
            if (delete && selected.Any(kind => !scanned.Contains(kind))) { if (!silent) status.Text = L("A selected category could not be scanned and cannot be cleaned.", "تعذر فحص فئة محددة، لذلك لا يمكن تنظيفها."); return; }
            using var cts = new CancellationTokenSource(); running = cts; cleanerOperationActive = true; cleanButton.IsEnabled = false;
            if (!delete){scanned.Clear();estimatedBytes.Clear();}
            foreach (var entry in controls.Values) entry.Check.IsEnabled = false;
            long cleanedBytes = 0; int cleanedFiles = 0, cleanedDirectories = 0, skippedTotal = 0, failedCategories = 0;
            try
            {
                var progress = new Progress<int>(n => { if (!silent && ReferenceEquals(running,cts)) status.Text = L($"Processed {DisplayFormat.Number(n)} files…", $"تمت معالجة {DisplayFormat.Isolate(DisplayFormat.Number(n))} ملف…"); });
                foreach (var kind in selected)
                {
                    try
                    {
                        var result = await service.RunCategoryAsync(kind, delete, progress, cts.Token);
                        controls[kind].Summary.Text = DisplayFormat.CategorySummary(result.Files, result.Directories, result.Bytes, result.Skipped, delete, arabic);
                        if (!delete){scanned.Add(kind);estimatedBytes[kind]=result.Bytes;UpdateCleanEnabled();}
                        else { cleanedBytes += result.Bytes; cleanedFiles += result.Files; cleanedDirectories += result.Directories; }
                        skippedTotal += result.Skipped;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or System.Runtime.InteropServices.ExternalException or ArgumentException or NotSupportedException)
                    {
                        controls[kind].Summary.Text = ex is UnauthorizedAccessException or System.Security.SecurityException ? L("Windows denied access · run as administrator to scan this category", "رفض Windows الوصول · شغّل كمسؤول لفحص هذه الفئة") : L("Unavailable or access denied", "غير متاح أو تعذر الوصول");
                        failedCategories++;
                        activity.Write("WARNING", $"Quick Cleaner {kind}: {ex.GetType().Name} · 0x{ex.HResult:X8} · {ex.Message}");
                        CrashDiagnostics.Record("Quick Cleaner "+kind,ex);
                    }
                }
                if (!delete)
                {
                    scanComplete = true;
                    if (!silent) status.Text = "";
                    activity.Write(skippedTotal == 0 ? "INFO" : "WARNING", "Quick cleanup scan finished.");
                }
                else
                {
                    if(cleanedBytes>0)await SpaceFreedService.Instance.AddAsync(cleanedBytes,"clean:"+Guid.NewGuid().ToString("N"));
                    var summary = DisplayFormat.CleanOutcomeSummary(cleanedBytes, cleanedFiles, cleanedDirectories, skippedTotal, failedCategories, arabic);
                    status.Text = summary;
                    status.SetResourceReference(TextBlock.ForegroundProperty,cleanedFiles>0||cleanedBytes>0?"ChromeAccent":"HomeDimBrush");
                    activity.Write(failedCategories>0||skippedTotal>0 ? "WARNING" : cleanedFiles > 0 || cleanedBytes > 0 ? "SUCCESS" : "INFO", summary);
                }
            }
            catch (OperationCanceledException) { if (!silent) status.Text = L("Cancelled; completed deletions cannot be undone.", "أُلغي العمل؛ لا يمكن التراجع عن الملفات التي حُذفت بالفعل."); }
            catch (Exception ex) { if (!silent) status.Text = L("Scan or cleanup failed; see Activity Log.", "تعذر الفحص أو التنظيف؛ راجع سجل النشاط."); scanComplete = false; activity.Write("ERROR", $"Quick Cleaner: {ex.GetType().Name}"); }
            finally { running = null; cleanerOperationActive = false; foreach (var entry in controls.Values) entry.Check.IsEnabled = true; UpdateCleanEnabled(); }
            // Refresh category summaries after cleaning without overwriting the success result.
            if (delete && outer.IsLoaded) await Run(false, true);
        }
        outer.Loaded += async (_, _) => await Run(false); outer.Unloaded += (_, _) => { if (running != null && !scanComplete) running.Cancel(); }; cleanButton.Click += async (_, _) => await Run(true);
        return outer;
    }
    private Border CleanerCard(UIElement content) => new() { Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(18), Margin = new Thickness(5), Child = content };
    private void NavigateTo(string key)
    {
        if (page == key || !keys.Contains(key) || !CanLeavePage()) return;
        page = key; activity.Write("INFO", "Opened section: " + key); Render();
    }
    private bool CanLeavePage()
    {
        if (!cleanerOperationActive && PageContent.Content is not DeepUninstallerView { IsOperationActive: true }
            && PageContent.Content is not FileBrowserView { IsOperationActive: true }) return true;
        activity.Write("WARNING", L("Finish or cancel the current operation before leaving this page.", "أكمل العملية الحالية أو ألغها قبل مغادرة الصفحة."));
        return false;
    }
    private void OpenStorageItem(string directory, string? reveal)
    {
        try
        {
            string target = Path.GetFullPath(Directory.Exists(directory) ? directory : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            currentDirectory = target;
            pendingFileReveal = reveal;
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            currentDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            pendingFileReveal = null;
            activity.Write("WARNING", "Storage item could not be opened in File Browser: " + e.Message);
        }
        NavigateTo("files");
    }
    private void ApplyNativeTitleBar() => NativeTitleBarService.Apply(this,
        (Color)ColorConverter.ConvertFromString(palette.Sidebar??palette.Card),
        (Color)ColorConverter.ConvertFromString(palette.Text));
    private void ClearLog(object sender, RoutedEventArgs e) { activity.Entries.Clear(); activity.Write("INFO", "Activity log cleared."); }
    private void ToggleLog(object sender, RoutedEventArgs e)
    {
        logCollapsed = !logCollapsed;
        Log.Visibility = logCollapsed ? Visibility.Collapsed : Visibility.Visible;
        LogRow.Height = new GridLength(logCollapsed ? 60 : 170);
        LogToggleButton.Content = logCollapsed ? "▼" : "▲";
        LogToggleButton.ToolTip = logCollapsed ? L("Expand log", "توسيع السجل") : L("Collapse log", "طي السجل");
    }
    private void SaveSettings()
    {
        try
        {
            // Preserve preferences owned by other pages (deep-uninstaller and
            // File Browser sorting) instead of resetting them to their defaults.
            var existing = settingsService.Load();
            settingsService.Save(new(ThemePicker.SelectedItem as string ?? ThemeCatalog.DefaultTheme, arabic, collapsed,
                CleanerSelection.Serialize(cleanerCategories), existing.DeepUninstallerSort,
                existing.FileBrowserSort, existing.FileBrowserSortDirection));
            settingsErrorReported = false;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            if (!settingsErrorReported) { activity.Write("WARNING", "Settings could not be saved; changes apply for this session. " + error.Message); settingsErrorReported = true; }
        }
    }
    private void ToggleSidebar(object sender, RoutedEventArgs e) { collapsed = !collapsed; SaveSettings(); Render(false, true); }
    private void ToggleThemeGallery(object sender,RoutedEventArgs e)
    {
        if(ThemeGalleryHost.Visibility==Visibility.Visible){CloseThemeGallery();return;}
        if(themeGallery==null)
        {
            string ArabicName(string name)=>name switch{"Midnight Champagne"=>"شامبانيا منتصف الليل","Blue"=>"الأزرق","Black"=>"الأسود","White"=>"الأبيض","Beige"=>"البيج","Espresso"=>"إسبريسو","Rose Noir"=>"الوردي الداكن","Pure Black"=>"الأسود الداكن",_=>name};
            var preferred=new[]{ThemeCatalog.DefaultTheme,"Blue","Black","White","Beige","Pure Black","Espresso","Rose Noir"};
            var options=preferred.Concat(palettes.Keys.Except(preferred)).Where(palettes.ContainsKey).Select(name=>
            {var p=palettes[name];return new ThemeOption(name,ArabicName(name),p.Background,p.Glow,p.Card,p.Border,p.Accent,p.Text,p.Dim,p.Sidebar,p.Hover,p.SecondaryBackground,p.SecondaryAccent);}).ToArray();
            themeGallery=new ThemeGalleryView(options);
            themeGallery.CancelRequested+=CloseThemeGallery;
            themeGallery.ApplyRequested+=name=>{CloseThemeGallery();ThemePicker.SelectedItem=name;};
            ThemeGalleryHost.Content=themeGallery;
        }
        ThemeGalleryHost.Visibility=Visibility.Visible;
        themeGallery.Open(ThemePicker.SelectedItem as string??ThemeCatalog.DefaultTheme,arabic);
    }
    private void CloseThemeGallery(){themeGallery?.RestorePreview();ThemeGalleryHost.Visibility=Visibility.Collapsed;ThemeButton.Focus();}
    private static void SetThemeBrush(ResourceDictionary resources,string key,Brush value)
    {
        // Keep shared brushes alive: theme changes must not reconstruct pages
        // or cancel an active uninstall, scan or download.
        if(resources[key] is SolidColorBrush old&&!old.IsFrozen&&value is SolidColorBrush next)
            old.Color=next.Color;
        else resources[key]=value.CloneCurrentValue();
    }
    private void SwitchLanguage(object sender, RoutedEventArgs e) { if (!CanLeavePage()) return; arabic = !arabic; activity.Write("INFO", "Language changed to " + (arabic ? "Arabic" : "English")); SaveSettings(); Render(); updateWindow?.SetLanguage(arabic); }
    private void ChangeTheme(object sender, SelectionChangedEventArgs e) { if (!ready || ThemePicker.SelectedItem is not string name) return; palette = palettes[name]; activity.Write("INFO", "Theme changed to " + name); SaveSettings(); Render(false); }

}
