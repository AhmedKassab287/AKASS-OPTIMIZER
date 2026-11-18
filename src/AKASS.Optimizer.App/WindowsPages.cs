using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;

namespace Akass.Optimizer;

internal sealed partial class MainWindow
{
    private UIElement StartupPage()
    {
        var body = Body();
        body.Children.Add(Intro(L("View Run registrations and Startup shortcuts. Use Windows Settings to manage Windows-controlled entries. Optional current-user shortcuts can be backed up and restored here.",
            "اعرض تسجيلات Run واختصارات بدء التشغيل. استخدم إعدادات Windows للعناصر التي يديرها النظام. يمكنك نسخ اختصارات المستخدم الاختيارية احتياطياً واستعادتها هنا.",
            "Run kayıtlarını ve Başlangıç kısayollarını görüntüleyin. Windows tarafından yönetilen öğeler için Windows Ayarları'nı kullanın. İsteğe bağlı kullanıcı kısayollarını burada yedekleyip geri yükleyebilirsiniz.")));
        var table = Table((L("Application", "التطبيق", "Uygulama"), "Name", 0),
            (L("Publisher", "الناشر", "Yayıncı"), "Publisher", 135),
            (L("Source", "المصدر", "Kaynak"), "Source", 120),
            (L("Status", "الحالة", "Durum"), "Status", 125));
        var location = Muted("");
        var count = Muted(L("Click Refresh to load registered startup items.", "اضغط تحديث لعرض عناصر بدء التشغيل المسجّلة.", "Kayıtlı başlangıç öğelerini yüklemek için Yenile'ye tıklayın."));
        var reload = ActionButton(L("Refresh", "تحديث", "Yenile"), () => Populate(true));
        var manage = ActionButton(L("Disable / restore", "تعطيل أو استعادة", "Devre dışı bırak / geri yükle"), async () =>
        {
            if (table.SelectedItem is not StartupView item || !item.Row.Editable) return;
            string prompt = item.Row.Enabled ? L("Move this shortcut into the reversible backup folder?",
                "هل تريد نقل هذا الاختصار إلى مجلد النسخ الاحتياطية القابل للاستعادة؟",
                "Kısayol geri yüklenebilir yedeğe taşınsın mı?") :
                L("Restore this shortcut to your Startup folder?", "هل تريد استعادة الاختصار إلى مجلد بدء التشغيل؟", "Kısayol Başlangıç klasörüne geri yüklensin mi?");
            if (!Confirm(prompt + "\n\n" + item.Name)) return;
            await Run(L("Updating Startup folder…", "جارٍ تحديث مجلد بدء التشغيل…", "Başlangıç klasörü güncelleniyor…"),
                token => Task.Run(() => {
                    model.Startup.Change(item.Row);
                    model.Store.Log("StartupChanged", item.Row.Enabled ? "Shortcut moved to backup" : "Shortcut restored");
                    return true;
                }, token), false);
            await Populate(false);
        }, true);
        manage.IsEnabled = false;
        var open = ActionButton(L("Open location", "فتح الموقع", "Konumu aç"), () =>
        {
            if (table.SelectedItem is StartupView item && StartupFolder(item.Row.Path) is { } path)
                OpenShell(path);
        });
        open.IsEnabled = false;
        table.SelectionChanged += (_, __) =>
        {
            var selected = table.SelectedItem as StartupView;
            manage.IsEnabled = selected?.Row.Editable == true;
            open.IsEnabled = selected != null && StartupFolder(selected.Row.Path) != null;
            location.Text = selected?.Row.Path ?? "";
        };

        body.Children.Add(Section(L("Startup entries", "عناصر بدء التشغيل", "Başlangıç kayıtları"), table, count, location,
            Actions(reload, manage, open, ActionButton(L("Windows Startup settings", "إعدادات بدء تشغيل Windows", "Windows Başlangıç ayarları"),
                () => OpenShell("ms-settings:startupapps")))));
        if (!testMode) body.Loaded += async (_, __) =>
        {
            if (table.ItemsSource == null)
            {
                try { await Populate(false); }
                catch (Exception error) { ShowError(error); }
            }
        };
        return body;

        async Task Populate(bool busy)
        {
            var rows = busy ? await Run(L("Reading startup items…", "جارٍ قراءة عناصر بدء التشغيل…", "Başlangıç öğeleri okunuyor…"),
                token => Task.Run(model.Startup.List, token)) : await Task.Run(model.Startup.List);
            var translated = rows.Select(item => new StartupView(item, item.Source switch
                {
                    "UserStartup" => L("User Startup", "مجلد المستخدم", "Kullanıcı Başlangıç"),
                    "CommonStartup" => L("Common Startup", "المجلد المشترك", "Ortak Başlangıç"),
                    "Backup" => L("Backup", "نسخة احتياطية", "Yedek"),
                    _ => item.Source
                },
                item.Enabled ? (item.Editable ? L("Startup folder", "مجلد بدء التشغيل", "Başlangıç klasörü") :
                    L("Registered", "مسجّل", "Kayıtlı")) : L("Backed up", "محفوظ احتياطياً", "Yedeklendi"))).ToArray();
            table.ItemsSource = translated;
            count.Text = translated.Length + " " + L("entries found", "عناصر مكتشفة", "kayıt bulundu");
            if (busy) Status("");
        }
    }

