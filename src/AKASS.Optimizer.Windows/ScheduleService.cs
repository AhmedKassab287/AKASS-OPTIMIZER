using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;

namespace Akass.Optimizer;

public enum Cadence { Daily, Weekly, Monthly }

public sealed class ScheduleStatus
{
    public bool Exists { get; set; }
    public bool Enabled { get; set; }
    public DateTime? Last { get; set; }
    public DateTime? Next { get; set; }
    public int? Result { get; set; }
}

public sealed class ScheduleService
{
    private static string Sid
    {
        get
        {
            using var user = WindowsIdentity.GetCurrent();
            return user.User?.Value ?? throw new InvalidOperationException("User SID unavailable.");
        }
    }
    private static string TaskName => "AKASS Optimizer Maintenance (" + Sid + ")";
    private static string TaskMutex => @"Local\AKASS.Optimizer.Cleanup." + Sid;

    public ScheduleStatus Read() => Use(folder =>
    {
        object? item = Find(folder);
        if (item == null) return new ScheduleStatus();
        try
        {
            dynamic task = item;
            DateTime last = task.LastRunTime;
            DateTime next = task.NextRunTime;
            return new ScheduleStatus { Exists = true, Enabled = task.Enabled, Last = last.Year > 2000 ? last : (DateTime?)null,
                Next = next.Year > 2000 && task.Enabled ? next : (DateTime?)null, Result = last.Year > 2000 ? (int?)task.LastTaskResult : null };
        }
        finally { Release(item); }
    });

    public void Register(Cadence cadence, DateTime starts)
    {
        if (starts <= DateTime.Now || starts > DateTime.Now.AddDays(30)) throw new ArgumentException("Choose a future start within 30 days.");
        Use(folder =>
        {
            object? service = null, definition = null, trigger = null, action = null, saved = null;
            try
            {
                service = Connect();
                definition = ((dynamic)service).NewTask(0);
                dynamic task = definition;
                task.RegistrationInfo.Description = "Remove eligible current-user temporary files older than seven days after opt-in.";
                task.Principal.UserId = Sid;
                task.Principal.LogonType = 3;
                task.Principal.RunLevel = 0;
                task.Settings.Enabled = true;
                task.Settings.DisallowStartIfOnBatteries = true;
                task.Settings.StopIfGoingOnBatteries = false;
                task.Settings.MultipleInstances = 2;
                task.Settings.ExecutionTimeLimit = "PT20M";
                task.Settings.StartWhenAvailable = true;
                trigger = task.Triggers.Create(cadence switch { Cadence.Daily => 2, Cadence.Weekly => 3, _ => 4 });
                dynamic schedule = trigger;
                schedule.StartBoundary = starts.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
                schedule.Enabled = true;
                if (cadence == Cadence.Daily) schedule.DaysInterval = (short)1;
                if (cadence == Cadence.Weekly)
                {
                    schedule.WeeksInterval = (short)1;
                    schedule.DaysOfWeek = (short)(1 << (int)starts.DayOfWeek);
                }
                if (cadence == Cadence.Monthly)
                {
                    schedule.DaysOfMonth = 1 << (Math.Min(starts.Day, 28) - 1);
                    schedule.MonthsOfYear = (short)4095;
                }
                action = task.Actions.Create(0);
                dynamic launch = action;
                launch.Path = LocalExecutable.Path;
                launch.Arguments = "--scheduled";
                launch.WorkingDirectory = Path.GetDirectoryName(LocalExecutable.Path);
                saved = folder.RegisterTaskDefinition(TaskName, task, 6, Sid, null, 3, null);
                return true;
            }
            finally { Release(saved); Release(action); Release(trigger); Release(definition); Release(service); }
        });
    }

    public void SetEnabled(bool enabled) => Use(folder =>
    {
        object? item = Find(folder);
        if (item == null) return false;
        try { ((dynamic)item).Enabled = enabled; return true; }
        finally { Release(item); }
    });

    public void Remove(bool ownExecutableOnly = false) => Use(folder =>
    {
        var item = Find(folder);
        if (item == null) return false;
        try
        {
            if (ownExecutableOnly)
            {
                dynamic actions = ((dynamic)item).Definition.Actions;
                if (actions.Count != 1 || !string.Equals((string)actions.Item(1).Path,
                    LocalExecutable.Path, StringComparison.OrdinalIgnoreCase)) return false;
            }
        }
        finally { Release(item); }
        folder.DeleteTask(TaskName, 0);
        return true;
    });

    public static IDisposable EnterCleanup()
    {
        var mutex = new Mutex(false, TaskMutex);
        try
        {
            bool owns;
            try { owns = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { owns = true; }
            if (!owns) throw new InvalidOperationException("Another cleanup is running.");
            return new CleanupLease(mutex);
        }
        catch { mutex.Dispose(); throw; }
    }

    private sealed class CleanupLease : IDisposable
    {
        private readonly Mutex mutex;
        public CleanupLease(Mutex mutex) => this.mutex = mutex;
        public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
    }

    private static object? Find(dynamic folder)
    {
        try { return folder.GetTask(TaskName); }
        catch (Exception error) when (IsMissingTask(error)) { return null; }
    }

    internal static bool IsMissingTask(Exception error) => error.HResult == unchecked((int)0x80070002) ||
        error.HResult == unchecked((int)0x8004130F);

    private static object Connect()
    {
        object service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        try { ((dynamic)service).Connect(); return service; }
        catch { Release(service); throw; }
    }

    private static T Use<T>(Func<dynamic, T> action)
    {
        object service = Connect();
        object? folder = null;
        try { folder = ((dynamic)service).GetFolder("\\"); return action(folder!); }
        finally { Release(folder); Release(service); }
    }

    private static void Release(object? value)
    {
        if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }
}
