using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using PolarUninstallerWpf.Services;

namespace PolarUninstallerWpf;

public partial class UpdateCenterWindow : Window
{
    private bool arabic;
    private UpdateResult current = new(UpdateState.Checking);
    private CancellationTokenSource? download;
    private string? downloadedPath;
    private string? downloadedDigest;
    private string? downloadedVersion;
    private bool installing;
    private bool finishing;

    private Func<string>? headingProvider;
    private Func<string>? subtitleProvider;
    private Func<string>? statusPillProvider;
    private Func<string>? statusTextProvider;
    private Func<string>? transferTextProvider;
    private Func<string>? installNoticeProvider;
    private Func<string>? actionMessageProvider;
    private bool actionMessageIsError;
    private DownloadProgress? lastProgress;

    private string L(string en, string ar) => arabic ? ar : en;

    public UpdateCenterWindow(bool arabic, UpdateResult result)
    {
        this.arabic = arabic;
        InitializeComponent();
        FlowDirection = arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Title = L("Polar Uninstaller Update", "تحديث Polar Uninstaller");
        BrandText.Text = "POLAR UNINSTALLER";
        CurrentLabel.Text = L("Installed version", "الإصدار المثبت");
        LatestLabel.Text = L("Available version", "الإصدار المتاح");
        WhatsNewHeading.Text = L("What's New", "ما الجديد");
        UpdateNowButton.Content = L("Update Now", "تحديث الآن");
        UpdateLaterButton.Content = L("Update Later", "تحديث لاحقًا");

        SourceInitialized += (_, _) =>
        {
            if (Application.Current?.Resources["ChromeBackground"] is SolidColorBrush background &&
                Application.Current.Resources["ChromeText"] is SolidColorBrush foreground)
                NativeTitleBarService.Apply(this, background.Color, foreground.Color);
        };
        Closing += OnClosing;
        Loaded += (_, _) => UpdateWindowLayout();
        RestoreWindowState();
        ShowResult(result);
    }

    private void ApplyStaticLanguage()
    {
        FlowDirection = arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Title = L("Polar Uninstaller Update", "تحديث Polar Uninstaller");
        BrandText.Text = "POLAR UNINSTALLER";
        CurrentLabel.Text = L("Installed version", "الإصدار المثبت");
        LatestLabel.Text = L("Available version", "الإصدار المتاح");
        WhatsNewHeading.Text = L("What's New", "ما Nuevo");
        UpdateNowButton.Content = L("Update Now", "تحديث الآن");
        UpdateLaterButton.Content = L("Update Later", "تحديث لاحقًا");
    }

    public void SetLanguage(bool newArabic)
    {
        if (arabic == newArabic) return;
        arabic = newArabic;
        ApplyStaticLanguage();
        RenderResult();
        RefreshDynamicTexts();
    }

    private void RefreshDynamicTexts()
    {
        if (actionMessageProvider is not null)
        {
            StatusText.Text = actionMessageProvider();
            if (actionMessageIsError)
                InstallNotice.Text = L("Your working installation has not been changed.", "لم يتم تغيير التثبيت العامل لديك.");
        }
        else if (statusTextProvider is not null)
        {
            StatusText.Text = statusTextProvider();
        }
        if (transferTextProvider is not null) TransferText.Text = transferTextProvider();
        if (installNoticeProvider is not null && actionMessageProvider is null) InstallNotice.Text = installNoticeProvider();
        if (headingProvider is not null) Heading.Text = headingProvider();
        if (subtitleProvider is not null) Subtitle.Text = subtitleProvider();
        if (statusPillProvider is not null) StatusPillText.Text = statusPillProvider();
    }

    private void UpdateWindowLayout()
    {
        // Keep the native frame responsive and avoid a stale invalid layout after
        // the restored size or the DPI changes.
        InvalidateMeasure();
    }

    public void ShowResult(UpdateResult result)
    {
        if (installing) return;
        current = result;
        var release = result.Release;
        var asset = release == null ? null : UpdateDownloadService.Select(release);

        if (release == null || release.Version != downloadedVersion)
        {
            downloadedPath = null;
            downloadedDigest = null;
            downloadedVersion = release?.Version;
        }
        if (downloadedPath != null && downloadedDigest != null && asset != null &&
            !string.Equals(downloadedDigest, asset.Digest, StringComparison.OrdinalIgnoreCase))
        {
            downloadedPath = null;
            downloadedDigest = null;
        }

        RenderResult();
    }

