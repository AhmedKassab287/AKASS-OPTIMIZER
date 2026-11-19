using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Akass.Optimizer;

internal sealed partial class MainWindow
{
    private UIElement Dashboard()
    {
        var body = Body();
        var hero = new Grid { Margin = new Thickness(0, 0, 0, 13) };
        hero.ColumnDefinitions.Add(new ColumnDefinition());
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
        var feature = Body();
        feature.Children.Add(Text(L("System health", "صحة النظام", "Sistem sağlığı"), 22, true));
        var healthDetail = Muted(L("Analyze storage, memory pressure and startup items before applying changes.",
            "حلّل التخزين وضغط الذاكرة وعناصر بدء التشغيل قبل إجراء تغييرات.",
            "Değişiklikleri uygulamadan önce depolamayı, bellek baskısını ve başlangıç öğelerini analiz edin."));
        healthDetail.Margin = new Thickness(0, 7, 0, 6);
        feature.Children.Add(healthDetail);
        feature.Children.Add(Actions(
            ActionButton(L("Analyze & Optimize", "تحليل وتحسين", "Analiz et ve iyileştir"), () => Analyze(true), true),
            ActionButton(L("Generate health report", "إنشاء تقرير الصحة", "Sağlık raporu oluştur"), () => Analyze(false))));
        var healthScore = Text("—", 38, true, theme.Accent);
        healthScore.HorizontalAlignment = HorizontalAlignment.Center;
        healthScore.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(healthScore, L("Health score out of 100", "درجة الصحة من 100", "100 üzerinden sağlık puanı"));
        hero.Children.Add(feature);
        Grid.SetColumn(healthScore, 1);
        hero.Children.Add(healthScore);
        body.Children.Add(Card(hero));

        var cards = new UniformGrid { Columns = 3 };
        var cpuValue = Metric(cards, "CPU", theme.Blue);
        var ramValue = Metric(cards, L("Physical memory", "الذاكرة الفعلية", "Fiziksel bellek"), theme.Accent);
        var driveValue = Metric(cards, L("System drive", "قرص النظام", "Sistem sürücüsü"), theme.Accent);
        body.Children.Add(cards);

        var history = new HistoryChart(theme);
        var legend = Actions(Text("●  CPU     ", 12, true, theme.Blue),
            Text("●  " + L("Physical memory", "الذاكرة الفعلية", "Fiziksel bellek"), 12, true, theme.Accent));
        body.Children.Add(Section(L("Resource usage · %", "استخدام الموارد · %", "Kaynak kullanımı · %"), history, legend));
        var device = Muted("");
        body.Children.Add(Section(L("Device information", "معلومات الجهاز", "Cihaz bilgileri"), device,
            Actions(ActionButton(PageName(Page.Cleaner), () => Navigate(Page.Cleaner)),
                ActionButton(PageName(Page.Memory), () => Navigate(Page.Memory)),
                ActionButton(PageName(Page.Startup), () => Navigate(Page.Startup)))));
        var cpuName = SystemMetrics.CpuName;
        var deviceModel = SystemMetrics.DeviceModel;
        refreshLive = () =>
        {
            var data = model.Current;
            if (data == null) return;
            ShowText(cpuValue.Value, words.Technical(Percent(data.Cpu)));
            ShowText(cpuValue.Caption, cpuName.Length > 0 ? words.Technical(cpuName) : words.Technical(Environment.ProcessorCount) + " " +
                L("logical processors", "معالجات منطقية", "mantıksal işlemci"));
            ShowText(ramValue.Value, words.Technical(data.Total > 0 ? Percent(data.Load) : "—"));
            string ramCaption = data.Total > 0 ? words.Technical(Size(data.Available) + " / " + Size(data.Total)) + " · " +
                words.EnumPressure(data.Pressure) : words.Unknown;
            if (data.Installed.HasValue)
                ramCaption += "\n" + L("Installed", "المثبتة", "Takılı") + "  " + words.Technical(Size(data.Installed));
            ShowText(ramValue.Caption, ramCaption);
            ShowText(driveValue.Value, words.Technical(Size(data.DriveFree)));
            string driveCaption = words.Technical(data.Drive) + " · " + words.Technical(Percent(data.DiskFreeRatio * 100)) + " " +
                L("free of", "متاحة من", "boş /") + " " + words.Technical(Size(data.DriveTotal));
            if (data.DriveTotal >= data.DriveFree && data.DriveFree.HasValue)
                driveCaption += "\n" + L("Used", "المستخدم", "Kullanılan") + "  " + words.Technical(Size(data.DriveTotal - data.DriveFree));
            ShowText(driveValue.Caption, driveCaption);
            ShowText(device, words.Technical("Windows " + SystemMetrics.WindowsVersion) + "  ·  " + words.Technical(Environment.ProcessorCount) + " " +
                L("logical processors", "معالجات منطقية", "mantıksal işlemci") + "  ·  " +
                words.Technical(System.Runtime.InteropServices.RuntimeInformation.OSArchitecture) + "  ·  " +
                L("Uptime", "وقت التشغيل", "Çalışma süresi") + " " + words.Technical(SystemMetrics.Uptime.ToString(@"d\.hh\:mm")) +
                (deviceModel.Length > 0 ? "  ·  " + words.Technical(deviceModel) : ""));
            history.Update(model.Series);
            var estimate = Policy.Health(data.DiskFreeRatio, model.Sustained, null, null);
            ShowText(healthScore, words.Technical(estimate.HasValue ? estimate.Value + "/100" : "—"));
        };
        return body;
    }

