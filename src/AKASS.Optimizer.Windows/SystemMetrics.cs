using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Akass.Optimizer;

public sealed class Snapshot
{
    public DateTime Time { get; set; } = DateTime.UtcNow;
    public ulong Total { get; set; }
    public ulong? Installed { get; set; }
    public ulong Available { get; set; }
    public uint Load { get; set; }
    public ulong? Commit { get; set; }
    public ulong? CommitLimit { get; set; }
    public double? CommitRatio => CommitLimit > 0 ? Commit / (double?)CommitLimit : null;
    public bool? WindowsLow { get; set; }
    public Pressure? Pressure { get; set; }
    public double? Cpu { get; set; }
    public string Drive { get; set; } = "";
    public ulong? DriveFree { get; set; }
    public ulong? DriveTotal { get; set; }
    public double? DiskFreeRatio => DriveTotal > 0 ? DriveFree / (double?)DriveTotal : null;
    public double? DiskRead { get; set; }
    public double? DiskWrite { get; set; }
    public double? Receive { get; set; }
    public double? Send { get; set; }
    public string Adapter { get; set; } = "";
}

public sealed class SystemMetrics : IDisposable
{
    private readonly SafeWaitHandle lowMemory = CreateMemoryResourceNotification(0);
    private readonly DiskCounters disk = new DiskCounters();
    private readonly ulong? installed = GetPhysicallyInstalledSystemMemory(out ulong kib) && kib > 0 && kib <= ulong.MaxValue / 1024
        ? kib * 1024 : (ulong?)null;
    private (ulong Idle, ulong Total)? previousCpu;
    private (string Id, long Received, long Sent, long Clock)? previousNetwork;
    private readonly object gate = new object();

    public static string CpuName => RegistryText(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString");
    public static string DeviceModel => RegistryText(@"HARDWARE\DESCRIPTION\System\BIOS", "SystemProductName");
    public static string WindowsVersion => Environment.OSVersion.Version.ToString();
    public static TimeSpan Uptime => TimeSpan.FromMilliseconds(GetTickCount64());

    public static TimeSpan? UserIdle()
    {
        var input = new LastInput { Size = 8 };
        return GetLastInputInfo(ref input) ? TimeSpan.FromMilliseconds(unchecked((uint)GetTickCount64() - input.Tick)) : (TimeSpan?)null;
    }

    public Snapshot Sample(int sensitivity = 2, bool includeNetwork = true)
    {
        lock (gate)
        {
            var current = new Snapshot { Installed = installed };
            var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf(typeof(MemoryStatus)) };
            if (GlobalMemoryStatusEx(ref memory))
            {
                current.Total = memory.TotalPhysical;
                current.Available = memory.AvailablePhysical;
                current.Load = memory.Load;
            }
            uint length = (uint)Marshal.SizeOf(typeof(PerformanceInfo));
            if (GetPerformanceInfo(out var performance, length))
            {
                current.Commit = performance.CommitTotal.ToUInt64() * performance.PageSize.ToUInt64();
                current.CommitLimit = performance.CommitLimit.ToUInt64() * performance.PageSize.ToUInt64();
            }
            if (!lowMemory.IsInvalid && QueryMemoryResourceNotification(lowMemory, out bool low))
                current.WindowsLow = low;
            if (current.Total > 0)
                current.Pressure = Policy.MemoryPressure(current.Total, current.Available, current.Load,
                    current.CommitRatio, current.WindowsLow, sensitivity);
            if (GetSystemTimes(out var idle, out var kernel, out var user))
            {
                ulong total = kernel.Value + user.Value;
                if (previousCpu is { } previous && total > previous.Total && idle.Value >= previous.Idle)
                    current.Cpu = Math.Max(0, Math.Min(100, 100 * (1 - (idle.Value - previous.Idle) / (double)(total - previous.Total))));
                previousCpu = (idle.Value, total);
            }
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
                current.Drive = drive.Name;
                current.DriveFree = (ulong)drive.AvailableFreeSpace;
                current.DriveTotal = (ulong)drive.TotalSize;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            if (includeNetwork)
            {
                (current.DiskRead, current.DiskWrite) = disk.Read();
                SampleNetwork(current);
            }
            return current;
        }
    }

