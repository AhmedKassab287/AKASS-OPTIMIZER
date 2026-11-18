using System;
using System.Globalization;
using System.IO;

namespace Akass.Optimizer;

public enum Pressure { Healthy, Elevated, High, Critical }

public static class Policy
{
    public const ulong MiB = 1024UL * 1024;
    public const ulong GiB = 1024 * MiB;

    public static Pressure MemoryPressure(ulong total, ulong available, uint load,
        double? commit, bool? windowsLow, int sensitivity = 2)
    {
        double factor = sensitivity == 1 ? .8 : sensitivity == 3 ? 1.25 : 1;
        double reserve = Math.Min(2 * GiB, Math.Max(384 * MiB, total * .06)) * factor;
        bool veryLow = total > 0 && available < reserve / 2;
        bool low = total > 0 && available < reserve;
        if (commit >= .97 || (windowsLow == true && veryLow)) return Pressure.Critical;
        if (windowsLow == true || (low && (load >= 88 || commit >= .85))) return Pressure.High;
        if (low || commit >= .80 || (load >= 85 && available < 2 * reserve)) return Pressure.Elevated;
        return Pressure.Healthy;
    }

    public static int? Health(double? freeDisk, Pressure? sustained, int? startupCount, bool? cleanupOpportunity)
    {
        double earned = 0, weight = 0;
        if (freeDisk.HasValue)
        {
            weight += 40;
            earned += 40 * Math.Max(0, Math.Min(1, (freeDisk.Value - .02) / .13));
        }
        if (sustained.HasValue)
        {
            weight += 35;
            earned += sustained.Value switch { Pressure.Healthy => 35, Pressure.Elevated => 28, Pressure.High => 14, _ => 0 };
        }
        if (startupCount.HasValue)
        {
            weight += 15;
            earned += 15 - Math.Min(9, Math.Max(0, startupCount.Value - 6));
        }
        if (cleanupOpportunity.HasValue)
        {
            weight += 10;
            earned += cleanupOpportunity.Value ? 6 : 10;
        }
        return weight == 0 ? null : (int?)Math.Round(100 * earned / weight);
    }

    public static string Canonical(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) ||
            path.StartsWith(@"\?\", StringComparison.Ordinal) || path.StartsWith(@"\\", StringComparison.Ordinal) ||
            path.Length < 3 || path[1] != ':' || (path[2] != '\\' && path[2] != '/'))
            throw new ArgumentException("A local absolute drive path is required.");
        if (path.IndexOf(':', 2) >= 0) throw new ArgumentException("Alternate data streams are not supported.");
        foreach (var component in path.Substring(3).Split('\\', '/'))
            if (component == ".." || component == "." || component.EndsWith(" ") || component.EndsWith("."))
                throw new ArgumentException("Ambiguous path component.");
        var full = Path.GetFullPath(path);
        return full.Length == 3 ? full : full.TrimEnd('\\', '/');
    }

    public static bool Inside(string root, string candidate) =>
        candidate.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    public static string Bytes(ulong value)
    {
        if (value >= GiB) return (value / (double)GiB).ToString("0.0", CultureInfo.CurrentCulture) + " GiB";
        if (value >= MiB) return (value / (double)MiB).ToString("0.0", CultureInfo.CurrentCulture) + " MiB";
        return (value / 1024d).ToString("0.0", CultureInfo.CurrentCulture) + " KiB";
    }
}
