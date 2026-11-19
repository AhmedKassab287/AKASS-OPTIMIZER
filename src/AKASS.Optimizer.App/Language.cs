using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Akass.Optimizer;

internal sealed class Language
{
    private const string LatinDigits = "0123456789";
    private const string TechnicalStart = "\u202A\u200E";
    private const string TechnicalEnd = "\u200E\u202C";
    private static readonly CultureInfo Arabic = ArabicCulture();
    public string Code { get; }
    public FlowDirection Direction => Code == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    public CultureInfo Culture { get; }

    public Language(string language)
    {
        Code = language;
        Culture = language == "ar" ? Arabic : CultureInfo.GetCultureInfo(language == "tr" ? "tr-TR" : "en-US");
        Thread.CurrentThread.CurrentCulture = Culture;
        Thread.CurrentThread.CurrentUICulture = Culture;
    }

    private static CultureInfo ArabicCulture()
    {
        var culture = new CultureInfo("ar-SY", false);
        culture.NumberFormat.NativeDigits = LatinDigits.Select(digit => digit.ToString()).ToArray();
        culture.NumberFormat.DigitSubstitution = DigitShapes.None;
        culture.NumberFormat.NumberDecimalSeparator = ".";
        culture.NumberFormat.NumberGroupSeparator = ",";
        culture.NumberFormat.NegativeSign = "-";
        culture.DateTimeFormat.ShortDatePattern = "yyyy-MM-dd";
        culture.DateTimeFormat.ShortTimePattern = "HH:mm";
        culture.DateTimeFormat.TimeSeparator = ":";
        return CultureInfo.ReadOnly(culture);
    }

    // Keep a measurement, version, or path together inside a right-to-left sentence.
    public string Technical(object? value)
    {
        string text = Convert.ToString(value, Culture) ?? "";
        return Code == "ar" && text.Length > 0 ? TechnicalStart + text + TechnicalEnd : text;
    }

    public string DateLabel(DateTime? value) => Technical(value?.ToString("g", Culture) ?? "—");

    public string MemoryChange(ulong? before, ulong? after)
    {
        string Bytes(ulong? value) => value.HasValue ? Policy.Bytes(value.Value) : "-";
        return Technical(Bytes(before) + " → " + Bytes(after));
    }

    public static string PlainText(string value) => value.Replace(TechnicalStart, "").Replace(TechnicalEnd, "");

    public static bool HasTechnicalText(string value) => value.IndexOf(TechnicalStart, StringComparison.Ordinal) >= 0;

    public static readonly DependencyProperty DisplayTextProperty = DependencyProperty.RegisterAttached("DisplayText", typeof(string),
        typeof(Language), new PropertyMetadata("", (target, change) => SetDisplayText((TextBlock)target, (string?)change.NewValue ?? "")));

    public static void SetDisplayText(TextBlock label, string value)
    {
        if (!HasTechnicalText(value))
        {
            label.Text = value;
            return;
        }
        label.Inlines.Clear();
        AddDisplayRuns(label.Inlines, value);
    }

    public static void AddDisplayRuns(InlineCollection runs, string value)
    {
        // WPF's legacy text formatter does not isolate bidi control characters in a plain string.
        int position = 0;
        while (position < value.Length)
        {
            int start = value.IndexOf(TechnicalStart, position, StringComparison.Ordinal);
            int end = start < 0 ? -1 : value.IndexOf(TechnicalEnd, start + TechnicalStart.Length, StringComparison.Ordinal);
            if (end < 0)
            {
                runs.Add(new Run(PlainText(value.Substring(position))));
                break;
            }
            if (position < start) runs.Add(new Run(value.Substring(position, start - position)));
            int content = start + TechnicalStart.Length;
            runs.Add(new Run(value.Substring(content, end - content)) { FlowDirection = FlowDirection.LeftToRight });
            position = end + TechnicalEnd.Length;
        }
    }

