using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace PolarUninstallerWpf.Services;

public static class LauncherArtworkService
{
    // Metadata readers are launcher-specific. Only local files are returned; no URLs are fetched.
    public static IReadOnlyList<string> Candidates(InstalledProgram program)
    {
        var result=new List<string>();string root=InventoryMerger.NormalizePath(program.Location);
        if(root.Length==0||!Directory.Exists(root))return result;
        void Local(string? value,string directory)
        {
            if(string.IsNullOrWhiteSpace(value))return;
            try
            {
                if(Uri.TryCreate(value,UriKind.Absolute,out var uri)&&!uri.IsFile)return;
                string clean=Environment.ExpandEnvironmentVariables(value.Trim());string index="";
                int comma=clean.LastIndexOf(',');if(comma>0&&int.TryParse(clean[(comma+1)..],out int resource)){index=","+resource;clean=clean[..comma];}
                clean=clean.Trim().Trim('"');
                string file=Path.GetFullPath(Path.IsPathRooted(clean)?clean:Path.Combine(directory,clean));
                if(file.StartsWith(root+"\\",StringComparison.OrdinalIgnoreCase)&&File.Exists(file))result.Add(file+index);
            }
            catch(ArgumentException){ }
        }
        string appx=Path.Combine(root,"AppxManifest.xml");
        if(File.Exists(appx))
        {
            try
            {
                var xml=System.Xml.Linq.XDocument.Load(appx);
                foreach(var visual in xml.Descendants().Where(x=>x.Name.LocalName=="VisualElements"))
                foreach(string key in new[]{"Square44x44Logo","Square150x150Logo","Logo"})
                {
                    string? value=visual.Attribute(key)?.Value;if(string.IsNullOrWhiteSpace(value)||value.StartsWith("ms-resource:",StringComparison.OrdinalIgnoreCase))continue;
                    Local(value,root);
                    string asset=Path.GetFullPath(Path.Combine(root,value));string? directory=Path.GetDirectoryName(asset);
                    if(directory!=null&&asset.StartsWith(root+"\\",StringComparison.OrdinalIgnoreCase)&&Directory.Exists(directory))
                        foreach(string variant in Directory.EnumerateFiles(directory,Path.GetFileNameWithoutExtension(asset)+".*"+Path.GetExtension(asset)).OrderByDescending(f=>f.Contains("scale-200",StringComparison.OrdinalIgnoreCase)).Take(16))Local(variant,root);
                }
            }
            catch(Exception e)when(e is IOException or UnauthorizedAccessException or System.Xml.XmlException or ArgumentException){ }
        }
        if(program.Identifier.StartsWith("epic:",StringComparison.OrdinalIgnoreCase)||program.Source.Equals("Epic Games",StringComparison.OrdinalIgnoreCase))
        {
            string manifests=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Epic","EpicGamesLauncher","Data","Manifests");
            try
            {
                if(Directory.Exists(manifests))foreach(string file in Directory.EnumerateFiles(manifests,"*.item"))
                {
                    try
                    {
                        using var json=JsonDocument.Parse(File.ReadAllText(file));var item=json.RootElement;
                        string Value(string key)=>item.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??"":"";
                        if(!LeftoverService.EqualPath(Value("InstallLocation"),root))continue;
                        foreach(string key in new[]{"DisplayIcon","IconPath","Icon","LaunchExecutable"})Local(Value(key),root);
                    }
                    catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException){ }
                }
            }
            catch(Exception e)when(e is IOException or UnauthorizedAccessException){ }
        }
        // GOG's per-game support files carry the exact numeric product ID.
        try
        {
            foreach(string info in Directory.EnumerateFiles(root,"goggame-*.info").Take(32))
            {
                using var json=JsonDocument.Parse(File.ReadAllText(info));var item=json.RootElement;
                if(item.TryGetProperty("gameId",out var id))
                {
                    string value=id.ToString();if(value.Length>0&&value.All(char.IsAsciiDigit))Local($"goggame-{value}.ico",root);
                }
            }
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException){ }
        // Ubisoft and Rockstar registration stores differ; accept only a registration
        // whose install directory matches this exact game, never the shared launcher.
        string identity=program.Source+" "+program.Identifier+" "+program.Publisher;
        var registrations=new[]{("Ubisoft",@"SOFTWARE\Ubisoft\Launcher\Installs"),("Rockstar",@"SOFTWARE\Rockstar Games"),("GOG",@"SOFTWARE\GOG.com\Games"),("Electronic Arts",@"SOFTWARE\EA Games"),("Blizzard",@"SOFTWARE\Blizzard Entertainment")};
        foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})
        foreach(var registration in registrations.Where(r=>identity.Contains(r.Item1,StringComparison.OrdinalIgnoreCase)))
        {
            string keyPath=registration.Item2;
            try
            {
                using var hive=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,view);using var parent=hive.OpenSubKey(keyPath);
                if(parent==null)continue;
                foreach(string name in parent.GetSubKeyNames())
                {
                    using var game=parent.OpenSubKey(name);if(game==null)continue;
                    bool matching=new[]{"InstallDir","InstallFolder","Install Folder","path","InstallPath"}.Any(k=>LeftoverService.EqualPath(Convert.ToString(game.GetValue(k))??"",root));
                    if(!matching)continue;
                    foreach(string k in new[]{"DisplayIcon","Icon","ExePath","Executable"})Local(Convert.ToString(game.GetValue(k))??"",root);
                }
            }
            catch(Exception e)when(e is IOException or UnauthorizedAccessException or System.Security.SecurityException){ }
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
