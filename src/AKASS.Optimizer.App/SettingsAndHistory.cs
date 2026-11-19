using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace Akass.Optimizer;

internal sealed partial class MainWindow
{
    private UIElement SettingsPage()
    {
        var body = Body();
        var settings = model.Preferences;
        var autoMemory = Check(L("Enable automatic memory optimization", "تفعيل تحسين الذاكرة التلقائي",
            "Otomatik bellek optimizasyonunu etkinleştir"), settings.AutoMemory);
        var memoryLimitLabel = L("Optimize below available RAM (MiB)", "الحد الأدنى للذاكرة المتاحة بالميبيبايت",
            "Kullanılabilir RAM eşiği (MiB)");
        var memoryLimit = Input(settings.AutoMemoryLimitMiB.ToString(words.Culture), memoryLimitLabel, 110, technical: true);
        memoryLimit.IsEnabled = settings.AutoMemory;
        autoMemory.Checked += (_, __) => memoryLimit.IsEnabled = true;
        autoMemory.Unchecked += (_, __) => memoryLimit.IsEnabled = false;
        var languageLabel = L("Language", "اللغة", "Dil");
        var themeLabel = L("Theme", "المظهر", "Tema");
        var pollLabel = L("Refresh interval (seconds)", "فاصل التحديث بالثواني", "Yenileme aralığı (saniye)");
        var sensitivityLabel = L("Memory pressure sensitivity", "حساسية ضغط الذاكرة", "Bellek baskısı hassasiyeti");
        var languages = new[] { "en", "ar", "tr" };
        var styles = new[] { "Dark", "Light", "System" };
        var language = Choice(new[] { "English", "العربية", "Türkçe" }, Array.IndexOf(languages, settings.Language), languageLabel);
        var palette = Choice(new[] { L("Dark", "داكن", "Koyu"), L("Light", "فاتح", "Açık"), L("System", "النظام", "Sistem") },
            Array.IndexOf(styles, settings.Theme), themeLabel);
        var refresh = Input(settings.Refresh.ToString(words.Culture), pollLabel, 110, technical: true);
        AutomationProperties.SetAutomationId(language, "SettingsLanguage");
        AutomationProperties.SetAutomationId(palette, "SettingsTheme");
        AutomationProperties.SetAutomationId(refresh, "SettingsRefresh");
        AutomationProperties.SetAutomationId(autoMemory, "SettingsAutoMemory");
        AutomationProperties.SetAutomationId(memoryLimit, "SettingsMemoryLimit");
        var sensitivity = Choice(new[] { L("Low", "منخفضة", "Düşük"), L("Normal", "عادية", "Normal"),
            L("High", "مرتفعة", "Yüksek") }, settings.Sensitivity - 1, sensitivityLabel);
        var traySetting = Check(L("Minimize to system tray", "التصغير إلى منطقة الإعلام", "Sistem tepsisine küçült"), settings.Tray);
        var background = Check(L("Monitor resources in the background", "مراقبة الموارد في الخلفية", "Kaynakları arka planda izle"), settings.Background);
        var notification = Check(L("Notify about sustained memory pressure", "الإشعار عند استمرار ضغط الذاكرة", "Sürekli bellek baskısını bildir"), settings.Notifications);
        var advanced = Check(L("Show advanced diagnostic categories", "عرض فئات التشخيص المتقدمة", "Gelişmiş tanılama kategorilerini göster"), settings.Advanced);
        bool launchState = false, launchReadable = true;
        try { launchState = model.Startup.LaunchesWithWindows; }
        catch (Exception error) when (error is UnauthorizedAccessException || error is System.Security.SecurityException)
        { launchReadable = false; }
        var launch = Check(L("Launch with Windows", "التشغيل مع Windows", "Windows ile başlat"), launchState);
        launch.IsEnabled = launchReadable;
        if (!launchReadable) launch.ToolTip = L("Windows blocked access to startup preferences.",
            "منع Windows الوصول إلى تفضيلات بدء التشغيل.", "Windows başlangıç tercihlerine erişimi engelledi.");
        var safeDefaults = ((CleanupKind[])Enum.GetValues(typeof(CleanupKind)))
            .Where(x => x is not (CleanupKind.Recycle or CleanupKind.Reports or CleanupKind.Dumps or CleanupKind.Prefetch))
            .Select(kind => (kind, box: Check(words.Category(kind), settings.CleanupDefaults.Contains(kind.ToString())))).ToArray();
        AutomationProperties.SetAutomationId(traySetting, "SettingsTray");
        AutomationProperties.SetAutomationId(background, "SettingsBackground");
        AutomationProperties.SetAutomationId(notification, "SettingsNotifications");
        AutomationProperties.SetAutomationId(advanced, "SettingsAdvanced");
        AutomationProperties.SetAutomationId(sensitivity, "SettingsSensitivity");
        foreach (var item in safeDefaults) AutomationProperties.SetAutomationId(item.box, "Cleanup" + item.kind);
        body.Children.Add(Section(L("Appearance", "المظهر", "Görünüm"),
            Actions(Field(languageLabel, language), Field(themeLabel, palette))));
        body.Children.Add(Section(L("Monitoring and startup", "المراقبة وبدء التشغيل", "İzleme ve başlangıç"),
            Actions(Field(pollLabel, refresh), Field(sensitivityLabel, sensitivity)),
            traySetting, background, notification, launch, advanced));
        body.Children.Add(Section(L("Automatic memory optimization", "تحسين الذاكرة التلقائي", "Otomatik bellek optimizasyonu"),
            autoMemory, Field(memoryLimitLabel, memoryLimit),
            Intro(L("Waits for sustained memory pressure and two minutes of inactivity. Runs in the tray when background monitoring is enabled. Choose an optional app for this session on the Memory Optimizer page.",
                "ينتظر استمرار ضغط الذاكرة ودقيقتين من عدم النشاط. يعمل في شريط النظام عند تفعيل المراقبة في الخلفية. يمكنك اختيار تطبيق لهذه الجلسة من صفحة إدارة الذاكرة.",
                "Bellek baskısının sürmesini ve iki dakika hareketsizliği bekler. Arka plan izleme açıksa sistem tepsisinde çalışır. Bellek yönetimi sayfasından bu oturum için bir uygulama seçebilirsiniz."))));
        var group = Body();
        foreach (var item in safeDefaults) group.Children.Add(item.box);
        body.Children.Add(Section(L("Preselected cleanup categories", "فئات التنظيف المحددة مسبقاً", "Önceden seçilen temizlik kategorileri"),
            group));
        var settingsActions = Actions(ActionButton(L("Save settings", "حفظ الإعدادات", "Ayarları kaydet"), () =>
        {
            if (model.Busy) throw new InvalidOperationException("Wait for the active operation.");
            if (!int.TryParse(NormalizeNumber(refresh.Text), out int interval) || interval < 1 || interval > 30)
            {
                Status(L("Refresh interval must be between 1 and 30 seconds.", "يجب أن يكون فاصل التحديث بين ثانية و30 ثانية.",
                    "Yenileme aralığı 1 ile 30 saniye olmalıdır."));
                return;
            }
            int limit = settings.AutoMemoryLimitMiB;
            if (autoMemory.IsChecked == true && (!int.TryParse(NormalizeNumber(memoryLimit.Text), out limit) ||
                limit < AutoMemoryPolicy.MinimumLimitMiB || limit > AutoMemoryPolicy.MaximumLimitMiB))
            {
                Status(L("Enter an available RAM limit from 128 to 16384 MiB.",
                    "أدخل حداً للذاكرة المتاحة بين 128 و16384 MiB.", "128 ile 16384 MiB arasında bir kullanılabilir RAM eşiği girin."));
                return;
            }
            var replacement = new Preferences
            {
                Language = languages[language.SelectedIndex], Theme = styles[palette.SelectedIndex], Tray = traySetting.IsChecked == true,
                Background = background.IsChecked == true, Notifications = notification.IsChecked == true,
                Advanced = advanced.IsChecked == true, Refresh = interval, Sensitivity = sensitivity.SelectedIndex + 1,
                AutoMemory = autoMemory.IsChecked == true, AutoMemoryLimitMiB = limit,
                CleanupDefaults = safeDefaults.Where(x => x.box.IsChecked == true).Select(x => x.kind.ToString()).ToArray(),
                ScheduledConsent = settings.ScheduledConsent, OriginalPlan = settings.OriginalPlan, GamingPlan = settings.GamingPlan
            };
            bool start = launch.IsChecked == true;
            if (launchReadable && start != launchState)
            {
                if (!Confirm(L("Change whether AKASS Optimizer starts when you sign in?",
                    "هل تريد تغيير تشغيل AKASS Optimizer عند تسجيل الدخول؟", "AKASS Optimizer oturum açılışında başlatma ayarı değişsin mi?"))) return;
                model.Startup.SetLaunchWithWindows(start);
            }
            try { model.Store.Save(replacement); }
            catch
            {
                if (launchReadable && start != launchState) model.Startup.SetLaunchWithWindows(launchState);
                throw;
            }
            if (!replacement.AutoMemory) model.AutoMemoryTarget = null;
            model.Preferences = replacement;
            Rebuild(Page.Settings);
            Status(L("Settings saved.", "تم حفظ الإعدادات.", "Ayarlar kaydedildi."));
        }, true), ActionButton(L("Reset settings", "إعادة ضبط الإعدادات", "Ayarları sıfırla"), async () =>
        {
            if (!Confirm(L("Reset preferences, disable launch with Windows and remove scheduled maintenance?",
                "هل تريد إعادة ضبط التفضيلات وإيقاف التشغيل مع Windows وإزالة الصيانة المجدولة؟",
                "Tercihler sıfırlansın, Windows ile başlatma kapatılsın ve zamanlanmış bakım kaldırılsın mı?"), true)) return;
            if (settings.OriginalPlan != Guid.Empty)
            {
                Status(L("End the gaming session first.", "أنه جلسة اللعب أولاً.", "Önce oyun oturumunu bitirin."));
                return;
            }
            await Run(L("Resetting settings…", "جارٍ إعادة ضبط الإعدادات…", "Ayarlar sıfırlanıyor…"), token => Task.Run(() =>
            {
                model.Schedule.Remove();
                if (model.Startup.LaunchesWithWindows) model.Startup.SetLaunchWithWindows(false);
                model.Store.Save(new Preferences());
                return true;
            }, token), false);
            model.Preferences = model.Store.ReadSettings();
            model.AutoMemoryTarget = null;
            Rebuild(Page.Settings);
        }));
        AutomationProperties.SetAutomationId((Button)settingsActions.Children[0], "SaveSettings");
        body.Children.Add(settingsActions);
        body.Children.Add(UpdatesPanel());
        body.Children.Add(SchedulePanel(settings));
        body.Children.Add(Section(L("Saved diagnostics", "بيانات التشخيص المحفوظة", "Kayıtlı tanılama"),
            Actions(ActionButton(L("Export activity log", "تصدير سجل النشاط", "Etkinlik günlüğünü dışa aktar"), ExportHistory),
                ActionButton(PageName(Page.History), () => Navigate(Page.History)))));
        return body;
    }

