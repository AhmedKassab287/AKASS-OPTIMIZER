using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Akass.Optimizer;

public sealed class DesktopModel : INotifyPropertyChanged
{
    public StateStore Store { get; }
    public Preferences Preferences { get; set; }
    public SystemMetrics Metrics { get; } = new SystemMetrics();
    public ProcessAnalyzer Processes { get; } = new ProcessAnalyzer();
    public StartupService Startup { get; } = new StartupService();
    public ScheduleService Schedule { get; } = new ScheduleService();
    public Cleaner Cleaner { get; } = new Cleaner();
    public HardwareSensors Sensors { get; } = new HardwareSensors();
    public FrameMonitor Frames { get; } = new FrameMonitor();
    public LargeFileFinder LargeFiles { get; } = new LargeFileFinder();
    public Snapshot? Current { get; private set; }
    public IReadOnlyList<(double? Cpu, double? Memory)> Series => series.ToArray();
    private readonly Queue<(double?, double?)> series = new Queue<(double?, double?)>();
    private readonly Queue<Pressure> pressures = new Queue<Pressure>();
    private bool sampling;
    private CancellationTokenSource? operation;
    private readonly AutoMemoryPolicy autoMemory = new AutoMemoryPolicy();
    private bool optimizingMemory;
    public bool Busy => operation != null || optimizingMemory;
    public ProcessRow? AutoMemoryTarget { get; set; }
    public ActivityRecord? LastMemoryOptimization { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public DesktopModel(StateStore store)
    {
        Store = store;
        Preferences = store.ReadSettings();
        LastMemoryOptimization = store.History().FirstOrDefault(x => x.Action == "AutoMemory" || x.Action == "ManualMemory");
    }

    public async Task<bool> Sample(bool includeNetwork, bool includePerformance = false)
    {
        if (sampling) return false;
        sampling = true;
        try
        {
            var sample = await Task.Run(() =>
            {
                var data = Metrics.Sample(Preferences.Sensitivity, includeNetwork);
                if (includePerformance)
                {
                    (data.CpuTemperature, data.GpuTemperature, data.GpuAdapter) = Sensors.Read();
                    (data.FramesPerSecond, data.FrameApp) = Frames.Read();
                }
                else Frames.Stop();
                return data;
            });
            Current = sample;
            series.Enqueue((sample.Cpu, sample.Total > 0 ? sample.Load : (double?)null));
            if (series.Count > 75) series.Dequeue();
            if (sample.Pressure is { } pressure) pressures.Enqueue(pressure);
            if (pressures.Count > 6) pressures.Dequeue();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Current)));
            return true;
        }
        finally { sampling = false; }
    }

    public async Task AutoOptimizeMemory(bool enabled)
    {
        if (Current is not { Total: > 0 } sample) return;
        if (!autoMemory.ShouldOptimize(enabled && !Busy, Preferences.AutoMemoryLimitMiB, sample.Available,
            sample.Load, sample.WindowsLow, SystemMetrics.Uptime, enabled ? SystemMetrics.UserIdle() : null)) return;

        optimizingMemory = true;
        try
        {
            var target = AutoMemoryTarget;
            int limit = Preferences.AutoMemoryLimitMiB;
            var outcome = await Task.Run(() => OptimizeMemory(target, limit));
            LastMemoryOptimization = outcome;
        }
        finally { optimizingMemory = false; }
    }

    public async Task<ActivityRecord> OptimizeMemoryManually(ProcessRow? target = null)
    {
        var record = await Task.Run(() => OptimizeMemory(target, null));
        LastMemoryOptimization = record;
        return record;
    }

    private ActivityRecord OptimizeMemory(ProcessRow? target, int? automaticLimit)
    {
        string action = automaticLimit.HasValue ? "AutoMemory" : "ManualMemory";
        var before = Metrics.Sample(includeNetwork: false);
        if (automaticLimit.HasValue && (before.Total == 0 ||
            before.Available >= (ulong)automaticLimit.Value * Policy.MiB || !(before.Load >= 80 || before.WindowsLow == true) ||
            SystemMetrics.UserIdle() is not { } idle || idle < AutoMemoryPolicy.IdlePeriod))
            return new ActivityRecord { Action = action, Detail = "recovered" };

        var measured = ProcessAnalyzer.TrimWorkingSet(target, requireUserIdle: automaticLimit.HasValue);
        var after = Metrics.Sample(includeNetwork: false);
        string detail = measured is { } trim
            ? string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}|{3}|{4}", target?.Name ?? "AKASS Optimizer",
                trim.Before, trim.After, before.Available, after.Available)
            : "unavailable";
        Store.Log(action, detail);
        return new ActivityRecord { Action = action, Detail = detail };
    }

    public Pressure? Sustained => pressures.Count < 3 ? (Pressure?)null :
        pressures.Count(x => x >= Pressure.High) >= 3 ?
            (pressures.Count(x => x == Pressure.Critical) >= 3 ? Pressure.Critical : Pressure.High) :
            pressures.Count(x => x >= Pressure.Elevated) >= 3 ? Pressure.Elevated : Pressure.Healthy;

    public async Task<T> Run<T>(Func<CancellationToken, Task<T>> step)
    {
        if (Busy) throw new InvalidOperationException("Another operation is active.");
        using var cancellation = new CancellationTokenSource();
        operation = cancellation;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Busy)));
        try { return await step(cancellation.Token); }
        finally
        {
            operation = null;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Busy)));
        }
    }

    public void Cancel() => operation?.Cancel();

    public static string CleanupLog(CleanupResult result) => string.Format(CultureInfo.InvariantCulture,
        "{0}|{1}|{2}|{3}|{4}", result.Removed, result.LogicalBytes, result.Skipped, result.Failed, result.Cancelled ? 1 : 0);
}
