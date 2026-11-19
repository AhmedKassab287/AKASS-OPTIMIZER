using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Akass.Optimizer;

internal sealed partial class MainWindow
{
    private const int OverlayHotkey = 71;
    private const int MoveOverlayHotkey = 72;
    private const string OverlayKeys = "Ctrl + F10";
    private const string MoveOverlayKeys = "Ctrl + Shift + F10";
    private const int TransparentOverlay = 0x20;
    private static readonly Brush OverlayMoveBackground = new SolidColorBrush(Color.FromArgb(226, 17, 24, 39));
    private HwndSource? hotkeySource;
    private bool hotkeyRegistered;
    private bool moveHotkeyRegistered;
    private Window? overlay;
    private Window? overlayGrip;
    private TextBlock? overlayReadout;
    private Border? overlayBorder;
    private bool overlayMovable;
    private System.Drawing.Point? dragOffset;
    private DispatcherTimer? overlayDragTimer;

    private Border MemoryBreakdown(out Action<Snapshot> update)
    {
        var labels = new[] { L("Hardware reserved", "محجوز للأجهزة", "Donanıma ayrılan"),
            L("In use", "قيد الاستخدام", "Kullanımda"), L("Modified", "معدّلة", "Değiştirilmiş"),
            L("Standby", "احتياطية", "Beklemede"), L("Free", "حرة", "Boş") };
        var colors = new[] { theme.Muted, theme.Accent, theme.Warning, theme.Blue, Brushes.SeaGreen };
        var bar = new Grid { Height = 12, Margin = new Thickness(0, 7, 0, 14), Background = theme.Line };
        var legend = new UniformGrid { Columns = labels.Length };
        var values = new TextBlock[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
            var segment = new Border { Background = colors[i], Height = 12 };
            Grid.SetColumn(segment, i);
            bar.Children.Add(segment);
            var cell = Body();
            cell.Children.Add(Text(labels[i], 12, false, colors[i]));
            values[i] = Text("—", 16, true);
            cell.Children.Add(values[i]);
            legend.Children.Add(cell);
        }
        var summary = Muted("");
        update = data =>
        {
            var measurements = new[] { data.HardwareReserved, data.PhysicalInUse, data.Modified, data.Standby, data.FreePages };
            for (int i = 0; i < measurements.Length; i++)
            {
                bar.ColumnDefinitions[i].Width = new GridLength(measurements[i] ?? 0, GridUnitType.Star);
                ShowText(values[i], words.Technical(Size(measurements[i])));
            }
            ShowText(summary, L("Available", "متاحة", "Kullanılabilir") + " " + words.Technical(Size(data.Total > 0 ? data.Available : null)) + "  ·  " +
                L("Cached", "ذاكرة التخزين المؤقت", "Önbellek") + " " + words.Technical(Size(data.Cached)) + "  ·  " +
                L("Usable", "قابلة للاستخدام", "Kullanılabilir toplam") + " " + words.Technical(Size(data.Total > 0 ? data.Total : null)) + "  ·  " +
                L("Installed", "مركّبة", "Takılı") + " " + words.Technical(Size(data.Installed)));
        };
        return Section(L("Physical memory breakdown", "توزيع الذاكرة الفعلية", "Fiziksel bellek dağılımı"), bar, legend, summary);
    }

