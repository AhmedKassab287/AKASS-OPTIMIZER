using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Akass.Optimizer;

internal enum Page { Dashboard, Cleaner, Memory, Startup, Monitor, LargeFiles, Repair, Battery, Gaming, History, Settings, About }

internal sealed partial class MainWindow : Window
{
    private readonly DesktopModel model;
    private readonly Forms.NotifyIcon tray;
    private readonly DispatcherTimer clock;
    private readonly bool testMode;
    private static readonly string VersionLabel = typeof(MainWindow).Assembly.GetName().Version!.ToString(3);
    private Language words;
    private Palette theme = null!;
    private ContentControl content = null!;
    private ScrollViewer pageScroll = null!;
    private TextBlock status = null!;
    private TextBlock title = null!;
    private ProgressBar traffic = null!;
    private Button cancel = null!;
    private Border header = null!;
    private readonly Dictionary<Page, Button> navigation = new Dictionary<Page, Button>();
    private Page page;
    private int generation;
    private bool exiting;
    private bool notifyPressure;
    private bool canCancel;
    private DateTime lastProcessRefresh;
    private Action? refreshLive;
    private Action<IReadOnlyList<ProcessRow>>? refreshProcesses;

    public MainWindow(DesktopModel model, bool testMode = false)
    {
        this.model = model;
        this.testMode = testMode;
        words = new Language(model.Preferences.Language);
        Width = 1160;
        Height = 850;
        MinWidth = 790;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 14;
        Title = "AKASS Optimizer";
        using var thisProcess = Process.GetCurrentProcess();
        var ownIcon = System.Drawing.Icon.ExtractAssociatedIcon(thisProcess.MainModule!.FileName);
        if (ownIcon != null)
            Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(ownIcon.Handle, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        tray = new Forms.NotifyIcon { Icon = ownIcon ?? System.Drawing.SystemIcons.Application };
        tray.DoubleClick += (_, __) => Dispatcher.BeginInvoke(new Action(RestoreWindow));
        clock = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
        clock.Tick += async (_, __) => await Tick();
        IsVisibleChanged += async (_, __) =>
        {
            RefreshClock();
            if (IsVisible && !testMode) await Tick();
        };
        StateChanged += (_, __) =>
        {
            if (WindowState == WindowState.Minimized && model.Preferences.Tray && !testMode) Hide();
            RefreshClock();
        };
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(model.Busy)) UpdateBusy();
        };
        Closing += ClosingWindow;
        SourceInitialized += (_, __) => { if (!testMode) RegisterOverlayHotkey(); };
        Closed += (_, __) =>
        {
            clock.Stop();
            tray.Visible = false;
            tray.ContextMenuStrip?.Dispose();
            tray.Dispose();
            ownIcon?.Dispose();
            ClosePerformanceViews();
            model.Metrics.Dispose();
            Application.Current.Shutdown();
        };
        Rebuild(Page.Dashboard);
    }

    private string L(string en, string ar, string tr) => words.Pick(en, ar, tr);

    private void Rebuild(Page selected)
    {
        words = new Language(model.Preferences.Language);
        theme = new Palette(model.Preferences.Theme == "Light" ||
            (model.Preferences.Theme == "System" && WindowsPrefersLightTheme()));
        Resources = theme.Resources();
        ApplyReadingDirection(this);
        Background = theme.Canvas;
        navigation.Clear();

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(212) });
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        var sidebar = new DockPanel { LastChildFill = true, Margin = new Thickness(15, 0, 15, 0) };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 27, 0, 29) };
        DockPanel.SetDock(brand, Dock.Top);
        if (Icon != null) brand.Children.Add(new Image { Source = Icon, Width = 36, Height = 36, Margin = new Thickness(0, 0, 9, 0) });
        var brandText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        brandText.Children.Add(Text("AKASS", 17, true));
        brandText.Children.Add(Text("OPTIMIZER", 10, true, theme.Muted));
        brand.Children.Add(brandText);
        sidebar.Children.Add(brand);
        var footer = Text(words.Technical("AKASS  /  V" + VersionLabel), 10, true, theme.Muted);
        footer.Margin = new Thickness(12, 12, 0, 18);
        DockPanel.SetDock(footer, Dock.Bottom);
        sidebar.Children.Add(footer);
        var menu = new StackPanel();
        foreach (Page destination in Enum.GetValues(typeof(Page)))
        {
            var item = ActionButton("", () => Navigate(destination));
            item.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            item.Padding = new Thickness(10, 7, 10, 7);
            item.Margin = new Thickness(0, 0, 0, 4);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = Glyph(destination), FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 15, Width = 26, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = PageName(destination), FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
            item.Content = row;
            AutomationProperties.SetName(item, PageName(destination));
            navigation.Add(destination, item);
            menu.Children.Add(item);
        }
        sidebar.Children.Add(new ScrollViewer { Content = menu, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        layout.Children.Add(new Border { Background = theme.Side, BorderBrush = theme.Line,
            BorderThickness = new Thickness(0, 0, 1, 0), Child = sidebar });

        var panel = new DockPanel { LastChildFill = true };
        Grid.SetColumn(panel, 1);
        layout.Children.Add(panel);
        var top = new DockPanel { LastChildFill = true, Margin = new Thickness(27, 20, 27, 17) };
        var live = Text(L("WINDOWS MAINTENANCE", "صيانة Windows", "WINDOWS BAKIMI"), 10, true, theme.Accent);
        live.Margin = new Thickness(8, 0, 0, 0);
        live.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(live, Dock.Right);
        top.Children.Add(live);
        title = Text("", 26, true);
        top.Children.Add(title);
        header = new Border { BorderBrush = theme.Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = top };
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        var bottom = new StackPanel();
        traffic = new ProgressBar { Height = 2, IsIndeterminate = true, Visibility = Visibility.Collapsed };
        bottom.Children.Add(traffic);
        var statusRow = new DockPanel { LastChildFill = true, Margin = new Thickness(23, 5, 23, 6), MinHeight = 27 };
        cancel = ActionButton(L("Cancel", "إلغاء", "İptal"), model.Cancel);
        cancel.Margin = new Thickness(6, 0, 0, 0);
        cancel.Padding = new Thickness(10, 3, 10, 3);
        DockPanel.SetDock(cancel, Dock.Right);
        statusRow.Children.Add(cancel);
        status = Text("", 12, false, theme.Muted);
        status.VerticalAlignment = VerticalAlignment.Center;
        statusRow.Children.Add(status);
        bottom.Children.Add(statusRow);
        DockPanel.SetDock(bottom, Dock.Bottom);
        panel.Children.Add(bottom);
        content = new ContentControl();
        pageScroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(27, 22, 24, 9) };
        panel.Children.Add(pageScroll);
        Content = layout;
        SetupTray();
        UpdateBusy();
        Navigate(selected);
    }

    private static bool WindowsPrefersLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int useLight) return useLight != 0;
        }
        catch (Exception error) when (error is UnauthorizedAccessException || error is System.Security.SecurityException ||
                                      error is IOException) { }
        var systemColor = SystemColors.WindowColor;
        return systemColor.R + systemColor.G + systemColor.B > 400;
    }

    private void SetupTray()
    {
        tray.Visible = model.Preferences.Tray && !testMode;
        tray.Text = "AKASS Optimizer";
        tray.ContextMenuStrip?.Dispose();
        var menu = new Forms.ContextMenuStrip
        {
            RightToLeft = words.Direction == FlowDirection.RightToLeft ? Forms.RightToLeft.Yes : Forms.RightToLeft.No
        };
        menu.Items.Add(PageName(Page.Dashboard), null, (_, __) => OpenFromTray(Page.Dashboard));
        menu.Items.Add(PageName(Page.Memory), null, (_, __) => OpenFromTray(Page.Memory));
        menu.Items.Add(L("Exit", "خروج", "Çıkış"), null,
            (_, __) => Dispatcher.BeginInvoke(new Action(() => { exiting = true; Close(); })));
        tray.ContextMenuStrip = menu;
    }

    private void OpenFromTray(Page destination) => Dispatcher.BeginInvoke(new Action(() =>
    {
        RestoreWindow();
        Navigate(destination);
    }));

    public void RestoreWindow()
    {
        if (exiting) return;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void ClosingWindow(object? sender, CancelEventArgs e)
    {
        if (model.Busy)
        {
            e.Cancel = true;
            exiting = false;
            Status(L("Wait for the active operation to finish.", "انتظر انتهاء العملية الجارية.", "Etkin işlemin bitmesini bekleyin."));
            return;
        }
        if (!testMode && model.Preferences.OriginalPlan != Guid.Empty)
        {
            try { GamingService.Restore(model.Store, model.Preferences); }
            catch (Exception error)
            {
                if (!Confirm(L("The power plan could not be restored. Exit and retry restoration next time?",
                    "تعذرت استعادة خطة الطاقة. هل تريد الخروج والمحاولة في التشغيل القادم؟",
                    "Güç planı geri yüklenemedi. Çıkıp sonraki açılışta yeniden denensin mi?") + "\n\n" + error.Message, true))
                { e.Cancel = true; exiting = false; }
            }
        }
    }

    private void Navigate(Page destination)
    {
        page = destination;
        pageScroll.ScrollToTop();
        generation++;
        refreshLive = null;
        refreshProcesses = null;
        refreshUpdatePanel = null;
        lastProcessRefresh = DateTime.MinValue;
        foreach (var entry in navigation)
        {
            entry.Value.Background = entry.Key == destination ? theme.Selected : theme.Side;
            entry.Value.BorderBrush = entry.Key == destination ? theme.AccentBorder : theme.Side;
            entry.Value.Foreground = entry.Key == destination ? theme.Accent : theme.Text;
        }
        title.Text = PageName(destination);
        if (!model.Busy) Status("");
        try
        {
            content.Content = destination switch
            {
                Page.Dashboard => Dashboard(),
                Page.Cleaner => CleanerPage(),
                Page.Memory => MemoryPage(),
                Page.Startup => StartupPage(),
                Page.Monitor => MonitorPage(),
                Page.LargeFiles => LargeFilesPage(),
                Page.Repair => RepairPage(),
                Page.Battery => BatteryPage(),
                Page.Gaming => GamingPage(),
                Page.History => HistoryPage(),
                Page.Settings => SettingsPage(),
                _ => AboutPage()
            };
            refreshLive?.Invoke();
        }
        catch (Exception error)
        {
            content.Content = Text(L("This page could not load.", "تعذر تحميل هذه الصفحة.", "Bu sayfa yüklenemedi."), 15, true);
            ShowError(error);
        }
        RefreshClock();
        if (!testMode && (refreshLive != null || refreshProcesses != null)) _ = Tick();
    }

    private void RefreshClock()
    {
        clock.Stop();
        if (testMode) return;
        bool visible = IsVisible && WindowState != WindowState.Minimized;
        bool live = visible && (refreshLive != null || refreshProcesses != null || page == Page.Dashboard);
        if (overlay != null || live || (visible && model.Preferences.AutoMemory) ||
            model.Preferences.Tray && model.Preferences.Background)
        {
            clock.Interval = TimeSpan.FromSeconds(overlay != null ? 1 : live ? model.Preferences.Refresh : Math.Max(12, model.Preferences.Refresh));
            clock.Start();
        }
        else model.Frames.Stop();
    }

    private async Task Tick()
    {
        try
        {
            if (exiting || testMode) return;
            if (DateTime.UtcNow >= nextUpdateCheck) _ = CheckForUpdates(manual: false);
            var visible = IsVisible && WindowState != WindowState.Minimized;
            if (!await model.Sample(visible && page == Page.Monitor, overlay != null || (visible && page == Page.Monitor))) return;
            UpdateOverlay();
            if (exiting) return;
            await model.AutoOptimizeMemory(model.Preferences.AutoMemory &&
                (visible || (model.Preferences.Tray && model.Preferences.Background)));
            if (IsVisible) refreshLive?.Invoke();
            if (model.Current?.Pressure is { } state && model.Preferences.Tray)
            {
                tray.Text = Short("AKASS · " + model.Current.Load + "% · " + words.EnumPressure(state), 63);
                bool high = model.Sustained >= Pressure.High;
                if (!notifyPressure && high && model.Preferences.Background && model.Preferences.Notifications && !testMode)
                    tray.ShowBalloonTip(4000, "AKASS Optimizer", L("Sustained memory pressure. Open Memory Optimizer to review heavy applications.",
                        "ضغط ذاكرة مستمر. افتح صفحة الذاكرة لمراجعة التطبيقات الثقيلة.",
                        "Sürekli bellek baskısı. Yoğun uygulamaları incelemek için Bellek sayfasını açın."), Forms.ToolTipIcon.Warning);
                notifyPressure = high;
            }
            if (visible && refreshProcesses != null && DateTime.UtcNow - lastProcessRefresh >= TimeSpan.FromSeconds(8))
            {
                lastProcessRefresh = DateTime.UtcNow;
                var refresh = refreshProcesses;
                int original = generation;
                var rows = await Task.Run(model.Processes.Sample);
                if (generation == original) refresh(rows);
            }
        }
        catch (Exception error) { if (!exiting && IsVisible) ShowError(error); }
    }

    private async Task<T> Run<T>(string label, Func<CancellationToken, Task<T>> action, bool cancellable = true)
    {
        canCancel = cancellable;
        Status(label);
        return await model.Run(action);
    }

    private void UpdateBusy()
    {
        if (traffic == null) return;
        traffic.Visibility = model.Busy ? Visibility.Visible : Visibility.Collapsed;
        cancel.Visibility = model.Busy && canCancel ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Status(string message)
    {
        ShowText(status, message);
        status.ToolTip = Akass.Optimizer.Language.PlainText(message);
    }

    private void ShowError(Exception error)
    {
        if (error is OperationCanceledException)
        {
            Status(L("Cancelled.", "أُلغيت العملية.", "İptal edildi."));
            return;
        }
        if (error is Win32Exception native && native.NativeErrorCode == 1223)
        {
            Status(L("Administrator prompt was cancelled.", "أُلغي طلب صلاحيات المسؤول.", "Yönetici istemi iptal edildi."));
            return;
        }
        Status(Short(L("Error", "خطأ", "Hata") + " · " + error.Message, 180));
        status.ToolTip = error.Message;
    }

    private static string Short(string value, int max) => value.Length <= max ? value : value.Substring(0, max - 1) + "…";

    private bool Confirm(string message, bool warning = false) => Akass.Optimizer.Language.HasTechnicalText(message)
        ? ShowMixedDialog(message, confirmation: true, warning: warning)
        : MessageBox.Show(this, message, "AKASS Optimizer", MessageBoxButton.YesNo,
            warning ? MessageBoxImage.Warning : MessageBoxImage.Question, MessageBoxResult.No, DialogTextOptions) == MessageBoxResult.Yes;

    private void Info(string message)
    {
        if (Akass.Optimizer.Language.HasTechnicalText(message)) ShowMixedDialog(message, confirmation: false);
        else MessageBox.Show(this, message, "AKASS Optimizer", MessageBoxButton.OK,
            MessageBoxImage.Information, MessageBoxResult.OK, DialogTextOptions);
    }

    private MessageBoxOptions DialogTextOptions => words.Code == "ar"
        ? MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading : MessageBoxOptions.None;

    private string PageName(Page value) => value switch
    {
        Page.Dashboard => L("Dashboard", "لوحة المعلومات", "Kontrol paneli"),
        Page.Cleaner => L("Smart Cleaner", "التنظيف الذكي", "Akıllı temizlik"),
        Page.Memory => L("Memory Optimizer", "إدارة الذاكرة", "Bellek yönetimi"),
        Page.Startup => L("Startup Manager", "إدارة بدء التشغيل", "Başlangıç yönetimi"),
        Page.Monitor => L("System Monitor", "مراقبة النظام", "Sistem izleyicisi"),
        Page.LargeFiles => L("Large Files", "الملفات الكبيرة", "Büyük dosyalar"),
        Page.Repair => L("Windows Repair", "إصلاح Windows", "Windows onarımı"),
        Page.Battery => L("Battery Health", "صحة البطارية", "Pil sağlığı"),
        Page.Gaming => L("Gaming Mode", "وضع اللعب", "Oyun modu"),
        Page.History => L("Maintenance History", "سجل الصيانة", "Bakım geçmişi"),
        Page.Settings => L("Settings", "الإعدادات", "Ayarlar"),
        _ => L("About", "حول التطبيق", "Hakkında")
    };

    private static string Glyph(Page value) => value switch
    {
        Page.Dashboard => "\uE80F", Page.Cleaner => "\uE74D", Page.Memory => "\uE950",
        Page.Startup => "\uE768", Page.Monitor => "\uE9D9", Page.LargeFiles => "\uE8B7",
        Page.Repair => "\uE90F", Page.Battery => "\uE83F", Page.Gaming => "\uE7FC",
        Page.History => "\uE81C", Page.Settings => "\uE713", _ => "\uE946"
    };
}