    private void RenderResult()
    {
        var release = current.Release;
        var asset = release == null ? null : UpdateDownloadService.Select(release);
        bool available = current.State == UpdateState.Available && release != null;
        bool validAsset = HasVerifiedDigest(asset);

        CurrentVersionText.Text = "v" + WpfUpdateService.CurrentVersion;
        LatestVersionText.Text = release == null ? "—" : "v" + release.Version;

        headingProvider = () => available ? L("Update Available", "يتوفر تحديث جديد") : L("Polar Update Center", "مركز تحديث Polar");
        Heading.Text = headingProvider();
        subtitleProvider = () => available
            ? L("A new version of Polar Uninstaller is ready to install.", "إصدار جديد من Polar Uninstaller جاهز للتثبيت.")
            : L("Polar Uninstaller keeps itself up to date securely.", "يحافظ Polar Uninstaller على تحديث نفسه بأمان.");
        Subtitle.Text = subtitleProvider();
        statusPillProvider = () => current.State switch
        {
            UpdateState.Checking => L("Checking", "جارٍ الفحص"),
            UpdateState.Available => L("Update available", "يتوفر تحديث"),
            UpdateState.UpToDate => L("Up to date", "محدّث"),
            UpdateState.NoCompatibleRelease => L("No update", "لا تحديث"),
            _ => L("Check failed", "تعذر الفحص")
        };
        StatusPillText.Text = statusPillProvider();

        string title = release == null ? "" : string.IsNullOrWhiteSpace(release.Title) ? "v" + release.Version : release.Title.Trim();
        bool titleUsable = !string.IsNullOrWhiteSpace(title) && (arabic
            ? title.Any(c => c >= '\u0600' && c <= '\u06FF')
            : title.Any(char.IsAsciiLetter));
        if (!titleUsable) title = "";
        string date = release?.Published is { } published ? " · " + published.ToLocalTime().ToString("d") : "";
        string size = asset is { Size: > 0 } ? " · " + (asset.Size / 1048576.0).ToString("N1") + " MB" : "";
        ReleaseMeta.Text = (title + date + size).Trim(' ', '·');

        if (release == null || string.IsNullOrWhiteSpace(release.Notes))
        {
            NotesViewer.Document = MarkdownRenderer.BuildMessageDocument(
                L("Release notes are not available for this version. You can still install the verified update.",
                  "ملاحظات الإصدار غير متاحة لهذا الإصدار. لا يزال بإمكانك تثبيت التحديث المُتحقق منه."), arabic);
            NotesLanguageHint.Visibility = Visibility.Collapsed;
        }
        else
        {
            var localized = ReleaseNotesTextService.Select(release.Notes, arabic);
            var noteText = ReleaseNotesTextService.StripDuplicateHeading(localized.Text, arabic);
            NotesViewer.Document = MarkdownRenderer.BuildDocument(noteText, arabic);
            NotesLanguageHint.Text = arabic
                ? "ملاحظات الإصدار منشورة بالإنجليزية."
                : "These release notes are published in Arabic.";
            NotesLanguageHint.Visibility = localized.LanguageFallback ? Visibility.Visible : Visibility.Collapsed;
        }

        UpdateNowButton.Visibility = Visibility.Visible;
        UpdateNowButton.IsEnabled = available && validAsset && downloadedPath == null && download == null && !installing;
        UpdateLaterButton.IsEnabled = !installing;
    }

