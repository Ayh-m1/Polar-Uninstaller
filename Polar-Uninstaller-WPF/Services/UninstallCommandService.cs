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
        if (string.IsNullOrWhiteSpace(raw)) return false;
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
                arguments = arguments.Select(a => a.StartsWith("/I", StringComparison.OrdinalIgnoreCase) ? "/X" + a[2..] : a).ToArray();
            return true;
        }
        finally { LocalFree(argv); }
    }
}