    private Border MemoryTools(TextBlock lastResult, Action refreshResult)
    {
        var free = Check(L("Free up RAM", "تحرير الذاكرة", "RAM boşalt"), true);
        var standby = Check(L("Empty standby RAM", "تفريغ الذاكرة الاحتياطية", "Bekleme belleğini boşalt"));
        var compactResult = Muted("");
        var deep = Check(L("Deeper RAM trim (manual)", "تحرير أعمق للرام (يدوي)", "Daha kapsamlı RAM boşaltma (elle)"));
        free.Checked += (_, __) => deep.IsEnabled = true;
        free.Unchecked += (_, __) => deep.IsEnabled = false;
        var optimize = ActionButton(L("Optimize now", "تحسين الآن", "Şimdi optimize et"), async () =>
        {
            bool freeRam = free.IsChecked == true, emptyStandby = standby.IsChecked == true;
            bool deepClean = freeRam && deep.IsChecked == true;
            if (!freeRam && !emptyStandby)
            {
                Status(L("Select a memory operation first.", "اختر عملية واحدة لتحسين الذاكرة على الأقل.", "Önce bir bellek işlemi seçin."));
                return;
            }
            var prompt = new System.Collections.Generic.List<string>();
            if (deepClean) prompt.Add(L("Deeper trimming includes active background processes. Switching back to them may briefly take longer.",
                "يشمل التحرير الأعمق عمليات الخلفية النشطة. قد يتأخر الرجوع إليها لفترة قصيرة.",
                "Daha kapsamlı boşaltma etkin arka plan işlemlerini de kapsar. Bu uygulamalara dönmek kısa süreliğine yavaşlayabilir."));
            if (emptyStandby) prompt.Add(L("Empty the Windows standby cache? Windows requires administrator approval. Apps may take longer to reopen.",
                "هل تريد تفريغ ذاكرة Windows الاحتياطية؟ يتطلب ذلك صلاحية المسؤول وقد تصبح إعادة فتح التطبيقات أبطأ.",
                "Windows bekleme önbelleği boşaltılsın mı? Yönetici onayı gerekir. Uygulamaların yeniden açılması daha uzun sürebilir."));
            if (prompt.Count > 0 && !Confirm(string.Join("\n\n", prompt), true)) return;

            await Run(L("Optimizing memory.", "جارٍ تحسين الذاكرة.", "Bellek optimize ediliyor."), async token =>
            {
                await model.OptimizeMemoryAsync(freeRam, emptyStandby, deepClean, token);
                return true;
            }, false);
            refreshResult();
            Status("");
        }, true);
        AutomationProperties.SetAutomationId(free, "MemoryFreeRamOption");
        AutomationProperties.SetAutomationId(standby, "MemoryStandbyOption");
        AutomationProperties.SetAutomationId(deep, "MemoryDeepOption");
        AutomationProperties.SetAutomationId(optimize, "MemoryOptimizeNow");
        return Section(L("Optimize memory", "تحسين الذاكرة", "Belleği optimize et"),
            Muted(L("Free up RAM trims eligible idle background processes without closing apps. Emptying standby clears Windows’ reusable cache.",
                "يحرّر خيار تحرير الذاكرة جزءاً من الذاكرة الفعلية للعمليات الخاملة في الخلفية دون إغلاق التطبيقات. ويفرّغ الخيار الثاني ذاكرة التخزين المؤقت القابلة لإعادة الاستخدام في Windows.",
                "RAM boşaltma, uygulamaları kapatmadan uygun, boşta çalışan arka plan işlemlerinin belleğini azaltır. Bekleme belleğini boşaltma, Windows’un yeniden kullanılabilir önbelleğini temizler.")),
            Actions(free, standby, deep), Actions(optimize), lastResult,
            Actions(ActionButton(L("Compact AKASS memory", "ضغط ذاكرة AKASS", "AKASS belleğini sıkıştır"), async () =>
            {
                var memory = await Run("AKASS", token => Task.Run(MemoryMaintenance.CompactAppHeap, token), false);
                ShowText(compactResult, L("AKASS memory", "ذاكرة AKASS", "AKASS belleği") + "  " +
                    words.MemoryChange(memory.Before, memory.After));
                model.Store.Log("ManualMemoryTools", Akass.Optimizer.Language.PlainText(compactResult.Text));
                Status("");
            })), compactResult);
    }

    internal bool PendingElevatedRestart { get; private set; }

