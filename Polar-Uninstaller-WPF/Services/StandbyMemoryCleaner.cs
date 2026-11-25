using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PolarUninstallerWpf.Services;

internal static class StandbyMemoryCleaner
{
    private const int MemoryListInformation = 80;
    private const int PurgeStandbyList = 4;
    private const uint AdjustPrivilegesAndQuery = 0x0028, Enabled = 2;
    private static readonly object Gate = new();

    internal readonly record struct Result(long Before, long After)
    {
        internal long Released => Math.Max(0, Before - After);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid { internal uint Low; internal int High; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Privilege { internal uint Count; internal Luid Luid; internal uint Attributes; }

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int informationClass, [Out] nuint[] data, int length, out int returned);
    [DllImport("ntdll.dll")]
    private static extern int NtSetSystemInformation(int informationClass, ref int command, int length);
    [DllImport("ntdll.dll")]
    private static extern uint RtlNtStatusToDosError(int status);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out Luid luid);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(SafeAccessTokenHandle token, bool disableAll, ref Privilege state,
        int size, out Privilege previous, out int returned);
    [DllImport("advapi32.dll", EntryPoint = "AdjustTokenPrivileges", SetLastError = true)]
    private static extern bool RestorePrivilege(SafeAccessTokenHandle token, bool disableAll, ref Privilege state,
        int size, IntPtr previous, IntPtr returned);

    internal static Task<long> ScanAsync(CancellationToken token) => Task.Run(ReadStandbyBytes, token);
    internal static Task<Result> ReleaseAsync(CancellationToken token) => Task.Run(() =>
    {
        lock (Gate)
        {
            token.ThrowIfCancellationRequested();
            long before = ReadStandbyBytes();
            if (before == 0) return new Result(0, 0);
            if (!OpenProcessToken(GetCurrentProcess(), AdjustPrivilegesAndQuery, out var handle))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            using (handle)
            {
                if (!LookupPrivilegeValue(null, "SeProfileSingleProcessPrivilege", out var luid))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                var privilege = new Privilege { Count = 1, Luid = luid, Attributes = Enabled };
                bool adjusted = AdjustTokenPrivileges(handle, false, ref privilege, Marshal.SizeOf<Privilege>(), out var previous, out _);
                int error = Marshal.GetLastWin32Error();
                if (!adjusted || error != 0) throw new Win32Exception(error);
                try
                {
                    token.ThrowIfCancellationRequested();
                    int command = PurgeStandbyList;
                    Check(NtSetSystemInformation(MemoryListInformation, ref command, sizeof(int)));
                }
                finally
                {
                    if (previous.Count > 0 && !RestorePrivilege(handle, false, ref previous, 0, IntPtr.Zero, IntPtr.Zero))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            return new Result(before, ReadStandbyBytes());
        }
    }, token);

    private static long ReadStandbyBytes()
    {
        // Five page-list counters, eight standby priorities, eight repurposed counters and pagefile count.
        var data = new nuint[22];
        int length = data.Length * IntPtr.Size;
        Check(NtQuerySystemInformation(MemoryListInformation, data, length, out int returned));
        if (returned < 13 * IntPtr.Size) throw new NotSupportedException("Windows did not return standby page counts.");
        long pages = 0;
        for (int i = 5; i < 13; i++) pages = checked(pages + (long)data[i]);
        return checked(pages * Environment.SystemPageSize);
    }

    private static void Check(int status)
    {
        if (status < 0) throw new Win32Exception(unchecked((int)RtlNtStatusToDosError(status)));
    }
}
