using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace PolarUninstallerWpf;

/// <summary>Read-only, selectable text for information outside interactive rows and buttons.</summary>
public sealed class SelectableText : RichTextBox
{
    public SelectableText()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = true;
        SetResourceReference(FontFamilyProperty, "InterfaceFont");
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Document.PagePadding = new Thickness(0);
        Document.Blocks.Clear();
        Document.Blocks.Add(new Paragraph { Margin = new Thickness(0) });
    }

    public InlineCollection Inlines => ((Paragraph)Document.Blocks.FirstBlock!).Inlines;

    public static readonly DependencyProperty TextAlignmentProperty = DependencyProperty.Register(
        nameof(TextAlignment), typeof(TextAlignment), typeof(SelectableText),
        new PropertyMetadata(TextAlignment.Left, (owner, change) => ((SelectableText)owner).Document.TextAlignment = (TextAlignment)change.NewValue));

    public TextAlignment TextAlignment
    {
        get => (TextAlignment)GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    public string Text
    {
        get => new TextRange(Document.Blocks.FirstBlock!.ContentStart, Document.Blocks.FirstBlock.ContentEnd).Text;
        set
        {
            Inlines.Clear();
            if (!string.IsNullOrEmpty(value)) Inlines.Add(value);
        }
    }
}
