using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PolarUninstallerWpf.Services;
namespace PolarUninstallerWpf;
public sealed record LeftoverChoice(string Kind,string Path,string Size,string Reason,Func<Task<IReadOnlyList<string>>> Remove);
public sealed class LeftoverReviewWindow:Window
{
    private readonly List<(CheckBox Check,LeftoverChoice Choice)> choices=new();
    public IReadOnlyList<LeftoverChoice> Selected=>choices.Where(c=>c.Check.IsChecked==true).Select(c=>c.Choice).ToArray();
    public LeftoverReviewWindow(bool arabic,IReadOnlyList<LeftoverChoice> items)
    {
        string L(string en,string ar)=> BidiText.Localize(arabic,en,ar);
        Title=L("Review application leftovers","مراجعة بقايا البرنامج");Width=760;Height=520;MinWidth=500;MinHeight=340;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        FlowDirection=arabic?FlowDirection.RightToLeft:FlowDirection.LeftToRight;
        SetResourceReference(BackgroundProperty,"ChromeBackground");SetResourceReference(ForegroundProperty,"ChromeText");SetResourceReference(FontFamilyProperty,"InterfaceFont");
        var root=new DockPanel{Margin=new Thickness(20)};Content=root;
        var title=new TextBlock{Text=Title,FontSize=22,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,16)};DockPanel.SetDock(title,Dock.Top);root.Children.Add(title);
        var actions=new WrapPanel{Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        void Action(string text,Action click){var button=new Button{Content=text,Style=(Style)Application.Current.Resources["ChromeButton"],Margin=new Thickness(0,0,8,0)};button.Click+=(_,_)=>click();actions.Children.Add(button);}
        Action(L("Select All","تحديد الكل"),()=>{foreach(var item in choices)item.Check.IsChecked=true;});
        Action(L("Deselect All","إلغاء التحديد"),()=>{foreach(var item in choices)item.Check.IsChecked=false;});
        string? approvedSelection=null;
        var remove=new Button{Content=L("Remove Selected","حذف المحدد"),Style=(Style)Application.Current.Resources["ChromeButton"],Margin=new Thickness(0,0,8,0)};
        remove.Click+=(_,_)=>
        {
            if(Selected.Count==0)return;
            string identity=string.Join("\n",Selected.Select(x=>x.Kind+":"+x.Path));
            if(identity==approvedSelection){DialogResult=true;return;}
            approvedSelection=identity;
            remove.Content=L($"Confirm removal ({Selected.Count})",$"تأكيد حذف ({Selected.Count})");
            title.Text=L("Confirm selected cleanup; Registry entries will be backed up.","أكّد تنظيف المحدد؛ ستُنسخ إدخالات الريجستري احتياطيًا.");
            title.FontSize=16;
        };
        actions.Children.Add(remove);
        Action(L("Skip Cleanup","تخطي التنظيف"),()=>DialogResult=false);
        var panel=new StackPanel();root.Children.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        string? category=null;
        if(items.Count==0)panel.Children.Add(new TextBlock{Text=L("No verified leftovers were found.","لم يُعثر على بقايا متحقق منها."),TextWrapping=TextWrapping.Wrap});
        foreach(var item in items.OrderBy(i=>i.Kind))
        {
            if(category!=item.Kind){category=item.Kind;panel.Children.Add(new TextBlock{Text=category,FontWeight=FontWeights.SemiBold,FontSize=16,Margin=new Thickness(0,12,0,6)});}
            var check=new CheckBox{Style=(Style)Application.Current.Resources["CleanerCheckBox"],IsChecked=false,Content=new TextBlock{Text=$"{item.Kind} · {item.Size}\n{item.Path}\n{item.Reason}",TextWrapping=TextWrapping.Wrap,MaxWidth=620}};
            var border=new Border{Child=check,CornerRadius=new CornerRadius(10),Padding=new Thickness(12),Margin=new Thickness(0,3,0,3)};border.SetResourceReference(Border.BackgroundProperty,"DialogSurfaceBrush");panel.Children.Add(border);choices.Add((check,item));
        }
        SourceInitialized+=(_,_)=>NativeTitleBarService.Apply(this,((SolidColorBrush)Application.Current.Resources["ChromeBackground"]).Color,((SolidColorBrush)Application.Current.Resources["ChromeText"]).Color);
    }
}
