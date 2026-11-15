using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;

namespace PolarUninstallerWpf.Services;

/// <summary>EA games require both a game-specific installerdata.xml identity
/// and a registered uninstaller for the exact install directory. No drive crawl
/// or shared EA database editing, and no client/anti-cheat entries are games.</summary>
internal static class EaGameDiscovery
{
    internal static string ProductId(string file)
    {
        if(!File.Exists(file)||new FileInfo(file).Length>2*1024*1024)return "";
        for(string? path=file;path!=null;path=Path.GetDirectoryName(path))
            if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)return "";
        using var reader=XmlReader.Create(file,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=2*1024*1024});
        var doc=XDocument.Load(reader);
        return doc.Descendants().Where(e=>new[]{"gameid","offerid","contentid"}.Contains(e.Name.LocalName.ToLowerInvariant()))
            .Select(e=>e.Value.Trim()).Where(id=>Regex.IsMatch(id,@"^[A-Za-z0-9][A-Za-z0-9._:\-]{2,199}$"))
            .Distinct(StringComparer.Ordinal).OrderBy(id=>id,StringComparer.Ordinal).FirstOrDefault()??"";
    }
    internal static IReadOnlyList<InstalledProgram> Discover(CancellationToken token)
    {
        var result=new List<InstalledProgram>();
        foreach(var hive in new[]{RegistryHive.LocalMachine,RegistryHive.CurrentUser})
        foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var root=RegistryKey.OpenBaseKey(hive,view);using var uninstall=root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");if(uninstall==null)continue;
                foreach(string keyName in uninstall.GetSubKeyNames())
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        using var key=uninstall.OpenSubKey(keyName);string location=Convert.ToString(key?.GetValue("InstallLocation"))??"";
                        if(!Path.IsPathFullyQualified(location)||!Directory.Exists(location))continue;
                        string publisher=Convert.ToString(key?.GetValue("Publisher"))??"";
                        if(!publisher.Contains("Electronic Arts",StringComparison.OrdinalIgnoreCase)&&!publisher.Equals("EA",StringComparison.OrdinalIgnoreCase))continue;
                        string name=Convert.ToString(key?.GetValue("DisplayName"))??"";
                        if(name.Length==0||Regex.IsMatch(name,@"(?i)^(EA app|EA Desktop|Origin)$|anti.?cheat|easy.?anticheat|redistributable|runtime"))continue;
                        string command=Convert.ToString(key?.GetValue("UninstallString"))??"";
                        if(!UninstallCommandService.TryParse(command,out _,out _))continue;
                        DirectRemovalService.ValidateRoot(location);
                        string id=ProductId(Path.Combine(location,"__Installer","installerdata.xml"));if(id.Length==0)continue;
                        result.Add(new(name,publisher,Convert.ToString(key?.GetValue("DisplayVersion"))??"",location,
                            Convert.ToString(key?.GetValue("DisplayIcon"))??"",Uninstall:command,Source:"EA",Identifier:"ea:"+id,RequiresOfficialRemoval:true));
                    }
                    catch(Exception error)when(LauncherMetadataDiscovery.ReadFailure(error)||error is XmlException){}
                }
            }
            catch(Exception error)when(LauncherMetadataDiscovery.ReadFailure(error)){}
        }
        return result;
    }
}
