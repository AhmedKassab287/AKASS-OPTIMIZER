using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Akass.Optimizer;

public sealed class FrameMonitor : IDisposable
{
    private readonly string executable = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "PresentMon", "PresentMon.exe");
    private readonly object gate = new object();
    private readonly int ownPid = CurrentPid();
    private Process? capture;
    private string session = "", app = "";
    private int target;
    private DateTime retryAfter;
    private FrameCsv frames = new FrameCsv();
    private bool disposed;

    public bool IsAvailable => File.Exists(executable);

    public (double? Fps, string App) Read()
    {
        lock (gate)
        {
            if (disposed || !IsAvailable) return (null, "");
            GetWindowThreadProcessId(ForegroundWindow, out var pid);
            if (pid > 0 && pid != ownPid && pid != target)
            {
                StopCore();
                target = (int)pid;
                retryAfter = DateTime.MinValue;
            }
            if (target == 0) return (null, "");
            if ((capture == null || capture.HasExited) && DateTime.UtcNow >= retryAfter)
            {
                StopCore();
                StartCapture();
            }
            return (frames.Read(DateTime.UtcNow), app);
        }
    }

    public void Stop()
    {
        lock (gate)
        {
            StopCore();
            target = 0;
        }
    }

    private void StartCapture()
    {
        retryAfter = DateTime.UtcNow.AddSeconds(6);
        try
        {
            using var process = Process.GetProcessById(target);
            app = process.ProcessName;
            session = "AKASS-" + Guid.NewGuid().ToString("N");
            capture = new Process { StartInfo = StartInfo("--process_id " + target.ToString(CultureInfo.InvariantCulture) +
                " --session_name " + session +
                " --output_stdout --no_console_stats --v1_metrics --no_track_input --no_track_gpu --terminate_on_proc_exit --timed 45 --terminate_after_timed") };
            capture.OutputDataReceived += (sender, e) =>
            {
                lock (gate)
                    if (ReferenceEquals(sender, capture) && e.Data != null) frames.Add(e.Data, DateTime.UtcNow);
            };
            capture.ErrorDataReceived += (_, _) => { };
            if (!capture.Start()) { StopCore(); return; }
            capture.BeginOutputReadLine();
            capture.BeginErrorReadLine();
        }
        catch (Exception error) when (error is Win32Exception or ArgumentException or InvalidOperationException or IOException)
        {
            StopCore();
        }
    }

    private ProcessStartInfo StartInfo(string arguments) => new ProcessStartInfo
    {
        FileName = executable, Arguments = arguments, UseShellExecute = false, CreateNoWindow = true,
        WorkingDirectory = Path.GetDirectoryName(executable)!, RedirectStandardOutput = true, RedirectStandardError = true
    };

    private void StopCore()
    {
        var old = capture;
        string oldSession = session;
        capture = null;
        frames = new FrameCsv();
        app = "";
        if (old == null) return;
        _ = Task.Run(() =>
        {
            using (old)
            {
                try
                {
                    if (old.HasExited) return;
                    using var stop = Process.Start(StartInfo("--terminate_existing_session --session_name " + oldSession));
                    if (stop != null && !stop.WaitForExit(2000)) stop.Kill();
                    if (!old.WaitForExit(1500)) old.Kill();
                }
                catch (Exception error) when (error is InvalidOperationException or Win32Exception) { }
            }
        });
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            StopCore();
        }
    }

    private static int CurrentPid() { using var p = Process.GetCurrentProcess(); return p.Id; }
    public static IntPtr ForegroundWindow => GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}

internal sealed class FrameCsv
{
    private readonly Dictionary<string, Queue<(DateTime Time, double Duration)>> samples =
        new Dictionary<string, Queue<(DateTime, double)>>(StringComparer.Ordinal);
    private int chainColumn = -1, durationColumn = -1;

    public void Add(string row, DateTime now)
    {
        if (row.Length > 8192) return;
        // Only the quoted app name can contain a separator in PresentMon's v1 output.
        if (row.StartsWith("\"", StringComparison.Ordinal))
        {
            int end = row.IndexOf("\",", 1, StringComparison.Ordinal);
            if (end < 0) return;
            row = "app" + row.Substring(end + 1);
        }
        var cells = row.Split(',');
        if (cells.Length > 1 && cells[0].TrimStart('\uFEFF') == "Application")
        {
            chainColumn = Array.FindIndex(cells, cell => cell.Equals("SwapChainAddress", StringComparison.OrdinalIgnoreCase));
            durationColumn = Array.FindIndex(cells, cell => cell.Equals("MsBetweenDisplayChange", StringComparison.OrdinalIgnoreCase));
            return;
        }
        if (chainColumn < 0 || durationColumn < 0 || cells.Length <= Math.Max(chainColumn, durationColumn) ||
            !double.TryParse(cells[durationColumn], NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
            !(value > 0 && value <= 2000)) return;
        if (!samples.TryGetValue(cells[chainColumn], out var chain))
        {
            if (samples.Count >= 16) return;
            samples.Add(cells[chainColumn], chain = new Queue<(DateTime, double)>());
        }
        chain.Enqueue((now, value));
        if (chain.Count > 240) chain.Dequeue();
    }

    public double? Read(DateTime now)
    {
        Queue<(DateTime Time, double Duration)>? best = null;
        foreach (var key in samples.Keys.ToArray())
        {
            var chain = samples[key];
            while (chain.Count > 0 && now - chain.Peek().Time > TimeSpan.FromSeconds(2.5)) chain.Dequeue();
            if (chain.Count == 0) samples.Remove(key);
            else if (best == null || chain.Count > best.Count) best = chain;
        }
        return best?.Count >= 2 ? 1000 / best.Average(frame => frame.Duration) : null;
    }
}
