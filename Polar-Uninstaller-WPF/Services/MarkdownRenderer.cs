using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace PolarUninstallerWpf.Services;

public enum MarkdownBlockKind { Heading, Paragraph, Bullet, OrderedItem, Quote, Code, Divider }
public sealed record MarkdownBlock(MarkdownBlockKind Kind, string Text, int Level = 0, int Number = 0, string? Language = null);

public enum MarkdownInlineKind { Text, Bold, Italic, Code, Link }
public sealed record MarkdownInline(MarkdownInlineKind Kind, string Text, string? Url = null);

/// <summary>
/// Converts the GitHub-flavoured Markdown returned by the Releases API into a
/// safe WPF FlowDocument. Only plain text and a deliberately small set of
/// formatting primitives are produced, so release-note content can never run
/// scripts or inject executable markup.
/// </summary>
public static class MarkdownRenderer
{
    private static readonly Regex HeadingPattern = new(@"^(#{1,6})[ \t]+(.+?)[ \t#]*$", RegexOptions.CultureInvariant);
    private static readonly Regex BulletPattern = new(@"^([ \t]*)[-*+][ \t]+(.*)$", RegexOptions.CultureInvariant);
    private static readonly Regex OrderedPattern = new(@"^([ \t]*)(\d{1,4})[.)][ \t]+(.*)$", RegexOptions.CultureInvariant);
    private static readonly Regex DividerPattern = new(@"^[ \t]{0,3}([-*_])(?:[ \t]*\1){2,}[ \t]*$", RegexOptions.CultureInvariant);
    private static readonly Regex ScriptPattern = new(@"<(script|style)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex TagPattern = new(@"</?[A-Za-z][^>]{0,400}>", RegexOptions.CultureInvariant);
    private static readonly Regex BreakPattern = new(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static IReadOnlyList<MarkdownBlock> Parse(string? markdown)
    {
        var blocks = new List<MarkdownBlock>();
        if (string.IsNullOrWhiteSpace(markdown)) return blocks;

        string[] lines = (markdown ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var paragraph = new StringBuilder();
        bool inCode = false;
        var code = new StringBuilder();
        string codeLanguage = "";

        void FlushParagraph()
        {
            if (paragraph.Length == 0) return;
            string text = paragraph.ToString().Trim();
            if (text.Length > 0) blocks.Add(new(MarkdownBlockKind.Paragraph, text));
            paragraph.Clear();
        }

        foreach (string rawLine in lines)
        {
            string line = rawLine ?? "";

            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (inCode)
                {
                    blocks.Add(new(MarkdownBlockKind.Code, code.ToString().TrimEnd(), Language: codeLanguage));
                    code.Clear();
                    codeLanguage = "";
                    inCode = false;
                }
                else
                {
                    FlushParagraph();
                    inCode = true;
                    codeLanguage = line.Trim().Substring(3).Trim();
                }
                continue;
            }

            if (inCode)
            {
                code.Append(line).Append('\n');
                continue;
            }

            string cleaned = SanitizeLine(line);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                FlushParagraph();
                continue;
            }

            var heading = HeadingPattern.Match(cleaned);
            if (heading.Success)
            {
                FlushParagraph();
                blocks.Add(new(MarkdownBlockKind.Heading, CleanInlineText(heading.Groups[2].Value), heading.Groups[1].Value.Length));
                continue;
            }

            if (DividerPattern.IsMatch(cleaned))
            {
                FlushParagraph();
                blocks.Add(new(MarkdownBlockKind.Divider, ""));
                continue;
            }

            var bullet = BulletPattern.Match(cleaned);
            if (bullet.Success)
            {
                FlushParagraph();
                string text = bullet.Groups[2].Value.Trim();
                int level = IndentLevel(bullet.Groups[1].Value);
                if (text.StartsWith("[ ] ", StringComparison.Ordinal)) text = "☐  " + text[4..];
                else if (text.StartsWith("[x] ", StringComparison.OrdinalIgnoreCase)) text = "☑  " + text[4..];
                blocks.Add(new(MarkdownBlockKind.Bullet, CleanInlineText(text), level));
                continue;
            }

            var ordered = OrderedPattern.Match(cleaned);
            if (ordered.Success)
            {
                FlushParagraph();
                int number = int.TryParse(ordered.Groups[2].Value, out int parsed) ? parsed : 1;
                blocks.Add(new(MarkdownBlockKind.OrderedItem, CleanInlineText(ordered.Groups[3].Value.Trim()), IndentLevel(ordered.Groups[1].Value), number));
                continue;
            }

            if (cleaned.TrimStart().StartsWith('>'))
            {
                FlushParagraph();
                string text = cleaned.TrimStart()[1..].TrimStart();
                blocks.Add(new(MarkdownBlockKind.Quote, CleanInlineText(text)));
                continue;
            }

            if (paragraph.Length > 0) paragraph.Append(' ');
            paragraph.Append(cleaned.Trim());
        }

        if (inCode && code.Length > 0) blocks.Add(new(MarkdownBlockKind.Code, code.ToString().TrimEnd(), Language: codeLanguage));
        FlushParagraph();
        return blocks;
    }

    /// <summary>Parses the small inline subset used by GitHub release notes.</summary>
    public static IReadOnlyList<MarkdownInline> ParseInline(string? text)
    {
        var result = new List<MarkdownInline>();
        if (string.IsNullOrEmpty(text)) return result;

        var buffer = new StringBuilder();
        void Flush()
        {
            if (buffer.Length == 0) return;
            result.Add(new(MarkdownInlineKind.Text, buffer.ToString()));
            buffer.Clear();
        }

        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '\\' && i + 1 < text.Length && IsEscapable(text[i + 1]))
            {
                buffer.Append(text[i + 1]);
                i += 2;
                continue;
            }

            if (c == '`')
            {
                int close = text.IndexOf('`', i + 1);
                if (close > i + 1)
                {
                    Flush();
                    result.Add(new(MarkdownInlineKind.Code, text[(i + 1)..close]));
                    i = close + 1;
                    continue;
                }
            }

            if (c == '[')
            {
                int labelEnd = text.IndexOf(']', i + 1);
                if (labelEnd > i + 1 && labelEnd + 1 < text.Length && text[labelEnd + 1] == '(')
                {
                    int urlEnd = text.IndexOf(')', labelEnd + 2);
                    if (urlEnd > labelEnd + 2)
                    {
                        string label = text[(i + 1)..labelEnd].Trim();
                        string url = text[(labelEnd + 2)..urlEnd].Trim();
                        if (TrySafeUrl(url, out string safeUrl))
                        {
                            Flush();
                            result.Add(new(MarkdownInlineKind.Link, label.Length == 0 ? safeUrl : label, safeUrl));
                            i = urlEnd + 1;
                            continue;
                        }
                    }
                }
            }

            if ((c == '*' || c == '_') && i + 1 < text.Length && text[i + 1] == c)
            {
                int close = text.IndexOf(new string(c, 2), i + 2, StringComparison.Ordinal);
                if (close > i + 2)
                {
                    Flush();
                    result.Add(new(MarkdownInlineKind.Bold, text[(i + 2)..close]));
                    i = close + 2;
                    continue;
                }
            }

            if (c is '*' or '_')
            {
                int close = text.IndexOf(c, i + 1);
                if (close > i + 1 && close < text.Length)
                {
                    Flush();
                    result.Add(new(MarkdownInlineKind.Italic, text[(i + 1)..close]));
                    i = close + 1;
                    continue;
                }
            }

            buffer.Append(c);
            i++;
        }

