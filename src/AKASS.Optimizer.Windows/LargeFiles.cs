using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Akass.Optimizer;

public sealed class LargeFile
{
    public string Path { get; set; } = "";
    public ulong Bytes { get; set; }
    public DateTime Modified { get; set; }
}

public sealed class LargeFileReport
{
    public List<LargeFile> Files { get; } = new List<LargeFile>();
    public int Examined { get; internal set; }
    public int Skipped { get; internal set; }
    public bool Limited { get; internal set; }
}

public sealed class LargeFileFinder
{
    public LargeFileReport Scan(string folder, ulong minimum, string restriction,
        CancellationToken token, IProgress<int>? progress = null)
    {
        var root = Policy.Canonical(folder);
        if (!FileSafety.VerifyDirectory(root))
            throw new IOException("Select an accessible local folder without junctions or symbolic links.");
        var extensions = new HashSet<string>(restriction.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => "." + x.TrimStart('.')), StringComparer.OrdinalIgnoreCase);
        var report = new LargeFileReport();
        var walk = new FileWalk(root, true, token, () => report.Skipped++, () => ++report.Examined > 1000000);
        foreach (var file in walk.Files())
        {
            if (report.Examined % 1000 == 0) progress?.Report(report.Examined);
            if (extensions.Count > 0 && !extensions.Contains(Path.GetExtension(file))) continue;
            try
            {
                if (!FileSafety.Probe(file, out var identity) || identity.Modified > long.MaxValue)
                { report.Skipped++; continue; }
                if (identity.Length < minimum) continue;
                report.Files.Add(new LargeFile { Path = file, Bytes = identity.Length,
                    Modified = DateTime.FromFileTimeUtc((long)identity.Modified) });
                // Keep the largest matches, even if an unusually broad scan returns many.
                if (report.Files.Count == 20000)
                {
                    report.Files.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
                    report.Files.RemoveRange(10000, 10000);
                    report.Limited = true;
                }
            }
            catch (Exception e) when (FileSafety.ExpectedFailure(e)) { report.Skipped++; }
        }
        report.Limited |= walk.Limited;
        report.Files.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
        if (report.Files.Count > 10000)
        {
            report.Files.RemoveRange(10000, report.Files.Count - 10000);
            report.Limited = true;
        }
        return report;
    }

    public static string CsvCell(string value)
    {
        if (value.Length > 0 && "=+-@\t\r\n".IndexOf(value[0]) >= 0) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
