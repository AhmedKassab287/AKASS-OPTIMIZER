using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Akass.Optimizer;

public enum CleanupKind { UserTemp, WindowsTemp, Chrome, Edge, Firefox, Thumbnails, Reports, Dumps, Recycle }

public sealed class CleanupGroup
{
    public CleanupKind Kind { get; }
    internal List<Candidate> Files { get; } = new List<Candidate>();
    public ulong Bytes { get; internal set; }
    public long Count => Kind == CleanupKind.Recycle ? SpecialCount : Files.Count;
    internal long SpecialCount { get; set; }
    public int Skipped { get; internal set; }
    public bool IsAvailable { get; internal set; } = true;
    internal CleanupGroup(CleanupKind kind) => Kind = kind;
}

internal sealed class Candidate
{
    public string Path = "";
    public string Root = "";
    public FileSafety.Identity Identity;
}

public sealed class CleanupScan
{
    public IReadOnlyList<CleanupGroup> Groups { get; }
    public bool Limited { get; internal set; }
    internal CleanupScan(IReadOnlyList<CleanupGroup> groups) => Groups = groups;
}

public sealed class CleanupResult
{
    public long Removed { get; internal set; }
    public int Skipped { get; internal set; }
    public int Failed { get; internal set; }
    public ulong LogicalBytes { get; internal set; }
    public long? SystemDriveChange { get; internal set; }
    public bool Cancelled { get; internal set; }
    public List<string> Issues { get; } = new List<string>();

    internal void Issue(string context, Exception error)
    {
        if (Issues.Count < 12) Issues.Add(context + " (" + error.HResult.ToString("X8") + ")");
    }
}

public sealed class Cleaner
{
    private readonly string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private readonly string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private readonly string windows = Directory.GetParent(Environment.SystemDirectory)!.FullName;
    private const int MaxEntries = 300000;
    private readonly string? fixtureRoot;
    public Cleaner() { }
    internal Cleaner(string fixtureRoot) => this.fixtureRoot = Policy.Canonical(fixtureRoot);

    public IReadOnlyList<(string Path, ulong Bytes)> Preview(CleanupGroup group, int limit) =>
        group.Files.Take(Math.Max(0, Math.Min(500, limit))).Select(x => (x.Path, x.Identity.Length)).ToArray();

    public CleanupScan Scan(CancellationToken token, IProgress<string>? progress = null, bool scheduled = false, bool advanced = false)
    {
        var specs = scheduled ? new[] { CleanupKind.UserTemp } : (CleanupKind[])Enum.GetValues(typeof(CleanupKind));
        var groups = specs.Where(kind => advanced || kind != CleanupKind.Dumps && kind != CleanupKind.Reports).Select(kind => new CleanupGroup(kind)).ToArray();
        var scan = new CleanupScan(groups);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        int examined = 0;
        foreach (var group in groups)
        {
            token.ThrowIfCancellationRequested();
            progress?.Report(group.Kind.ToString());
            if (group.Kind == CleanupKind.Recycle)
            {
                group.IsAvailable = QueryRecycle(out var bin);
                if (group.IsAvailable) { group.Bytes = (ulong)bin.Bytes; group.SpecialCount = bin.Items; }
                continue;
            }

            try
            {
                var roots = Roots(group.Kind, scheduled).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                group.IsAvailable = roots.Length > 0;
                var cutoff = DateTime.UtcNow.AddDays(scheduled ? -7 : Age(group.Kind));
                foreach (var root in roots)
                {
                    token.ThrowIfCancellationRequested();
                    if (!FileSafety.VerifyDirectory(root)) { group.Skipped++; continue; }
                    var walk = new FileWalk(root, group.Kind != CleanupKind.Thumbnails && group.Kind != CleanupKind.Dumps,
                        token, () => group.Skipped++, () => ++examined > MaxEntries);
                    foreach (var path in walk.Files())
                    {
                        try
                        {
                            if (!Matches(group.Kind, path) || File.GetLastWriteTimeUtc(path) >= cutoff ||
                                !FileSafety.Probe(path, out var identity) || identity.Links != 1 ||
                                identity.Modified >= (ulong)cutoff.ToFileTimeUtc())
                            { group.Skipped++; continue; }
                            if (!visited.Add(identity.Key)) continue;
                            group.Files.Add(new Candidate { Path = path, Root = root, Identity = identity });
                            group.Bytes += identity.Length;
                        }
                        catch (Exception e) when (FileSafety.ExpectedFailure(e)) { group.Skipped++; }
                    }
                    if (walk.Limited) { scan.Limited = true; break; }
                }
            }
            catch (Exception e) when (FileSafety.ExpectedFailure(e)) { group.Skipped++; }
            if (scan.Limited)
            {
                foreach (var unscanned in groups.SkipWhile(next => next != group).Skip(1)) unscanned.IsAvailable = false;
                break;
            }
        }
        return scan;
    }