    private Border SchedulePanel(Preferences settings)
    {
        var info = Muted("");
        var next = Muted("");
        var options = Choice(new[] { L("Daily", "يومياً", "Günlük"), L("Weekly", "أسبوعياً", "Haftalık"),
            L("Monthly", "شهرياً", "Aylık") }, 1, L("Frequency", "التكرار", "Sıklık"));
        var first = new DatePicker { SelectedDate = DateTime.Today.AddDays(1), Width = 155, Margin = new Thickness(0, 0, 8, 6) };
        if (words.Code == "ar")
        {
            first.SelectedDateFormat = DatePickerFormat.Short;
            first.FlowDirection = FlowDirection.LeftToRight;
        }
        var time = Input("10:00", L("Start time (24 hours)", "وقت البدء (24 ساعة)", "Başlangıç saati (24 saat)"), 100, technical: true);
        Button pause = null!, resume = null!, remove = null!;
        var edit = ActionButton(L("Save schedule", "حفظ الجدول", "Zamanlamayı kaydet"), async () =>
        {
            if (!first.SelectedDate.HasValue || !TimeSpan.TryParseExact(NormalizeNumber(time.Text), new[] { @"hh\:mm", @"h\:mm" },
                CultureInfo.InvariantCulture, out var clockTime) || clockTime < TimeSpan.Zero || clockTime >= TimeSpan.FromDays(1))
            {
                Status(L("Enter a valid date and 24-hour time.", "أدخل تاريخاً ووقتاً صالحين بنظام 24 ساعة.",
                    "Geçerli bir tarih ve 24 saat biçiminde saat girin."));
                return;
            }
            DateTime starts = first.SelectedDate.Value.Date + clockTime;
            var cadence = (Cadence)options.SelectedIndex;
            if (cadence == Cadence.Monthly && starts.Day > 28)
            {
                Status(L("Choose a day from 1 to 28 for monthly runs.", "اختر يوماً من 1 إلى 28 للجدول الشهري.",
                    "Aylık çalıştırma için 1-28 arasında bir gün seçin."));
                return;
            }
            if (!Confirm(L("Allow unattended deletion of eligible current-user temporary files at least seven days old?",
                "هل تسمح بالحذف المجدول لملفات المستخدم المؤقتة المؤهلة التي مر عليها سبعة أيام على الأقل؟",
                "En az yedi günlük uygun kullanıcı geçici dosyalarının gözetimsiz silinmesine izin verilsin mi?"), true)) return;
            await Run(L("Registering maintenance…", "جارٍ تسجيل الصيانة…", "Bakım kaydediliyor…"), token => Task.Run(() =>
            {
                model.Schedule.Register(cadence, starts);
                settings.ScheduledConsent = true;
                try { model.Store.Save(settings); }
                catch { settings.ScheduledConsent = false; model.Schedule.SetEnabled(false); throw; }
                model.Store.Log("ScheduleChanged", cadence.ToString());
                return true;
            }, token), false);
            await Finish();
        }, true);
        pause = ActionButton(L("Pause", "إيقاف مؤقت", "Duraklat"), () => Change(false, false));
        resume = ActionButton(L("Resume", "استئناف", "Sürdür"), () => Change(true, false));
        remove = ActionButton(L("Manual only / remove", "يدوي فقط وإزالة الجدول", "Yalnızca el ile / kaldır"), () => Change(false, true));
        pause.IsEnabled = resume.IsEnabled = remove.IsEnabled = false;
        var card = Section(L("Scheduled maintenance", "الصيانة المجدولة", "Zamanlanmış bakım"),
            Intro(L("An opted-in schedule removes eligible user temporary files older than seven days. It runs under your account while signed in and connected to AC power.",
                "يحذف الجدول الذي توافق عليه ملفات المستخدم المؤقتة المؤهلة الأقدم من سبعة أيام. يعمل بحسابك أثناء تسجيل الدخول والاتصال بالكهرباء.",
                "Onaylanan zamanlama yedi günden eski uygun kullanıcı geçici dosyalarını kaldırır. Oturumunuz açık ve cihaz prize takılıyken çalışır.")),
            info, next, Actions(options, first, time), Actions(edit, pause, resume, remove));
        if (!testMode) card.Loaded += async (_, __) =>
        {
            try { await RefreshSchedule(); }
            catch (Exception error) when (error is System.IO.IOException || error is UnauthorizedAccessException ||
                error is System.Security.SecurityException || error is System.Runtime.InteropServices.COMException)
            {
                info.Text = L("Windows Task Scheduler is unavailable.", "خدمة جدولة المهام في Windows غير متاحة.",
                    "Windows Görev Zamanlayıcı kullanılamıyor.");
                info.ToolTip = error.Message;
                edit.IsEnabled = false;
            }
        };
        return card;

        async Task RefreshSchedule()
        {
            var state = await Task.Run(model.Schedule.Read);
            info.Text = !state.Exists || !settings.ScheduledConsent ? L("Manual only", "يدوي فقط", "Yalnızca el ile") :
                state.Enabled ? L("Schedule active", "الجدول نشط", "Zamanlama etkin") : L("Paused", "متوقف مؤقتاً", "Duraklatıldı");
            ShowText(next, L("Last", "السابق", "Son") + "  " + words.DateLabel(state.Last) +
                "  ·  " + L("Next", "التالي", "Sonraki") + "  " + words.DateLabel(state.Next) +
                (state.Result.HasValue ? "  ·  " + L("Result code", "رمز النتيجة", "Sonuç kodu") + "  " + words.Technical(state.Result.Value) : ""));
            pause.IsEnabled = state.Exists && state.Enabled;
            resume.IsEnabled = state.Exists && !state.Enabled && settings.ScheduledConsent;
            remove.IsEnabled = state.Exists;
        }

        async Task Finish() { await RefreshSchedule(); Status(""); }

        async Task Change(bool enabled, bool delete)
        {
            if (delete && !Confirm(L("Remove the maintenance schedule?", "هل تريد إزالة جدول الصيانة؟", "Bakım zamanlaması kaldırılsın mı?"))) return;
            await Run(L("Updating maintenance schedule…", "جارٍ تحديث جدول الصيانة…", "Bakım zamanlaması güncelleniyor…"),
                token => Task.Run(() =>
                {
                    if (delete)
                    {
                        settings.ScheduledConsent = false;
                        model.Store.Save(settings);
                        model.Schedule.Remove();
                    }
                    else model.Schedule.SetEnabled(enabled);
                    model.Store.Log("ScheduleChanged", delete ? "Removed" : enabled ? "Resumed" : "Paused");
                    return true;
                }, token), false);
            await Finish();
        }
    }

