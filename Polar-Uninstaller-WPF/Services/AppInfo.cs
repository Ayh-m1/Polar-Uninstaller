using System.Reflection;

namespace PolarUninstallerWpf.Services;

public static class AppInfo
{
    public const string Name="Polar Uninstaller";
    public const string Publisher="Polar";
    public static string Version
    {
        get
        {
            var version=typeof(AppInfo).Assembly.GetName().Version!;
            return $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }
}