    public CleanupResult Clean(CleanupScan scan, IEnumerable<CleanupKind> selected,
        bool recycleConfirmed, CancellationToken token, IProgress<string>? progress = null, bool scheduled = false)
    {
        var kinds = new HashSet<CleanupKind>(selected);
        var result = new CleanupResult();
        long? before = Free();
        try
        {
            foreach (var group in scan.Groups.Where(g => kinds.Contains(g.Kind) && (!scheduled || g.Kind == CleanupKind.UserTemp)))
            {
                token.ThrowIfCancellationRequested();
                progress?.Report(group.Kind.ToString());
                if (group.Kind == CleanupKind.Recycle)
                {
                    if (recycleConfirmed) EmptyRecycle(result);
                    continue;
                }
                if (BrowserRunning(group.Kind)) { result.Skipped += group.Files.Count; continue; }
                HashSet<string> allowedRoots;
                try { allowedRoots = new HashSet<string>(Roots(group.Kind, scheduled), StringComparer.OrdinalIgnoreCase); }
                catch (Exception e) when (FileSafety.ExpectedFailure(e))
                {
                    result.Failed += group.Files.Count;
                    result.Issue(group.Kind.ToString(), e);
                    continue;
                }
                var cutoff = (ulong)DateTime.UtcNow.AddDays(scheduled ? -7 : Age(group.Kind)).ToFileTimeUtc();
                int checkedCount = 0;
                foreach (var file in group.Files)
                {
                    token.ThrowIfCancellationRequested();
                    if (++checkedCount % 100 == 0) progress?.Report(group.Kind + " " + checkedCount + "/" + group.Files.Count);
                    try
                    {
                        if (!allowedRoots.Contains(file.Root) || file.Identity.Modified >= cutoff || !Matches(group.Kind, file.Path) ||
                            !FileSafety.Delete(file.Root, file.Path, file.Identity))
                        { result.Skipped++; continue; }
                        result.Removed++;
                        result.LogicalBytes += file.Identity.Length;
                    }
                    catch (Exception e) when (FileSafety.ExpectedFailure(e))
                    { result.Failed++; result.Issue(group.Kind.ToString(), e); }
                }
            }
        }
        catch (OperationCanceledException) { result.Cancelled = true; }
        long? after = Free();
        result.SystemDriveChange = after - before;
        return result;
    }