    private Border PerformancePanel(out Action<Snapshot> update)
    {
        var cards = new UniformGrid { Columns = 3 };
        var cpu = Metric(cards, L("CPU temperature", "حرارة المعالج", "İşlemci sıcaklığı"), theme.Blue);
        var gpu = Metric(cards, L("GPU temperature", "حرارة كرت الشاشة", "Ekran kartı sıcaklığı"), theme.Accent);
        var fps = Metric(cards, "FPS", theme.Accent);
        var driver = ActionButton(L("Open CPU temperature driver page", "فتح صفحة تعريف حرارة المعالج",
            "İşlemci sıcaklık sürücüsünün sayfasını aç"), () => OpenShell(HardwareSensors.DriverPage));
        var restart = ActionButton(L("Restart as administrator", "إعادة التشغيل كمسؤول",
            "Yönetici olarak yeniden başlat"), RestartForPerformance);
        driver.Visibility = restart.Visibility = Visibility.Collapsed;
        bool elevated = HardwareSensors.IsElevated();
        update = data =>
        {
            ShowText(cpu.Value, words.Technical(Degrees(data.CpuTemperature)));
            ShowText(cpu.Caption, data.CpuSensorStatus switch
            {
                CpuSensorStatus.DriverMissing => L("CPU temperature needs the PawnIO driver. Restart AKASS after installing it.",
                    "حرارة المعالج تتطلب تعريف PawnIO. أعد تشغيل AKASS بعد تثبيته.", "İşlemci sıcaklığı PawnIO sürücüsünü gerektirir. Kurulumdan sonra AKASS'ı yeniden başlatın."),
                CpuSensorStatus.AccessRequired => L("Try running as administrator to access the CPU sensor.",
                    "جرّب تشغيل البرنامج كمسؤول للوصول إلى حساس حرارة المعالج.", "İşlemci sensörüne erişmek için yönetici olarak çalıştırmayı deneyin."),
                CpuSensorStatus.Reading => words.Technical(SystemMetrics.CpuName),
                _ => L("CPU temperature sensor unavailable.", "قراءة حساس حرارة المعالج غير متوفرة.", "İşlemci sıcaklık sensörü kullanılamıyor.")
            });
            ShowText(gpu.Value, words.Technical(Degrees(data.GpuTemperature)));
            ShowText(gpu.Caption, words.Technical(data.GpuAdapter));
            ShowText(fps.Value, words.Technical(FramesPerSecond(data.FramesPerSecond)));
            ShowText(fps.Caption, data.FrameCaptureStatus switch
            {
                FrameCaptureStatus.PermissionDenied => L("Windows blocked FPS capture. Run as administrator.",
                    "منع Windows قياس الـFPS. شغّل البرنامج كمسؤول.", "Windows FPS ölçümünü engelledi. Yönetici olarak çalıştırın."),
                FrameCaptureStatus.Unavailable => L("FPS capture tool is missing.", "أداة قياس الـFPS غير موجودة.", "FPS ölçüm aracı eksik."),
                FrameCaptureStatus.Failed => L("FPS capture failed. Reopen the overlay to retry.", "تعذّر قياس الـFPS. أعد فتح العرض للمحاولة مجدداً.", "FPS ölçümü başarısız oldu. Tekrar denemek için ekran üstü göstergeyi açın."),
                FrameCaptureStatus.AppSubmitted => words.Technical(data.FrameApp) + " · " + L("Game-submitted frames", "الإطارات التي ترسلها اللعبة", "Oyunun gönderdiği kareler"),
                FrameCaptureStatus.Displayed => words.Technical(data.FrameApp) + " · " + L("Displayed frames", "الإطارات المعروضة", "Gösterilen kareler"),
                _ => L("Waiting for frames from the active game.", "بانتظار إطارات اللعبة النشطة.", "Etkin oyundan kare bekleniyor.")
            });
            driver.Visibility = data.CpuSensorStatus == CpuSensorStatus.DriverMissing ? Visibility.Visible : Visibility.Collapsed;
            restart.Visibility = !elevated && (data.FrameCaptureStatus == FrameCaptureStatus.PermissionDenied ||
                data.CpuSensorStatus == CpuSensorStatus.AccessRequired) ? Visibility.Visible : Visibility.Collapsed;
        };
        var move = ActionButton(L("Move overlay", "تحريك شريط القياسات", "Ekran üstü göstergeyi taşı"), ToggleOverlayMove);
        AutomationProperties.SetAutomationId(move, "MovePerformanceOverlay");
        string moveHelp = L("Drag the grip on the overlay to move it. Releasing the mouse saves the position and hides the background.",
            "اسحب المقبض الموجود على شريط القياسات لتحريكه. إفلات الماوس يحفظ مكانه ويخفي الخلفية.",
            "Göstergeyi tutamacından sürükleyin. Fareyi bırakınca konum kaydedilir ve arka plan gizlenir.");
        return Section(L("Game and hardware telemetry", "قياسات أداء الألعاب والجهاز", "Oyun ve donanım ölçümleri"), cards,
            Actions(ActionButton(L("Toggle overlay", "إظهار أو إخفاء القياسات فوق الشاشة", "Ekran üstü göstergeleri aç/kapat"), ToggleOverlay), move, driver, restart),
            Muted((moveHotkeyRegistered ? words.Technical(MoveOverlayKeys) + " · " : "") + moveHelp),
            Muted(hotkeyRegistered ? words.Technical(OverlayKeys) : L("The global shortcut is unavailable. Use the button.",
                "الاختصار غير متاح. استخدم الزر.", "Genel kısayol kullanılamıyor. Düğmeyi kullanın.")));
    }

