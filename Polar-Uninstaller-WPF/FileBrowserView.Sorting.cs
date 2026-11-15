using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using PolarUninstallerWpf.Services;

namespace PolarUninstallerWpf;

public partial class FileBrowserView
{
    private SortMenuButton? sortButton;
    private string? cycleColumn;
    private int cycleStep;
    private static bool DefaultAscending(string key)=>key is nameof(BrowserEntry.Name) or nameof(BrowserEntry.Type);
    private string SortLabel(string key)=>T(key==nameof(BrowserEntry.Name)?"Name":key==nameof(BrowserEntry.Size)?"Size":key==nameof(BrowserEntry.Type)?"Type":"Modified");
    private void AddSortControl()
    {
        sortButton=new SortMenuButton{Margin=new Thickness(0,0,6,6),MinHeight=34,Padding=new Thickness(10,5,10,5)};
        sortButton.Configure(arabic,new[]{nameof(BrowserEntry.Name),nameof(BrowserEntry.Size),nameof(BrowserEntry.Type),nameof(BrowserEntry.Modified)}.Select(key=>new SortChoice(key,SortLabel(key),DefaultAscending(key))).ToArray(),sort,direction==ListSortDirection.Ascending,(Style)Resources["ExplorerActionButton"],(Style)Resources["ExplorerMenuItem"],(Style)Resources["ExplorerFocus"]);
        sortButton.SortRequested+=async(key,asc)=>{cycleColumn=null;cycleStep=0;await RunBrowserAction(()=>SetSortAsync(key,asc));};
        Toolbar.Children.Add(sortButton);UpdateSortIndicators();
    }
    private (string Key,bool Ascending) NextHeaderSort(string key)
    {
        int step=cycleColumn==key?cycleStep+1:1;
        return step>=3?(nameof(BrowserEntry.Size),false):(key,step==1?DefaultAscending(key):!DefaultAscending(key));
    }
    private async Task SetSortAsync(string key,bool asc)
    {
        sort=key;direction=asc?ListSortDirection.Ascending:ListSortDirection.Descending;
        ApplySort();SaveSortPreference();
        System.Diagnostics.Debug.WriteLine($"File Browser sort: {key} {direction}");
        if(key==nameof(BrowserEntry.Size)&&(!autoSizeRunning||autoSizeGeneration!=navigationGeneration))await AutoSizeFoldersAsync(items,navigationGeneration);
    }
    private void UpdateHeader(GridViewColumn column,string key,TextBlock? arrow)
    {
        if(column.Header is not GridViewColumnHeader header||arrow==null)return;
        bool active=sort==key;
        arrow.Text=active?(direction==ListSortDirection.Ascending?"▲":"▼"):"↕";
        arrow.Opacity=active?1:.55;arrow.SetResourceReference(TextBlock.ForegroundProperty,active?"ChromeAccent":"HomeTextBrush");
        header.FontWeight=active?FontWeights.SemiBold:FontWeights.Normal;
        header.FlowDirection=key is nameof(BrowserEntry.Size) or nameof(BrowserEntry.Modified)?FlowDirection.LeftToRight:FlowDirection;
        header.HorizontalContentAlignment=key==nameof(BrowserEntry.Size)?HorizontalAlignment.Right:HorizontalAlignment.Left;
        var next=NextHeaderSort(key);
        string action=T("SortNext",SortLabel(next.Key),T(next.Ascending?(next.Key==nameof(BrowserEntry.Size)?"SmallestFirst":next.Key==nameof(BrowserEntry.Modified)?"OldestFirst":"Ascending"):(next.Key==nameof(BrowserEntry.Size)?"LargestFirst":next.Key==nameof(BrowserEntry.Modified)?"NewestFirst":"Descending")));
        if(header.ToolTip is not ToolTip previous||!Equals(previous.Content,action))
        {bool open=header.ToolTip is ToolTip old&&old.IsOpen;if(header.ToolTip is ToolTip oldTip)oldTip.IsOpen=false;header.ToolTip=new ToolTip{Content=action,PlacementTarget=header,IsOpen=open};}
        ToolTipService.SetShowOnDisabled(header,true);
        AutomationProperties.SetHelpText(header,action);AutomationProperties.SetItemStatus(header,active?(direction==ListSortDirection.Ascending?"ascending":"descending"):"none");
    }
    private void ClearSearchClick(object sender,RoutedEventArgs e){Search.Clear();Search.Focus();}
}