    private async void UpdateNowClick(object sender, RoutedEventArgs e)
    {
        if (installing || download != null) return;
        if (current.State != UpdateState.Available || current.Release == null) return;
        var release = current.Release;
        var asset = UpdateDownloadService.Select(release);
        if (!HasVerifiedDigest(asset) || asset == null)
        {
            ShowActionMessage(() => L("This release does not publish a verified SHA-256 update package.", "هذا الإصدار لا ينشر حزمة تحديث مُتحققة بـ SHA-256."), true);
            return;
        }

        if (downloadedPath != null && File.Exists(downloadedPath))
        {
            await InstallVerifiedUpdateAsync(asset);
            return;
        }

        using var cancellation = new CancellationTokenSource();
        download = cancellation;
        installing = false;
        ProgressPanel.Visibility = Visibility.Visible;
        DownloadBar.Visibility = Visibility.Visible;
        DownloadBar.IsIndeterminate = false;
        DownloadBar.Value = 0;
        statusTextProvider = () => L("Downloading update…", "جارٍ تنزيل التحديث…");
        transferTextProvider = () => "";
        installNoticeProvider = () => L("The official GitHub asset will be verified with SHA-256 before installation.",
            "سيتم التحقق من ملف GitHub الرسمي عبر SHA-256 قبل التثبيت.");
        lastProgress = null;
        actionMessageProvider = null;
        StatusText.Text = statusTextProvider();
        TransferText.Text = "";
        InstallNotice.Text = installNoticeProvider();
        SetBusy(true);

        try
        {
            var progress = new Progress<DownloadProgress>(p =>
            {
                if (download is null) return;
                lastProgress = p;
                DownloadBar.IsIndeterminate = !p.Total.HasValue;
                double percent = p.Total is > 0 ? Math.Clamp(100.0 * p.Bytes / p.Total.Value, 0, 100) : 0;
                DownloadBar.Value = percent;
                transferTextProvider = () => BuildTransferText(p);
                StatusText.Text = statusTextProvider!();
                TransferText.Text = transferTextProvider();
            });

            string file = await UpdateDownloadService.DownloadAsync(release, progress, cancellation.Token);
            downloadedPath = file;
            downloadedDigest = asset.Digest;
            downloadedVersion = release.Version;
            DownloadBar.IsIndeterminate = false;
            DownloadBar.Value = 100;
            lastProgress = null;
            statusTextProvider = () => L("Download complete. Verifying the update…", "اكتمل التنزيل. جارٍ التحقق من التحديث…");
            transferTextProvider = () => L("SHA-256 and executable verification passed.", "نجح التحقق من SHA-256 والملف التنفيذي.");
            actionMessageProvider = null;
            StatusText.Text = statusTextProvider();
            TransferText.Text = transferTextProvider();
            await InstallVerifiedUpdateAsync(asset);
        }
        catch (OperationCanceledException)
        {
            lastProgress = null;
            statusTextProvider = () => L("Download cancelled.", "أُلغي التنزيل.");
            transferTextProvider = () => "";
            actionMessageProvider = null;
            StatusText.Text = statusTextProvider();
            TransferText.Text = "";
            downloadedPath = null;
            downloadedDigest = null;
        }
        catch (Exception ex) when (ex is IOException or System.Net.Http.HttpRequestException or UnauthorizedAccessException or InvalidDataException)
        {
            lastProgress = null;
            var captured = ex;
            statusTextProvider = () => L("The update could not be downloaded: ", "تعذر تنزيل التحديث: ") + DownloadErrorText(captured);
            transferTextProvider = () => "";
            installNoticeProvider = () => "";
            actionMessageProvider = null;
            StatusText.Text = statusTextProvider();
            TransferText.Text = "";
            InstallNotice.Text = "";
            downloadedPath = null;
            downloadedDigest = null;
        }
        finally
        {
            download = null;
            DownloadBar.IsIndeterminate = false;
            if (!installing && !finishing) SetBusy(false);
        }
    }