    private StackPanel Field(string label, UIElement control)
    {
        var field = Body();
        field.Children.Add(Muted(label));
        field.Children.Add(control);
        return field;
    }

    private UIElement HistoryPage()
    {
        var table = Table((L("When", "الوقت", "Zaman"), "Time", 165),
            (L("Action", "الإجراء", "İşlem"), "Action", 200),
            (L("Result", "النتيجة", "Sonuç"), "Result", 0));
        table.MaxHeight = 490;
        TechnicalColumns(table, 0);
        if (words.Code == "ar") MixedTextColumn(table, 2, nameof(HistoryView.PlainResult));
        var empty = Muted("");
        void Refresh()
        {
            var log = model.Store.History();
            table.ItemsSource = log.Select(item => new HistoryView(item.Time.ToLocalTime(),
                words.DescribeOperation(item.Action), words.HistoryDetail(item))).ToArray();
            empty.Text = log.Count == 0 ? L("No maintenance activity recorded.", "لا يوجد نشاط صيانة مسجّل.", "Bakım etkinliği kaydedilmedi.") : "";
        }
        Refresh();
        return Section(L("Recent maintenance activity", "نشاط الصيانة الأخير", "Son bakım etkinliği"), table, empty,
            Actions(ActionButton(L("Refresh", "تحديث", "Yenile"), Refresh),
                ActionButton(L("Export log", "تصدير السجل", "Günlüğü dışa aktar"), ExportHistory),
                ActionButton(L("Clear history", "مسح السجل", "Geçmişi temizle"), () =>
                {
                    if (!Confirm(L("Permanently remove the local activity log?", "هل تريد حذف سجل النشاط المحلي نهائياً?",
                        "Yerel etkinlik günlüğü kalıcı olarak kaldırılsın mı?"), true)) return;
                    model.Store.ClearHistory();
                    Refresh();
                })));
    }

