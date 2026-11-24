using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PolarUninstallerWpf.Services;

namespace PolarUninstallerWpf;

public sealed class PermanentDeleteDialog : Window
{
    public Button CancelButton {get;}
    public Button ConfirmButton {get;}
    public PermanentDeleteDialog(bool arabic,int count,long knownBytes,bool incomplete,IReadOnlyList<string> paths,Style danger,Style normal,bool hiddenOrSystem=false)
    {
        string T(string key,params object[] values)=>BrowserText.Get(key,arabic,values);
        Title=T("PermanentTitle");Width=650;MinWidth=460;SizeToContent=SizeToContent.Height;MaxHeight=650;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;ResizeMode=ResizeMode.CanResize;
        FlowDirection=arabic?FlowDirection.RightToLeft:FlowDirection.LeftToRight;
        SetResourceReference(BackgroundProperty,"ChromeBackground");SetResourceReference(ForegroundProperty,"HomeTextBrush");SetResourceReference(FontFamilyProperty,"InterfaceFont");
        var root=new StackPanel{Margin=new Thickness(22)};KeyboardNavigation.SetTabNavigation(root,KeyboardNavigationMode.Cycle);
        root.Children.Add(new TextBlock{Text=T("PermanentWarning"),FontSize=17,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
        root.Children.Add(new TextBlock{Text=T("SelectedCount",count),Margin=new Thickness(0,14,0,4)});
        if(hiddenOrSystem)root.Children.Add(new TextBlock{Text=T("HiddenDeleteWarning"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)});
        var size=new StackPanel{Orientation=Orientation.Horizontal};size.Children.Add(new TextBlock{Text=T("KnownSize")+": "});
        size.Children.Add(new TextBlock{Text=BrowserEntry.FormatBytes(knownBytes),FlowDirection=FlowDirection.LeftToRight});root.Children.Add(size);
        if(incomplete)root.Children.Add(new TextBlock{Text=T("UnknownSizes"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)});
        root.Children.Add(new TextBox{Text=string.Join(Environment.NewLine,paths),IsReadOnly=true,TextWrapping=TextWrapping.Wrap,FlowDirection=FlowDirection.LeftToRight,MaxHeight=230,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(0,14,0,18),Background=Brushes.Transparent,Foreground=(Brush)Application.Current.Resources["HomeTextBrush"],BorderBrush=(Brush)Application.Current.Resources["HomeBorderBrush"]});
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        CancelButton=new Button{Content=T("Cancel"),IsCancel=true,IsDefault=true,Style=normal,MinWidth=100,Margin=new Thickness(0,0,12,0)};
        ConfirmButton=new Button{Content=T("PermanentConfirm"),Style=danger,MinWidth=130};
        CancelButton.Click+=(_,_)=>DialogResult=false;ConfirmButton.Click+=(_,_)=>DialogResult=true;
        System.Windows.Automation.AutomationProperties.SetName(CancelButton,T("Cancel"));System.Windows.Automation.AutomationProperties.SetName(ConfirmButton,T("PermanentConfirm"));
        actions.Children.Add(CancelButton);actions.Children.Add(ConfirmButton);root.Children.Add(actions);Content=root;
        Loaded+=(_,_)=>CancelButton.Focus();
        SourceInitialized+=(_,_)=>NativeTitleBarService.Apply(this,((SolidColorBrush)Application.Current.Resources["ChromeBackground"]).Color,((SolidColorBrush)Application.Current.Resources["HomeTextBrush"]).Color);
    }
}