    private void RestartForPerformance()
    {
        if (HardwareSensors.IsElevated() || model.Busy ||
            !Confirm(L("Restart AKASS Optimizer as administrator to allow performance monitoring?",
                "هل تريد إعادة تشغيل AKASS Optimizer كمسؤول لإتاحة قياس الأداء؟",
                "Performans ölçümüne izin vermek için AKASS Optimizer yönetici olarak yeniden başlatılsın mı?"))) return;
        PendingElevatedRestart = true;
        exiting = true;
        Close();
        if (!exiting) PendingElevatedRestart = false;
    }

    private static string Degrees(double? value) => value.HasValue ? value.Value.ToString("0") + " °C" : "—";
    private static string FramesPerSecond(double? value) => value.HasValue ? value.Value.ToString("0") : "—";

    private void RegisterOverlayHotkey()
    {
        hotkeySource = PresentationSource.FromVisual(this) as HwndSource;
        if (hotkeySource == null) return;
        hotkeySource.AddHook(OverlayMessage);
        hotkeyRegistered = RegisterHotKey(hotkeySource.Handle, OverlayHotkey, 0x4002, 0x79);
        moveHotkeyRegistered = RegisterHotKey(hotkeySource.Handle, MoveOverlayHotkey, 0x4006, 0x79);
    }

    private IntPtr OverlayMessage(IntPtr handle, int message, IntPtr param, IntPtr data, ref bool handled)
    {
        if (message == 0x0312 && param.ToInt32() == OverlayHotkey)
        {
            handled = true;
            ToggleOverlay();
        }
        else if (message == 0x0312 && param.ToInt32() == MoveOverlayHotkey)
        {
            handled = true;
            ToggleOverlayMove();
        }
        return IntPtr.Zero;
    }

