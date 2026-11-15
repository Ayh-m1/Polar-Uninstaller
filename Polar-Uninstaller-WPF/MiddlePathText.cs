using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PolarUninstallerWpf;

public sealed class MiddlePathText:TextBlock
{
    public static readonly DependencyProperty FullPathProperty=DependencyProperty.Register(nameof(FullPath),typeof(string),typeof(MiddlePathText),new PropertyMetadata("",(d,_)=>((MiddlePathText)d).Refresh()));
    public string FullPath{get=>(string)GetValue(FullPathProperty);set=>SetValue(FullPathProperty,value);}
    public MiddlePathText(){FlowDirection=FlowDirection.LeftToRight;TextAlignment=TextAlignment.Left;TextTrimming=TextTrimming.CharacterEllipsis;TextWrapping=TextWrapping.NoWrap;SizeChanged+=(_,_)=>Refresh();}
    private void Refresh()
    {
        string path=FullPath??"";ToolTip=path;
        if(ActualWidth<=0){Text=path;return;}
        double Width(string value)=>new FormattedText(value,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface(FontFamily,FontStyle,FontWeight,FontStretch),FontSize,Foreground??Brushes.White,VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace;
        if(Width(path)<=ActualWidth){Text=path;return;}
        var starts=StringInfo.ParseCombiningCharacters(path);int lo=0,hi=starts.Length;
        string Clip(int length){int left=length/2,right=length-left;return path[..(left==0?0:starts[left])]+"…"+(right==0?"":path[starts[starts.Length-right]..]);}
        while(lo<hi){int mid=(lo+hi+1)/2;if(Width(Clip(mid))<=ActualWidth)lo=mid;else hi=mid-1;}Text=Clip(lo);
    }
}
