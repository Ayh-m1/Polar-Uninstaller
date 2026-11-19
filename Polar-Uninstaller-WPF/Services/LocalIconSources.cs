using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;

namespace PolarUninstallerWpf.Services;

public enum IconResolutionMethod { Registry,Executable,Uninstaller,Msi,Store,Shell,GameLauncher,Badge,Component,Legacy }
public sealed record LocalIconSource(string Path,int Index,IconResolutionMethod Method,bool Shell=false);
public static class LocalIconSources
{
    public static bool TryParseDisplayIcon(string input,out string path,out int index)
    {
        path="";index=0;if(string.IsNullOrWhiteSpace(input)||input.IndexOfAny(['\r','\n','\0'])>=0)return false;
        string value=Environment.ExpandEnvironmentVariables(input.Trim());
        var match=Regex.Match(value,@",\s*(-?\d+)\s*$");if(match.Success){if(!int.TryParse(match.Groups[1].Value,out index))return false;value=value[..match.Index];}
        path=value.Trim().Trim('"');
        if(!Path.IsPathFullyQualified(path)||path.StartsWith(@"\\",StringComparison.Ordinal)||Uri.TryCreate(path,UriKind.Absolute,out var uri)&&!uri.IsFile)return false;
        try{return new DriveInfo(Path.GetPathRoot(path)!).DriveType!=DriveType.Network;}catch(ArgumentException){return false;}
    }
    public static bool Component(InstalledProgram p)=>Regex.IsMatch(p.Name,@"(?i)redistributable|\bruntime\b|\.net|gameinput|\bdriver\b|visual c\+\+|\bSDK\b");
    /// <summary>
    /// True when a decoded source is a generic installer/redistributable picture
    /// instead of the product's own logo. Such a picture never identifies the
    /// component, so a runtime row uses the neutral app-drawn component icon.
    /// </summary>
    public static bool GenericInstallerSource(string path,IconResolutionMethod method)
    {
        if(method is IconResolutionMethod.Msi or IconResolutionMethod.Component)return true;
        string name=Path.GetFileName(path);
        return name.Length>0&&Regex.IsMatch(name,@"^(?:installer\.ico|msiexec\.exe|vc_?redist.*\.exe|vcredist.*\.exe)$",RegexOptions.IgnoreCase);
    }
    private static bool MainImage(string file)=>!Regex.IsMatch(Path.GetFileNameWithoutExtension(file),@"(?i)^(unins|uninstall|setup|install|update|helper|crash|msiexec|rundll32)|crashhandler");
    private static bool SharedLauncher(string file)=>new[]{"steam","EpicGamesLauncher","UbisoftConnect","upc","GalaxyClient","Battle.net","EADesktop","RiotClientServices","SocialClubHelper"}.Contains(Path.GetFileNameWithoutExtension(file),StringComparer.OrdinalIgnoreCase);
    public static IEnumerable<LocalIconSource> Candidates(InstalledProgram p,int pixels)
    {
        bool game=GameClassificationService.IsGame(p);var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<LocalIconSource> Values(IEnumerable<string> values,IconResolutionMethod kind,bool shell=false)
        {
            foreach(string value in values)
                if(TryParseDisplayIcon(value,out var file,out int index)&&(!game||!SharedLauncher(file))&&seen.Add(file+"|"+index+"|"+shell))yield return new(file,index,kind,shell);
        }
        foreach(var item in Values(new[]{p.RegisteredIcon.Length>0?p.RegisteredIcon:p.Registration==null?p.IconPath:""},(game&&p.RegisteredIcon.Length==0)?IconResolutionMethod.GameLauncher:IconResolutionMethod.Registry))yield return item;
        // Classic clients such as Steam keep their own executable outside the dead
        // ARP folder; Valve's exact registration supplies that single file.
        string launcherOwned=IconSourceGuard.LauncherExecutable(p);
        if(launcherOwned.Length>0)foreach(var item in Values(new[]{launcherOwned},IconResolutionMethod.Executable))yield return item;
        if(game)
        {
            foreach(var item in Values(p.IconCandidates??[],IconResolutionMethod.GameLauncher))yield return item;
            foreach(var item in Values(LauncherArtworkService.Candidates(p),IconResolutionMethod.GameLauncher))yield return item;
        }
        foreach(var item in Values(MainExecutables(p),IconResolutionMethod.Executable))yield return item;
        string uninstall=InstallationPathResolver.Executable(p.Uninstall),root=InventoryMerger.NormalizePath(p.Location);
        if(uninstall.Length>0&&root.Length>0&&StorageCacheUpdates.Within(uninstall,root)&&!Path.GetFileName(uninstall).Equals("msiexec.exe",StringComparison.OrdinalIgnoreCase))
            foreach(var item in Values(new[]{uninstall},IconResolutionMethod.Uninstaller))yield return item;
        foreach(var item in Values(MsiIcons(p),IconResolutionMethod.Msi))yield return item;
        foreach(var item in Values(StoreIcons(p,pixels),IconResolutionMethod.Store))yield return item;
        if(!game)foreach(var item in Values(p.IconCandidates??[],IconResolutionMethod.Registry))yield return item;
        // Shell lookup is intentionally a separate final step; generic EXE icons are rejected by the decoder.
        foreach(var item in Values(new[]{p.IconPath}.Concat(MainExecutables(p).Take(1)).Append(root),IconResolutionMethod.Shell,true))yield return item;
    }
    public static IReadOnlyList<string> MainExecutables(InstalledProgram p)
    {
        string root=InventoryMerger.NormalizePath(p.Location);if(root.Length==0||!TryParseDisplayIcon(root,out _,out _)||!Directory.Exists(root))return [];
        var result=new List<string>();var pending=new Queue<(string,int)>();pending.Enqueue((root,0));int folders=0;
        while(pending.TryDequeue(out var item)&&folders++<48)
        {
            try
            {
                if((File.GetAttributes(item.Item1)&FileAttributes.ReparsePoint)!=0)continue;
                result.AddRange(Directory.EnumerateFiles(item.Item1,"*.exe").Where(MainImage).Take(64));
                if(item.Item2<3)foreach(string sub in Directory.EnumerateDirectories(item.Item1).Take(16))pending.Enqueue((sub,item.Item2+1));
            }
            catch(Exception e)when(ReadError(e)){ }
        }
        string product=InstallationPathResolver.NormalizeProduct(p.Name);
        int Score(string file)
        {
            string stem=InstallationPathResolver.NormalizeProduct(Path.GetFileNameWithoutExtension(file));
            int score=stem==product?1000:product.Contains(stem,StringComparison.OrdinalIgnoreCase)&&stem.Length>2?400:0;
            if(score==0)try{var info=FileVersionInfo.GetVersionInfo(file);if(InstallationPathResolver.NormalizeProduct(info.ProductName??"")==product)score=800;}catch(Exception e)when(ReadError(e)){ }
            return score;
        }
        bool shared=StorageCacheUpdates.Within(root,Environment.GetFolderPath(Environment.SpecialFolder.Windows))||new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),Path.GetPathRoot(root)!}.Any(p=>p.Equals(root,StringComparison.OrdinalIgnoreCase));
        return result.Where(f=>!shared||Score(f)>0).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(Score).ThenBy(f=>f.Length).Take(24).ToArray();
    }
    public static string PackedProductId(string product)
    {
        if(!Guid.TryParse(product,out var id))return "";string text=id.ToString("N").ToUpperInvariant();
        string Reverse(string x)=>new(x.Reverse().ToArray());
        return Reverse(text[..8])+Reverse(text[8..12])+Reverse(text[12..16])+string.Concat(Enumerable.Range(0,8).Select(i=>Reverse(text.Substring(16+i*2,2))));
    }
    private static IEnumerable<string> MsiIcons(InstalledProgram p)
    {
        string product=p.Registration==null?Regex.Match(p.Uninstall,@"\{[0-9A-Fa-f-]{36}\}").Value:Path.GetFileName(p.Registration.Path);
        string packed=PackedProductId(product);if(packed.Length==0)yield break;
        var result=new List<string>();string sid=WindowsIdentity.GetCurrent().User?.Value??"";
        foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})foreach(var hive in new[]{RegistryHive.LocalMachine,RegistryHive.CurrentUser})
        foreach(string keyPath in new[]{@"SOFTWARE\Classes\Installer\Products\"+packed,@"SOFTWARE\Microsoft\Installer\Products\"+packed,@"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\UserData\S-1-5-18\Products\"+packed+@"\InstallProperties",@"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\UserData\"+sid+@"\Products\"+packed+@"\InstallProperties"})
            try{using var key=RegistryKey.OpenBaseKey(hive,view);using var item=key.OpenSubKey(keyPath);foreach(string field in new[]{"ProductIcon","DisplayIcon"})if(item?.GetValue(field) is string value&&!string.IsNullOrWhiteSpace(value))result.Add(value);}catch(Exception e)when(ReadError(e)){ }
        string cached=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"Installer",product);
        try{if(Directory.Exists(cached))result.AddRange(Directory.EnumerateFiles(cached,"*.ico").Take(8));}catch(Exception e)when(ReadError(e)){ }
        foreach(string file in result)yield return file;
    }
    /// <summary>
    /// Returns the MSIX package root only when the entry's own install folder is that
    /// exact package (or inside it). Matching by name/publisher is forbidden: a fuzzy
    /// substring match once gave Steam the Microsoft Teams logo ("msteams" contains
    /// "steam").
    /// </summary>
    public static string StoreRootFor(InstalledProgram p,string windowsApps)
    {
        if(!TryParseDisplayIcon(p.Location,out var location,out _)||!Directory.Exists(location))return "";
        try
        {
            if(StorageCacheUpdates.Within(location,windowsApps))
            {
                string relative=Path.GetRelativePath(windowsApps,location);
                if(relative.StartsWith("..",StringComparison.Ordinal))return "";
                string package=relative.Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)[0];
                string exact=Path.Combine(windowsApps,package);
                return Directory.Exists(exact)?exact:"";
            }
            return File.Exists(Path.Combine(location,"AppxManifest.xml"))?Path.GetFullPath(location):"";
        }
        catch(Exception e)when(ReadError(e)||e is NotSupportedException){return "";}
    }
    public static IReadOnlyList<string> StoreIcons(InstalledProgram p,int pixels)
    {
        var result=new List<string>();var roots=new List<string>();
        string windowsApps=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"WindowsApps");
        string ownedRoot=StoreRootFor(p,windowsApps);
        if(ownedRoot.Length>0)roots.Add(ownedRoot);
        foreach(string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        try
        {
            string manifest=Path.Combine(root,"AppxManifest.xml");if(!File.Exists(manifest))continue;
            using var reader=XmlReader.Create(manifest,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=2*1024*1024});var xml=XDocument.Load(reader);
            foreach(var attribute in xml.Descendants().SelectMany(e=>e.Attributes()).Where(a=>new[]{"Square44x44Logo","Square150x150Logo","Logo"}.Contains(a.Name.LocalName)))
            {
                string asset=Path.GetFullPath(Path.Combine(root,attribute.Value));if(!StorageCacheUpdates.Within(asset,root))continue;
                string? folder=Path.GetDirectoryName(asset);if(folder==null||!Directory.Exists(folder))continue;
                int Score(string f){var m=Regex.Match(f,@"(?:targetsize-|scale-)(\d+)");int size=m.Success?int.Parse(m.Groups[1].Value):100;return Math.Abs(size-(pixels>64?200:100));}
                result.AddRange(Directory.EnumerateFiles(folder,Path.GetFileNameWithoutExtension(asset)+"*"+Path.GetExtension(asset)).OrderBy(Score).Take(8));
                result.Add(asset);
            }
        }catch(Exception e)when(ReadError(e)||e is XmlException){ }
        return result;
    }
    private static bool ReadError(Exception e)=>e is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception;
}
