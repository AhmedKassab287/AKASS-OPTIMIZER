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

public enum FrameCaptureStatus { Unavailable, WaitingForFrames, PermissionDenied, Failed, Displayed, AppSubmitted }

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
    private FrameCaptureStatus status;
    private bool disposed;

    public bool IsAvailable => File.Exists(executable);

    public (double? Fps, string App, FrameCaptureStatus Status) Read()
    {
        lock (gate)
        {
            if (disposed || !IsAvailable) return (null, "", FrameCaptureStatus.Unavailable);
            GetWindowThreadProcessId(ForegroundWindow, out var pid);
            if (pid > 0 && pid != ownPid && pid != target)
            {
                StopCore();
                target = (int)pid;
                retryAfter = DateTime.MinValue;
            }
            if (target == 0) return (null, "", FrameCaptureStatus.WaitingForFrames);
            if ((capture == null || capture.HasExited) && DateTime.UtcNow >= retryAfter && status != FrameCaptureStatus.PermissionDenied)
            {
                StopCore();
                StartCapture();
            }
            double? value = frames.Read(DateTime.UtcNow, out bool submitted);
            return (value, app, value.HasValue ? (submitted ? FrameCaptureStatus.AppSubmitted : FrameCaptureStatus.Displayed) : status);
        }
    }

    public void Stop()
    {
        lock (gate)
        {
            StopCore();
            target = 0;
            status = FrameCaptureStatus.WaitingForFrames;
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
            status = FrameCaptureStatus.WaitingForFrames;
            capture = new Process { StartInfo = StartInfo("--process_id " + target.ToString(CultureInfo.InvariantCulture) +
                " --session_name " + session +
                " --output_stdout --no_console_stats --v1_metrics --no_track_input --no_track_gpu --terminate_on_proc_exit --timed 120 --terminate_after_timed") };
            capture.OutputDataReceived += (sender, e) =>
            {
                lock (gate)
                    if (ReferenceEquals(sender, capture) && e.Data != null) frames.Add(e.Data, DateTime.UtcNow);
            };
            capture.ErrorDataReceived += (sender, e) =>
            {
                lock (gate)
                    if (ReferenceEquals(sender, capture) && e.Data != null)
                        status = ReadDiagnostic(status, e.Data);
            };
            if (!capture.Start()) { StopCore(); return; }
            capture.BeginOutputReadLine();
            capture.BeginErrorReadLine();
        }
        catch (Exception error) when (error is Win32Exception or ArgumentException or InvalidOperationException or IOException)
        {
            StopCore();
            status = error is Win32Exception { NativeErrorCode: 5 or 740 }
                ? FrameCaptureStatus.PermissionDenied : FrameCaptureStatus.Failed;
        }
    }

    internal static FrameCaptureStatus ReadDiagnostic(FrameCaptureStatus current, string message)
    {
        if (message.IndexOf("access denied", StringComparison.OrdinalIgnoreCase) >= 0 ||
            message.IndexOf("Performance Log Users", StringComparison.OrdinalIgnoreCase) >= 0)
            return FrameCaptureStatus.PermissionDenied;
        return current != FrameCaptureStatus.PermissionDenied && message.StartsWith("error:", StringComparison.OrdinalIgnoreCase)
            ? FrameCaptureStatus.Failed : current;
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
    private readonly Dictionary<string, Queue<(DateTime Time, double? Displayed, double? Submitted)>> samples =
        new Dictionary<string, Queue<(DateTime, double?, double?)>>(StringComparer.Ordinal);
    private int chainColumn = -1, displayedColumn = -1, submittedColumn = -1;

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
            displayedColumn = Array.FindIndex(cells, cell => cell.Equals("MsBetweenDisplayChange", StringComparison.OrdinalIgnoreCase));
            submittedColumn = Array.FindIndex(cells, cell => cell.Equals("MsBetweenPresents", StringComparison.OrdinalIgnoreCase));
            return;
        }
        if (chainColumn < 0 || cells.Length <= chainColumn) return;
        double? displayed = ParseDuration(cells, displayedColumn), submitted = ParseDuration(cells, submittedColumn);
        if (displayed == null && submitted == null) return;
        if (!samples.TryGetValue(cells[chainColumn], out var chain))
        {
            if (samples.Count >= 16) return;
            samples.Add(cells[chainColumn], chain = new Queue<(DateTime, double?, double?)>());
        }
        chain.Enqueue((now, displayed, submitted));
        if (chain.Count > 240) chain.Dequeue();
    }

    private static double? ParseDuration(string[] cells, int column) => column >= 0 && column < cells.Length &&
        double.TryParse(cells[column], NumberStyles.Float, CultureInfo.InvariantCulture, out double value) &&
        value > 0 && value <= 2000 ? value : (double?)null;

    public double? Read(DateTime now, out bool submitted)
    {
        Queue<(DateTime Time, double? Displayed, double? Submitted)>? best = null;
        foreach (var key in samples.Keys.ToArray())
        {
            var chain = samples[key];
            while (chain.Count > 0 && now - chain.Peek().Time > TimeSpan.FromSeconds(2.5)) chain.Dequeue();
            if (chain.Count == 0) samples.Remove(key);
            else if (best == null || chain.Count > best.Count) best = chain;
        }
        submitted = false;
        if (best == null) return null;
        if (best.Count(frame => frame.Displayed.HasValue) >= 2) return 1000 / best.Average(frame => frame.Displayed);
        if (best.Count(frame => frame.Submitted.HasValue) < 2) return null;
        submitted = true;
        return 1000 / best.Average(frame => frame.Submitted);
    }
}