    private IEnumerable<string> Roots(CleanupKind kind, bool scheduled)
    {
        IEnumerable<string> candidates = kind switch
        {
            CleanupKind.UserTemp => UserTemp(scheduled),
            CleanupKind.WindowsTemp => new[] { Path.Combine(windows, "Temp") },
            CleanupKind.Chrome => Chromium(Path.Combine(local, "Google", "Chrome", "User Data")),
            CleanupKind.Edge => Chromium(Path.Combine(local, "Microsoft", "Edge", "User Data")),
            CleanupKind.Firefox => Firefox(),
            CleanupKind.Thumbnails => new[] { Path.Combine(local, "Microsoft", "Windows", "Explorer") },
            CleanupKind.Reports => new[] { Path.Combine(local, "Microsoft", "Windows", "WER", "ReportArchive"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "WER", "ReportArchive") },
            CleanupKind.Dumps => new[] { Path.Combine(local, "CrashDumps"), Path.Combine(windows, "Minidump"), windows },
            _ => Array.Empty<string>()
        };
        foreach (var candidate in candidates)
        {
            var full = Policy.Canonical(candidate);
            if (Directory.Exists(full)) yield return full;
        }
    }

    private IEnumerable<string> UserTemp(bool scheduled)
    {
        if (fixtureRoot != null) { yield return fixtureRoot; yield break; }
        yield return Path.Combine(local, "Temp");
        if (scheduled) yield break;
        string? path = Environment.GetEnvironmentVariable("TEMP");
        if (string.IsNullOrWhiteSpace(path)) yield break;
        string resolved;
        try { resolved = Policy.Canonical(path!); }
        catch (Exception e) when (FileSafety.ExpectedFailure(e)) { yield break; }
        var parent = Path.GetDirectoryName(resolved);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.Equals(parent, local, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(parent, profile, StringComparison.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(resolved);
            if (string.Equals(name, "Temp", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Tmp", StringComparison.OrdinalIgnoreCase)) yield return resolved;
        }
    }

    private static IEnumerable<string> Chromium(string basePath)
    {
        if (!FileSafety.VerifyDirectory(basePath)) yield break;
        foreach (var profile in Directory.EnumerateDirectories(basePath).Take(250))
        {
            if (!FileSafety.VerifyDirectory(profile) || !File.Exists(Path.Combine(profile, "Preferences"))) continue;
            yield return Path.Combine(profile, "Cache", "Cache_Data");
            yield return Path.Combine(profile, "Code Cache");
            yield return Path.Combine(profile, "GPUCache");
        }
    }

    private IEnumerable<string> Firefox()
    {
        var profilesRoot = Path.Combine(local, "Mozilla", "Firefox", "Profiles");
        if (FileSafety.VerifyDirectory(profilesRoot))
            foreach (var profile in Directory.EnumerateDirectories(profilesRoot).Take(250))
                yield return Path.Combine(profile, "cache2");

        var roamingRoot = Path.Combine(roaming, "Mozilla", "Firefox");
        var ini = Path.Combine(roamingRoot, "profiles.ini");
        if (!FileSafety.VerifyDirectory(roamingRoot) || !FileSafety.Probe(ini, out var config) || config.Length > 65536)
            yield break;
        foreach (var line in File.ReadLines(ini).Take(1024))
        {
            if (!line.StartsWith("Path=", StringComparison.OrdinalIgnoreCase)) continue;
            string raw = line.Substring(5).Trim().Replace('/', '\\');
            string profilePath;
            try { profilePath = Policy.Canonical(Path.IsPathRooted(raw) ? raw : Path.Combine(roamingRoot, raw)); }
            catch (Exception e) when (FileSafety.ExpectedFailure(e)) { continue; }
            if (Policy.Inside(roamingRoot, profilePath) || Policy.Inside(profilesRoot, profilePath))
                yield return Path.Combine(profilePath, "cache2");
        }
    }

    private static int Age(CleanupKind kind) => kind switch
    {
        CleanupKind.Reports or CleanupKind.Dumps => 14,
        CleanupKind.UserTemp or CleanupKind.WindowsTemp => 1,
        _ => 0
    };

    private static bool Matches(CleanupKind kind, string file)
    {
        string name = Path.GetFileName(file);
        return kind switch
        {
            CleanupKind.Thumbnails => name.StartsWith("thumbcache_", StringComparison.OrdinalIgnoreCase) &&
                                      name.EndsWith(".db", StringComparison.OrdinalIgnoreCase),
            CleanupKind.Dumps => name.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase) &&
                (!string.Equals(Path.GetDirectoryName(file), Directory.GetParent(Environment.SystemDirectory)!.FullName,
                    StringComparison.OrdinalIgnoreCase) || name.Equals("MEMORY.DMP", StringComparison.OrdinalIgnoreCase)),
            CleanupKind.Edge or CleanupKind.Chrome or CleanupKind.Firefox or CleanupKind.Reports => true,
            CleanupKind.UserTemp or CleanupKind.WindowsTemp => new[] { ".tmp", ".temp", ".etl", ".log" }
                .Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase),
            _ => !name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) &&
                 !name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase) &&
                 !name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
        };
    }

    private static bool BrowserRunning(CleanupKind kind)
    {
        string? name = kind switch { CleanupKind.Chrome => "chrome", CleanupKind.Edge => "msedge",
            CleanupKind.Firefox => "firefox", _ => null };
        if (name == null) return false;
        var processes = Process.GetProcessesByName(name);
        foreach (var process in processes) process.Dispose();
        return processes.Length > 0;
    }

    private static long? Free()
    {
        try { return new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!).AvailableFreeSpace; }
        catch (Exception e) when (FileSafety.ExpectedFailure(e)) { return null; }
    }

    private static bool QueryRecycle(out RecycleInfo info)
    {
        info = new RecycleInfo { Size = Marshal.SizeOf(typeof(RecycleInfo)) };
        return SHQueryRecycleBin(null, ref info) == 0 && info.Bytes >= 0 && info.Items >= 0;
    }

    private static void EmptyRecycle(CleanupResult result)
    {
        if (!QueryRecycle(out var before)) { result.Failed++; return; }
        if (before.Items == 0) return;
        int status = SHEmptyRecycleBin(IntPtr.Zero, null, 1 | 2 | 4);
        if (QueryRecycle(out var after))
        {
            result.Removed += Math.Max(0, before.Items - after.Items);
            result.LogicalBytes += (ulong)Math.Max(0, before.Bytes - after.Bytes);
        }
        if (status != 0) { result.Failed++; result.Issue("Recycle Bin", new COMException(null, status)); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RecycleInfo { public int Size; public long Bytes, Items; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHQueryRecycleBin(string? root, ref RecycleInfo info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? root, uint flags);
}

internal sealed class FileWalk
{
    private readonly string root;
    private readonly bool recursive;
    private readonly CancellationToken token;
    private readonly Action skipped;
    private readonly Func<bool> limit;
    public bool Limited { get; private set; }

    public FileWalk(string root, bool recursive, CancellationToken token, Action skipped, Func<bool> limit)
    { this.root = root; this.recursive = recursive; this.token = token; this.skipped = skipped; this.limit = limit; }

    public IEnumerable<string> Files()
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var folder = pending.Pop();
            if (!FileSafety.VerifyDirectory(folder)) { skipped(); continue; }
            IEnumerator<string> listing;
            try { listing = Directory.EnumerateFileSystemEntries(folder).GetEnumerator(); }
            catch (Exception e) when (FileSafety.ExpectedFailure(e)) { skipped(); continue; }
            using var entries = listing;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                string entry;
                FileAttributes attributes;
                try
                {
                    if (!entries.MoveNext()) break;
                    entry = entries.Current;
                }
                catch (Exception e) when (FileSafety.ExpectedFailure(e)) { skipped(); break; }
                if (limit()) { Limited = true; yield break; }
                try { attributes = File.GetAttributes(entry); }
                catch (Exception e) when (FileSafety.ExpectedFailure(e)) { skipped(); continue; }
                if ((attributes & FileAttributes.ReparsePoint) != 0) { skipped(); continue; }
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (recursive) pending.Push(entry);
                    continue;
                }
                yield return entry;
            }
        }
    }
}
