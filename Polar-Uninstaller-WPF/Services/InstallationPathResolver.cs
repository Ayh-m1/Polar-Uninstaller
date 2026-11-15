using System.IO;
using System.Text.RegularExpressions;

namespace PolarUninstallerWpf.Services;
public static class InstallationPathResolver
{
    public static string NormalizeProduct(string name)
    {
        name=Regex.Replace(name,@"\b(?:version|v)?\s*\d+(?:\.\d+)+(?:[-\w.]*)?", "",RegexOptions.IgnoreCase);
        name=Regex.Replace(name,@"\((?:x64|x86|64-bit|32-bit)\)","",RegexOptions.IgnoreCase);
        return Regex.Replace(name,@"[^\p{L}\p{N}]","").ToLowerInvariant();
    }
    public static string Executable(string input)
    {
        input=Environment.ExpandEnvironmentVariables(input.Trim());
        var match=Regex.Match(input,"^\\s*(?:\"(?<p>[^\"]+\\.(?:exe|ico|dll))\"|(?<p>.+?\\.(?:exe|ico|dll)))(?:\\s|,|$)",RegexOptions.IgnoreCase);
        return match.Success?match.Groups["p"].Value:"";
    }
    public static string Resolve(string name,string registered,string icon,string command)
    {
        if(InventoryMerger.NormalizePath(registered).Length>0)return Environment.ExpandEnvironmentVariables(registered.Trim().Trim('"'));
        string product=NormalizeProduct(name);if(product.Length<3)return "";
        foreach(var value in new[]{icon,command})
        {
            try
            {
                string executable=Executable(value);if(!Path.IsPathFullyQualified(executable)||!File.Exists(executable))continue;
                var parent=Path.GetDirectoryName(executable);
                if(parent!=null&&NormalizeProduct(Path.GetFileName(parent))==product&&InventoryMerger.NormalizePath(parent).Length>0)return parent;
            }
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException){ }
        }
        return "";
    }
}
