using System.Globalization;
using System.Security.Cryptography;
using System.Text;
namespace PolarUninstallerWpf.Services;
public static class IconBadgeService
{
    public static string Letter(string name)=>string.IsNullOrWhiteSpace(name)?"?":StringInfo.GetNextTextElement(name.Trim()).ToUpperInvariant();
    public static double Tint(string name)=>.08+SHA256.HashData(Encoding.UTF8.GetBytes(name.Trim().ToUpperInvariant()))[0]/255d*.12;
}
