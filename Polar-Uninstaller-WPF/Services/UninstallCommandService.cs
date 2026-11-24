using System.IO;
using System.Runtime.InteropServices;

namespace PolarUninstallerWpf.Services;

/// <summary>One parser for capability selection and execution; never use a shell
/// to interpret Registry command text or accept a relative executable path.</summary>
public static class UninstallCommandService
{
    [DllImport("shell32.dll", SetLastError = true)] private static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string command, out int argc);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    public static bool TryParse(string raw, out string executable, out string[] arguments)
    {
        executable = ""; arguments = [];
        if (string.IsNullOrWhiteSpace(raw)||raw.Any(c=>c is '\r' or '\n' or '\0')) return false;
        string expanded = Environment.ExpandEnvironmentVariables(raw.Trim());
        string registeredExe = InstallationPathResolver.Executable(expanded);
        if (!expanded.StartsWith('"') && Path.IsPathFullyQualified(registeredExe) && File.Exists(registeredExe))
            expanded = "\"" + registeredExe + "\"" + expanded[registeredExe.Length..];
        IntPtr argv = CommandLineToArgvW(expanded, out int count);
        if (argv == IntPtr.Zero) return false;
        try
        {
            if (count == 0) return false;
            var parts = Enumerable.Range(0, count).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size)) ?? "").ToArray();
            string path = parts[0];
            if (path.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase) || path.Equals("msiexec", StringComparison.OrdinalIgnoreCase))
                path = Path.Combine(Environment.SystemDirectory, "msiexec.exe");
            if (!Path.IsPathFullyQualified(path) || !File.Exists(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
            string name = Path.GetFileName(path);
            if (new[] { "cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe", "rundll32.exe", "mshta.exe" }.Contains(name, StringComparer.OrdinalIgnoreCase)) return false;
            executable = path; arguments = parts.Skip(1).ToArray();
            if (name.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase))
            {
                // Only /I and /package are install actions. /INSTALLDIR etc. are not.
                bool Product(string value)=>Guid.TryParse(value,out _)||Path.IsPathFullyQualified(value)&&value.EndsWith(".msi",StringComparison.OrdinalIgnoreCase)&&File.Exists(value);
                arguments=arguments.Select(a=>a.Equals("/i",StringComparison.OrdinalIgnoreCase)||a.Equals("/package",StringComparison.OrdinalIgnoreCase)?"/X":
                    a.StartsWith("/i",StringComparison.OrdinalIgnoreCase)&&Product(a[2..])?"/X"+a[2..]:a).ToArray();
                var normalized=arguments;
                if(!normalized.Where((a,index)=>a.Equals("/x",StringComparison.OrdinalIgnoreCase)?index+1<normalized.Length&&Product(normalized[index+1]):a.StartsWith("/x",StringComparison.OrdinalIgnoreCase)&&Product(a[2..])).Any())return false;
            }
            return true;
        }
        finally { LocalFree(argv); }
    }
    public static bool IsQuiet(IEnumerable<string> arguments)=>arguments.Any(arg=>new[]{"/quiet","/qn","/qn+","/passive","/silent","/verysilent","/s","--silent","--quiet","-s"}.Contains(arg,StringComparer.OrdinalIgnoreCase));

    public static string ResolveRegistered(string uninstall,string quiet,string modify,string keyName,bool windowsInstaller)
    {
        if(TryParse(uninstall,out _,out _))return uninstall;
        if(TryParse(quiet,out _,out _))return quiet;
        // A ModifyPath can launch repair/settings. Only a valid MSI product is convertible.
        if(TryParse(modify,out var executable,out var arguments)&&System.IO.Path.GetFileName(executable).Equals("msiexec.exe",StringComparison.OrdinalIgnoreCase))
            return "\""+executable+"\" "+string.Join(" ",arguments.Select(a=>a.Contains(' ')?"\""+a+"\"":a));
        return windowsInstaller&&Guid.TryParse(keyName,out _)?"msiexec.exe /X "+keyName:uninstall;
    }
}
