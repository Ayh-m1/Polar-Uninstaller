using System.IO;
using System.Text.Json;
using System.Windows;

namespace PolarUninstallerWpf.Services;

/// <summary>A single released version and its changelog entries.</summary>
public sealed record ReleaseNote(
    string Version,
    IReadOnlyList<string> Entries,
    IReadOnlyList<string>? FeatureHighlights = null,
    IReadOnlyList<string>? ImprovementHighlights = null,
    IReadOnlyList<string>? FixHighlights = null)
{
    public IReadOnlyList<string> Features => Entries.Where(ReleaseNotesService.IsFeature).ToArray();
    public IReadOnlyList<string> Improvements => Entries.Where(ReleaseNotesService.IsImprovement).ToArray();
    public IReadOnlyList<string> Fixes => Entries.Where(ReleaseNotesService.IsFix).ToArray();
    public IReadOnlyList<string> General => Entries.Where(entry => !ReleaseNotesService.IsFeature(entry) && !ReleaseNotesService.IsImprovement(entry) && !ReleaseNotesService.IsFix(entry)).ToArray();

    /// <summary>True when the release publishes explicit highlight lists.</summary>
    public bool HasHighlights { get; init; }

    public IReadOnlyList<string> DisplayFeatures => HasHighlights ? FeatureHighlights ?? Array.Empty<string>() : Features.Take(4).ToArray();
    public IReadOnlyList<string> DisplayImprovements => HasHighlights ? ImprovementHighlights ?? Array.Empty<string>() : Improvements.Take(4).ToArray();
    public IReadOnlyList<string> DisplayFixes => HasHighlights ? FixHighlights ?? Array.Empty<string>() : Fixes.Take(4).ToArray();
}

/// <summary>Loads the embedded release notes and provides lightweight category classification.</summary>
public static class ReleaseNotesService
{
    public static string DisplayEntry(string entry)=>System.Text.RegularExpressions.Regex.Replace(entry,
        @"^(?:New Features|Improvements|Bug Fixes|Fixes|Performance & Stability|Compatibility|إضافات جديدة|تحسينات|إصلاحات|الأداء والاستقرار|التوافق)\s*[:：]\s*",
        "",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    public static IReadOnlyList<ReleaseNote> Load() => Load("en");

    public static IReadOnlyList<ReleaseNote> Load(bool arabic) => Load(arabic ? "ar" : "en");

    public static IReadOnlyList<ReleaseNote> Load(string languageKey)
    {        var result = new List<ReleaseNote>();
        try
        {
            using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/PolarUninstaller;component/Assets/ReleaseNotes.json"))?.Stream;
            if (stream is null) return result;
            using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8);
            using var json = JsonDocument.Parse(reader.ReadToEnd());
            string key = languageKey == "ar" ? "ar" : "en";
            foreach (var release in json.RootElement.EnumerateArray())
            {
                string version = release.GetProperty("version").GetString() ?? "";
                var entries = new List<string>();
                if (release.TryGetProperty(key, out var array) && array.ValueKind == JsonValueKind.Array)
                    foreach (var item in array.EnumerateArray())
                        if (item.ValueKind == JsonValueKind.String && item.GetString() is string text && !string.IsNullOrWhiteSpace(text))
                            entries.Add(text);

                var featureHighlights = new List<string>();
                var improvementHighlights = new List<string>();
                var fixHighlights = new List<string>();
                bool hasHighlights = false;
                if (release.TryGetProperty("highlights", out var highlights) &&
                    highlights.ValueKind == JsonValueKind.Object &&
                    highlights.TryGetProperty(key, out var languageHighlights) &&
                    languageHighlights.ValueKind == JsonValueKind.Object)
                {
                    hasHighlights = true;
                    ReadHighlightArray(languageHighlights, "features", featureHighlights);
                    ReadHighlightArray(languageHighlights, "improvements", improvementHighlights);
                    ReadHighlightArray(languageHighlights, "fixes", fixHighlights);
                }

                result.Add(new ReleaseNote(version, entries, featureHighlights, improvementHighlights, fixHighlights)
                {
                    HasHighlights = hasHighlights
                });
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or UriFormatException)
        {
        }
        return result;
    }

    /// <summary>
    /// Finds the release note that belongs to the installed version. The What's
    /// New page uses this instead of the first entry so older releases can never
    /// be shown as if they were current.
    /// </summary>
    public static ReleaseNote? FindCurrent(IEnumerable<ReleaseNote> releases, string installedVersion)
    {
        string target = NormalizeVersion(installedVersion);
        return releases.FirstOrDefault(release =>
            string.Equals(NormalizeVersion(release.Version), target, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Loads the release note for the currently running version.</summary>
    public static ReleaseNote? LoadCurrent(bool arabic) => FindCurrent(Load(arabic), AppInfo.Version);

    private static string NormalizeVersion(string? version) =>
        (version ?? string.Empty).Trim().TrimStart('v', 'V').Trim();

    private static void ReadHighlightArray(JsonElement parent, string propertyName, List<string> target)
    {
        if (!parent.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array) return;
        foreach (var item in array.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String && item.GetString() is string text && !string.IsNullOrWhiteSpace(text))
                target.Add(text);
    }

    public static bool IsFix(string entry)
    {
        string text = entry.ToLowerInvariant();
        return text.Contains("fix") || text.Contains("bug") || text.Contains("issue") ||
               text.Contains("إصلاح") || text.Contains("تصحيح") || text.Contains("مشكلة");
    }

    public static bool IsFeature(string entry)
    {
        if (IsFix(entry)) return false;
        string text = entry.ToLowerInvariant();
        return text.Contains("add") || text.Contains("new") || text.Contains("introduc") ||
               text.Contains("redesign") || text.Contains("feature") ||
               text.Contains("إضافة") || text.Contains("جديد") || text.Contains("إعادة تصميم") ||
               text.Contains("ما الجديد");
    }

    public static bool IsImprovement(string entry) => !IsFix(entry) && !IsFeature(entry);
}
