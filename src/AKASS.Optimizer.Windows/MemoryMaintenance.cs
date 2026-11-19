using System;
using System.ComponentModel;
using System.Runtime;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Akass.Optimizer;

public static class MemoryMaintenance
{
    public static (ulong Before, ulong After) CompactAppHeap()
    {
        ulong before = (ulong)GC.GetTotalMemory(false);
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        return (before, (ulong)GC.GetTotalMemory(false));
    }

    internal static ToolResult ClearStandby()
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x28, out var token)) throw new Win32Exception(Marshal.GetLastWin32Error());
        using (token)
        {
            if (!LookupPrivilegeValue(null, "SeProfileSingleProcessPrivilege", out var luid))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var enabled = new TokenPrivilege { Count = 1, Id = luid, Attributes = 2 };
            int size = Marshal.SizeOf(typeof(TokenPrivilege));
            if (!AdjustTokenPrivileges(token, false, ref enabled, size, out var previous, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            int error = Marshal.GetLastWin32Error();
            if (error != 0) throw new Win32Exception(error);
            try
            {
                // The NT memory-list control is version-dependent. Report a rejected operation as-is.
                int purgeStandbyList = 4;
                int status = NtSetSystemInformation(80, ref purgeStandbyList, sizeof(int));
                return new ToolResult(status, "Standby list status: 0x" + status.ToString("X8") + ".");
            }
            finally { AdjustTokenPrivileges(token, false, ref previous, 0, IntPtr.Zero, IntPtr.Zero); }
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] private struct TokenPrivilege { public uint Count; public Luid Id; public uint Attributes; }
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out SafeFileHandle token);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool LookupPrivilegeValue(string? system, string name, out Luid id);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool AdjustTokenPrivileges(SafeFileHandle token,
        bool disableAll, ref TokenPrivilege state, int bytes, out TokenPrivilege previous, out int used);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool AdjustTokenPrivileges(SafeFileHandle token,
        bool disableAll, ref TokenPrivilege state, int bytes, IntPtr previous, IntPtr used);
    [DllImport("ntdll.dll")] private static extern int NtSetSystemInformation(int informationClass, ref int command, int length);
}