    private void ToggleOverlay()
    {
        if (overlay != null)
        {
            FinishOverlayDrag();
            overlay.Close();
            overlay = null;
            overlayGrip = null;
            overlayReadout = null;
            overlayBorder = null;
            overlayMovable = false;
            dragOffset = null;
            RefreshClock();
            return;
        }
        overlayReadout = new TextBlock { Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold,
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 3, ShadowDepth = 1, Opacity = 1 } };
        overlayBorder = new Border { Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9), Padding = new Thickness(34, 10, 15, 10), Child = overlayReadout };
        overlay = new Window { Content = overlayBorder, Background = Brushes.Transparent, AllowsTransparency = true,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowActivated = false, ShowInTaskbar = false,
            Topmost = true, SizeToContent = SizeToContent.WidthAndHeight };
        ApplyReadingDirection(overlay);
        overlay.FlowDirection = FlowDirection.LeftToRight;
        overlay.SourceInitialized += (_, __) => ApplyOverlayInput();
        overlayBorder.MouseLeftButtonDown += (_, e) => { if (overlayMovable && BeginOverlayDrag()) e.Handled = true; };
        overlayBorder.MouseLeftButtonUp += (_, __) => FinishOverlayDrag();
        UpdateOverlay();
        overlay.Show();
        ShowOverlayGrip();
        UpdateOverlay();
        RefreshClock();
        _ = Tick();
    }

    private void ShowOverlayGrip()
    {
        var dots = new TextBlock { Text = "⠿", FontSize = 16, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 2, ShadowDepth = 1, Opacity = 1 } };
        // A nonzero alpha gives the grip a mouse hit area without a visible panel.
        var grip = new Border { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), Child = dots, Cursor = Cursors.SizeAll };
        AutomationProperties.SetName(grip, L("Drag performance overlay", "اسحب لتحريك شريط القياسات", "Performans göstergesini sürükle"));
        grip.MouseLeftButtonDown += (_, e) => { if (BeginOverlayDrag()) e.Handled = true; };
        grip.MouseLeftButtonUp += (_, __) => FinishOverlayDrag();
        overlayGrip = new Window { Owner = overlay, Content = grip, Background = Brushes.Transparent, AllowsTransparency = true,
            Width = 26, Height = 28, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            ShowActivated = false, ShowInTaskbar = false, Topmost = true };
        overlayGrip.SourceInitialized += (_, __) => ApplyOverlayWindowStyle(overlayGrip, clickThrough: false);
        overlayGrip.Show();
    }

    private void UpdateOverlay()
    {
        if (overlay == null || overlayReadout == null) return;
        var data = model.Current;
        string processor = data?.CpuTemperature is { } temperature ? Degrees(temperature) : Percent(data?.Cpu);
        string fps = data?.FrameCaptureStatus == FrameCaptureStatus.PermissionDenied
            ? L("Permission needed", "تحتاج صلاحية", "İzin gerekli") : FramesPerSecond(data?.FramesPerSecond);
        overlayReadout.Text = "CPU  " + processor + "     GPU  " + Degrees(data?.GpuTemperature) + "     FPS  " + fps;
        if (dragOffset.HasValue) return;
        IntPtr handle = new WindowInteropHelper(overlay).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var size)) return;
        var placement = model.Preferences.PerformanceOverlay;
        var display = Forms.Screen.AllScreens.FirstOrDefault(screen => screen.DeviceName == placement?.Display);
        var work = (display ?? Forms.Screen.FromHandle(FrameMonitor.ForegroundWindow)).WorkingArea;
        int width = size.Right - size.Left, height = size.Bottom - size.Top;
        int x = display != null ? OverlayAxis(work.Left, work.Width, width, placement!.X) : Math.Max(work.Left, work.Right - width - 14);
        int y = display != null ? OverlayAxis(work.Top, work.Height, height, placement!.Y) : Math.Min(work.Top + 14, Math.Max(work.Top, work.Bottom - height));
        PositionOverlay(handle, x, y);
    }

    private void ToggleOverlayMove()
    {
        if (overlay == null) ToggleOverlay();
        if (dragOffset.HasValue) FinishOverlayDrag();
        else
        {
            overlayMovable = !overlayMovable;
            ApplyOverlayInput();
        }
    }

    private void ApplyOverlayInput()
    {
        if (overlay == null || overlayBorder == null) return;
        ApplyOverlayWindowStyle(overlay, clickThrough: !overlayMovable);
        overlayBorder.Cursor = overlayMovable ? Cursors.SizeAll : null;
        overlayBorder.Background = overlayMovable ? OverlayMoveBackground : Brushes.Transparent;
        overlayBorder.BorderBrush = overlayMovable ? Brushes.Turquoise : Brushes.Transparent;
    }

    private static void ApplyOverlayWindowStyle(Window? window, bool clickThrough)
    {
        if (window == null) return;
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        int style = GetWindowLong(handle, -20) | 0x08000080; // No activation; omit the taskbar button.
        SetWindowLong(handle, -20, clickThrough ? style | TransparentOverlay : style & ~TransparentOverlay);
        SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 0x37);
    }

    private bool BeginOverlayDrag()
    {
        if (overlay == null || dragOffset.HasValue || !GetWindowRect(new WindowInteropHelper(overlay).Handle, out var bounds)) return false;
        overlayMovable = true;
        ApplyOverlayInput();
        var cursor = Forms.Cursor.Position;
        dragOffset = new System.Drawing.Point(cursor.X - bounds.Left, cursor.Y - bounds.Top);
        overlayDragTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
        overlayDragTimer.Tick += (_, __) =>
        {
            if ((GetAsyncKeyState(GetSystemMetrics(23) == 0 ? 1 : 2) & 0x8000) == 0) FinishOverlayDrag();
            else DragOverlay();
        };
        overlayDragTimer.Start();
        return true;
    }

    private void DragOverlay()
    {
        if (overlay == null || dragOffset is not { } offset) return;
        var cursor = Forms.Cursor.Position;
        var work = Forms.Screen.FromPoint(cursor).WorkingArea;
        IntPtr handle = new WindowInteropHelper(overlay).Handle;
        if (!GetWindowRect(handle, out var bounds)) return;
        int x = ClampOverlayAxis(cursor.X - offset.X, work.Left, work.Width, bounds.Right - bounds.Left);
        int y = ClampOverlayAxis(cursor.Y - offset.Y, work.Top, work.Height, bounds.Bottom - bounds.Top);
        PositionOverlay(handle, x, y);
    }

    private bool PositionOverlay(IntPtr readout, int x, int y)
    {
        if (!SetWindowPos(readout, IntPtr.Zero, x, y, 0, 0, 0x15)) return false;
        if (overlayGrip == null || !GetWindowRect(readout, out var bounds)) return true;
        IntPtr grip = new WindowInteropHelper(overlayGrip).Handle;
        if (!GetWindowRect(grip, out var area)) return true;
        int verticalInset = Math.Max(0, (bounds.Bottom - bounds.Top - (area.Bottom - area.Top)) / 2);
        return SetWindowPos(grip, IntPtr.Zero, bounds.Left, bounds.Top + verticalInset, 0, 0, 0x15);
    }

    private void FinishOverlayDrag()
    {
        if (!dragOffset.HasValue || overlay == null) return;
        overlayDragTimer?.Stop();
        overlayDragTimer = null;
        dragOffset = null;
        overlayMovable = false;
        ApplyOverlayInput();
        SaveOverlayPlacement(new WindowInteropHelper(overlay).Handle);
    }

    private void SaveOverlayPlacement(IntPtr handle)
    {
        if (!GetWindowRect(handle, out var bounds)) return;
        var screen = Forms.Screen.FromHandle(handle);
        var work = screen.WorkingArea;
        var preferences = model.Preferences;
        var previous = preferences.PerformanceOverlay;
        preferences.PerformanceOverlay = new OverlayPlacement { Display = screen.DeviceName,
            X = OverlayFraction(bounds.Left, work.Left, work.Width, bounds.Right - bounds.Left),
            Y = OverlayFraction(bounds.Top, work.Top, work.Height, bounds.Bottom - bounds.Top) };
        try { model.Store.Save(preferences); }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
        { preferences.PerformanceOverlay = previous; ShowError(error); }
    }

    internal static int ClampOverlayAxis(int value, int origin, int extent, int size) =>
        Math.Max(origin, Math.Min(value, origin + Math.Max(0, extent - size)));

    internal static int OverlayAxis(int origin, int extent, int size, double fraction) =>
        origin + (int)Math.Round(Math.Max(0, extent - size) * fraction);

    internal static double OverlayFraction(int value, int origin, int extent, int size) =>
        extent <= size ? 0 : (ClampOverlayAxis(value, origin, extent, size) - origin) / (double)(extent - size);

    internal void SmokeOverlayPlacement()
    {
        if (ClampOverlayAxis(-2200, -1920, 1920, 300) != -1920 ||
            OverlayAxis(-1920, 1920, 300, OverlayFraction(-900, -1920, 1920, 300)) != -900)
            throw new InvalidOperationException("Overlay positioning failed on a screen with a negative origin.");
        ToggleOverlay();
        try
        {
            IntPtr handle = new WindowInteropHelper(overlay!).Handle;
            IntPtr grip = new WindowInteropHelper(overlayGrip!).Handle;
            Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.ApplicationIdle);
            if ((GetWindowLong(handle, -20) & TransparentOverlay) == 0 || overlayBorder!.Background != Brushes.Transparent ||
                !GetWindowRect(handle, out var bounds) || !GetWindowRect(grip, out var gripBounds) ||
                WindowFromPoint(new System.Drawing.Point((gripBounds.Left + gripBounds.Right) / 2,
                    (gripBounds.Top + gripBounds.Bottom) / 2)) != grip ||
                WindowFromPoint(new System.Drawing.Point((bounds.Left + bounds.Right) / 2,
                    (bounds.Top + bounds.Bottom) / 2)) == handle)
                throw new InvalidOperationException("The overlay grip is not independently clickable.");
            if (!BeginOverlayDrag() || overlayBorder.Background == Brushes.Transparent)
                throw new InvalidOperationException("The overlay grip could not start a drag directly.");
            var screen = Forms.Screen.FromHandle(handle);
            var work = screen.WorkingArea;
            int x = OverlayAxis(work.Left, work.Width, bounds.Right - bounds.Left, 0.35);
            int y = OverlayAxis(work.Top, work.Height, bounds.Bottom - bounds.Top, 0.6);
            if (!PositionOverlay(handle, x, y))
                throw new InvalidOperationException("The overlay window could not be moved.");
            FinishOverlayDrag();
            var saved = model.Store.ReadSettings().PerformanceOverlay;
            if (saved?.Display != screen.DeviceName || Math.Abs(saved.Y - 0.6) > 0.01 ||
                (GetWindowLong(handle, -20) & TransparentOverlay) == 0 || overlayBorder.Background != Brushes.Transparent ||
                !GetWindowRect(grip, out var movedGrip) || movedGrip.Left != x)
                throw new InvalidOperationException("The overlay position was not saved or the window was not locked.");
            UpdateOverlay();
            if (!GetWindowRect(handle, out var restored) || Math.Abs(restored.Left - x) > 1 || Math.Abs(restored.Top - y) > 1)
                throw new InvalidOperationException("The overlay refresh reset the saved position.");
        }
        finally { if (overlay != null) ToggleOverlay(); }
    }

    private void ClosePerformanceViews()
    {
        overlayDragTimer?.Stop();
        overlay?.Close();
        if (hotkeySource is { IsDisposed: false })
        {
            if (hotkeyRegistered) UnregisterHotKey(hotkeySource.Handle, OverlayHotkey);
            if (moveHotkeyRegistered) UnregisterHotKey(hotkeySource.Handle, MoveOverlayHotkey);
            hotkeySource.RemoveHook(OverlayMessage);
        }
        model.Frames.Dispose();
        model.Sensors.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)] private struct WindowRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr owner, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr owner, int id);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr owner, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr owner, int index, int value);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr owner, out WindowRect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr owner, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(System.Drawing.Point point);
}