    private async Task Analyze(bool openPlan)
    {
        int origin = generation;
        var analysis = await Run(L("Analyzing maintenance opportunities…", "جارٍ تحليل خيارات الصيانة…", "Bakım fırsatları analiz ediliyor…"),
            token => Task.Run(() => {
                var metrics = model.Metrics.Sample(model.Preferences.Sensitivity, false);
                var start = model.Startup.List(out bool startupIncomplete);
                var scan = model.Cleaner.Scan(token, advanced: model.Preferences.Advanced);
                return (metrics, start, scan, startupIncomplete);
            }, token));
        int count = analysis.start.Count(x => x.Enabled);
        var approvedGroups = analysis.scan.Groups.Where(x => x.Kind != CleanupKind.Recycle && x.Kind != CleanupKind.Dumps &&
            x.Kind != CleanupKind.Reports && x.Kind != CleanupKind.Prefetch).ToArray();
        ulong bytes = approvedGroups.Aggregate(0UL, (total, group) => total + group.Bytes);
        bool? cleanupOpportunity = bytes >= 512 * Policy.MiB ? true : analysis.scan.Limited ? (bool?)null : false;
        var score = Policy.Health(analysis.metrics.DiskFreeRatio, model.Sustained, analysis.startupIncomplete ? null : count, cleanupOpportunity);
        string report = L("Maintenance score", "درجة الصيانة", "Bakım puanı") + "  " + words.Technical(score.HasValue ? score.Value + "/100" : "—") + "\n\n" +
            L("System drive free", "المساحة المتاحة على قرص النظام", "Sistem sürücüsünde boş alan") + "  " +
            words.Technical(Percent(analysis.metrics.DiskFreeRatio * 100)) + "\n" +
            L("Memory pressure now", "ضغط الذاكرة الحالي", "Anlık bellek baskısı") + "  " +
            words.EnumPressure(analysis.metrics.Pressure) + "\n" +
            L("Discovered startup items", "عناصر بدء التشغيل المكتشفة", "Bulunan başlangıç öğeleri") + "  " + count + "\n" +
            L("Eligible file sizes", "أحجام الملفات المؤهلة", "Uygun dosya boyutları") + "  " + words.Technical(Size(bytes)) + "\n\n" +
            L("Review storage in Smart Cleaner, pressure in Memory Optimizer, and entries in Startup Manager.",
                "راجع الملفات في التنظيف الذكي، وضغط الذاكرة في صفحة الذاكرة، والعناصر في إدارة بدء التشغيل.",
                "Depolamayı Akıllı temizlik, baskıyı Bellek yönetimi ve kayıtları Başlangıç yönetimi sayfasında inceleyin.");
        if (analysis.startupIncomplete)
            report += "\n\n" + L("Some startup sources were inaccessible or exceeded the scan limit.",
                "تعذر الوصول إلى بعض مصادر بدء التشغيل أو تجاوزت حد الفحص.",
                "Bazı başlangıç kaynakları erişilemedi veya tarama sınırını aştı.");
        if (generation != origin) return;
        Status(L("Analysis completed.", "اكتمل التحليل.", "Analiz tamamlandı."));
        if (openPlan)
        {
            Info(report);
            pendingScan = analysis.scan;
            Navigate(Page.Cleaner);
        }
        else
        {
            UIElement display = words.Code == "ar" ? ReportText(report) : ResultText(report);
            var dialog = new Window { Owner = this, Width = 560, Height = 420, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Title = L("Health report", "تقرير الصحة", "Sağlık raporu"), Background = theme.Canvas };
            ApplyReadingDirection(dialog);
            var layout = Body();
            layout.Margin = new Thickness(18);
            layout.Children.Add(display);
            layout.Children.Add(Actions(ActionButton(L("Save report", "حفظ التقرير", "Raporu kaydet"),
                () => SaveText(Akass.Optimizer.Language.PlainText(report), "AKASS-Health.txt", "Text (*.txt)|*.txt", dialog))));
            dialog.Content = layout;
            dialog.ShowDialog();
        }
    }

