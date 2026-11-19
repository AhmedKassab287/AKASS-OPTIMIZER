using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Akass.Optimizer;

internal sealed partial class MainWindow
{
    private CleanupScan? pendingScan;
    private CleanupResult? lastCleanup;

    private UIElement CleanerPage()
    {
        var body = Body();
        body.Children.Add(Intro(L("Scan eligible caches and temporary files, then review what will be permanently deleted.",
            "افحص الذاكرة المؤقتة والملفات المؤهلة ثم راجع ما سيُحذف نهائياً.",
            "Uygun önbellek ve geçici dosyaları tarayın. Kalıcı olarak silinecekleri inceleyin.")));
        var list = Body();
        var summary = Muted("");
        var results = Muted("");
        var choices = new Dictionary<CleanupKind, (CheckBox Box, CleanupGroup Group)>();
        var apply = ActionButton(L("Clean selected", "تنظيف المحدد", "Seçilenleri temizle"), async () =>
        {
            if (pendingScan is not { } scan) return;
            var selected = choices.Where(x => x.Value.Box.IsChecked == true).Select(x => x.Key).ToArray();
            if (selected.Length == 0) return;
            string items = string.Join("\n", selected.Select(x => "• " + words.Category(x)));
            if (!Confirm(L("Permanently remove these selected items?",
                "هل تريد حذف العناصر المحددة نهائياً؟", "Seçilen öğeler kalıcı olarak kaldırılsın mı?") + "\n\n" + items, true)) return;
            bool recycle = selected.Contains(CleanupKind.Recycle) && Confirm(
                L("Empty the entire Recycle Bin for this Windows account? You cannot restore its contents afterwards.",
                    "هل تريد إفراغ سلة المحذوفات كاملة لهذا الحساب؟ لن تتمكن من استعادة محتوياتها.",
                    "Bu Windows hesabının Geri Dönüşüm Kutusu tamamen boşaltılsın mı? İçeriği sonradan geri yüklenemez."), true);
            if (!recycle) selected = selected.Where(kind => kind != CleanupKind.Recycle).ToArray();
            if (selected.Length == 0) return;
            int origin = generation;
            var progress = new Progress<string>(value => Status(words.Progress(value)));
            var cleaned = await Run(L("Removing verified files…", "جارٍ حذف الملفات التي تم التحقق منها…", "Doğrulanan dosyalar kaldırılıyor…"),
                token => Task.Run(() =>
                {
                    using var lease = ScheduleService.EnterCleanup();
                    var clear = model.Cleaner.Clean(scan, selected, recycle, token, progress);
                    model.Store.Log("Cleanup", DesktopModel.CleanupLog(clear));
                    return clear;
                }, token));
            pendingScan = null;
            lastCleanup = cleaned;
            if (origin == generation) Navigate(Page.Cleaner);
        }, true);
        apply.IsEnabled = false;

        void Update()
        {
            if (choices.Count == 0)
            {
                summary.Text = L("No scan results. Start a scan to see estimated file sizes.",
                    "لا توجد نتائج. ابدأ الفحص لعرض الأحجام المقدّرة.",
                    "Tarama sonucu yok. Tahmini dosya boyutları için taramayı başlatın.");
                apply.IsEnabled = false;
                return;
            }
            var selected = choices.Values.Where(x => x.Box.IsChecked == true).Select(x => x.Group).ToArray();
            ulong total = selected.Aggregate(0UL, (bytes, group) => bytes + group.Bytes);
            apply.IsEnabled = selected.Length > 0;
            summary.Text = L("Selected file sizes", "أحجام الملفات المحددة", "Seçilen dosya boyutları") + "  " + Size(total) +
                " · " + selected.Sum(x => x.Count) + " " + L("items", "عنصر", "öğe") +
                (pendingScan?.Limited == true ? "\n" + L("Scan limit reached. Results are partial.",
                    "بلغ الفحص الحد الأقصى. النتائج جزئية.", "Tarama sınırına ulaşıldı. Sonuçlar kısmi.") : "");
        }

        void Display()
        {
            list.Children.Clear();
            choices.Clear();
            if (pendingScan is { } scan)
            {
                foreach (var group in scan.Groups)
                {
                    var line = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 2, 0, 2) };
                    var details = ActionButton(L("Review", "مراجعة", "İncele"), () => ShowCategory(group));
                    details.IsEnabled = group.Count > 0;
                    DockPanel.SetDock(details, Dock.Right);
                    line.Children.Add(details);
                    var check = Check(words.Category(group.Kind),
                        model.Preferences.CleanupDefaults.Contains(group.Kind.ToString()) && group.Count > 0);
                    check.IsEnabled = group.Count > 0;
                    choices.Add(group.Kind, (check, group));
                    check.Checked += (_, __) => Update();
                    check.Unchecked += (_, __) => Update();
                    var note = Muted((group.IsAvailable ? Size(group.Bytes) + " · " + group.Count : words.Unknown) +
                        (group.Skipped > 0 ? " · " + group.Skipped + " " + L("excluded", "مستثنى", "hariç") : ""));
                    var left = Body();
                    left.Children.Add(check);
                    left.Children.Add(note);
                    line.Children.Add(left);
                    list.Children.Add(line);
                }
            }
            Update();
        }

        var scanButton = ActionButton(L("Scan cleanup locations", "فحص أماكن التنظيف", "Temizlik konumlarını tara"), async () =>
        {
            int origin = generation;
            var progress = new Progress<string>(value => Status(words.Progress(value)));
            pendingScan = await Run(L("Scanning cleanup locations…", "جارٍ فحص أماكن التنظيف…", "Temizlik konumları taranıyor…"),
                token => Task.Run(() => model.Cleaner.Scan(token, progress, advanced: model.Preferences.Advanced), token));
            if (origin == generation)
            {
                Display();
                Status(L("Scan completed. Review the selected categories.", "اكتمل الفحص. راجع الفئات المحددة.",
                    "Tarama tamamlandı. Seçilen kategorileri inceleyin."));
            }
        });
        body.Children.Add(Section(L("Cleanup categories", "فئات التنظيف", "Temizlik kategorileri"),
            Actions(scanButton, apply), summary, list));
        if (lastCleanup is { } result)
        {
            string delta = result.SystemDriveChange is { } change ? (change >= 0 ? "+" : "−") + Size((ulong)Math.Abs((double)change)) : "—";
            results.Text = (result.Cancelled ? L("Cancelled after completed removals.", "أُلغيت العملية بعد عمليات الحذف المكتملة.",
                "Tamamlanan silmelerden sonra iptal edildi.") + "\n" : "") +
                L("Items removed", "العناصر المحذوفة", "Kaldırılan öğeler") + "  " + result.Removed +
                "  ·  " + L("Removed file sizes", "أحجام الملفات المحذوفة", "Kaldırılan dosya boyutları") + "  " + Size(result.LogicalBytes) +
                "\n" + L("System drive free-space change", "تغيّر المساحة المتاحة على قرص النظام", "Sistem sürücüsü boş alan değişimi") +
                "  " + delta + "  ·  " + L("Skipped", "تم تجاوز", "Atlanan") + "  " + result.Skipped +
                "  ·  " + L("Failed", "فشل", "Başarısız") + "  " + result.Failed +
                (result.Issues.Count > 0 ? "\n" + string.Join("\n", result.Issues.Distinct()) : "");
            body.Children.Add(Section(L("Latest cleanup", "آخر عملية تنظيف", "Son temizlik"), results));
        }
        body.Children.Add(Section(L("Windows-managed cleanup", "التنظيف بواسطة Windows", "Windows tarafından yönetilen temizlik"),
            Muted(L("Use Windows Storage settings for update and Delivery Optimization files. Windows may rebuild thumbnails after deletion. Prefetch stays available for application loading.",
                "استخدم إعدادات تخزين Windows لملفات التحديث وتحسين التسليم. قد يعيد Windows إنشاء الصور المصغّرة. تُستخدم ملفات Prefetch لتسريع تحميل التطبيقات.",
                "Güncelleme ve Teslim İyileştirme dosyaları için Windows Depolama ayarlarını kullanın. Küçük resimler yeniden oluşturulabilir. Prefetch uygulama yüklemelerine yardımcı olur.")),
            Actions(ActionButton(L("Open Storage settings", "فتح إعدادات التخزين", "Depolama ayarlarını aç"),
                () => OpenShell("ms-settings:storagepolicies")))));
        body.Children.Add(Section(L("System maintenance", "صيانة النظام", "Sistem bakımı"),
            Muted(L("Manual scans include user Temp files older than one hour and Windows Temp files older than a day. Enable advanced maintenance for old Prefetch traces. Protected folders may require running AKASS as administrator.",
                "يشمل الفحص اليدوي ملفات Temp للمستخدم الأقدم من ساعة وملفات Temp لنظام Windows الأقدم من يوم. فعّل الصيانة المتقدمة لإظهار ملفات Prefetch القديمة. قد تتطلب المجلدات المحمية تشغيل AKASS كمسؤول.",
                "El ile tarama bir saatten eski kullanıcı ve bir günden eski Windows Temp dosyalarını içerir. Eski Prefetch dosyaları için gelişmiş bakımı etkinleştirin. Korumalı klasörler yönetici yetkisi gerektirebilir.")),
            Actions(ActionButton(L("Flush DNS cache", "مسح ذاكرة DNS", "DNS önbelleğini temizle"), async () =>
            {
                if (!Confirm(L("Flush DNS cache? Windows may request administrator permission.",
                    "مسح ذاكرة DNS المؤقتة؟ قد يطلب Windows صلاحية المسؤول.",
                    "DNS önbelleği temizlensin mi? Windows yönetici izni isteyebilir."))) return;
                var dns = await Run("DNS", token => RepairService.RunAsync(RepairKind.FlushDns, token), false);
                model.Store.Log("WindowsTool", "DNS · " + dns.ExitCode);
                Info(ExitLabel(dns) + "\n\n" + dns.Output);
                Status(ExitLabel(dns));
            }))));
        Display();
        return body;
    }

    private void ShowCategory(CleanupGroup group)
    {
        string text = group.Kind switch
        {
            CleanupKind.UserTemp => L("User Temp files must be older than one hour. Locked and changed files are skipped.",
                "يجب أن يتجاوز عمر ملف Temp ساعة واحدة. يتم تخطي الملفات المفتوحة والمتغيرة.",
                "Kullanıcı Temp dosyaları bir saatten eski olmalıdır. Kilitli veya değişen dosyalar atlanır."),
            CleanupKind.Prefetch => L("Only .pf files older than seven days are eligible. Windows may rebuild them and initial launches may take longer.",
                "يُعرض فقط ما مضى عليه سبعة أيام من ملفات .pf. قد يعيد Windows إنشاءها وقد يستغرق أول تشغيل وقتاً أطول.",
                "Yalnızca yedi günden eski .pf dosyaları uygundur. Windows bu dosyaları yeniden oluşturabilir ve ilk açılış uzayabilir."),
            CleanupKind.Recycle => L("The Windows tool empties the Recycle Bin for this account across supported drives.",
                "تفرغ أداة Windows سلة المحذوفات لهذا الحساب على محركات الأقراص المدعومة.",
                "Windows aracı bu hesabın desteklenen sürücülerdeki Geri Dönüşüm Kutusunu boşaltır."),
            CleanupKind.Reports or CleanupKind.Dumps => L("These files may help diagnose crashes. Keep them if you are troubleshooting.",
                "قد تساعد هذه الملفات في تشخيص الأعطال. احتفظ بها أثناء استكشاف المشكلات.",
                "Bu dosyalar çökmeleri teşhis etmeye yardımcı olabilir. Sorun giderirken saklayın."),
            CleanupKind.Thumbnails => L("Windows may rebuild thumbnails after cleanup.", "قد يعيد Windows إنشاء الصور المصغّرة.", "Windows küçük resimleri yeniden oluşturabilir."),
            CleanupKind.Chrome or CleanupKind.Edge or CleanupKind.Firefox => L("Close the browser before cleaning its cache.",
                "أغلق المتصفح قبل تنظيف ذاكرته المؤقتة.", "Önbelleği temizlemeden önce tarayıcıyı kapatın."),
            _ => L("Files must be at least one day old. Locked or changed files are skipped.",
                "يجب أن يكون عمر الملفات يوماً واحداً على الأقل. تُتجاوز الملفات المقفلة أو التي تغيّرت.",
                "Dosyalar en az bir günlük olmalıdır. Kilitli veya değişen dosyalar atlanır.")
        };
        if (group.Kind == CleanupKind.Recycle) { Info(text); return; }
        var preview = model.Cleaner.Preview(group, 400).Select(x => Size(x.Bytes) + "   " + x.Path);
        var layout = Body();
        layout.Margin = new Thickness(18);
        layout.Children.Add(Intro(text));
        layout.Children.Add(ResultText(string.Join("\n", preview)));
        if (group.Count > 400) layout.Children.Add(Muted(L("Showing the first 400 candidates.", "عرض أول 400 ملف مرشح.", "İlk 400 aday gösteriliyor.")));
        new Window { Owner = this, Width = 730, Height = 440, Content = layout, Background = theme.Canvas,
            FlowDirection = words.Direction, WindowStartupLocation = WindowStartupLocation.CenterOwner, Title = words.Category(group.Kind) }.ShowDialog();
    }

    private UIElement LargeFilesPage()
    {
        var body = Body();
        var selected = Input(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            L("Selected folder", "المجلد المحدد", "Seçili klasör"), 320);
        selected.IsReadOnly = true;
        selected.FlowDirection = FlowDirection.LeftToRight;
        var sizeLabel = L("Minimum size (MiB)", "الحجم الأدنى (MiB)", "En küçük boyut (MiB)");
        var typeLabel = L("Extensions, separated by commas", "الامتدادات مفصولة بفواصل", "Uzantılar, virgülle ayırın");
        var minimum = Input("100", sizeLabel, 105);
        var types = Input("", typeLabel, 170);
        var fileTable = Table((L("File path", "مسار الملف", "Dosya yolu"), "Path", 0),
            (L("Size", "الحجم", "Boyut"), "Display", 110), (L("Modified", "آخر تعديل", "Değiştirilme"), "Modified", 152));
        fileTable.Columns[1].SortMemberPath = "Bytes";
        var summary = Muted(L("Choose a local folder or drive and scan for matching files.",
            "اختر مجلداً أو قرصاً محلياً وافحص الملفات المطابقة.", "Yerel klasör veya sürücü seçip eşleşen dosyaları tarayın."));
        FileDisplay[]? entries = null;
        var browse = ActionButton(L("Choose folder", "اختيار مجلد", "Klasör seç"), () =>
        {
            using var dialog = new Forms.FolderBrowserDialog { SelectedPath = selected.Text, ShowNewFolderButton = false };
            if (dialog.ShowDialog(new OwnerWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle)) == Forms.DialogResult.OK)
                selected.Text = dialog.SelectedPath;
        });
        var open = ActionButton(L("Open containing folder", "فتح المجلد الحاوي", "İçeren klasörü aç"), () =>
        {
            if (fileTable.SelectedItem is FileDisplay item)
            {
                string path = Path.GetDirectoryName(item.Path)!;
                if (Directory.Exists(path)) OpenShell(path);
                else Status(words.Unknown);
            }
        });
        open.IsEnabled = false;
        fileTable.SelectionChanged += (_, __) => open.IsEnabled = fileTable.SelectedItem != null;
        var export = ActionButton(L("Export CSV", "تصدير CSV", "CSV dışa aktar"), () =>
        {
            if (entries == null) return;
            var text = new StringBuilder();
            text.AppendLine(string.Join(",", new[] { L("Path", "مسار", "Yol"), L("Bytes", "بايت", "Bayt"),
                L("Modified UTC", "وقت التعديل UTC", "Değiştirilme UTC") }.Select(LargeFileFinder.CsvCell)));
            foreach (var item in entries)
                text.AppendLine(LargeFileFinder.CsvCell(item.Path) + "," + item.Bytes.ToString(CultureInfo.InvariantCulture) +
                    "," + item.Utc.ToString("O", CultureInfo.InvariantCulture));
            SaveText(text.ToString(), "AKASS-Large-Files.csv", "CSV (*.csv)|*.csv");
        });
        export.IsEnabled = false;
        var scan = ActionButton(L("Scan files", "فحص الملفات", "Dosyaları tara"), async () =>
        {
            if (!ulong.TryParse(minimum.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var limit) || limit < 1 || limit > 1000000000)
            { Status(L("Enter a positive minimum size in MiB.", "أدخل حداً أدنى موجباً بالميبيبايت.", "MiB cinsinden pozitif alt sınır girin.")); return; }
            string path = selected.Text;
            string extensions = types.Text;
            var progress = new Progress<int>(count => Status(L("Entries examined", "العناصر المفحوصة", "İncelenen kayıtlar") + "  " + count.ToString("N0")));
            var report = await Run(L("Searching for large files…", "جارٍ البحث عن الملفات الكبيرة…", "Büyük dosyalar aranıyor…"),
                token => Task.Run(() => model.LargeFiles.Scan(path, limit * Policy.MiB, extensions, token, progress), token));
            entries = report.Files.Select(x => new FileDisplay(x)).ToArray();
            fileTable.ItemsSource = entries;
            export.IsEnabled = entries.Length > 0;
            summary.Text = entries.Length + " " + L("matching files", "ملفات مطابقة", "eşleşen dosya") + " · " +
                report.Skipped + " " + L("skipped", "تم تجاوزه", "atlandı") +
                (report.Limited ? " · " + L("Partial results", "نتائج جزئية", "Kısmi sonuçlar") : "");
            Status("");
        }, true);
        var sizeColumn = Body();
        sizeColumn.Children.Add(Muted(sizeLabel));
        sizeColumn.Children.Add(minimum);
        var typeColumn = Body();
        typeColumn.Children.Add(Muted(typeLabel));
        typeColumn.Children.Add(types);
        body.Children.Add(Section(L("Find large files", "البحث عن الملفات الكبيرة", "Büyük dosyaları bul"),
            Actions(selected, browse), Actions(sizeColumn, typeColumn), Actions(scan), summary));
        body.Children.Add(Section(L("Results", "النتائج", "Sonuçlar"), fileTable, Actions(open, export)));
        return body;
    }

    private sealed class FileDisplay
    {
        private readonly LargeFile file;
        public string Path => file.Path;
        public ulong Bytes => file.Bytes;
        public string Display => Policy.Bytes(Bytes);
        public DateTime Utc => file.Modified;
        public DateTime Modified => Utc.ToLocalTime();
        public FileDisplay(LargeFile file) => this.file = file;
    }

    private sealed class OwnerWindow : Forms.IWin32Window
    {
        public IntPtr Handle { get; }
        public OwnerWindow(IntPtr handle) => Handle = handle;
    }

    private TextBox ResultText(string text)
    {
        var input = Input(text, L("Output", "المخرجات", "Çıktı"));
        input.Height = 245;
        input.Margin = new Thickness(0, 3, 0, 5);
        input.IsReadOnly = true;
        input.TextWrapping = TextWrapping.Wrap;
        input.VerticalContentAlignment = VerticalAlignment.Top;
        input.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        return input;
    }

    private void SaveText(string contentText, string filename, string filter, Window? owner = null)
    {
        var dialog = new SaveFileDialog { FileName = filename, Filter = filter, OverwritePrompt = true };
        if (dialog.ShowDialog(owner ?? this) == true)
        {
            File.WriteAllText(dialog.FileName, contentText, new UTF8Encoding(true));
            Status(L("File saved.", "تم حفظ الملف.", "Dosya kaydedildi."));
        }
    }

    private static void OpenShell(string target)
    {
        using var process = Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
    }
}
