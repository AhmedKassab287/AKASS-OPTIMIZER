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
                values[i].Text = Size(measurements[i]);
            }
            summary.Text = L("Available", "متاحة", "Kullanılabilir") + " " + Size(data.Total > 0 ? data.Available : null) + "  ·  " +
                L("Cached", "ذاكرة التخزين المؤقت", "Önbellek") + " " + Size(data.Cached) + "  ·  " +
                L("Usable", "قابلة للاستخدام", "Kullanılabilir toplam") + " " + Size(data.Total > 0 ? data.Total : null) + "  ·  " +
                L("Installed", "مركّبة", "Takılı") + " " + Size(data.Installed);
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
                result.Text = outcome.ExitCode == 0 ? labelsStandby(before, after) : outcome.Output;
                model.Store.Log("ManualMemoryTools", result.Text);
                Status("");
            }), ActionButton(L("Compact AKASS heap", "ضغط ذاكرة AKASS المدارة", "AKASS yığınını sıkıştır"), async () =>
            {
                var resultBytes = await Run("AKASS", token => Task.Run(MemoryMaintenance.CompactAppHeap, token), false);
                result.Text = L("AKASS managed heap", "ذاكرة AKASS المدارة", "AKASS yönetilen yığını") + "  " +
                    Size(resultBytes.Before) + " → " + Size(resultBytes.After);
                model.Store.Log("ManualMemoryTools", result.Text);
                Status("");
            })), result);

        string labelsStandby(Snapshot before, Snapshot after) => L("Standby RAM", "الذاكرة الاحتياطية", "Bekleme belleği") + "  " +
            Size(before.Standby) + " → " + Size(after.Standby);
    }

    private Border PerformancePanel(out Action<Snapshot> update)
    {
        var cards = new UniformGrid { Columns = 3 };
        var cpu = Metric(cards, L("CPU temperature", "حرارة المعالج", "İşlemci sıcaklığı"), theme.Blue);
        var gpu = Metric(cards, L("GPU temperature", "حرارة كرت الشاشة", "Ekran kartı sıcaklığı"), theme.Accent);
        var fps = Metric(cards, "FPS", theme.Accent);
        update = data =>
        {
            cpu.Value.Text = Degrees(data.CpuTemperature);
            gpu.Value.Text = Degrees(data.GpuTemperature);
            gpu.Caption.Text = data.GpuAdapter;
            fps.Value.Text = FramesPerSecond(data.FramesPerSecond);
            fps.Caption.Text = data.FrameApp;
        };
        return Section(L("Game and hardware telemetry", "إحصائيات الألعاب والعتاد", "Oyun ve donanım telemetrisi"), cards,
            Muted(L("FPS tracks displayed frames of the foreground app. Dashes indicate that a sensor or frame data is unavailable.",
                "يعرض FPS الإطارات المعروضة من التطبيق النشط. تشير الشرطة إلى عدم توفر المستشعر أو بيانات الإطارات.",
                "FPS, öndeki uygulamanın ekranda gösterilen karelerini ölçer. Çizgi, sensör veya kare verisinin bulunmadığını belirtir.")),
            Actions(ActionButton(L("Toggle overlay", "عرض أو إخفاء الإحصائيات فوق الشاشة", "Ekran üstü göstergeleri aç/kapat"), ToggleOverlay)),
            Muted(hotkeyRegistered ? OverlayKeys : L("The global shortcut is unavailable. Use the button.",
                "الاختصار العام غير متاح. استخدم الزر.", "Genel kısayol kullanılamıyor. Düğmeyi kullanın.")));
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
        overlayReadout.Text = "CPU  " + Degrees(data?.CpuTemperature) + "     GPU  " + Degrees(data?.GpuTemperature) +
            "     FPS  " + FramesPerSecond(data?.FramesPerSecond);
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