    private (TextBlock Value, TextBlock Caption) Metric(Panel row, string label, Brush valueColor)
    {
        var column = Body();
        column.Children.Add(Text(label, 12, true, theme.Muted));
        var value = Text("—", 25, true, valueColor);
        value.Margin = new Thickness(0, 8, 0, 3);
        column.Children.Add(value);
        var caption = Muted(" ");
        column.Children.Add(caption);
        row.Children.Add(Card(column, new Thickness(0, 0, 8, 13)));
        return (value, caption);
    }

    private UIElement MemoryPage()
    {
        var body = Body();
        var cards = new UniformGrid { Columns = 3 };
        var physical = Metric(cards, L("Available memory", "الذاكرة المتاحة", "Kullanılabilir bellek"), theme.Accent);
        var commit = Metric(cards, L("Commit charge", "الذاكرة المحجوزة", "Taahhüt edilen bellek"), theme.Blue);
        var pressure = Metric(cards, L("Memory pressure", "ضغط الذاكرة", "Bellek baskısı"), theme.Accent);
        body.Children.Add(cards);
        var chart = new HistoryChart(theme, false);
        var analysis = Muted("");
        var autoStatus = Muted("");
        body.Children.Add(MemoryBreakdown(out var updateBreakdown));
        var autoResult = Muted("");
        void UpdateAuto()
        {
            string automatic = model.Preferences.AutoMemory
                ? L("Enabled", "مفعّل", "Etkin") + " · " + words.Technical(Size((ulong)model.Preferences.AutoMemoryLimitMiB * Policy.MiB))
                : L("Disabled", "غير مفعّل", "Devre dışı");
            automatic += " · " + L("Target", "التطبيق المستهدف", "Hedef") + " " +
                (model.AutoMemoryTarget is { } target ? words.Technical(target.Name) :
                    L("Eligible background processes", "العمليات المؤهلة في الخلفية", "Uygun arka plan işlemleri"));
            ShowText(autoStatus, automatic);
            ShowText(autoResult, model.LastMemoryOptimization is { } result
                ? words.DateLabel(result.Time.ToLocalTime()) + " · " + words.HistoryDetail(result) : "");
        }

        body.Children.Add(MemoryTools(autoResult, UpdateAuto));
        body.Children.Add(Section(L("Automatic memory optimization", "تحسين الذاكرة التلقائي", "Otomatik bellek optimizasyonu"),
            autoStatus, Actions(ActionButton(PageName(Page.Settings), () => Navigate(Page.Settings)))));
        UpdateAuto();
        body.Children.Add(Section(L("Physical memory history · %", "سجل الذاكرة الفعلية · %", "Fiziksel bellek geçmişi · %"), chart, analysis));
        body.Children.Add(ProcessesPanel(allowClose: true, onTargetChanged: UpdateAuto));
        var explanation = Intro(L("Working sets can include shared pages. The totals can overlap. Commit tracks allocated virtual memory that needs backing. A rise in available RAM alone does not measure responsiveness.",
            "قد تتضمن مجموعات العمل صفحات مشتركة وقد تتداخل قيمها. يقيس الحجز الذاكرة الافتراضية التي تحتاج إلى دعم. ارتفاع الذاكرة المتاحة وحده لا يقيس الاستجابة.",
            "Çalışma kümeleri paylaşılan sayfalar içerebilir ve toplamları örtüşebilir. Taahhüt, fiziksel veya disk desteği gereken sanal bellektir. Boş RAM artışı tek başına yanıt hızını ölçmez."));
        body.Children.Add(explanation);
        refreshLive = () =>
        {
            if (model.Current is not { } data) return;
            updateBreakdown(data);
            UpdateAuto();
            ShowText(physical.Value, words.Technical(data.Total > 0 ? Size(data.Available) : "-"));
            string physicalCaption = data.Total > 0 ? words.Technical(Size(data.Total - data.Available) + " / " + Size(data.Total)) + " · " +
                words.Technical(Percent(data.Load)) : words.Unknown;
            if (data.Installed.HasValue)
                physicalCaption += "\n" + L("Installed", "المثبتة", "Takılı") + "  " + words.Technical(Size(data.Installed));
            ShowText(physical.Caption, physicalCaption);
            ShowText(commit.Value, words.Technical(Size(data.Commit)));
            ShowText(commit.Caption, words.Technical(Size(data.CommitLimit)) + " · " + words.Technical(Percent(data.CommitRatio * 100)) + " " +
                L("of limit", "من الحد", "sınır kullanımı"));
            pressure.Value.Text = words.EnumPressure(data.Pressure);
            pressure.Caption.Text = L("Windows low-memory signal", "إشارة Windows لنقص الذاكرة", "Windows düşük bellek sinyali")
                + " · " + YesNo(data.WindowsLow);
            analysis.Text = data.Pressure switch {
                Pressure.Healthy => L("Available memory and commit headroom look adequate.", "الذاكرة المتاحة وهامش الحجز كافيان حالياً.", "Kullanılabilir bellek ve taahhüt payı yeterli."),
                Pressure.Elevated => L("Review large working sets if the system feels slow.", "راجع العمليات الكبيرة إذا لاحظت بطئاً.", "Sistem yavaşsa büyük çalışma kümelerini inceleyin."),
                Pressure.High => L("Save your work and review large applications you can close.", "احفظ عملك وراجع التطبيقات الكبيرة التي يمكن إغلاقها.", "Çalışmanızı kaydedin ve kapatabileceğiniz büyük uygulamaları inceleyin."),
                Pressure.Critical => L("Commit is near its limit or Windows reports low memory with very little available RAM. Save work promptly.",
                    "اقترب الحجز من حده أو أبلغ Windows عن نقص حاد في الذاكرة. احفظ عملك بسرعة.",
                    "Taahhüt sınıra yakın veya Windows RAM'in çok düşük olduğunu bildiriyor. Çalışmanızı kaydedin."),
                _ => words.Unknown
            };
            chart.Update(model.Series);
        };
        return body;
    }