    private async Task InstallVerifiedUpdateAsync(WpfAsset asset)
    {
        if (installing) return;
        if (downloadedPath == null || !File.Exists(downloadedPath))
        {
            ShowActionMessage(() => L("The downloaded update is missing. Please download it again.", "التحديث الذي تم تنزيله غير موجود. يرجى تنزيله مجددًا."), true);
            downloadedPath = null;
            downloadedDigest = null;
            return;
        }

        bool standalone = IsStandaloneAsset(asset);
        if (standalone)
        {
            var verification = UpdateVerificationService.VerifyStandaloneExecutable(
                downloadedPath, current.Release!.Version, asset.Digest[7..], asset.Size);
            if (!verification.IsValid)
            {
                ShowActionMessage(() => L("The downloaded update could not be verified: ", "تعذر التحقق من التحديث الذي تم تنزيله: ") +
                    UpdateErrorText(verification.Error), true);
                downloadedPath = null;
                downloadedDigest = null;
                SetBusy(false);
                return;
            }
        }
        else if (!UpdateInstallerService.IsInstalledCopy(out _))
        {
            ShowActionMessage(() => L("This release provides only a Setup package. Automatic updates for portable copies require a standalone EXE asset.",
                "هذا الإصدار يوفر حزمة تثبيت فقط. التحديث التلقائي للنسخ المحمولة يتطلب ملف EXE مستقلًا."), true);
            SetBusy(false);
            return;
        }

        installing = true;
        SetBusy(true);
        DownloadBar.IsIndeterminate = true;
        statusTextProvider = () => L("Preparing the verified update…", "جارٍ تجهيز التحديث المُتحقق منه…");
        transferTextProvider = () => "";
        installNoticeProvider = () => L("The previous working version is kept until the replacement is verified.",
            "يُحتفظ بالإصدار السابق العامل حتى يتم التحقق من الاستبدال.");
        actionMessageProvider = null;
        lastProgress = null;
        StatusText.Text = statusTextProvider();
        TransferText.Text = "";
        InstallNotice.Text = installNoticeProvider();

        try
        {
            if (standalone)
            {
                var result = await UpdateInstallerService.LaunchHelperAsync(
                    downloadedPath, current.Release!.Version, asset.Digest[7..], CancellationToken.None);
                if (!result.Success)
                {
                    installing = false;
                    DownloadBar.IsIndeterminate = false;
                    var capturedError = result.Error;
                    statusTextProvider = () => L("The update was not installed.", "لم يتم تثبيت التحديث.");
                    installNoticeProvider = () => L("Could not prepare the update: ", "تعذر تجهيز التحديث: ") + UpdateErrorText(capturedError);
                    actionMessageProvider = null;
                    StatusText.Text = statusTextProvider();
                    InstallNotice.Text = installNoticeProvider();
                    SetBusy(false);
                    return;
                }

                finishing = true;
                DownloadBar.IsIndeterminate = false;
                DownloadBar.Value = 100;
                statusTextProvider = () => L("Update verified. Polar Uninstaller will restart to finish.",
                    "تم التحقق من التحديث. سيعيد Polar Uninstaller التشغيل لإكمال العملية.");
                transferTextProvider = () => L("The running version stays untouched until the helper takes over.",
                    "لن يتغير الإصدار العامل حتى يتولى المساعد العملية.");
                actionMessageProvider = null;
                StatusText.Text = statusTextProvider();
                TransferText.Text = transferTextProvider();
                await Task.Delay(700);
                Application.Current.Shutdown(0);
                return;
            }

            var start = new ProcessStartInfo(downloadedPath)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = $"--wait-pid {Environment.ProcessId}"
            };
            Process.Start(start);
            finishing = true;
            statusTextProvider = () => L("The Setup package was started. Polar Uninstaller will close while it installs.",
                "تم تشغيل حزمة التثبيت. سيُغلق Polar Uninstaller أثناء التثبيت.");
            actionMessageProvider = null;
            StatusText.Text = statusTextProvider();
            await Task.Delay(1500);
            Application.Current.Shutdown(0);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            installing = false;
            DownloadBar.IsIndeterminate = false;
            var captured = ex;
            statusTextProvider = () => L("The update could not be started.", "تعذر بدء التحديث.");
            installNoticeProvider = () => L("Could not start the update: ", "تعذر بدء التحديث: ") + UpdateErrorText(captured.Message);
            actionMessageProvider = null;
            StatusText.Text = statusTextProvider();
            InstallNotice.Text = installNoticeProvider();
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        bool available = current.State == UpdateState.Available && current.Release != null;
        var asset = current.Release == null ? null : UpdateDownloadService.Select(current.Release);
        UpdateNowButton.IsEnabled = !busy && available && HasVerifiedDigest(asset) && !installing;
        UpdateLaterButton.IsEnabled = !installing;
    }