    private sealed class StartupView
    {
        public StartupEntry Row { get; }
        public string Name => Row.Name;
        public string Publisher => Row.Publisher;
        public string Source { get; }
        public string Status { get; }
        public StartupView(StartupEntry row, string source, string status) { Row = row; Source = source; Status = status; }
    }

    private static string? StartupFolder(string raw)
    {
        try
        {
            string path = Path.GetDirectoryName(Policy.Canonical(raw))!;
            return Directory.Exists(path) && File.Exists(raw) ? path : null;
        }
        catch (Exception error) when (error is IOException || error is ArgumentException || error is UnauthorizedAccessException)
        { return null; }
    }

    private ToolResult? latestTool;

    private UIElement RepairPage()
    {
        var body = Body();
        var toolInfo = new[]
        {
            (RepairKind.Sfc, "SFC /scannow", L("Verify Windows system files and replace damaged copies when possible. Administrator access. Scan and possible repair.",
                "تحقّق من ملفات Windows واستبدل التالف عند الإمكان. يتطلب صلاحيات المسؤول. فحص وقد يتبعه إصلاح.",
                "Windows sistem dosyalarını doğrular ve mümkünse bozuk kopyaları değiştirir. Yönetici erişimi. Tarama ve olası onarım.")),
            (RepairKind.DismScan, "DISM /ScanHealth", L("Run a component-store corruption scan. Administrator access. Extended diagnosis.",
                "افحص تلف مخزن مكونات النظام. يتطلب صلاحيات المسؤول. تشخيص موسع.",
                "Bileşen deposu bozulmasını tarar. Yönetici erişimi. Ayrıntılı tanılama.")),
            (RepairKind.DismRestore, "DISM /RestoreHealth", L("Repair the Windows component store. Administrator access. May access Windows Update for repair files.",
                "يصلح مخزن مكونات Windows. يتطلب صلاحيات المسؤول وقد يستخدم Windows Update لجلب ملفات الإصلاح.",
                "Windows bileşen deposunu onarır. Yönetici erişimi. Onarım dosyaları için Windows Update'e erişebilir.")),
            (RepairKind.FlushDns, "ipconfig /flushdns", L("Clear the DNS resolver cache while troubleshooting network issues. Administrator access. New queries rebuild the cache.",
                "يمسح ذاكرة DNS المؤقتة لاستكشاف مشاكل الشبكة. يتطلب صلاحيات المسؤول. تعيد الاستعلامات الجديدة بناء الذاكرة.",
                "Ağ sorunlarını giderirken DNS çözümleyici önbelleğini temizler. Yönetici erişimi. Yeni sorgular önbelleği yeniden oluşturur.")),
            (RepairKind.BatteryReport, "powercfg /batteryreport", L("Generate an HTML report with Windows battery history. Standard user. Choose where to save the report.",
                "ينشئ تقرير HTML لسجل بطارية Windows. صلاحيات عادية. اختر مكان الحفظ.",
                "Windows pil geçmişiyle bir HTML raporu üretir. Standart kullanıcı. Kayıt konumunu seçin."))
        };
        var output = ResultText(latestTool?.Output ?? "");
        output.FlowDirection = FlowDirection.LeftToRight;
        var result = Muted(latestTool == null ? L("Tool output appears here after an operation.", "تظهر المخرجات هنا بعد التشغيل.", "İşlemden sonra araç çıktısı burada görünür.") : ExitLabel(latestTool));
        foreach (var item in toolInfo)
        {
            var operation = ActionButton(L("Run", "تشغيل", "Çalıştır"), async () =>
            {
                if (item.Item1 == RepairKind.BatteryReport)
                {
                    await GenerateBatteryReport();
                    if (latestTool != null) { output.Text = latestTool.Output; result.Text = ExitLabel(latestTool); }
                    return;
                }
                if (!Confirm(item.Item2 + "\n\n" + item.Item3, item.Item1 != RepairKind.DismScan)) return;
                result.Text = L("Running. Windows reports completion in the captured output.", "الأداة قيد التشغيل. تُعرض النتيجة بعد اكتمالها.",
                    "Çalışıyor. Windows tamamlandığında yakalanan çıktı görüntülenir.");
                latestTool = await Run(item.Item2 + "…", token => RepairService.RunAsync(item.Item1, token), false);
                output.Text = latestTool.Output;
                result.Text = ExitLabel(latestTool);
                model.Store.Log("WindowsTool", item.Item2 + " · " + latestTool.ExitCode);
                Status(result.Text);
            });
            body.Children.Add(Section(item.Item2, Muted(item.Item3), Actions(operation)));
        }
        body.Children.Add(Section(L("Diagnostic output", "مخرجات التشخيص", "Tanılama çıktısı"), result, output,
            Actions(ActionButton(L("Save output", "حفظ المخرجات", "Çıktıyı kaydet"), () =>
            {
                if (latestTool != null) SaveText(ExitLabel(latestTool) + "\n\n" + latestTool.Output,
                    "AKASS-Windows-Tool.txt", "Text (*.txt)|*.txt");
            }))));
        return body;
    }

