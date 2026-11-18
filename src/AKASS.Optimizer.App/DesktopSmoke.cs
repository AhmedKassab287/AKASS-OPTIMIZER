using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Akass.Optimizer;

internal static class DesktopSmoke
{
    public static int Run(string folder)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int exit = 1;
        app.Startup += async (_, __) =>
        {
            Directory.CreateDirectory(folder);
            try
            {
                var model = new DesktopModel(new StateStore(Path.Combine(folder, "state-" + Guid.NewGuid().ToString("N"))));
                var view = new MainWindow(model, true);
                view.ShowActivated = false;
                view.ShowInTaskbar = false;
                app.MainWindow = view;
                view.Show();
                view.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
                var cases = from language in new[] { "en", "ar", "tr" }
                    from theme in new[] { "Dark", "Light", "System" }
                    from page in (Page[])Enum.GetValues(typeof(Page)) select new { language, theme, page };
                int checkedCount = 0;
                foreach (var test in cases)
                {
                    model.Preferences.Theme = test.theme;
                    if (!view.SmokePage(test.page, test.language))
                        throw new InvalidOperationException("Page failed: " + test.language + " / " + test.page);
                    checkedCount++;
                }
                await CheckSettings(view, model);
                view.SmokePage(Page.Memory, "en");
                var optimize = LogicalChildren(view).OfType<Button>().Single(x => x.Content as string == "Optimize AKASS now");
                optimize.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                for (int attempt = 0; attempt < 50 && model.Busy; attempt++) await Task.Delay(100);
                if (model.LastMemoryOptimization?.Detail.Split('|').Length != 5 ||
                    !model.Store.History().Any(x => x.Action == "ManualMemory"))
                    throw new InvalidOperationException("Manual optimization did not produce a measured history record.");

                model.Preferences.Theme = "Dark";
                view.SmokePage(Page.Dashboard, "en");
                SaveDashboard(view, Path.Combine(folder, "dashboard.png"));

                File.WriteAllText(Path.Combine(folder, "ui-smoke.txt"),
                    "PASS: " + checkedCount + " page, locale and theme scenarios. RTL applied to Arabic.\n" +
                    "PASS: settings validation and persistence, theme changes, scroll track and thumb, manual memory action and history.");
                exit = 0;
                view.Close();
            }
            catch (Exception error)
            {
                File.WriteAllText(Path.Combine(folder, "ui-smoke.txt"), error.ToString());
                app.Shutdown();
            }
        };
        app.Run();
        return exit;
    }

    private static async Task CheckSettings(MainWindow view, DesktopModel model)
    {
        T Control<T>(string id) where T : FrameworkElement => LogicalChildren(view).OfType<T>()
            .Single(element => AutomationProperties.GetAutomationId(element) == id);
        void Save() => Control<Button>("SaveSettings").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        view.SmokePage(Page.Settings, "en");
        Control<ComboBox>("SettingsLanguage").SelectedIndex = 1;
        Control<ComboBox>("SettingsTheme").SelectedIndex = 1;
        Control<CheckBox>("SettingsTray").IsChecked = false;
        Control<CheckBox>("SettingsBackground").IsChecked = false;
        Control<CheckBox>("SettingsNotifications").IsChecked = false;
        Control<CheckBox>("SettingsAdvanced").IsChecked = true;
        Control<ComboBox>("SettingsSensitivity").SelectedIndex = 2;
        Control<CheckBox>("CleanupChrome").IsChecked = true;
        Control<CheckBox>("SettingsAutoMemory").IsChecked = true;
        Control<TextBox>("SettingsRefresh").Text = "0";
        Control<TextBox>("SettingsMemoryLimit").Text = "127";
        Save();
        if (model.Store.ReadSettings().Language != "en")
            throw new InvalidOperationException("An invalid refresh interval was saved.");
        Control<TextBox>("SettingsRefresh").Text = "5";
        Save();
        if (model.Store.ReadSettings().Language != "en")
            throw new InvalidOperationException("An invalid memory limit was saved.");
        Control<TextBox>("SettingsMemoryLimit").Text = "768";
        Save();
        var saved = model.Store.ReadSettings();
        if (saved.Language != "ar" || saved.Theme != "Light" || saved.Refresh != 5 || saved.Tray || saved.Background ||
            saved.Notifications || !saved.Advanced || saved.Sensitivity != 3 || !saved.CleanupDefaults.Contains("Chrome") ||
            !saved.AutoMemory || saved.AutoMemoryLimitMiB != 768 || view.FlowDirection != FlowDirection.RightToLeft)
            throw new InvalidOperationException("Changes did not survive persistence and the Arabic rebuild.");
        await CheckScroll(view);

        Control<ComboBox>("SettingsLanguage").SelectedIndex = 2;
        Control<ComboBox>("SettingsTheme").SelectedIndex = 0;
        Control<CheckBox>("SettingsAutoMemory").IsChecked = false;
        Save();
        saved = model.Store.ReadSettings();
        if (saved.Language != "tr" || saved.Theme != "Dark" || saved.AutoMemory || saved.AutoMemoryLimitMiB != 768 ||
            view.FlowDirection != FlowDirection.LeftToRight || Control<TextBox>("SettingsMemoryLimit").IsEnabled)
            throw new InvalidOperationException("Changes did not survive persistence and the Turkish rebuild.");
        await CheckScroll(view);
    }

    private static async Task CheckScroll(MainWindow view)
    {
        await view.Dispatcher.InvokeAsync(view.UpdateLayout, DispatcherPriority.ApplicationIdle);
        var viewer = LogicalChildren(view).OfType<ScrollViewer>().Single(x => x.Padding.Top == 22);
        if (viewer.ScrollableHeight <= 0)
            throw new InvalidOperationException("The settings view did not overflow for the scrollbar check.");
        viewer.ScrollToTop();
        await view.Dispatcher.InvokeAsync(view.UpdateLayout, DispatcherPriority.ApplicationIdle);
        var bar = (ScrollBar)viewer.Template.FindName("PART_VerticalScrollBar", viewer);
        if (SystemParameters.HighContrast) return;
        if (bar.Template.FindName("PART_Track", bar) is not Track track || track.Thumb.MinHeight != 24)
            throw new InvalidOperationException("The scroll viewer did not use the themed rounded thumb.");
        ((RoutedCommand)track.IncreaseRepeatButton.Command).Execute(null, track.IncreaseRepeatButton);
        await view.Dispatcher.InvokeAsync(view.UpdateLayout, DispatcherPriority.ApplicationIdle);
        if (viewer.VerticalOffset <= 0) throw new InvalidOperationException("Clicking the track did not scroll the page.");
        viewer.ScrollToTop();
        await view.Dispatcher.InvokeAsync(view.UpdateLayout, DispatcherPriority.ApplicationIdle);
        track.Thumb.RaiseEvent(new DragDeltaEventArgs(0, 50) { RoutedEvent = Thumb.DragDeltaEvent });
        await view.Dispatcher.InvokeAsync(view.UpdateLayout, DispatcherPriority.ApplicationIdle);
        if (viewer.VerticalOffset <= 0) throw new InvalidOperationException("Dragging the rounded thumb did not scroll the page.");
    }

    private static IEnumerable<DependencyObject> LogicalChildren(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in LogicalChildren(child)) yield return descendant;
        }
    }

    public static int RunLive(string folder)
    {
        Directory.CreateDirectory(folder);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int exit = 1;
        app.Startup += async (_, __) =>
        {
            try
            {
                var model = new DesktopModel(new StateStore(Path.Combine(folder, "state")));
                model.Preferences.Tray = false;
                model.Preferences.Background = false;
                var view = new MainWindow(model) { ShowActivated = false, ShowInTaskbar = false };
                app.MainWindow = view;
                view.Show();
                await Task.Delay(2500);
                using var process = Process.GetCurrentProcess();
                using var user = WindowsIdentity.GetCurrent();
                var startCpu = process.TotalProcessorTime;
                var elapsed = Stopwatch.StartNew();
                await Task.Delay(6000);
                process.Refresh();
                double cpu = 100 * (process.TotalProcessorTime - startCpu).TotalSeconds /
                    (elapsed.Elapsed.TotalSeconds * Environment.ProcessorCount);
                if (model.Current?.Total is not > 0 || model.Current?.Cpu == null || model.Series.Count < 3)
                    throw new InvalidOperationException("The live dashboard did not receive repeated system samples.");
                SaveDashboard(view, Path.Combine(folder, "dashboard-live.png"));
                File.WriteAllLines(Path.Combine(folder, "live-smoke.txt"), new[]
                {
                    "PASS: live dashboard received " + model.Series.Count + " real samples.",
                    "Windows build: " + SystemMetrics.WindowsVersion,
                    "Elevated: " + new WindowsPrincipal(user).IsInRole(WindowsBuiltInRole.Administrator),
                    FormattableString.Invariant($"Average process CPU across logical processors: {cpu:0.00}% over {elapsed.Elapsed.TotalSeconds:0.0} s."),
                    FormattableString.Invariant($"Process private memory: {process.PrivateMemorySize64 / (double)Policy.MiB:0.0} MiB.")
                });
                exit = 0;
                view.Close();
            }
            catch (Exception error)
            {
                File.WriteAllText(Path.Combine(folder, "live-smoke.txt"), error.ToString());
                app.Shutdown();
            }
        };
        app.Run();
        return exit;
    }

    private static void SaveDashboard(MainWindow view, string filename)
    {
        var content = (FrameworkElement)view.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight),
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(filename);
        png.Save(output);
    }
}
