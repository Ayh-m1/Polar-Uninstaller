using System.IO;
using System.Text.Json;
using System.Windows;

namespace PolarUninstallerWpf.Services;

public static class BrowserText
{
    private static readonly Lazy<Dictionary<string,string>> English=new(()=>Load("en"));
    private static readonly Lazy<Dictionary<string,string>> Arabic=new(()=>Load("ar"));
    private static Dictionary<string,string> Load(string language)
    {
        using var stream=Application.GetResourceStream(new Uri($"pack://application:,,,/PolarUninstaller;component/Localization/FileBrowser.{language}.json"))!.Stream;
        return JsonSerializer.Deserialize<Dictionary<string,string>>(stream)??throw new InvalidDataException("File Browser translations unavailable");
    }
    public static string Get(string key,bool arabic,params object[] values)=>string.Format(DisplayFormat.NumericCulture,(arabic?Arabic:English).Value[key],values);
    public static string ActionLabel(string action,int count,bool arabic)
    {
        string form=count==1?"One":arabic&&count==2?"Two":arabic&&count%100 is >=3 and <=10?"Few":arabic&&count%100 is >=11 and <=99?"Many":"Other";
        return Get(action+form,arabic,count);
    }
}