    private void ShowActionMessage(Func<string> messageProvider, bool error)
    {
        ProgressPanel.Visibility = Visibility.Visible;
        DownloadBar.Visibility = Visibility.Collapsed;
        DownloadBar.IsIndeterminate = false;
        actionMessageProvider = messageProvider;
        actionMessageIsError = error;
        statusTextProvider = null;
        transferTextProvider = () => "";
        installNoticeProvider = error ? () => L("Your working installation has not been changed.", "لم يتم تغيير التثبيت العامل لديك.") : installNoticeProvider;
        StatusText.Text = messageProvider();
        TransferText.Text = "";
        InstallNotice.Text = error ? L("Your working installation has not been changed.", "لم يتم تغيير التثبيت العامل لديك.") : "";
    }

    private string BuildTransferText(DownloadProgress p)
    {
        double percent = p.Total is > 0 ? Math.Clamp(100.0 * p.Bytes / p.Total.Value, 0, 100) : 0;
        string unit = arabic ? "MB/ث" : "MB/s";
        return (p.Total.HasValue ? $"{percent:N0}% · " : "") +
            $"{p.Bytes / 1048576.0:N1} / {(p.Total.HasValue ? (p.Total.Value / 1048576.0).ToString("N1") : "—")} MB · " +
            L("Speed: ", "السرعة: ") + $"{p.BytesPerSecond / 1048576.0:N2} {unit}";
    }