    public static string NormalizeNumberInput(string value)
    {
        var chars = value.Trim().ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (chars[i] >= '\u0660' && chars[i] <= '\u0669') chars[i] = LatinDigits[chars[i] - '\u0660'];
            else if (chars[i] >= '\u06F0' && chars[i] <= '\u06F9') chars[i] = LatinDigits[chars[i] - '\u06F0'];
        }
        return new string(chars);
    }

    public string Pick(string en, string ar, string tr) => Code == "ar" ? ar : Code == "tr" ? tr : en;

    public string EnumPressure(Pressure? value) => value switch {
        Pressure.Healthy => Pick("Healthy", "طبيعية", "Normal"),
        Pressure.Elevated => Pick("Elevated usage", "استخدام مرتفع", "Yükselmiş kullanım"),
        Pressure.High => Pick("High pressure", "ضغط مرتفع", "Yüksek baskı"),
        Pressure.Critical => Pick("Critical pressure", "ضغط حرج", "Kritik baskı"),
        _ => Unknown
    };

    public string Unknown => Pick("Unavailable", "غير متاح", "Kullanılamıyor");

    public string Category(CleanupKind value) => value switch
    {
        CleanupKind.Prefetch => Pick("Old Windows Prefetch traces", "ملفات Prefetch القديمة", "Eski Windows Prefetch dosyaları"),
        CleanupKind.UserTemp => Pick("User temporary files", "ملفات المستخدم المؤقتة", "Kullanıcı geçici dosyaları"),
        CleanupKind.WindowsTemp => Pick("Windows temporary files", "ملفات Windows المؤقتة", "Windows geçici dosyaları"),
        CleanupKind.Chrome => Pick("Chrome cache", "ذاكرة Chrome المؤقتة", "Chrome önbelleği"),
        CleanupKind.Edge => Pick("Edge cache", "ذاكرة Edge المؤقتة", "Edge önbelleği"),
        CleanupKind.Firefox => Pick("Firefox cache", "ذاكرة Firefox المؤقتة", "Firefox önbelleği"),
        CleanupKind.Thumbnails => Pick("Thumbnail cache", "ذاكرة الصور المصغّرة", "Küçük resim önbelleği"),
        CleanupKind.Reports => Pick("Archived error reports", "تقارير الأخطاء المؤرشفة", "Arşivlenmiş hata raporları"),
        CleanupKind.Dumps => Pick("Crash dumps", "ملفات تفريغ الأعطال", "Çökme dökümleri"),
        _ => Pick("Recycle Bin", "سلة المحذوفات", "Geri Dönüşüm Kutusu")
    };

    public string DescribeOperation(string action) => action switch
    {
        "AutoMemory" => Pick("Automatic memory optimization", "تحسين الذاكرة التلقائي", "Otomatik bellek optimizasyonu"),
        "ManualMemory" => Pick("Memory optimization", "تحسين الذاكرة", "Bellek optimizasyonu"),
        "Cleanup" => Pick("Cleanup", "تنظيف", "Temizlik"),
        "ScheduledCleanup" => Pick("Scheduled cleanup", "تنظيف مجدول", "Zamanlanmış temizlik"),
        "CloseApp" => Pick("Application close request", "طلب إغلاق تطبيق", "Uygulama kapatma isteği"),
        "StartupChanged" => Pick("Startup item changed", "تغيير عنصر بدء التشغيل", "Başlangıç öğesi değiştirildi"),
        "GamingStart" => Pick("Gaming session started", "بدء جلسة اللعب", "Oyun oturumu başlatıldı"),
        "GamingEnd" => Pick("Gaming session ended", "إنهاء جلسة اللعب", "Oyun oturumu bitti"),
        "WindowsTool" => Pick("Windows tool executed", "تشغيل أداة Windows", "Windows aracı çalıştırıldı"),
        "ScheduleChanged" => Pick("Maintenance schedule changed", "تغيير جدول الصيانة", "Bakım zamanlaması değiştirildi"),
        _ => action
    };

    public string HistoryDetail(ActivityRecord record)
    {
        if (record.Action == "CloseApp") return record.Detail switch
        {
            "Exited" => Pick("Application stopped.", "تم إيقاف التطبيق.", "Uygulama sonlandırıldı."),
            "StillRunning" => Pick("Application is still running.", "ما زال التطبيق يعمل.", "Uygulama hâlâ çalışıyor."),
            "Unavailable" or "WM_CLOSE unavailable" => Pick("The application could not be closed.", "تعذّر إغلاق التطبيق.", "Uygulama kapatılamadı."),
            "WM_CLOSE sent" => Pick("Close request sent.", "تم إرسال طلب الإغلاق.", "Kapatma isteği gönderildi."),
            _ => record.Detail
        };
        var parts = record.Detail.Split('|');
        if (record.Action is "AutoMemory" or "ManualMemory")
        {
            if (parts.Length == 5 && ulong.TryParse(parts[1], out var before) && ulong.TryParse(parts[2], out var after) &&
                ulong.TryParse(parts[3], out var freeBefore) && ulong.TryParse(parts[4], out var freeAfter))
                return string.Format(Pick("{0} · Working set {1} · Available RAM {2}",
                    "{0} · الذاكرة المقيمة {1} · ذاكرة النظام المتاحة {2}",
                    "{0} · Çalışma kümesi {1} · Kullanılabilir RAM {2}"),
                    Technical(parts[0]), MemoryChange(before, after), MemoryChange(freeBefore, freeAfter));
            return record.Detail == "recovered"
                ? Pick("Skipped after rechecking pressure or activity.", "تم التخطي بعد إعادة فحص الضغط أو النشاط.",
                    "Baskı veya etkinlik yeniden kontrol edilince atlandı.")
                : Pick("The working set could not be trimmed safely.", "تعذّر تقليص الذاكرة المقيمة بأمان.",
                    "Çalışma kümesi güvenli biçimde küçültülemedi.");
        }
        if ((record.Action == "Cleanup" || record.Action == "ScheduledCleanup") && parts.Length == 5 &&
            ulong.TryParse(parts[1], out var bytes))
            return string.Format(Pick("{0} removed · {1} · {2} skipped · {3} failed",
                "أزيل {0} · {1} · تم تجاوز {2} · فشل {3}", "{0} kaldırıldı · {1} · {2} atlandı · {3} başarısız"),
                Technical(parts[0]), Technical(Policy.Bytes(bytes)), Technical(parts[2]), Technical(parts[3])) +
                (parts[4] == "1" ? " · " + Pick("Cancelled", "أُلغيت", "İptal edildi") : "");
        return record.Detail;
    }

    public string Progress(string raw)
    {
        var parts = raw.Split(new[] { ' ' }, 2);
        return Enum.TryParse<CleanupKind>(parts[0], out var kind)
            ? Category(kind) + (parts.Length > 1 ? "  " + Technical(parts[1]) : "") : raw;
    }
}