        Flush();
        return result;
    }

    public static FlowDocument BuildDocument(string? markdown, bool arabic)
    {
        // The document direction follows the notes that are actually displayed.
        // Arabic mode may receive English-only notes as a fallback, and English
        // mode may receive an Arabic-only release body; both must stay readable.
        bool rightToLeft = ContainsArabic(markdown);
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontSize = 14,
            LineHeight = 22,
            TextAlignment = rightToLeft ? TextAlignment.Right : TextAlignment.Left,
            FlowDirection = rightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight
        };
        document.SetResourceReference(TextElement.ForegroundProperty, "ChromeText");
        document.SetResourceReference(TextElement.FontFamilyProperty, "InterfaceFont");

        foreach (var block in Parse(markdown))
            document.Blocks.Add(BuildBlock(block, rightToLeft));

        if (document.Blocks.Count == 0)
            document.Blocks.Add(BuildMessageParagraph(arabic ? "لا توجد ملاحظات إصدار متاحة." : "No release notes are available.", arabic));

        return document;
    }

    /// <summary>True when the text contains Arabic script and should flow right-to-left.</summary>
    public static bool ContainsArabic(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (char c in text)
        {
            if (c is >= '\u0600' and <= '\u06FF') return true;
            if (c is >= '\u0750' and <= '\u077F') return true;
            if (c is >= '\uFB50' and <= '\uFDFF') return true;
            if (c is >= '\uFE70' and <= '\uFEFF') return true;
        }
        return false;
    }

    public static FlowDocument BuildMessageDocument(string message, bool arabic)
    {
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontSize = 14,
            LineHeight = 22,
            TextAlignment = arabic ? TextAlignment.Right : TextAlignment.Left,
            FlowDirection = arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight
        };
        document.SetResourceReference(TextElement.ForegroundProperty, "HomeDimBrush");
        document.SetResourceReference(TextElement.FontFamilyProperty, "InterfaceFont");
        document.Blocks.Add(BuildMessageParagraph(message, arabic));
        return document;
    }

    private static Paragraph BuildMessageParagraph(string message, bool arabic)
    {
        var paragraph = new Paragraph(new Run(message))
        {
            FontStyle = FontStyles.Italic,
            TextAlignment = arabic ? TextAlignment.Right : TextAlignment.Left
        };
        paragraph.SetResourceReference(TextElement.ForegroundProperty, "HomeDimBrush");
        return paragraph;
    }

    private static Block BuildBlock(MarkdownBlock block, bool rightToLeft)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0, 0, 0, 8),
            TextAlignment = rightToLeft ? TextAlignment.Right : TextAlignment.Left
        };

        switch (block.Kind)
        {
            case MarkdownBlockKind.Heading:
                paragraph.FontWeight = FontWeights.SemiBold;
                paragraph.FontSize = block.Level switch { 1 => 22, 2 => 18, 3 => 16, _ => 14.5 };
                paragraph.Margin = new Thickness(0, block.Level <= 2 ? 4 : 12, 0, block.Level <= 2 ? 10 : 6);
                paragraph.KeepWithNext = true;
                if (block.Level <= 2) paragraph.SetResourceReference(TextElement.ForegroundProperty, "ChromeAccent");
                AddInlines(paragraph, block.Text);
                break;

            case MarkdownBlockKind.Bullet:
                paragraph.Margin = new Thickness(8 + block.Level * 16, 0, 0, 5);
                paragraph.TextIndent = -14;
                paragraph.Inlines.Add(new Run("•  ") { FontWeight = FontWeights.SemiBold });
                AddInlines(paragraph, block.Text);
                break;

            case MarkdownBlockKind.OrderedItem:
                paragraph.Margin = new Thickness(8 + block.Level * 16, 0, 0, 5);
                paragraph.TextIndent = -22;
                paragraph.Inlines.Add(new Run($"{block.Number}.  ") { FontWeight = FontWeights.SemiBold });
                AddInlines(paragraph, block.Text);
                break;

            case MarkdownBlockKind.Quote:
                paragraph.FontStyle = FontStyles.Italic;
                paragraph.Padding = new Thickness(10, 2, 10, 2);
                paragraph.Margin = new Thickness(0, 2, 0, 10);
                paragraph.BorderThickness = rightToLeft ? new Thickness(0, 0, 3, 0) : new Thickness(3, 0, 0, 0);
                paragraph.SetResourceReference(Block.BorderBrushProperty, "ChromeAccent");
                paragraph.SetResourceReference(Block.BackgroundProperty, "HomeCardBrush");
                AddInlines(paragraph, block.Text);
                break;

            case MarkdownBlockKind.Code:
                paragraph.FontFamily = new FontFamily("Consolas");
                paragraph.FontSize = 12.5;
                paragraph.Padding = new Thickness(10);
                paragraph.Margin = new Thickness(0, 2, 0, 10);
                paragraph.SetResourceReference(Block.BackgroundProperty, "ChromeBorder");
                AddCodeLines(paragraph, block.Text);
                break;

            case MarkdownBlockKind.Divider:
                paragraph.Margin = new Thickness(0, 10, 0, 10);
                paragraph.BorderThickness = new Thickness(0, 1, 0, 0);
                paragraph.SetResourceReference(Block.BorderBrushProperty, "ChromeBorder");
                break;

            default:
                AddInlines(paragraph, block.Text);
                break;
        }

        return paragraph;
    }

    private static void AddInlines(Paragraph paragraph, string text)
    {
        foreach (var span in ParseInline(text))
        {
            switch (span.Kind)
            {
                case MarkdownInlineKind.Bold:
                    paragraph.Inlines.Add(new Bold(new Run(span.Text)));
                    break;

                case MarkdownInlineKind.Italic:
                    paragraph.Inlines.Add(new Italic(new Run(span.Text)));
                    break;

                case MarkdownInlineKind.Code:
                    var code = new Run(span.Text) { FontFamily = new FontFamily("Consolas"), FontSize = 12.5 };
                    code.SetResourceReference(TextElement.BackgroundProperty, "ChromeBorder");
                    paragraph.Inlines.Add(code);
                    break;

                case MarkdownInlineKind.Link:
                    var hyperlink = new Hyperlink(new Run(span.Text))
                    {
                        NavigateUri = new Uri(span.Url!, UriKind.Absolute),
                        ToolTip = span.Url,
                        Cursor = Cursors.Hand
                    };
                    hyperlink.SetResourceReference(TextElement.ForegroundProperty, "ChromeAccent");
                    hyperlink.RequestNavigate += (_, e) =>
                    {
                        if (TrySafeUrl(e.Uri?.AbsoluteUri, out string safe)) OpenInBrowser(safe);
                        e.Handled = true;
                    };
                    paragraph.Inlines.Add(hyperlink);
                    break;

                default:
                    AddTextWithBreaks(paragraph, span.Text);
                    break;
            }
        }
    }

    private static void AddCodeLines(Paragraph paragraph, string text)
    {
        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new Run(lines[i]));
        }
    }

    private static void AddTextWithBreaks(Paragraph paragraph, string text)
    {
        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new Run(lines[i]));
        }
    }

    private static int IndentLevel(string whitespace)
    {
        int spaces = 0;
        foreach (char c in whitespace) spaces += c == '\t' ? 4 : 1;
        return Math.Clamp(spaces / 2, 0, 4);
    }

    private static bool IsEscapable(char c) => c is '\\' or '`' or '*' or '_' or '[' or ']' or '(' or ')' or '#' or '-' or '+' or '.' or '>' or '!';

    private static bool TrySafeUrl(string? url, out string safe)
    {
        safe = "";
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return false;
        safe = uri.AbsoluteUri;
        return true;
    }

    private static void OpenInBrowser(string url)
    {
        try
        {
            var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            start.ArgumentList.Add(url);
            Process.Start(start);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            WpfUpdateService.Log("Could not open release-note link: " + ex.Message);
        }
    }

    private static string SanitizeLine(string line)
    {
        string text = ScriptPattern.Replace(line, "");
        text = BreakPattern.Replace(text, "\n");
        text = TagPattern.Replace(text, "");
        text = WebUtility.HtmlDecode(text);
        var clean = new StringBuilder(text.Length);
        foreach (char c in text)
            if (c is '\n' or '\t' || !char.IsControl(c)) clean.Append(c);
        return clean.ToString();
    }

    private static string CleanInlineText(string text) =>
        WebUtility.HtmlDecode(TagPattern.Replace(text, "")).Trim();
}
