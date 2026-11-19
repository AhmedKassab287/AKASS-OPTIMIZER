using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;

namespace Akass.Optimizer;

internal sealed class Language
{
    public string Code { get; }
    public FlowDirection Direction => Code == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public Language(string language)
    {
        Code = language;
        var culture = CultureInfo.GetCultureInfo(language == "ar" ? "ar-SY" : language == "tr" ? "tr-TR" : "en-US");
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
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
        var parts = record.Detail.Split('|');
        if (record.Action is "AutoMemory" or "ManualMemory")
        {
            if (parts.Length == 5 && ulong.TryParse(parts[1], out var before) && ulong.TryParse(parts[2], out var after) &&
                ulong.TryParse(parts[3], out var freeBefore) && ulong.TryParse(parts[4], out var freeAfter))
                return string.Format(Pick("{0} · Working set {1} → {2} · Available RAM {3} → {4}",
                    "{0} · الذاكرة المقيمة {1} ← {2} · ذاكرة النظام المتاحة {3} ← {4}",
                    "{0} · Çalışma kümesi {1} → {2} · Kullanılabilir RAM {3} → {4}"),
                    parts[0], Policy.Bytes(before), Policy.Bytes(after), Policy.Bytes(freeBefore), Policy.Bytes(freeAfter));
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
                parts[0], Policy.Bytes(bytes), parts[2], parts[3]) +
                (parts[4] == "1" ? " · " + Pick("Cancelled", "أُلغيت", "İptal edildi") : "");
        return record.Detail;
    }

    public string Progress(string raw)
    {
        var parts = raw.Split(new[] { ' ' }, 2);
        return Enum.TryParse<CleanupKind>(parts[0], out var kind)
            ? Category(kind) + (parts.Length > 1 ? "  " + parts[1] : "") : raw;
    }
}
