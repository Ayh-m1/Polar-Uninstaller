using System.Text.RegularExpressions;

namespace PolarUninstallerWpf.Services;

/// <summary>Display-only isolation. Never use this output as a path or executable argument.</summary>
public static class BidiText
{
    // Keep grouping commas, attached percent signs, timestamps and drive
    // punctuation within the run. Explicitly isolate paths with Arabic names.
    private static readonly Regex Latin = new(
        @"[\u2066-\u2068][^\u2069]*\u2069|\.?[A-Za-z0-9]+(?:[,_./:\\+%=\-][A-Za-z0-9]+| +[A-Za-z0-9]+)*[:\\%]?",
        RegexOptions.Compiled);

    public static string IsolateLatin(string text) =>
        Latin.Replace(text, match => match.Value[0] is >= '\u2066' and <= '\u2068' ? match.Value : DisplayFormat.Isolate(match.Value));

    public static string Localize(bool arabic, string english, string translation) =>
        arabic ? IsolateLatin(translation) : english;
}
