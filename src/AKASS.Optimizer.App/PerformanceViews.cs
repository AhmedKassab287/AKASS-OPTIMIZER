using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace Akass.Optimizer;

internal sealed partial class MainWindow
{
    private const int OverlayHotkey = 71;
    private const string OverlayKeys = "Ctrl + F10";
    private HwndSource? hotkeySource;
    private bool hotkeyRegistered;
    private Window? overlay;
    private TextBlock? overlayReadout;

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
            ulong? inUse = null;
            if (data.FreePages is ulong free && data.Standby is ulong standby && data.Modified is ulong modified &&
                free <= data.Total && standby <= data.Total - free && modified <= data.Total - free - standby)
                inUse = data.Total - free - standby - modified;
            var measurements = new[] { data.HardwareReserved, inUse, data.Modified, data.Standby, data.FreePages };
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

    private Border MemoryTools()
    {
        var result = Muted("");
        return Section(L("Manual memory tools", "أدوات الذاكرة اليدوية", "El ile bellek araçları"),
            Muted(L("Clearing standby may slow the next launch. Heap compaction is limited to AKASS Optimizer.",
                "قد يبطئ مسح الذاكرة الاحتياطية تشغيل التطبيقات التالية. ضغط الذاكرة المدارة يخص AKASS Optimizer فقط.",
                "Bekleme listesini temizlemek sonraki açılışları yavaşlatabilir. Yığın sıkıştırması yalnızca AKASS Optimizer içindir.")),
            Actions(ActionButton(L("Clear standby RAM", "مسح الذاكرة الاحتياطية", "Bekleme belleğini temizle"), async () =>
            {
                if (!Confirm(L("Clear the Windows standby cache? Administrator permission is required.",
                    "مسح ذاكرة Windows الاحتياطية؟ يتطلب الإجراء صلاحية المسؤول.",
                    "Windows bekleme önbelleği temizlensin mi? Yönetici izni gerekir."), true)) return;
                var before = await Task.Run(() => model.Metrics.Sample(includeNetwork: false));
                var outcome = await Run("Standby RAM", token => RepairService.RunAsync(RepairKind.ClearStandby, token), false);
                var after = await Task.Run(() => model.Metrics.Sample(includeNetwork: false));
                ShowText(result, outcome.ExitCode == 0 ? labelsStandby(before, after) : outcome.Output);
                model.Store.Log("ManualMemoryTools", Akass.Optimizer.Language.PlainText(result.Text));
                Status("");
            }), ActionButton(L("Compact AKASS heap", "ضغط ذاكرة AKASS المدارة", "AKASS yığınını sıkıştır"), async () =>
            {
                var resultBytes = await Run("AKASS", token => Task.Run(MemoryMaintenance.CompactAppHeap, token), false);
                ShowText(result, L("AKASS managed heap", "ذاكرة AKASS المدارة", "AKASS yönetilen yığını") + "  " +
                    words.MemoryChange(resultBytes.Before, resultBytes.After));
                model.Store.Log("ManualMemoryTools", Akass.Optimizer.Language.PlainText(result.Text));
                Status("");
            })), result);

        string labelsStandby(Snapshot before, Snapshot after) => L("Standby RAM", "الذاكرة الاحتياطية", "Bekleme belleği") + "  " +
            words.MemoryChange(before.Standby, after.Standby);
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
        return Section(L("Game and hardware telemetry", "قياسات أداء الألعاب والجهاز", "Oyun ve donanım ölçümleri"), cards,
            Actions(ActionButton(L("Toggle overlay", "إظهار أو إخفاء القياسات فوق الشاشة", "Ekran üstü göstergeleri aç/kapat"), ToggleOverlay), driver, restart),
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
    }

    private IntPtr OverlayMessage(IntPtr handle, int message, IntPtr param, IntPtr data, ref bool handled)
    {
        if (message == 0x0312 && param.ToInt32() == OverlayHotkey)
        {
            handled = true;
            ToggleOverlay();
        }
        return IntPtr.Zero;
    }

    private void ToggleOverlay()
    {
        if (overlay != null)
        {
            overlay.Close();
            overlay = null;
            overlayReadout = null;
            RefreshClock();
            return;
        }
        overlayReadout = new TextBlock { Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold };
        var background = new Border { Background = new SolidColorBrush(Color.FromArgb(226, 17, 24, 39)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(52, 70, 98)), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9), Padding = new Thickness(15, 10, 15, 10), Child = overlayReadout };
        overlay = new Window { Content = background, Background = Brushes.Transparent, AllowsTransparency = true,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowActivated = false, ShowInTaskbar = false,
            Topmost = true, SizeToContent = SizeToContent.WidthAndHeight };
        ApplyReadingDirection(overlay);
        overlay.FlowDirection = FlowDirection.LeftToRight;
        overlay.SourceInitialized += (_, __) =>
        {
            IntPtr handle = new WindowInteropHelper(overlay).Handle;
            int style = GetWindowLong(handle, -20);
            SetWindowLong(handle, -20, style | 0x080000A0); // No activation, tool window, click-through.
        };
        UpdateOverlay();
        overlay.Show();
        UpdateOverlay();
        RefreshClock();
        _ = Tick();
    }

    private void UpdateOverlay()
    {
        if (overlay == null || overlayReadout == null) return;
        var data = model.Current;
        string processor = data?.CpuTemperature is { } temperature ? Degrees(temperature) : Percent(data?.Cpu);
        string fps = data?.FrameCaptureStatus == FrameCaptureStatus.PermissionDenied
            ? L("Permission needed", "تحتاج صلاحية", "İzin gerekli") : FramesPerSecond(data?.FramesPerSecond);
        overlayReadout.Text = "CPU  " + processor + "     GPU  " + Degrees(data?.GpuTemperature) + "     FPS  " + fps;
        IntPtr handle = new WindowInteropHelper(overlay).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var size)) return;
        var screen = Forms.Screen.FromHandle(FrameMonitor.ForegroundWindow).WorkingArea;
        SetWindowPos(handle, IntPtr.Zero, screen.Right - (size.Right - size.Left) - 14, screen.Top + 14, 0, 0, 0x15);
    }

    private void ClosePerformanceViews()
    {
        overlay?.Close();
        if (hotkeySource is { IsDisposed: false })
        {
            if (hotkeyRegistered) UnregisterHotKey(hotkeySource.Handle, OverlayHotkey);
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
}