    private UIElement MonitorPage()
    {
        var body = Body();
        body.Children.Add(PerformancePanel(out var updatePerformance));
        var cards = new UniformGrid { Columns = 3 };
        var cpu = Metric(cards, "CPU", theme.Blue);
        var disk = Metric(cards, L("Disk I/O", "قراءة وكتابة القرص", "Disk G/Ç"), theme.Accent);
        var network = Metric(cards, L("Network", "الشبكة", "Ağ"), theme.Accent);
        body.Children.Add(cards);
        var chart = new HistoryChart(theme);
        var current = Muted("");
        body.Children.Add(Section(L("CPU and memory history · %", "سجل المعالج والذاكرة · %", "CPU ve bellek geçmişi · %"), chart, current));
        body.Children.Add(ProcessesPanel(allowClose: false));
        var cpuName = SystemMetrics.CpuName;
        refreshLive = () =>
        {
            if (model.Current is not { } data) return;
            updatePerformance(data);
            ShowText(cpu.Value, words.Technical(Percent(data.Cpu)));
            ShowText(cpu.Caption, words.Technical(cpuName + " · " + Environment.ProcessorCount));
            ShowText(disk.Value, words.Technical("↓ " + Rate(data.DiskRead)));
            ShowText(disk.Caption, words.Technical("↑ " + Rate(data.DiskWrite) + " · " + Size(data.DriveFree) + " / " + Size(data.DriveTotal)));
            ShowText(network.Value, words.Technical("↓ " + Rate(data.Receive)));
            ShowText(network.Caption, words.Technical("↑ " + Rate(data.Send)) + " · " +
                (data.Adapter.Length == 0 ? L("No active adapter detected", "لم يُكتشف محوّل نشط", "Etkin bağdaştırıcı bulunamadı") : words.Technical(data.Adapter)));
            ShowText(current, "● CPU " + words.Technical(Percent(data.Cpu)) + "     ● " + L("RAM", "الذاكرة", "RAM") + " " +
                (data.Total > 0 ? words.Technical(Percent(data.Load) + " · " + Size(data.Available) + " / " + Size(data.Total)) : "—") +
                " · " + words.EnumPressure(data.Pressure));
            chart.Update(model.Series);
        };
        return body;
    }

