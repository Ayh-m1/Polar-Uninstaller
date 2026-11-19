using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
namespace PolarUninstallerWpf;
internal sealed class RecoveryWindow:Window
{
    public RecoveryWindow(bool arabic,string? file,string? details=null)
    {
        Title="Polar Uninstaller";Width=520;SizeToContent=SizeToContent.Height;WindowStartupLocation=WindowStartupLocation.CenterScreen;FlowDirection=arabic?FlowDirection.RightToLeft:FlowDirection.LeftToRight;
        SetResourceReference(BackgroundProperty,"ChromeBackground");SetResourceReference(ForegroundProperty,"ChromeText");
        var content=new StackPanel{Margin=new(24)};content.Children.Add(new TextBlock{Text=arabic?"حدث خطأ وتعذر الاستمرار بأمان. حُفظ تقرير محلي إن أمكن. أعد فتح البرنامج.":"An error prevented safe continuation. A local report was saved if possible. Please reopen the app.",TextWrapping=TextWrapping.Wrap});
        var actions=new WrapPanel{Margin=new(0,20,0,0)};content.Children.Add(actions);
        void Add(string en,string ar,Action action){var button=new Button{Content=arabic?ar:en,Margin=new(4)};if(TryFindResource("ChromeButton") is Style style)button.Style=style;button.Click+=(_,_)=>{try{action();}catch(Exception e){Services.CrashDiagnostics.Record("Crash report action",e);}};actions.Children.Add(button);}
        Add("Open log folder","افتح مجلد السجل",()=>Process.Start(new ProcessStartInfo(Services.AppSafety.LogDirectory){UseShellExecute=true}));
        Add("Copy details","انسخ التفاصيل",()=>Clipboard.SetText("Polar Uninstaller "+Services.AppInfo.Version+Environment.NewLine+(details??file??"")));
        Add("Close","أغلق",Close);Content=content;
    }
}