    private static bool HasVerifiedDigest(WpfAsset? asset) =>
        asset != null &&
        asset.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) &&
        asset.Digest.Length == 71 &&
        asset.Digest[7..].All(Uri.IsHexDigit);

    private static bool IsStandaloneAsset(WpfAsset asset) =>
        string.Equals(asset.Name, WpfUpdateService.StandaloneAssetName, StringComparison.OrdinalIgnoreCase);

    private void UpdateLaterClick(object sender, RoutedEventArgs e)
    {
        if (installing) return;
        download?.Cancel();
        Close();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (installing && !finishing)
        {
            e.Cancel = true;
            ShowActionMessage(() => L("An update is being installed. Please wait for it to finish.",
                "جارٍ تثبيت تحديث. يرجى الانتظار حتى يكتمل."), false);
            return;
        }
        if (download != null) download.Cancel();
        SaveWindowState();
    }

    private static string StatePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PolarUninstallerWPF-Development", "update-window.json");

    private void RestoreWindowState()
    {
        try
        {
            if (!File.Exists(StatePath)) return;
            using var document = JsonDocument.Parse(File.ReadAllText(StatePath));
            var root = document.RootElement;
            double width = root.TryGetProperty("Width", out var w) ? w.GetDouble() : Width;
            double height = root.TryGetProperty("Height", out var h) ? h.GetDouble() : Height;
            double left = root.TryGetProperty("Left", out var l) ? l.GetDouble() : Left;
            double top = root.TryGetProperty("Top", out var t) ? t.GetDouble() : Top;
            bool maximized = root.TryGetProperty("Maximized", out var m) && m.ValueKind == JsonValueKind.True;

            if (width < MinWidth || height < MinHeight || double.IsNaN(width) || double.IsNaN(height)) return;
            var virtualScreen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            var desired = new Rect(left, top, width, height);
            var visible = Rect.Intersect(virtualScreen, desired);
            if (visible.IsEmpty || visible.Width < 120 || visible.Height < 80) return;

            WindowStartupLocation = WindowStartupLocation.Manual;
            Width = width;
            Height = height;
            Left = left;
            Top = top;
            if (maximized) WindowState = WindowState.Maximized;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
        }
    }

    private void SaveWindowState()
    {
        try
        {
            if (WindowState == WindowState.Minimized) return;
            Rect bounds = WindowState == WindowState.Maximized ? RestoreBounds : new Rect(Left, Top, Width, Height);
            if (bounds.Width < MinWidth || bounds.Height < MinHeight) return;
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            var data = new
            {
                bounds.Width,
                bounds.Height,
                bounds.Left,
                bounds.Top,
                Maximized = WindowState == WindowState.Maximized
            };
            File.WriteAllText(StatePath, JsonSerializer.Serialize(data));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }
    }

    private string DownloadErrorText(Exception exception)
    {
        string message = exception.Message;
        return message switch
        {
            "older_version" => L("The update is not newer than the installed version.", "التحديث ليس أحدث من الإصدار المثبت."),
            "no_installer" => L("No compatible update package was published.", "لم تُنشر حزمة تحديث متوافقة."),
            "missing_checksum" => L("The update is missing a published SHA-256 checksum.", "يفتقد التحديث بصمة SHA-256 المنشورة."),
            "invalid_url" => L("The update download URL is invalid.", "رابط تنزيل التحديث غير صالح."),
            "insufficient_space" => L("There is not enough free disk space to download the update.", "لا توجد مساحة كافية على القرص لتنزيل التحديث."),
            "size_mismatch" => L("The downloaded file size does not match the published size.", "حجم الملف الذي تم تنزيله لا يطابق الحجم المنشور."),
            "checksum_mismatch" => L("The downloaded file failed SHA-256 verification.", "فشل التحقق من SHA-256 للملف الذي تم تنزيله."),
            "invalid_pe" => L("The downloaded file is not a valid Windows executable.", "الملف الذي تم تنزيله ليس ملفًا تنفيذيًا صالحًا لنظام Windows."),
            "version_mismatch" => L("The downloaded file version does not match the release version.", "إصدار الملف الذي تم تنزيله لا يطابق إصدار الإصدار المنشور."),
            "invalid_signature" => L("The downloaded file has an invalid or untrusted signature.", "الملف الذي تم تنزيله يحمل توقيعًا غير صالح أو غير موثوق."),
            "download_failed" => L("The download failed. Check your connection and retry.", "تعذر التنزيل. تحقق من الاتصال وحاول مجددًا."),
            _ when message.StartsWith("verify_error:", StringComparison.Ordinal) => L("The downloaded file could not be verified.", "تعذر التحقق من الملف الذي تم تنزيله."),
            _ => message
        };
    }

    private string UpdateErrorText(string? error) => error switch
    {
        "missing_target" => L("The current application path could not be determined.", "تعذر تحديد مسار التطبيق الحالي."),
        "invalid_target" => L("The current application is not a valid Polar executable.", "التطبيق الحالي ليس ملف Polar تنفيذيًا صالحًا."),
        "missing_source" => L("The downloaded update is missing.", "التحديث الذي تم تنزيله غير موجود."),
        "target_mismatch" => L("The update target does not match the running application.", "هدف التحديث لا يطابق التطبيق قيد التشغيل."),
        "not_published" => L("The update is not a published asset for this version.", "التحديث ليس ملفًا منشورًا لهذا الإصدار."),
        "published_check_failed" => L("Could not verify the update against GitHub. Check your connection and retry.", "تعذر التحقق من التحديث عبر GitHub. تحقق من الاتصال وحاول مجددًا."),
        "published_mismatch" => L("The downloaded file does not match the published release asset.", "الملف الذي تم تنزيله لا يطابق ملف الإصدار المنشور."),
        "downgrade" => L("The downloaded version is older than the installed version.", "الإصدار الذي تم تنزيله أقدم من الإصدار المثبت."),
        "insufficient_space" => L("There is not enough free disk space to install the update.", "لا توجد مساحة كافية على القرص لتثبيت التحديث."),
        "access_denied" => L("Windows denied access to the application folder.", "رفض Windows الوصول إلى مجلد التطبيق."),
        "elevation_denied" => L("Administrator permission was denied.", "تم رفض إذن المسؤول."),
        "helper_timeout" => L("The update helper did not respond in time. The application was not changed.", "لم يستجب مساعد التحديث في الوقت المحدد. لم يتم تغيير التطبيق."),
        "helper_start_failed" => L("The update helper could not be started.", "تعذر بدء مساعد التحديث."),
        "replacement_failed" => L("The executable could not be replaced. The previous version was kept.", "تعذر استبدال الملف التنفيذي. تم الاحتفاظ بالإصدار السابق."),
        "staged_checksum" => L("The staged update failed verification. The previous version was kept.", "فشل التحقق من التحديث المجهز. تم الاحتفاظ بالإصدار السابق."),
        "replacement_checksum" => L("The replaced executable failed verification. The previous version was restored.", "فشل التحقق من الملف التنفيذي المستبدل. تمت استعادة الإصدار السابق."),
        _ when error?.StartsWith("helper_exit_", StringComparison.Ordinal) == true => L("The update helper exited unexpectedly. The application was not changed.", "انتهى مساعد التحديث بشكل غير متوقع. لم يتم تغيير التطبيق."),
        _ => error ?? L("The update could not be prepared.", "تعذر تجهيز التحديث.")
    };
}
