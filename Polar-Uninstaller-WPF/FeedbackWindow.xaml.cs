using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PolarUninstallerWpf.Services;

namespace PolarUninstallerWpf;

public partial class FeedbackWindow : Window
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static readonly Uri Endpoint = new("https://fhismwsluqusboiasxlj.supabase.co/functions/v1/submit-feedback");
    private readonly bool arabic;
    private readonly ActivityLogService activity;
    private bool sending;
    private string L(string en, string ar) => arabic ? ar : en;

    public FeedbackWindow(bool arabic, ActivityLogService activity)
    {
        this.arabic = arabic;
        this.activity = activity;
        InitializeComponent(); VersionLabel.Text="v"+AppInfo.Version;
        FlowDirection = arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Title = L("Feedback & Reports", "الملاحظات والبلاغات");
        Heading.Text = L("✉  Feedback & Reports", "✉  الملاحظات والبلاغات");
        Subtitle.Text = L("Found a problem or have an idea? Share it with the Polar team.", "واجهت مشكلة أو لديك فكرة؟ شاركها مع فريق Polar.");
        CategoryLabel.Text = L("Category", "الفئة"); Bug.Content = L("Report a Bug", "الإبلاغ عن مشكلة"); Suggestion.Content = L("Suggestion", "اقتراح"); Other.Content = L("Other Feedback", "ملاحظات أخرى");
        MessageLabel.Text = L("Message", "الرسالة"); Placeholder.Text = L("Describe the problem or share your suggestion…", "صف المشكلة أو شارك اقتراحك…");
        Privacy.Text = L("Privacy: only the category, message and application version are sent.", "الخصوصية: تُرسل الفئة والرسالة وإصدار التطبيق فقط.");
        CancelButton.Content = L("Cancel", "إلغاء"); SendButton.Content = L("Send", "إرسال"); UpdateCounter();
        SourceInitialized += (_, _) => NativeTitleBarService.Apply(this, ((SolidColorBrush)Application.Current.Resources["ChromeBackground"]).Color, ((SolidColorBrush)Application.Current.Resources["ChromeText"]).Color);
    }

    private void UpdateCounter() { Counter.Text = $"{MessageInput.Text.Length} / 2000"; Placeholder.Visibility = MessageInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; }
    private void MessageChanged(object sender, TextChangedEventArgs e) { if (Counter != null && Placeholder != null) UpdateCounter(); }
    private void CancelClick(object sender, RoutedEventArgs e) => Close();
    protected override void OnClosing(CancelEventArgs e) { if (sending) e.Cancel = true; base.OnClosing(e); }

    private async void SendClick(object sender, RoutedEventArgs e)
    {
        if (sending) return;
        string? category = Bug.IsChecked == true ? "bug" : Suggestion.IsChecked == true ? "suggestion" : Other.IsChecked == true ? "other" : null;
        string message = MessageInput.Text.Trim();
        if (category == null) { Status.Text = L("Please select a category.", "يرجى اختيار الفئة."); return; }
        if (message.Length < 5) { Status.Text = L("Please enter at least five characters.", "يرجى كتابة خمسة أحرف على الأقل."); return; }
        if (message.Length > 2000) { Status.Text = L("The message must be at most 2,000 characters.", $"يجب ألا تتجاوز الرسالة {DisplayFormat.Number(2000)} حرف."); return; }
        sending = true; SendButton.IsEnabled = false; CancelButton.IsEnabled = false;
        Status.Text = L("Sending…", "جارٍ الإرسال…");
        activity.Write("INFO", "Feedback submission started.");
        try
        {
            using var response = await Client.PostAsJsonAsync(Endpoint, new { category, message, app_version = AppInfo.Version });
            if (!response.IsSuccessStatusCode)
            {
                Status.Text = response.StatusCode == HttpStatusCode.TooManyRequests ? L("Too many requests. Please try again later.", "طلبات كثيرة؛ حاول مجددًا لاحقًا.") : L("The feedback service could not accept the message. Please try again.", "تعذر على خدمة الملاحظات قبول الرسالة. حاول مرة أخرى.");
                activity.Write("WARNING", $"Feedback submission failed (HTTP {(int)response.StatusCode}).");
                return;
            }
            if (response.Content.Headers.ContentLength > 8192) throw new InvalidDataException();
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var buffer = new MemoryStream();
            var chunk = new byte[2048]; int count;
            while ((count = await stream.ReadAsync(chunk)) > 0) { if (buffer.Length + count > 8192) throw new InvalidDataException(); buffer.Write(chunk, 0, count); }
            buffer.Position = 0;
            using var json = await JsonDocument.ParseAsync(buffer);
            if (!json.RootElement.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True) throw new InvalidDataException();
            activity.Write("SUCCESS", "Feedback submitted.");
            Status.Text = L("Thank you — your feedback was sent.", "شكرًا لك، تم إرسال ملاحظاتك.");
            MessageInput.Clear();
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException or IOException or InvalidOperationException)
        {
            activity.Write("WARNING", "Feedback submission failed.");
            Status.Text = L("Could not send feedback. Your message has been kept; check your connection and try again.", "تعذر إرسال الملاحظات. احتُفظ برسالتك؛ تحقق من الاتصال وحاول مجددًا.");
        }
        finally { sending = false; SendButton.IsEnabled = true; CancelButton.IsEnabled = true; }
    }
}