    private string ExitLabel(ToolResult outcome) => L("Exit code", "رمز الخروج", "Çıkış kodu") + "  " + outcome.ExitCode;

    private UIElement BatteryPage()
    {
        var body = Body();
        var cards = new UniformGrid { Columns = 2 };
        var charge = Metric(cards, L("Charge", "الشحن", "Şarj"), theme.Accent);
        var connection = Metric(cards, L("Power source", "مصدر الطاقة", "Güç kaynağı"), theme.Blue);
        body.Children.Add(cards);
        var capacity = Muted("");
        var load = ActionButton(L("Read battery capacities", "قراءة سعة البطارية", "Pil kapasitesini oku"), async () =>
        {
            var data = await Run(L("Reading Windows battery report…", "جارٍ قراءة تقرير بطارية Windows…", "Windows pil raporu okunuyor…"),
                token => Task.Run(() => BatteryService.Capacity(model.Store), token), false);
            capacity.Text = data.Count == 0 ? L("Windows did not provide battery capacities.", "لم يوفّر Windows بيانات سعة البطارية.",
                "Windows pil kapasitesi bilgisi sağlamadı.") :
                string.Join("\n\n", data.Select(x => x.Name + "\n" +
                    L("Design capacity", "السعة التصميمية", "Tasarım kapasitesi") + "  " + Capacity(x.Design) + "   ·   " +
                    L("Full charge", "الشحن الكامل", "Tam şarj") + "  " + Capacity(x.Full) + "   ·   " +
                    L("Estimated wear", "التآكل المقدّر", "Tahmini yıpranma") + "  " + Percent(x.Wear * 100)));
            Status("");
        }, true);
        body.Children.Add(Section(L("Battery history and capacity", "سجل البطارية وسعتها", "Pil geçmişi ve kapasite"),
            capacity, Actions(load, ActionButton(L("Generate HTML report", "إنشاء تقرير HTML", "HTML raporu oluştur"), GenerateBatteryReport))));
        refreshLive = () =>
        {
            var data = BatteryService.Status();
            charge.Value.Text = Percent(data.Percent);
            charge.Caption.Text = data.Present == false ?
                L("No battery detected", "لم تُكتشف بطارية", "Pil algılanmadı") :
                data.Charging == true ? L("Charging", "قيد الشحن", "Şarj oluyor") : words.Unknown;
            if (data.Charging == false && data.Present == true)
                charge.Caption.Text = L("Not charging", "ليست قيد الشحن", "Şarj olmuyor");
            connection.Value.Text = data.AcOnline == true ? "AC" : data.AcOnline == false && data.Present == true ?
                L("Battery", "البطارية", "Pil") : "—";
            connection.Caption.Text = data.AcOnline == true ? L("Plugged in", "متصل بالكهرباء", "Prize takılı") : "";
            load.IsEnabled = data.Present == true && !model.Busy;
            if (data.Present == false) capacity.Text = charge.Caption.Text;
        };
        return body;
    }

