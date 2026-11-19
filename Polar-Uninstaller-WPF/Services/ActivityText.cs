using System.Text.Json;
using System.Windows;

namespace PolarUninstallerWpf.Services;

public static class ActivityText
{
    private static readonly Lazy<IReadOnlyDictionary<string,string>> Pairs=new(()=>
    {
        using var stream=Application.GetResourceStream(new Uri("pack://application:,,,/PolarUninstaller;component/Localization/Activity.ar.json"))?.Stream;
        return stream==null?new Dictionary<string,string>():JsonSerializer.Deserialize<Dictionary<string,string>>(stream)??new();
    });
    private static readonly Lazy<KeyValuePair<string,string>[]> EnglishOrder=new(()=>Pairs.Value.OrderByDescending(p=>p.Key.Length).ToArray());
    private static readonly Lazy<KeyValuePair<string,string>[]> ArabicOrder=new(()=>Pairs.Value.OrderByDescending(p=>p.Value.Length).ToArray());
    public static string Localize(string message,bool arabic)
    {
        message=message.Replace("\u2066","").Replace("\u2067","").Replace("\u2068","").Replace("\u2069","");
        var pairs=Pairs.Value;
        foreach(var pair in arabic?EnglishOrder.Value:ArabicOrder.Value)
        {
            string source=arabic?pair.Key:pair.Value,target=arabic?pair.Value:pair.Key;
            if(message==source)return target;
            // Only explicit prefix labels can translate a mixed diagnostic payload.
            if(source.EndsWith(' ')&&message.StartsWith(source,StringComparison.Ordinal))
            {
                string rest=message[source.Length..];
                if(pairs.TryGetValue(rest,out var translated)&&arabic)return target+translated;
                if(arabic)return target+DisplayFormat.Isolate(rest);
                return target+rest.Trim('\u2066','\u2069');
            }
        }
        return message;
    }
}
