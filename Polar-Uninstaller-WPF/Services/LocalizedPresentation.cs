using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace PolarUninstallerWpf.Services;

/// <summary>Updates language-sensitive properties without replacing their controls or operation owners.</summary>
internal sealed class LocalizedPresentation
{
    private readonly Dictionary<(DependencyObject, DependencyProperty), Func<bool, string>> values = new();
    private bool arabic;

    public void Set(DependencyObject owner, DependencyProperty property, Func<bool, string> value)
    {
        values[(owner, property)] = value;
        Apply(owner, property, value(arabic));
    }

    public void Text(TextBlock owner, string en, string ar) => Set(owner, TextBlock.TextProperty, language => BidiText.Localize(language, en, ar));

    public void Refresh(bool language)
    {
        arabic = language;
        foreach (var (key, value) in values)
            Apply(key.Item1, key.Item2, value(language));
    }

    private static void Apply(DependencyObject owner, DependencyProperty property, string value)
    {
        if (owner is TextBlock text && property == TextBlock.TextProperty)
        {
            SetRuns(text, value);
            return;
        }

        owner.SetCurrentValue(property, value);
    }

    public static void SetLeftToRight(TextBlock text, string value, bool arabic)
    {
        text.Text = value;
        // The plain Text value stays available to bindings and accessibility.
        if (text.Inlines.FirstInline is not Run run) return;
        if (arabic) run.FlowDirection = FlowDirection.LeftToRight;
        else run.ClearValue(Inline.FlowDirectionProperty);
    }

    // WPF text layout needs explicit run direction for neutral characters
    // such as a trailing percent sign or a colon attached to a drive letter.
    public static void SetRuns(TextBlock text, string value)
    {
        if (!value.Contains('\u2066'))
        {
            text.Text = value;
            return;
        }

        text.Inlines.Clear();
        int start = 0;
        while (start < value.Length)
        {
            int open = value.IndexOf('\u2066', start);
            int close = open < 0 ? -1 : value.IndexOf('\u2069', open + 1);
            if (close < 0)
            {
                text.Inlines.Add(value[start..]);
                break;
            }

            if (open > start) text.Inlines.Add(value[start..open]);
            text.Inlines.Add(new Run(value[(open + 1)..close]) { FlowDirection = FlowDirection.LeftToRight });
            start = close + 1;
        }
    }
}
