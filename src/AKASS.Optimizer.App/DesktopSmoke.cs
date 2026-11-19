using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
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
                CheckArabicReadingOrder(view);
                CheckUpdateBadge(view);
                view.SmokePage(Page.Monitor, "en");
                view.SmokeOverlayPlacement();
                view.SmokePage(Page.Memory, "en");
                var memoryOptions = LogicalChildren(view).OfType<CheckBox>().ToDictionary(control =>
                    AutomationProperties.GetAutomationId(control), control => control.IsChecked);
                if (memoryOptions["MemoryFreeRamOption"] != true || memoryOptions["MemoryStandbyOption"] != false ||
                    memoryOptions["MemoryDeepOption"] != false ||
                    !LogicalChildren(view).OfType<Button>().Any(x => AutomationProperties.GetAutomationId(x) == "MemoryOptimizeNow"))
                    throw new InvalidOperationException("Manual memory controls do not expose the two options safely by default.");
                var partial = new ActivityRecord { Action = "ManualMemory", Detail = "batch|2|1048576|2097152|cancelled|1048576|1048576|42|33" };
                string summary = new Language("en").HistoryDetail(partial);
                if (!summary.Contains("Processes trimmed 2") || !summary.Contains("Standby clearing cancelled") ||
                    !summary.Contains("1.0 MiB → 2.0 MiB") || !summary.Contains("42% → 33%"))
                    throw new InvalidOperationException("A partially successful memory run does not show both outcomes.");
                await model.OptimizeMemoryManually();
                if (model.LastMemoryOptimization?.Detail.Split('|').Length != 5 ||
                    !model.Store.History().Any(x => x.Action == "ManualMemory"))
                    throw new InvalidOperationException("Manual optimization did not produce a measured history record.");

                model.Preferences.Theme = "Dark";
                view.SmokePage(Page.Dashboard, "en");
                SaveDashboard(view, Path.Combine(folder, "dashboard.png"));

                File.WriteAllText(Path.Combine(folder, "ui-smoke.txt"),
                    "PASS: " + checkedCount + " page, locale and theme scenarios. RTL applied to Arabic.\n" +
                    "PASS: settings validation and persistence, theme changes, scroll track and thumb, manual memory action and history.\n" +
                    "PASS: RAM and standby controls are independent and partial results remain visible.\n" +
                    "PASS: The mouse grip is clickable. Releasing the overlay saves its position and restores transparent click-through.\n" +
                    "PASS: Arabic settings accept Arabic-script digits. Mixed values, versions and paths keep their reading order.\n" +
                    "PASS: The Settings update badge and accessible status follow update availability in all three languages.");
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

        Control<TextBox>("SettingsRefresh").Text = "٧";
        Control<TextBox>("SettingsMemoryLimit").Text = "۹۶۰";
        Save();
        saved = model.Store.ReadSettings();
        if (saved.Refresh != 7 || saved.AutoMemoryLimitMiB != 960 ||
            Control<TextBox>("SettingsRefresh").FlowDirection != FlowDirection.LeftToRight ||
            Control<TextBox>("SettingsRefresh").Text != "7" || Control<TextBox>("SettingsMemoryLimit").Text != "960")
            throw new InvalidOperationException("Arabic numerical entry did not round-trip in Settings.");

        Control<ComboBox>("SettingsLanguage").SelectedIndex = 2;
        Control<ComboBox>("SettingsTheme").SelectedIndex = 0;
        Control<CheckBox>("SettingsAutoMemory").IsChecked = false;
        Save();
        saved = model.Store.ReadSettings();
        if (saved.Language != "tr" || saved.Theme != "Dark" || saved.AutoMemory || saved.AutoMemoryLimitMiB != 960 ||
            view.FlowDirection != FlowDirection.LeftToRight || Control<TextBox>("SettingsMemoryLimit").IsEnabled)
            throw new InvalidOperationException("Changes did not survive persistence and the Turkish rebuild.");
        await CheckScroll(view);
    }

    private static void CheckArabicReadingOrder(MainWindow view)
    {
        view.SmokePage(Page.About, "ar");
        var language = new Language("ar");
        var date = new DateTime(2026, 11, 19, 9, 35, 0);
        if (NumberSubstitution.GetSubstitution(view) != NumberSubstitutionMethod.European ||
            Policy.Bytes(Policy.GiB + Policy.GiB / 2) != "1.5 GiB" ||
            Language.PlainText(language.DateLabel(date)) != "2026-11-19 09:35" ||
            !TimeSpan.TryParseExact(Language.NormalizeNumberInput("٢٣:۵۹"), @"hh\:mm", CultureInfo.InvariantCulture, out var time) ||
            time != new TimeSpan(23, 59, 0))
            throw new InvalidOperationException("The Arabic number, date, or time format is inconsistent.");

        string version = typeof(MainWindow).Assembly.GetName().Version!.ToString(3);
        if (!LogicalChildren(view).OfType<TextBlock>().Any(label => label.Inlines.OfType<Run>().Any(run =>
            run.Text.Contains(version) && run.FlowDirection == FlowDirection.LeftToRight)))
            throw new InvalidOperationException("The Arabic version label has no isolated numeric value.");

        var label = new TextBlock { FlowDirection = FlowDirection.RightToLeft, Language = view.Language };
        NumberSubstitution.SetSubstitution(label, NumberSubstitutionMethod.European);
        foreach (string value in new[] { "75/100", "12.5%", "1.5 GiB → 2.0 GiB", version, @"C:\Users\ملف 42\test (2).txt" })
        {
            string isolated = language.Technical(value);
            Language.SetDisplayText(label, "القيمة " + isolated + " متاحة");
            var runs = label.Inlines.OfType<Run>().ToArray();
            if (runs.Length != 3 || runs[1].FlowDirection != FlowDirection.LeftToRight || runs[1].Text != value || Language.PlainText(isolated) != value)
                throw new InvalidOperationException("A technical value could not be isolated for Arabic.");
            label.Measure(new Size(600, 100));
            label.Arrange(new Rect(0, 0, 600, 100));
            double Position(Run run, int offset)
            {
                var leading = run.ContentStart.GetPositionAtOffset(offset)!.GetCharacterRect(LogicalDirection.Forward);
                var trailing = run.ContentStart.GetPositionAtOffset(offset + 1)!.GetCharacterRect(LogicalDirection.Backward);
                if (leading.IsEmpty || trailing.IsEmpty) throw new InvalidOperationException("Text was not laid out.");
                return (leading.X + trailing.X) / 2;
            }
            var start = Position(runs[1], 0);
            var end = Position(runs[1], value.Length - 1);
            var arabicStart = Position(runs[0], 1);
            var arabicEnd = Position(runs[0], 4);
            if ((end - start) * (arabicEnd - arabicStart) >= 0)
                throw new InvalidOperationException($"A value is rendered out of reading order: {value} ({start}, {end}; {arabicStart}, {arabicEnd}).");
        }

        view.SmokePage(Page.LargeFiles, "ar");
        var grid = LogicalChildren(view).OfType<DataGrid>().Single();
        if (grid.FlowDirection != FlowDirection.RightToLeft || grid.Columns.OfType<DataGridBoundColumn>().Any(column =>
            !column.ElementStyle.Setters.OfType<Setter>().Any(setter => setter.Property == FrameworkElement.FlowDirectionProperty &&
                (FlowDirection)setter.Value == FlowDirection.LeftToRight)))
            throw new InvalidOperationException("The file table does not separate RTL headings from LTR values.");
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

    private static void CheckUpdateBadge(MainWindow view)
    {
        foreach (var (locale, hint) in new[] { ("en", "Update available"), ("ar", "تحديث متاح"), ("tr", "Güncelleme mevcut") })
        {
            view.SmokePage(Page.Dashboard, locale);
            var settings = LogicalChildren(view).OfType<Button>().Single(item =>
                AutomationProperties.GetAutomationId(item) == "SettingsNavigation");
            var badge = LogicalChildren(settings).OfType<Border>().Single(item =>
                AutomationProperties.GetAutomationId(item) == "SettingsUpdateBadge");
            if (badge.Visibility != Visibility.Collapsed || settings.ToolTip != null)
                throw new InvalidOperationException("The Settings badge appeared without an update.");
            view.RefreshUpdateBadge(true);
            if (badge.Visibility != Visibility.Visible || !Equals(settings.ToolTip, hint) ||
                AutomationProperties.GetItemStatus(settings) != hint)
                throw new InvalidOperationException("The Settings badge did not announce the available update.");

            settings.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            if (badge.Visibility != Visibility.Visible || !LogicalChildren(view).OfType<Button>().Any(item =>
                AutomationProperties.GetAutomationId(item) == "CheckForUpdates"))
                throw new InvalidOperationException("Opening Settings hid the update badge or its update controls.");
            view.RefreshUpdateBadge(false);
            if (badge.Visibility != Visibility.Collapsed || settings.ToolTip != null || AutomationProperties.GetItemStatus(settings) != "")
                throw new InvalidOperationException("The Settings badge did not clear when no update remained.");
        }
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
