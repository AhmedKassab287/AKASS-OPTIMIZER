using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Windows;

#if NETFRAMEWORK
[assembly: System.Reflection.AssemblyVersion("1.4.2.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.4.2.0")]
[assembly: System.Reflection.AssemblyCompany("Ahmed Kassab")]
[assembly: System.Reflection.AssemblyProduct("AKASS Optimizer")]
[assembly: System.Reflection.AssemblyTitle("AKASS Optimizer")]
[assembly: System.Reflection.AssemblyDescription("Windows maintenance and resource monitoring")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]
#endif

namespace Akass.Optimizer;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
#if NETFRAMEWORK
        AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling", false);
        AppContext.SetSwitch("Switch.System.IO.BlockLongPaths", false);
        AppContext.SetSwitch("Switch.System.Windows.DoNotScaleForDpiChanges", false);
        AppContext.SetSwitch("Switch.System.Windows.DoNotUsePresentationDpiCapabilityTier2OrGreater", false);
#endif
        if (args.Length == 3 && args[0] == "--repair-helper")
        {
            try { return RepairService.Helper(args[1], args[2]); }
            catch { return 1; }
        }
        var store = new StateStore();
        if (args.SequenceEqual(new[] { "--scheduled" })) return RunScheduled(store);
        if (args.SequenceEqual(new[] { "--uninstall" })) return Unregister(store);
        if (args.Length == 2 && args[0] == "--smoke-ui") return DesktopSmoke.Run(args[1]);
        if (args.Length == 2 && args[0] == "--smoke-live") return DesktopSmoke.RunLive(args[1]);

        try
        {
            int exitCode = RunDesktop(args, store, out string? installer, out bool restartElevated);
            if (installer != null)
            {
                using var setup = Process.Start(new ProcessStartInfo
                {
                    FileName = installer,
                    Arguments = "/SILENT /NORESTART /SP- /AKASSUPDATE=1",
                    UseShellExecute = true
                });
                if (setup == null) throw new InvalidOperationException("Windows did not start the update installer.");
            }
            else if (restartElevated)
            {
                using var current = Process.GetCurrentProcess();
                var start = new ProcessStartInfo { FileName = current.MainModule!.FileName, UseShellExecute = true, Verb = "runas" };
                try { Process.Start(start)?.Dispose(); }
                catch (Win32Exception error) when (error.NativeErrorCode == 1223)
                {
                    start.Verb = "open";
                    Process.Start(start)?.Dispose();
                }
            }
            return exitCode;
        }
        catch (Exception error)
        {
            MessageBox.Show("AKASS Optimizer\n\n" + error.Message, "AKASS Optimizer", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }

    private static int RunDesktop(string[] args, StateStore store, out string? installer, out bool restartElevated)
    {
        installer = null;
        restartElevated = false;
        using var identity = WindowsIdentity.GetCurrent();
        using var installerGuard = new Mutex(false, @"Local\AKASS.Optimizer.InUse");
        using var instance = new Mutex(false, @"Local\AKASS.Optimizer.UI." + identity.User?.Value);
        bool acquired;
        try { acquired = instance.WaitOne(0); }
        catch (AbandonedMutexException) { acquired = true; }
        using var activation = new EventWaitHandle(false, EventResetMode.AutoReset,
            @"Local\AKASS.Optimizer.Activate." + identity.User?.Value);
        if (!acquired) { activation.Set(); return 0; }
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var services = new DesktopModel(store);
            var window = new MainWindow(services);
            app.MainWindow = window;
            var signal = ThreadPool.RegisterWaitForSingleObject(activation, (_, __) =>
                window.Dispatcher.BeginInvoke(new Action(window.RestoreWindow)), null, -1, false);
            try
            {
                if (!args.Contains("--tray") || !services.Preferences.Tray) window.Show();
                else new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
                int exitCode = app.Run();
                installer = window.PendingInstaller;
                restartElevated = window.PendingElevatedRestart;
                return exitCode;
            }
            finally { signal.Unregister(null); }
        }
        finally { instance.ReleaseMutex(); }
    }

    private static int RunScheduled(StateStore store)
    {
        try
        {
            using var installerGuard = new Mutex(false, @"Local\AKASS.Optimizer.InUse");
            using var lease = ScheduleService.EnterCleanup();
            if (!store.ReadSettings().ScheduledConsent) return 0;
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(17));
            var cleaner = new Cleaner();
            var scan = cleaner.Scan(timeout.Token, scheduled: true);
            var result = cleaner.Clean(scan, new[] { CleanupKind.UserTemp }, false, timeout.Token, scheduled: true);
            store.Log("ScheduledCleanup", DesktopModel.CleanupLog(result));
            return result.Cancelled || result.Failed > 0 ? 1 : 0;
        }
        catch (Exception e)
        {
            try { store.Log("ScheduledCleanup", e.GetType().Name + " (" + e.HResult.ToString("X8") + ")"); }
            catch { }
            return 1;
        }
    }

    private static int Unregister(StateStore store)
    {
        try
        {
            using var lease = ScheduleService.EnterCleanup();
            var preferences = store.ReadSettings();
            if (preferences.OriginalPlan != Guid.Empty) GamingService.Restore(store, preferences);
            var startup = new StartupService();
            if (startup.LaunchesWithWindows) startup.SetLaunchWithWindows(false);
            new ScheduleService().Remove(ownExecutableOnly: true);
            return 0;
        }
        catch { return 1; }
    }
}
