using System.Windows;
using System.Windows.Controls;
using PolarUninstallerWpf.Services;

namespace PolarUninstallerWpf;

public partial class HomeView : UserControl
{
    private readonly bool arabic;
    private readonly ApplicationInventoryService inventory;
    public event Action<string>? NavigationRequested;

    public HomeView(bool arabic, ApplicationInventoryService inventory)
    {
        InitializeComponent();
        this.arabic = arabic;
        this.inventory = inventory;
        FlowDirection = arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        VersionText.Text = "v"+AppInfo.Version;
        Heading.Text = arabic ? "اختر قسماً للبدء" : "Choose a section to start";
        Subtitle.Text = arabic ? "افحص البرامج وأزلها ونظّف ما تتركه خلفها." : "Scan, remove and clean up everything an app leaves behind.";
        Count.Text = "…";
        CountCaption.Text = arabic ? "جارٍ تحميل البرامج…" : "Loading programs…";
        UninstallTitle.Text = arabic ? "الإزالة العميقة" : "Deep Uninstaller";
        UninstallDesc.Text = arabic ? "استعرض البرامج المسجلة وملفاتها." : "Review installed programs and their details.";
        CleanerTitle.Text = arabic ? "التنظيف السريع" : "Quick Cleaner";
        CleanerDesc.Text = arabic ? "نظّف الملفات المؤقتة بخطوة واحدة." : "Clear temporary files and caches in one pass.";
        DashboardTitle.Text = arabic ? "لوحة المعلومات" : "Dashboard";
        DashboardDesc.Text = arabic ? "معلومات البرنامج في لمحة." : "A quick overview of your system.";
        FilesTitle.Text = arabic ? "متصفح الملفات" : "File Browser";
        FilesDesc.Text = arabic ? "استعرض المجلدات والملفات." : "Browse folders and files directly.";
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        inventory.Changed -= InventoryChanged;
        inventory.Changed += InventoryChanged;
        if (inventory.Current is { } cached) Apply(cached);
        else
        {
            Count.Text = "…";
            CountCaption.Text = arabic ? "جارٍ الفحص…" : "scanning…";
        }

        try
        {
            var snapshot = await inventory.RefreshAsync();
            if (inventory.IsCurrent(snapshot)) Apply(snapshot);
        }
        catch (Exception)
        {
            if (!IsLoaded) return;
            Count.Text = "—";
            CountCaption.Text = arabic ? "تعذر الفحص" : "Scan unavailable";
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => inventory.Changed -= InventoryChanged;

    private void InventoryChanged(ApplicationInventory snapshot)
    {
        if (!IsLoaded) return;
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => Apply(snapshot));
            return;
        }
        Apply(snapshot);
    }

    private void Apply(ApplicationInventory snapshot)
    {
        if (!IsLoaded) return;
        Count.Text = snapshot.IsComplete ? snapshot.Count.ToString() : "…";
        CountCaption.Text = snapshot.IsComplete
            ? (arabic ? "برامج مسجلة" : "programs found")
            : (arabic ? "جارٍ الفحص…" : "scanning…");
    }

    private void Navigate(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string key }) NavigationRequested?.Invoke(key);
    }
}