    private sealed class ProcessView
    {
        public ProcessRow Row { get; }
        public string Name => Row.Name;
        public int Pid => Row.Pid;
        public ulong WorkingBytes => Row.Working;
        public ulong PrivateBytes => Row.Private;
        public double? CpuNumber => Row.Cpu;
        public string Working => Policy.Bytes(Row.Working);
        public string Private => Policy.Bytes(Row.Private);
        public string Cpu => Percent(Row.Cpu);
        public ProcessView(ProcessRow row) => Row = row;
    }

    private Border ProcessesPanel(bool allowClose, Action? onTargetChanged = null)
    {
        var table = Table((L("Process", "العملية", "İşlem"), "Name", 0), ("PID", "Pid", 65),
            (L("Working set", "مجموعة العمل", "Çalışma kümesi"), "Working", 110),
            (L("Private bytes", "الذاكرة الخاصة", "Özel baytlar"), "Private", 110),
            ("CPU", "Cpu", 65));
        TechnicalColumns(table, 0, 1, 2, 3, 4);
        table.Columns[2].SortMemberPath = "WorkingBytes";
        table.Columns[3].SortMemberPath = "PrivateBytes";
        table.Columns[4].SortMemberPath = "CpuNumber";
        var hint = Muted(L("Collecting process information…", "جارٍ جمع معلومات العمليات…", "İşlem bilgileri toplanıyor…"));
        refreshProcesses = items =>
        {
            var selected = (table.SelectedItem as ProcessView)?.Row;
            var descriptions = (table.ItemsSource == null ? Array.Empty<System.ComponentModel.SortDescription>() :
                System.Windows.Data.CollectionViewSource.GetDefaultView(table.ItemsSource).SortDescriptions.ToArray());
            var rows = items.Select(x => new ProcessView(x)).ToArray();
            table.ItemsSource = rows;
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(rows);
            foreach (var sort in descriptions) view.SortDescriptions.Add(sort);
            if (selected != null) table.SelectedItem = rows.FirstOrDefault(x => x.Pid == selected.Pid && x.Row.Started == selected.Started);
            hint.Text = rows.Length == 0 ? L("No readable processes.", "لم تتوفر عمليات قابلة للقراءة.", "Okunabilir işlem yok.") :
                L("Select an application to review it.", "اختر تطبيقاً لمراجعته.", "İncelemek için bir uygulama seçin.");
        };
        if (!allowClose) return Section(L("Running processes", "العمليات الجارية", "Çalışan işlemler"), table, hint);
        var after = Muted("");
        var autoTarget = ActionButton(L("Use selected app for auto optimization", "اختر التطبيق المحدد للتحسين التلقائي",
            "Otomatik optimizasyon için seçili uygulamayı kullan"), () =>
        {
            if (model.Busy || table.SelectedItem is not ProcessView selected || !selected.Row.CanClose) return;
            if (!Confirm(string.Format(L("Allow this running instance of {0} to have its working set trimmed while it is idle and in the background?",
                "هل تسمح بتقليص الذاكرة المقيمة لهذه النسخة من {0} عندما يكون التطبيق خاملاً وفي الخلفية؟",
                "{0} uygulamasının bu örneği arka planda ve boşta olduğunda çalışma kümesi küçültülsün mü?"), words.Technical(selected.Name)))) return;
            model.AutoMemoryTarget = selected.Row;
            onTargetChanged?.Invoke();
        });
        var clearTarget = ActionButton(L("Remove selected app from automatic optimization", "إزالة التطبيق من تحسين الذاكرة التلقائي", "Uygulamayı otomatik bellek optimizasyonundan kaldır"), () =>
        {
            if (model.Busy) return;
            model.AutoMemoryTarget = null;
            onTargetChanged?.Invoke();
        });
        clearTarget.ToolTip = L("Automatic optimization returns to eligible background processes.",
            "بعد إلغاء الاختيار، يعود التحسين التلقائي إلى العمليات المؤهلة في الخلفية.",
            "Seçim kaldırılınca otomatik optimizasyon uygun arka plan işlemlerine döner.");
        autoTarget.IsEnabled = false;
        var trimSelected = ActionButton(L("Free selected app RAM", "تحرير ذاكرة التطبيق المحدد", "Seçili uygulama belleğini boşalt"), async () =>
        {
            if (table.SelectedItem is not ProcessView selected || !selected.Row.CanClose) return;
            if (!Confirm(L("Trim the working set of this background app?", "تقليص مجموعة عمل هذا التطبيق في الخلفية؟",
                "Bu arka plan uygulamasının çalışma kümesi küçültülsün mü?") + "\n\n" + words.Technical(selected.Name))) return;
            var result = await Run("RAM", _ => model.OptimizeMemoryManually(selected.Row), false);
            ShowText(after, words.HistoryDetail(result));
            onTargetChanged?.Invoke();
            Status("");
        });
        trimSelected.IsEnabled = false;
        table.SelectionChanged += (_, __) =>
        {
            trimSelected.IsEnabled = (table.SelectedItem as ProcessView)?.Row.CanClose == true;
            autoTarget.IsEnabled = model.Preferences.AutoMemory && trimSelected.IsEnabled;
        };
        var close = ActionButton(L("End application", "إنهاء التطبيق", "Uygulamayı sonlandır"), async () =>
        {
            if (table.SelectedItem is not ProcessView selected || !selected.Row.CanEnd)
            {
                Status(L("Select a running desktop application you own.", "اختر تطبيق سطح مكتب مفتوحاً بحسابك.", "Hesabınızla çalışan bir masaüstü uygulaması seçin."));
                return;
            }
            if (!Confirm(L($"Close {words.Technical(selected.Name)}? Save anything you are working on first.",
                $"هل تريد إغلاق {words.Technical(selected.Name)}؟ احفظ عملك أولاً.",
                $"{words.Technical(selected.Name)} kapatılsın mı? Önce çalışmanızı kaydedin."), true)) return;

            var result = await Run(L("Closing the application…", "جارٍ إغلاق التطبيق…", "Uygulama kapatılıyor…"),
                _ => Task.Run(() => ProcessAnalyzer.StopApplication(selected.Row, force: false)), false);
            if (result.Remaining is { } remaining && Confirm(L(
                $"{words.Technical(selected.Name)} is still running. Force it and its background processes to exit? Unsaved work may be lost.",
                $"ما زال {words.Technical(selected.Name)} يعمل. هل تريد إنهاءه وإيقاف عملياته في الخلفية؟ قد تفقد العمل غير المحفوظ.",
                $"{words.Technical(selected.Name)} hâlâ çalışıyor. Arka plan işlemleriyle birlikte zorla kapatılsın mı? Kaydedilmeyen çalışmalar kaybolabilir."), true))
                result = await Run(L("Stopping the application…", "جارٍ إنهاء التطبيق…", "Uygulama sonlandırılıyor…"),
                    _ => Task.Run(() => ProcessAnalyzer.StopApplication(remaining, force: true)), false);

            model.Store.Log("CloseApp", result.Result.ToString());
            ShowText(after, result.Result switch
            {
                AppStopResult.Exited => L($"{words.Technical(selected.Name)} stopped.", $"توقف {words.Technical(selected.Name)}.", $"{words.Technical(selected.Name)} sonlandırıldı."),
                AppStopResult.StillRunning => L($"{words.Technical(selected.Name)} is still running.", $"ما زال {words.Technical(selected.Name)} يعمل.", $"{words.Technical(selected.Name)} hâlâ çalışıyor."),
                _ => L("The selected process exited or Windows denied access.", "انتهت العملية المحددة بالفعل أو منع Windows الوصول إليها.",
                    "Seçilen işlem sona erdi veya Windows erişimi engelledi.")
            });
            lastProcessRefresh = DateTime.MinValue;
            Status("");
        });
        return Section(L("Review memory-heavy applications", "مراجعة التطبيقات الأكثر استهلاكاً", "Bellek kullanan uygulamaları incele"),
            table, hint, Actions(trimSelected, close, autoTarget, clearTarget), after);
    }
}
