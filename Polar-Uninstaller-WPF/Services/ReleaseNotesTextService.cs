using System.Text;
using System.Text.RegularExpressions;

namespace PolarUninstallerWpf.Services;

public sealed record LocalizedReleaseNotes(string Text, bool LanguageFallback, bool FallbackIsArabic = false);

/// <summary>
/// Chooses the part of a GitHub release body that matches the application
/// language. Release notes often contain an English and an Arabic section;
/// when only one language is published the complete body is shown instead of
/// hiding the update information.
/// </summary>
public static class ReleaseNotesTextService
{
    private static readonly Regex SectionHeading = new(@"^(#{1,6})[ \t]+(.+?)[ \t#]*$", RegexOptions.CultureInvariant);
    private static readonly string[] ArabicMarkers = ["العربية", "عربي", "Arabic"];
    private static readonly string[] EnglishMarkers = ["English", "الإنجليزية", "الانجليزية"];

    public static LocalizedReleaseNotes Select(string? body, bool arabic)
    {
        string normalized = (body ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (normalized.Length == 0) return new("", false);

        string[] lines = normalized.Split('\n');
        if (arabic)
        {
            var section = FindSection(lines, ArabicMarkers);
            if (section is { } arabicSection) return new(Join(lines, arabicSection.Start, arabicSection.End).Trim(), false);
            // A bilingual body without an Arabic heading still contains Arabic
            // text, so showing it is better than instructing the user to switch.
            if (ContainsArabic(normalized)) return new(normalized, false);
            return new(normalized, true, false);
        }

        var english = FindSection(lines, EnglishMarkers);
        if (english is { } englishSection) return new(Join(lines, englishSection.Start, englishSection.End).Trim(), false);

        // Some releases label only the Arabic section. Everything outside that
        // section is the English (or neutral) part.
        var arabicOnly = FindSection(lines, ArabicMarkers);
        if (arabicOnly is { } arabicRange)
        {
            string before = Join(lines, 0, arabicRange.Start).Trim();
            string after = Join(lines, arabicRange.End, lines.Length).Trim();
            string remaining = (before + "\n" + after).Trim();
            if (remaining.Length > 0 && !IsMostlyArabic(remaining))
                return new(remaining, false);
            return new(Join(lines, arabicRange.Start, arabicRange.End).Trim(), true, true);
        }

        if (ContainsArabic(normalized))
            return new(normalized, true, true);
        return new(normalized, false);
    }

    private static (int Start, int End)? FindSection(string[] lines, string[] markers)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            var match = SectionHeading.Match(lines[i]);
            if (!match.Success) continue;
            string title = match.Groups[2].Value;
            if (!markers.Any(marker => title.Contains(marker, StringComparison.OrdinalIgnoreCase))) continue;

            int level = match.Groups[1].Value.Length;
            int end = lines.Length;
            for (int j = i + 1; j < lines.Length; j++)
            {
                var next = SectionHeading.Match(lines[j]);
                if (next.Success && next.Groups[1].Value.Length <= level) { end = j; break; }
            }
            return (i + 1, end);
        }
        return null;
    }

    private static string Join(string[] lines, int start, int end)
    {
        if (start >= end) return "";
        var builder = new StringBuilder();
        for (int i = start; i < end; i++) builder.Append(lines[i]).Append('\n');
        return builder.ToString();
    }

    public static string StripDuplicateHeading(string? text, bool arabic)
    {
        if (string.IsNullOrWhiteSpace(text)) return text ?? string.Empty;
        string[] lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        string[] needles = arabic
            ? new[] { "ما Nuevo", "ما الجديد", "what's new", "whats new" }
            : new[] { "what's new", "whats new", "ما Nuevo", "ما الجديد" };

        int matchIndex = -1;
        int nonEmpty = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].Trim();
            if (trimmed.Length == 0) continue;
            if (++nonEmpty > 10) break;

            var match = SectionHeading.Match(trimmed);
            if (!match.Success) continue;
            string title = match.Groups[2].Value.Trim().ToLowerInvariant();
            if (needles.Any(needle => title.Contains(needle.ToLowerInvariant(), StringComparison.Ordinal)))
            {
                matchIndex = i;
                break;
            }
        }

        if (matchIndex < 0) return text ?? string.Empty;

        var result = new List<string>(lines);
        result.RemoveAt(matchIndex);
        if (matchIndex < result.Count && result[matchIndex].Trim().Length == 0)
            result.RemoveAt(matchIndex);
        return string.Join("\n", result).Trim('\n');
    }

    private static bool ContainsArabic(string text) =>
        text.Any(c =>
            (c >= '\u0600' && c <= '\u06FF') ||
            (c >= '\u0750' && c <= '\u077F') ||
            (c >= '\uFB50' && c <= '\uFDFF') ||
            (c >= '\uFE70' && c <= '\uFEFF'));

    private static bool IsMostlyArabic(string text)
    {
        int arabic = 0, latin = 0;
        foreach (char c in text)
        {
            if (c >= '\u0600' && c <= '\u06FF') arabic++;
            else if (char.IsAsciiLetter(c)) latin++;
        }
        return arabic > 0 && arabic >= latin;
    }

    private static bool ContainsLatinLetter(string text) => text.Any(char.IsAsciiLetter);
}