    private void SampleNetwork(Snapshot sample)
    {
        try
        {
            var adapters = NetworkInterface.GetAllNetworkInterfaces();
            var adapter = adapters.Where(x => x.OperationalStatus == OperationalStatus.Up &&
                  x.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                  x.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .OrderByDescending(x => x.GetIPProperties().GatewayAddresses.Count > 0)
                .ThenByDescending(x => x.Speed).FirstOrDefault();
            if (adapter == null) { previousNetwork = null; return; }
            var stats = adapter.GetIPStatistics();
            sample.Adapter = adapter.Name;
            long clock = Stopwatch.GetTimestamp();
            if (previousNetwork is { } previous && previous.Id == adapter.Id && clock > previous.Clock &&
                stats.BytesReceived >= previous.Received && stats.BytesSent >= previous.Sent)
            {
                double seconds = (clock - previous.Clock) / (double)Stopwatch.Frequency;
                sample.Receive = (stats.BytesReceived - previous.Received) / seconds;
                sample.Send = (stats.BytesSent - previous.Sent) / seconds;
            }
            previousNetwork = (adapter.Id, stats.BytesReceived, stats.BytesSent, clock);
        }
        catch (NetworkInformationException) { previousNetwork = null; }
    }

    internal static string RegistryText(string path, string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            return (key?.GetValue(name) as string)?.Trim() ?? "";
        }
        catch (System.Security.SecurityException) { return ""; }
        catch (UnauthorizedAccessException) { return ""; }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disk.Dispose();
            lowMemory.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile,
            TotalVirtual, AvailableVirtual, ExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PerformanceInfo
    {
        public uint Size;
        public UIntPtr CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable,
            SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize;
        public uint Handles, Processes, Threads;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low, High;
        public ulong Value => ((ulong)High << 32) | Low;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus value);
    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "K32GetPerformanceInfo")]
    private static extern bool GetPerformanceInfo(out PerformanceInfo value, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeWaitHandle CreateMemoryResourceNotification(int type);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicallyInstalledSystemMemory(out ulong totalInKilobytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryMemoryResourceNotification(SafeWaitHandle handle, [MarshalAs(UnmanagedType.Bool)] out bool state);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
    [DllImport("kernel32.dll")] private static extern ulong GetTickCount64();
    [StructLayout(LayoutKind.Sequential)] private struct LastInput { public uint Size, Tick; }
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInput input);

    private sealed class DiskCounters : IDisposable
    {
        private IntPtr query, read, write;
        public DiskCounters()
        {
            if (PdhOpenQuery(null, IntPtr.Zero, out query) != 0) { query = IntPtr.Zero; return; }
            if (PdhAddEnglishCounter(query, @"\PhysicalDisk(_Total)\Disk Read Bytes/sec", IntPtr.Zero, out read) != 0)
                read = IntPtr.Zero;
            if (PdhAddEnglishCounter(query, @"\PhysicalDisk(_Total)\Disk Write Bytes/sec", IntPtr.Zero, out write) != 0)
                write = IntPtr.Zero;
        }

        public (double?, double?) Read() => query != IntPtr.Zero && PdhCollectQueryData(query) == 0 ?
            (Counter(read), Counter(write)) : (null, null);

        private static double? Counter(IntPtr counter) =>
            counter != IntPtr.Zero && PdhGetFormattedCounterValue(counter, 0x200, IntPtr.Zero, out var item) == 0 &&
            item.State <= 1 && !double.IsNaN(item.Value) && !double.IsInfinity(item.Value) ? Math.Max(0, item.Value) : (double?)null;

        public void Dispose()
        {
            if (query != IntPtr.Zero) PdhCloseQuery(query);
            query = IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct CounterValue
        {
            [FieldOffset(0)] public uint State;
            [FieldOffset(8)] public double Value;
        }

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhOpenQuery(string? source, IntPtr user, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
        [DllImport("pdh.dll")] private static extern int PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll")] private static extern int PdhGetFormattedCounterValue(IntPtr counter, uint format, IntPtr type, out CounterValue value);
        [DllImport("pdh.dll")] private static extern int PdhCloseQuery(IntPtr query);
    }
}

public sealed class ProcessRow
{
    public string Name { get; set; } = "";
    public int Pid { get; set; }
    public long Started { get; set; }
    public ulong Working { get; set; }
    public ulong Private { get; set; }
    public double? Cpu { get; set; }
    public bool CanClose { get; set; }
}

public sealed class ProcessAnalyzer
{
    private Dictionary<(int, long), (double Cpu, long Clock)> previous = new Dictionary<(int, long), (double, long)>();

    private readonly object gate = new object();
    public IReadOnlyList<ProcessRow> Sample()
    {
        lock (gate) return Collect();
    }

    private IReadOnlyList<ProcessRow> Collect()
    {
        var next = new Dictionary<(int, long), (double, long)>();
        var rows = new List<ProcessRow>();
        long clock = Stopwatch.GetTimestamp();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var row = new ProcessRow { Pid = process.Id, Name = process.ProcessName,
                        Working = (ulong)Math.Max(0, process.WorkingSet64), Private = (ulong)Math.Max(0, process.PrivateMemorySize64) };
                    rows.Add(row);
                    row.Started = process.StartTime.ToUniversalTime().Ticks;
                    if (OpenProcess(0x1000 | 0x0010, false, row.Pid) is { } handle)
                    {
                        using (handle)
                        {
                            uint length = (uint)Marshal.SizeOf(typeof(ProcessMemory));
                            if (!handle.IsInvalid && GetProcessMemoryInfo(handle, out var memory, length))
                            {
                                row.Working = memory.WorkingSet.ToUInt64();
                                row.Private = memory.PrivateUsage.ToUInt64();
                            }
                        }
                    }
                    double cpu = process.TotalProcessorTime.TotalSeconds;
                    var key = (row.Pid, row.Started);
                    if (previous.TryGetValue(key, out var past) && clock > past.Clock && cpu >= past.Cpu)
                        row.Cpu = Math.Min(100, 100 * (cpu - past.Cpu) * Stopwatch.Frequency /
                            ((clock - past.Clock) * (double)Environment.ProcessorCount));
                    next[key] = (cpu, clock);
                    row.CanClose = Eligible(process);
                }
                catch (Exception e) when (e is Win32Exception || e is InvalidOperationException ||
                                          e is NotSupportedException || e is UnauthorizedAccessException) { }
            }
        }
        previous = next;
        return rows.OrderByDescending(x => x.Working).ToList();
    }

    public static bool CloseGracefully(ProcessRow target)
    {
        try
        {
            using var process = Process.GetProcessById(target.Pid);
            return process.StartTime.ToUniversalTime().Ticks == target.Started && Eligible(process) && process.CloseMainWindow();
        }
        catch (Exception e) when (e is Win32Exception || e is ArgumentException ||
                                  e is InvalidOperationException || e is NotSupportedException) { return false; }
    }

    public static (ulong Before, ulong After)? TrimWorkingSet(ProcessRow? target = null)
    {
        try
        {
            using var process = target == null ? Process.GetCurrentProcess() : Process.GetProcessById(target.Pid);
            // Keep the same handle through validation and trimming so PID reuse cannot retarget the action.
            using var handle = OpenProcess(0x1000 | 0x0100 | 0x0010, false, process.Id);
            if (handle.IsInvalid) return null;
            if (target != null)
            {
                if (!GetProcessTimes(handle, out long created, out _, out _, out _) ||
                    DateTime.FromFileTimeUtc(created).Ticks != target.Started || !Eligible(process) ||
                    !CanTrimBackground(handle, process.Id)) return null;
                long cpu = process.TotalProcessorTime.Ticks;
                System.Threading.Thread.Sleep(450);
                if (process.TotalProcessorTime.Ticks - cpu > TimeSpan.FromMilliseconds(10).Ticks ||
                    !CanTrimBackground(handle, process.Id) || SystemMetrics.UserIdle() is not { } idle ||
                    idle < AutoMemoryPolicy.IdlePeriod) return null;
            }

            uint length = (uint)Marshal.SizeOf(typeof(ProcessMemory));
            if (!GetProcessMemoryInfo(handle, out var before, length) || !EmptyWorkingSet(handle) ||
                !GetProcessMemoryInfo(handle, out var after, length)) return null;
            return (before.WorkingSet.ToUInt64(), after.WorkingSet.ToUInt64());
        }
        catch (Exception error) when (FileSafety.ExpectedFailure(error) || error is InvalidOperationException) { return null; }
    }

    private static bool CanTrimBackground(SafeFileHandle handle, int id)
    {
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || GetWindowThreadProcessId(foreground, out uint foregroundId) == 0 ||
            foregroundId == (uint)id || !IsProcessCritical(handle, out bool critical) || critical ||
            !OpenProcessToken(handle, 8, out var token)) return false;
        using (token)
        using (var owner = new WindowsIdentity(token.DangerousGetHandle()))
        using (var user = WindowsIdentity.GetCurrent())
            return owner.User != null && owner.User.Equals(user.User) &&
                GetTokenInformation(token, 20, out uint elevated, 4, out _) && elevated == 0;
    }

    private static bool Eligible(Process process)
    {
        if (process.MainWindowHandle == IntPtr.Zero || IsWindowsShell(process, out var executable)) return false;
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programs86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var userPrograms = Path.Combine(local, "Programs");
        if (!Policy.Inside(programFiles, executable) && !Policy.Inside(programs86, executable) &&
            !Policy.Inside(userPrograms, executable)) return false;
        if (executable.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) >= 0 ||
            executable.IndexOf("security", StringComparison.OrdinalIgnoreCase) >= 0 ||
            executable.IndexOf("antivirus", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        using var handle = OpenProcess(0x1000, false, process.Id);
        if (handle.IsInvalid || !OpenProcessToken(handle, 8, out var token)) return false;
        using (token)
        using (var owner = new WindowsIdentity(token.DangerousGetHandle()))
        using (var user = WindowsIdentity.GetCurrent())
            return owner.User != null && owner.User.Equals(user.User);
    }

    private static bool IsWindowsShell(Process process, out string executable)
    {
        executable = "";
        using var self = Process.GetCurrentProcess();
        if (process.Id == self.Id || process.SessionId != self.SessionId) return true;
        executable = process.MainModule?.FileName ?? "";
        var windows = Directory.GetParent(Environment.SystemDirectory)!.FullName;
        return executable.Length == 0 || Policy.Inside(windows, executable);
    }

#if NETFRAMEWORK
    private sealed class SafeAccessTokenHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeAccessTokenHandle() : base(true) { }
        protected override bool ReleaseHandle() => CloseHandle(handle);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    }
#endif

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemory
    {
        public uint Size, Faults;
        public UIntPtr PeakWorking, WorkingSet, QuotaPeakPaged, QuotaPaged, QuotaPeakNonpaged,
            QuotaNonpaged, Pagefile, PeakPagefile, PrivateUsage;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeFileHandle OpenProcess(uint access, bool inherit, int id);
    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "K32GetProcessMemoryInfo")]
    private static extern bool GetProcessMemoryInfo(SafeFileHandle handle, out ProcessMemory value, uint size);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(SafeFileHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(
        SafeAccessTokenHandle token, int kind, out uint value, uint size, out uint needed);
    [DllImport("kernel32.dll", EntryPoint = "K32EmptyWorkingSet", SetLastError = true)] private static extern bool EmptyWorkingSet(SafeFileHandle handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool IsProcessCritical(SafeFileHandle handle, [MarshalAs(UnmanagedType.Bool)] out bool critical);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessTimes(SafeFileHandle handle,
        out long created, out long exited, out long kernel, out long user);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
}
