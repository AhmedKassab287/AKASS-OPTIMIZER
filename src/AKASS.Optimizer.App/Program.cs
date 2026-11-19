using System;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Windows;

#if NETFRAMEWORK
[assembly: System.Reflection.AssemblyVersion("1.2.1.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.2.1.0")]
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
                return app.Run();
            }
            finally { signal.Unregister(null); }
        }
        catch (Exception error)
        {
            MessageBox.Show("AKASS Optimizer could not start.\n" + error.Message, "AKASS Optimizer",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
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
