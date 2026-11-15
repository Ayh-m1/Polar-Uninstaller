using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace PolarUninstallerWpf.Services;

public static class UiTree
{
    // Mouse events can originate from a Run/Hyperlink, which is not a Visual.
    public static DependencyObject? Parent(DependencyObject source) => source switch
    {
        ContentElement content => ContentOperations.GetParent(content)
            ?? (content as FrameworkContentElement)?.Parent,
        Visual or Visual3D => VisualTreeHelper.GetParent(source) ?? LogicalTreeHelper.GetParent(source),
        _ => LogicalTreeHelper.GetParent(source)
    };
}