    private sealed class HistoryView
    {
        public DateTime Time { get; }
        public string Action { get; }
        public string Result { get; }
        public string PlainResult => Akass.Optimizer.Language.PlainText(Result);
        public HistoryView(DateTime time, string action, string result) { Time = time; Action = action; Result = result; }
    }

    private void ExportHistory()
    {
        var export = new StringBuilder();
        foreach (var entry in model.Store.History())
            export.AppendLine(entry.Time.ToString("O", CultureInfo.InvariantCulture) + "  " +
                words.DescribeOperation(entry.Action) + "  " + Akass.Optimizer.Language.PlainText(words.HistoryDetail(entry)));
        SaveText(export.ToString(), "AKASS-Maintenance-History.txt", "Text (*.txt)|*.txt");
    }

    private UIElement AboutPage()
    {
        var body = Body();
        var brand = Text("AKASS Optimizer", 30, true);
        if (Icon != null)
            body.Children.Add(new Image { Source = Icon, Width = 70, Height = 70, HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 16) });
        body.Children.Add(brand);
        body.Children.Add(Muted(words.Technical(VersionLabel + "  ·  Ahmed Kassab  ·  Windows x64")));
        body.Children.Add(Intro(L("Local Windows maintenance, diagnostics and performance monitoring.",
            "صيانة Windows وتشخيصه ومراقبة الأداء محلياً.", "Yerel Windows bakımı, tanılama ve performans izleme.")));
        body.Children.Add(Actions(ActionButton("akass.dev", () => OpenShell("https://akass.dev"), true)));
        body.Children.Add(UpdatesPanel());
        return Card(body);
    }

    internal bool SmokePage(Page destination, string locale)
    {
        model.Preferences.Language = locale;
        Rebuild(destination);
        UpdateLayout();
        if (!string.IsNullOrEmpty(status.Text)) throw new InvalidOperationException(status.ToolTip?.ToString() ?? status.Text);
        return content.Content is FrameworkElement &&
            FlowDirection == (locale == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight);
    }
}