    private static string Capacity(int? value) => value.HasValue ? value.Value.ToString("N0") + " mWh" : "—";

    private async Task GenerateBatteryReport()
    {
        var browse = new SaveFileDialog { FileName = "AKASS-Battery-Report.html", Filter = "HTML (*.html)|*.html",
            OverwritePrompt = false };
        if (browse.ShowDialog(this) != true) return;
        if (File.Exists(browse.FileName))
        {
            Status(L("Choose a new filename.", "اختر اسماً لملف جديد.", "Yeni bir dosya adı seçin."));
            return;
        }
        string file = browse.FileName;
        latestTool = await Run(L("Generating battery report…", "جارٍ إنشاء تقرير البطارية…", "Pil raporu oluşturuluyor…"),
            token => Task.Run(() => BatteryService.Report(file), token), false);
        model.Store.Log("WindowsTool", "powercfg /batteryreport · " + latestTool.ExitCode);
        if (latestTool.ExitCode == 0 && File.Exists(file))
            Status(L("Battery report saved.", "حُفظ تقرير البطارية.", "Pil raporu kaydedildi."));
        else
        {
            Status(ExitLabel(latestTool));
            Info(ExitLabel(latestTool) + "\n\n" + Short(latestTool.Output, 2300));
        }
    }

    private UIElement GamingPage()
    {
        var body = Body();
        body.Children.Add(Intro(L("Review background applications and temporarily select an installed Windows power plan. A plan change can affect energy and battery use.",
            "راجع تطبيقات الخلفية واختر مؤقتاً خطة طاقة موجودة في Windows. قد يؤثر تغيير الخطة على استهلاك الطاقة والبطارية.",
            "Arka plan uygulamalarını inceleyin ve yüklü bir Windows güç planını geçici olarak seçin. Plan değişikliği enerji ve pil kullanımını etkileyebilir.")));
        var plans = Choice(Array.Empty<string>(), -1, L("Power plans", "خطط الطاقة", "Güç planları"));
        var planInfo = Muted("");
        var stats = Muted("");
        var candidates = Array.Empty<PowerPlan>();
        Button change = null!, restore = null!;
        change = ActionButton(L("Start session", "بدء الجلسة", "Oturumu başlat"), async () =>
        {
            if (plans.SelectedIndex < 0 || plans.SelectedIndex >= candidates.Length) return;
            var target = candidates[plans.SelectedIndex];
            if (!Confirm(L("Use this power plan until the session ends?", "هل تريد استخدام خطة الطاقة حتى تنتهي الجلسة؟",
                "Bu güç planı oturum bitene kadar kullanılsın mı?") + "\n\n" + target.Name)) return;
            await Run(L("Switching power plan…", "جارٍ تبديل خطة الطاقة…", "Güç planı değiştiriliyor…"),
                token => Task.Run(() => {
                    GamingService.Begin(model.Store, model.Preferences, target.Id);
                    if (model.Preferences.OriginalPlan != Guid.Empty) model.Store.Log("GamingStart", "Power plan changed");
                    return true;
                }, token), false);
            await ReloadPlans();
            Status("");
        }, true);
        restore = ActionButton(L("End session / restore", "إنهاء الجلسة واستعادة الخطة", "Oturumu bitir / geri yükle"), async () =>
        {
            var changed = await Run(L("Finishing gaming session…", "جارٍ إنهاء جلسة اللعب…", "Oyun oturumu bitiriliyor…"),
                token => Task.Run(() => {
                    bool restored = GamingService.Restore(model.Store, model.Preferences);
                    model.Store.Log("GamingEnd", restored ? "Original plan restored" : "Active plan already changed");
                    return restored;
                }, token), false);
            await ReloadPlans();
            Status(changed ? L("Original power plan restored.", "استُعيدت خطة الطاقة الأصلية.", "Özgün güç planı geri yüklendi.") :
                L("Kept the power plan currently selected in Windows.", "بقيت خطة الطاقة المحددة حالياً في Windows.", "Windows'ta seçili güç planı korundu."));
        });
        change.IsEnabled = restore.IsEnabled = false;
        body.Children.Add(Section(L("Gaming session", "جلسة اللعب", "Oyun oturumu"), planInfo, Actions(plans),
            Actions(change, restore, ActionButton(L("Refresh plans", "تحديث الخطط", "Planları yenile"), ReloadPlans)), stats));
        body.Children.Add(ProcessesPanel(allowClose: true));
        refreshLive = () =>
        {
            if (model.Current is { } data)
                stats.Text = "CPU " + Percent(data.Cpu) + "  ·  RAM " + (data.Total > 0 ? Percent(data.Load) : "—") +
                    "  ·  " + words.EnumPressure(data.Pressure);
        };
        if (!testMode) body.Loaded += async (_, __) =>
        {
            try { await ReloadPlans(); }
            catch (Exception e) { ShowError(e); }
        };
        return body;

        async Task ReloadPlans()
        {
            var power = await Task.Run(() => (GamingService.List(), GamingService.Active()));
            candidates = power.Item1.ToArray();
            plans.ItemsSource = candidates.Select(x => x.Name).ToArray();
            plans.SelectedIndex = Array.FindIndex(candidates, x => x.Id == power.Item2);
            planInfo.Text = L("Active power plan", "خطة الطاقة النشطة", "Etkin güç planı") + "  ·  " +
                (candidates.FirstOrDefault(x => x.Id == power.Item2)?.Name ?? power.Item2.ToString()) +
                (model.Preferences.OriginalPlan != Guid.Empty ? "\n" + L("A session is active or needs restoration.",
                    "توجد جلسة نشطة أو بانتظار الاستعادة.", "Oturum etkin veya geri yükleme gerekiyor.") : "");
            restore.IsEnabled = model.Preferences.OriginalPlan != Guid.Empty;
            change.IsEnabled = !restore.IsEnabled && candidates.Length > 1;
        }
    }
}
