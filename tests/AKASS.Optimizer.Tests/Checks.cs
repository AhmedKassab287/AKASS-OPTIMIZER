using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Akass.Optimizer;

internal static class Checks
{
    private static readonly List<string> failures = new List<string>();
    private static int passed;
    private static int skipped;

    public static int Main(string[] args)
    {
        if (args.SequenceEqual(new[] { "--working-set-fixture" }))
        {
            var pages = new byte[48 * 1024 * 1024];
            for (int i = 0; i < pages.Length; i += 4096) pages[i] = 1;
            Console.WriteLine("Ready");
            Console.ReadLine();
            GC.KeepAlive(pages);
            return 0;
        }
        var root = Path.Combine(Path.GetTempPath(), "AKASS-Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Test("GitHub updates stage only a complete, verified newer installer", () =>
            {
                const string asset = "AKASS-Optimizer-1.10.0-win-x64-setup.exe";
                const string url = "https://github.com/AhmedKassab287/AKASS-OPTIMIZER/releases/download/v1.10.0/" + asset;
                const string hash = "sha256:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
                GitHubUpdate? Read(Version current, string digest = hash, string location = url, bool preview = false)
                {
                    string metadata = "{\"tag_name\":\"v1.10.0\",\"prerelease\":" + (preview ? "true" : "false") +
                        ",\"assets\":[{\"name\":\"" + asset + "\",\"browser_download_url\":\"" + location +
                        "\",\"state\":\"uploaded\",\"size\":3,\"digest\":\"" + digest + "\"}]}";
                    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(metadata));
                    return GitHubUpdate.ReadRelease(stream, current);
                }

                var older = new Version(1, 9, 5, 0);
                var update = Read(older)!;
                Equal(new Version(1, 10, 0), update.Version);
                True(Read(new Version(1, 10, 0, 0)) == null);
                True(Read(new Version(1, 11, 0)) == null);
                True(Read(older, preview: true) == null);
                Throws<InvalidDataException>(() => Read(older, digest: ""));
                Throws<InvalidDataException>(() => Read(older, location: "https://example.com/" + asset));

                string state = Path.Combine(root, "updates-state");
                string Stage(string value)
                {
                    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(value));
                    return update.StageAsync(stream, state, CancellationToken.None).GetAwaiter().GetResult();
                }

                Throws<InvalidDataException>(() => Stage("ab"));
                Throws<InvalidDataException>(() => Stage("abcd"));
                Throws<InvalidDataException>(() => Stage("bad"));
                True(!Directory.EnumerateFiles(Path.Combine(state, "Updates")).Any());
                string path = Stage("abc");
                Equal("abc", File.ReadAllText(path));
                Equal(".exe", Path.GetExtension(path));
            });
            Test("memory pressure and commit", () =>
            {
                Equal(Pressure.Healthy, Policy.MemoryPressure(16 * Policy.GiB, 3 * Policy.GiB, 82, .55, false));
                Equal(Pressure.Healthy, Policy.MemoryPressure(128 * Policy.GiB, 10 * Policy.GiB, 92, .52, false));
                Equal(Pressure.Elevated, Policy.MemoryPressure(8 * Policy.GiB, Policy.GiB, 75, .81, false));
                Equal(Pressure.High, Policy.MemoryPressure(4 * Policy.GiB, 800 * Policy.MiB, 80, .5, true));
                Equal(Pressure.Critical, Policy.MemoryPressure(16 * Policy.GiB, 6 * Policy.GiB, 55, .99, false));
                Equal(Pressure.Critical, Policy.MemoryPressure(4 * Policy.GiB, 40 * Policy.MiB, 98, .8, true));
            });
            Test("automatic memory waits for pressure and idle, then enforces cooldown", () =>
            {
                var auto = new AutoMemoryPolicy();
                TimeSpan start = TimeSpan.FromHours(2);
                TimeSpan idle = TimeSpan.FromMinutes(3);
                bool Check(int seconds, ulong available = 250, uint load = 94, TimeSpan? inactivity = null) =>
                    auto.ShouldOptimize(true, 512, available * Policy.MiB, load, false,
                        start + TimeSpan.FromSeconds(seconds), inactivity ?? idle);

                True(!Check(0, 500, 50));
                True(!Check(2) && !Check(16));
                True(!Check(18, inactivity: TimeSpan.FromSeconds(30)));
                True(Check(20));
                True(!Check(40));
                True(!Check(44, 580) && !Check(60) && !Check(81));
                True(!Check(84, 680) && !Check(90) && !Check(108));
                True(!Check(605));
                True(!Check(619));
                True(Check(621));
                True(!auto.ShouldOptimize(false, 512, 100 * Policy.MiB, 99, true, start, idle));
            });
            Test("native working-set trim affects only the verified target", () =>
            {
                using var self = System.Diagnostics.Process.GetCurrentProcess();
                True(ProcessAnalyzer.TrimWorkingSet(new ProcessRow { Pid = self.Id, Started = 1 }) == null);
                var pages = new byte[16 * 1024 * 1024];
                for (int i = 0; i < pages.Length; i += 4096) pages[i] = 1;
                var result = ProcessAnalyzer.TrimWorkingSet();
                True(result.HasValue && result.Value.Before >= 16 * Policy.MiB &&
                    result.Value.After < result.Value.Before);
                GC.KeepAlive(pages);
            });
            Test("background RAM trim keeps the process alive and rejects an old PID identity", () =>
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (FrameMonitor.ForegroundWindow == IntPtr.Zero || HardwareSensors.IsElevated() || !Policy.Inside(local, root))
                {
                    Skip("The background trim fixture needs an interactive unelevated Windows session.");
                    return;
                }
                string folder = Under(root, "trim-app");
                foreach (string file in Directory.EnumerateFiles(AppDomain.CurrentDomain.BaseDirectory))
                    if (Path.GetFileName(file).StartsWith("AKASS.Optimizer.Tests.", StringComparison.OrdinalIgnoreCase) ||
                        file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
                using var process = Process.Start(new ProcessStartInfo(Path.Combine(folder, "AKASS.Optimizer.Tests.exe"), "--working-set-fixture")
                {
                    UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, CreateNoWindow = true
                }) ?? throw new InvalidOperationException("Could not start the memory fixture.");
                try
                {
                    var ready = process.StandardOutput.ReadLineAsync();
                    True(ready.Wait(10000) && ready.Result == "Ready");
                    process.Refresh();
                    long before = process.WorkingSet64;
                    long started = process.StartTime.ToUniversalTime().Ticks;
                    var row = new ProcessRow { Pid = process.Id, Started = started - 1, Working = (ulong)before };
                    Equal(0, ProcessAnalyzer.TrimBackgroundWorkingSets(new[] { row }, requireUserIdle: false));
                    row.Started = started;
                    Equal(1, ProcessAnalyzer.TrimBackgroundWorkingSets(new[] { row }, requireUserIdle: false));
                    process.Refresh();
                    True(!process.HasExited && process.WorkingSet64 < before);
                }
                finally
                {
                    if (!process.HasExited) process.StandardInput.WriteLine("exit");
                    if (!process.WaitForExit(2000)) process.Kill();
                }
            });
            Test("health scoring and missing values", () =>
            {
                Equal<int?>(null, Policy.Health(null, null, null, null));
                Equal<int?>(100, Policy.Health(.4, Pressure.Healthy, 4, false));
                Equal<int?>(0, Policy.Health(.01, Pressure.Critical, null, null));
                Equal<int?>(100, Policy.Health(.15, null, null, null));
                Equal<int?>(87, Policy.Health(.4, Pressure.Elevated, 8, true));
            });
            Test("path validation and path boundary", () =>
            {
                Equal(@"C:\Cache\x.tmp", Policy.Canonical(@"C:\Cache\x.tmp"));
                Equal(false, Policy.Inside(@"C:\Cache", @"C:\CacheOther\x.tmp"));
                Equal(true, Policy.Inside(@"C:\Cache", @"C:\cache\x.tmp"));
                foreach (var path in new[] { @"C:\Cache\..\Secret", @"C:\Cache\stream:copy", @"C:relative", @"\\server\share",
                                             @"C:\Cache\.. \Secret", @"C:\Cache\x. ", @"\\?\C:\Cache", @"C:\Cache\.\x" })
                    Throws<ArgumentException>(() => Policy.Canonical(path));
            });
            Test("configuration validation and local history", () =>
            {
                var store = new StateStore(Path.Combine(root, "state"));
                Equal("en", store.ReadSettings().Language);
                True(!store.ReadSettings().AutoMemory);
                Equal(512, store.ReadSettings().AutoMemoryLimitMiB);
                var preferences = new Preferences { Language = "ar", Theme = "bad", Refresh = 999, Sensitivity = 0,
                    CleanupDefaults = new[] { "UserTemp", "UserTemp", "Recycle", "Unknown" } };
                store.Save(preferences);
                Equal(30, store.ReadSettings().Refresh);
                Equal(1, store.ReadSettings().Sensitivity);
                Equal("Dark", store.ReadSettings().Theme);
                Equal("ar", store.ReadSettings().Language);
                Equal("UserTemp", store.ReadSettings().CleanupDefaults.Single());
                store.Log("Cleanup", "2|44000|0|0|0");
                store.Log("CloseApp", "request");
                Equal("CloseApp", store.History().First().Action);
                store.ClearHistory();
                Equal(0, store.History().Count);
            });
            Test("startup path parsing and registry enumeration", () =>
            {
                Equal(@"C:\Program Files\App\App.exe", StartupService.ParseExecutable("\"C:\\Program Files\\App\\App.exe\" -start"));
                Equal(@"C:\Tools\App.exe", StartupService.ParseExecutable(@"C:\Tools\App.exe /quiet"));
                Equal<string?>(null, StartupService.ParseExecutable("calc.exe -start"));
                Equal<string?>(null, StartupService.ParseExecutable(@"C:\Tools\..\evil.exe"));
                _ = new StartupService().List();
            });
            Test("Windows memory, CPU, network and storage APIs", () =>
            {
                using var metrics = new SystemMetrics();
                var first = metrics.Sample();
                Thread.Sleep(200);
                var after = metrics.Sample();
                True(first.Total > 0 && after.Available <= first.Total);
                True(after.CommitLimit > 0 && after.Commit <= after.CommitLimit && after.WindowsLow.HasValue);
                True(after.Cpu >= 0 && after.Cpu <= 100);
                True(after.DriveTotal > 0 && after.DriveFree <= after.DriveTotal);
            });
            Test("PresentMon displayed FPS selects the active swap chain and expires stale data", () =>
            {
                var frames = new FrameCsv();
                var now = new DateTime(2026, 11, 19, 10, 0, 0, DateTimeKind.Utc);
                frames.Add("Application,ProcessID,SwapChainAddress,MsBetweenPresents,MsBetweenDisplayChange", now);
                frames.Add("\"Game, demo.exe\",100,0x1,100,16", now);
                frames.Add("Game.exe,100,0x1,100,16", now.AddMilliseconds(16));
                frames.Add("Game.exe,100,0x1,100,NA", now.AddMilliseconds(32));
                frames.Add("Game.exe,100,0x2,100,3", now.AddMilliseconds(32));
                Equal<double?>(62.5, frames.Read(now.AddMilliseconds(40), out bool submitted));
                True(!submitted);
                frames.Add("Game.exe,100,0x1,10,NA", now.AddMilliseconds(2540));
                frames.Add("Game.exe,100,0x1,10,NA", now.AddMilliseconds(2556));
                Equal<double?>(100, frames.Read(now.AddMilliseconds(2560), out submitted));
                True(submitted);
                Equal<double?>(null, frames.Read(now.AddSeconds(6), out _));
                Equal(FrameCaptureStatus.PermissionDenied, FrameMonitor.ReadDiagnostic(FrameCaptureStatus.WaitingForFrames,
                    "error: failed to start trace session: access denied."));
            });
            Test("manual Temp cleanup previews old files beyond the scheduled extension filter", () =>
            {
                var folder = Under(root, "manual-temp");
                var old = Path.Combine(folder, "installer.tmp.wav");
                var current = Path.Combine(folder, "current.tmp");
                File.WriteAllText(old, "temporary copy");
                File.WriteAllText(current, "recent");
                File.SetCreationTimeUtc(old, DateTime.UtcNow.AddDays(-8));
                File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-8));
                var cleaner = new Cleaner(folder);
                var manual = cleaner.Scan(CancellationToken.None).Groups.Single(x => x.Kind == CleanupKind.UserTemp);
                True(cleaner.Preview(manual, 500).Any(x => x.Path == old));
                True(!cleaner.Preview(manual, 500).Any(x => x.Path == current));
                var scheduled = cleaner.Scan(CancellationToken.None, scheduled: true).Groups.Single();
                True(!cleaner.Preview(scheduled, 500).Any(x => x.Path == old));
                True(File.Exists(old) && File.Exists(current));
            });
            Test("running processes and stale target", () =>
            {
                var sampler = new ProcessAnalyzer();
                var rows = sampler.Sample();
                using var self = System.Diagnostics.Process.GetCurrentProcess();
                var row = rows.Single(x => x.Pid == self.Id);
                True(row.Working > 0 && row.Private > 0 && !row.CanClose && !row.CanEnd);
                Equal(AppStopResult.Unavailable, ProcessAnalyzer.StopApplication(row, force: true).Result);
            });
            Test("ending an owned app closes the selected executable and leaves an unrelated path alive", () =>
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (HardwareSensors.IsElevated() || !Policy.Inside(local, root))
                {
                    Skip("This check needs an unelevated session with writable local app data.");
                    return;
                }
                string name = "akass-process-" + Guid.NewGuid().ToString("N") + ".exe";
                string first = Path.Combine(Under(root, "end-app"), name);
                string other = Path.Combine(Under(root, "end-other"), name);
                File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), first);
                File.Copy(first, other);

                Process Start(string file) => Process.Start(new ProcessStartInfo(file, "/d /q")
                {
                    UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, CreateNoWindow = true
                }) ?? throw new InvalidOperationException("Could not start the app fixture.");

                using var target = Start(first);
                using var sibling = Start(first);
                using var unrelated = Start(other);
                try
                {
                    var row = new ProcessRow { Pid = target.Id, Name = target.ProcessName, Started = target.StartTime.ToUniversalTime().Ticks };
                    Equal(AppStopResult.Unavailable, ProcessAnalyzer.StopApplication(new ProcessRow { Pid = row.Pid, Started = row.Started - 1 }, true).Result);
                    Equal(AppStopResult.StillRunning, ProcessAnalyzer.StopApplication(row, force: false).Result);
                    Equal(AppStopResult.Exited, ProcessAnalyzer.StopApplication(row, force: true).Result);
                    True(target.WaitForExit(1000) && sibling.WaitForExit(1000) && !unrelated.HasExited);
                }
                finally
                {
                    foreach (var process in new[] { target, sibling, unrelated })
                    {
                        if (!process.HasExited) process.StandardInput.WriteLine("exit");
                        if (!process.WaitForExit(1000)) process.Kill();
                    }
                }
            });
            Test("battery capacity XML parser and active power scheme", () =>
            {
                string xml = Path.Combine(root, "a.xml");
                File.WriteAllText(xml, "<BatteryReport xmlns='urn:test'><Batteries><Battery><Id>Pack</Id><DesignCapacity>50000</DesignCapacity><FullChargeCapacity>40000</FullChargeCapacity></Battery></Batteries></BatteryReport>");
                var pack = BatteryService.ParseReport(xml).Single();
                Equal<int?>(50000, pack.Design);
                True(pack.Wear.HasValue && Math.Abs(pack.Wear.Value - .2) < .00001);
                _ = BatteryService.Status();
                var active = GamingService.Active();
                True(GamingService.List().Any(x => x.Id == active));
            });
            Test("large file filtering, totals, CSV and cancellation", () =>
            {
                var folder = Under(root, "large");
                File.WriteAllBytes(Path.Combine(folder, "first.tmp"), new byte[2048]);
                File.WriteAllBytes(Path.Combine(folder, "small.tmp"), new byte[16]);
                File.WriteAllBytes(Path.Combine(folder, "other.jpg"), new byte[4096]);
                var report = new LargeFileFinder().Scan(folder, 1000, "tmp", CancellationToken.None);
                Equal(1, report.Files.Count);
                Equal(2048UL, report.Files.Single().Bytes);
                Equal("\"'=2*2\"", LargeFileFinder.CsvCell("=2*2"));
                using var stop = new CancellationTokenSource();
                stop.Cancel();
                Throws<OperationCanceledException>(() => new LargeFileFinder().Scan(folder, 1000, "", stop.Token));
            });
            Test("handle-verified delete of an unchanged file", () =>
            {
                var folder = Under(root, "delete");
                var file = Path.Combine(folder, "safe.tmp");
                File.WriteAllText(file, "content");
                True(FileSafety.Probe(file, out var identity));
                True(FileSafety.Delete(folder, file, identity));
                True(!File.Exists(file));
            });
            Test("replacement, write lock and scope changes are skipped", () =>
            {
                var folder = Under(root, "race");
                var file = Path.Combine(folder, "verify.tmp");
                File.WriteAllText(file, "old");
                True(FileSafety.Probe(file, out var identity));
                File.WriteAllText(file, "new longer");
                Equal(false, FileSafety.Delete(folder, file, identity));
                True(FileSafety.Probe(file, out var live));
                using (var held = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    Equal(false, FileSafety.Delete(folder, file, live));
                Equal(false, FileSafety.Delete(Under(root, "other"), file, live));
                True(File.Exists(file));
            });
            Test("hard links and duplicate identity", () =>
            {
                var folder = Under(root, "links");
                var file = Path.Combine(folder, "original.tmp");
                var link = Path.Combine(folder, "duplicate.tmp");
                File.WriteAllText(file, "shared");
                if (!CreateHardLink(link, file, IntPtr.Zero)) { Skip("Hard links unavailable on fixture volume."); return; }
                try
                {
                    True(FileSafety.Probe(file, out var identity) && identity.Links == 2);
                    True(FileSafety.Probe(link, out var second) && second.Key == identity.Key);
                    Equal(false, FileSafety.Delete(folder, file, identity));
                }
                finally { File.Delete(link); }
            });
            Test("reparse points stay outside scan and deletion scope", () =>
            {
                var inside = Under(root, "inside");
                var outside = Under(root, "outside");
                var target = Path.Combine(outside, "untouched.tmp");
                File.WriteAllText(target, "fixture");
                var link = Path.Combine(inside, "link");
                bool symbolic = CreateSymbolicLink(link, outside, 1 | 2);
                if (!symbolic)
                {
                    var shell = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                        Arguments = "/d /c mklink /J \"" + link + "\" \"" + outside + "\"",
                        UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                    };
                    using var junction = System.Diagnostics.Process.Start(shell)!;
                    junction.WaitForExit();
                    if (junction.ExitCode != 0) { Skip("Neither link nor junction creation is permitted."); return; }
                }
                try
                {
                    Equal(false, FileSafety.VerifyDirectory(link));
                    Equal(0, new LargeFileFinder().Scan(inside, 1, "", CancellationToken.None).Files.Count);
                    True(FileSafety.Probe(target, out var identity));
                    Throws<IOException>(() => FileSafety.Delete(inside, Path.Combine(link, "untouched.tmp"), identity));
                    True(File.Exists(target));
                }
                finally { Directory.Delete(link, false); }
            });
            Test("missing scheduled task and permission errors are distinguished", () =>
            {
                True(ScheduleService.IsMissingTask(new FileNotFoundException()));
                True(ScheduleService.IsMissingTask(new COMException(null, unchecked((int)0x8004130F))));
                True(!ScheduleService.IsMissingTask(new UnauthorizedAccessException()));
            });
            Test("cleanup policy, dry run, confirmation scope and scheduled age", () =>
            {
                var folder = Under(root, "cleaner");
                var first = Path.Combine(folder, "first.tmp");
                var recent = Path.Combine(folder, "recent.tmp");
                var protectedFile = Path.Combine(folder, "document.pdf");
                var tooYoung = Path.Combine(folder, "unscheduled.tmp");
                foreach (var file in new[] { first, recent, protectedFile, tooYoung }) File.WriteAllText(file, "12345");
                File.SetLastWriteTimeUtc(first, DateTime.UtcNow.AddDays(-10));
                File.SetLastWriteTimeUtc(protectedFile, DateTime.UtcNow.AddDays(-10));
                File.SetLastWriteTimeUtc(tooYoung, DateTime.UtcNow.AddDays(-2));
                var cleaner = new Cleaner(folder);
                var scan = cleaner.Scan(CancellationToken.None, scheduled: true);
                Equal(1, scan.Groups.Count);
                Equal(1L, scan.Groups.Single().Count);
                Equal(5UL, scan.Groups.Single().Bytes);
                True(File.Exists(first));
                var none = cleaner.Clean(scan, Array.Empty<CleanupKind>(), false, CancellationToken.None, scheduled: true);
                Equal(0L, none.Removed);
                var result = cleaner.Clean(scan, new[] { CleanupKind.UserTemp }, false, CancellationToken.None, scheduled: true);
                Equal(1L, result.Removed);
                Equal(5UL, result.LogicalBytes);
                True(!File.Exists(first) && File.Exists(recent) && File.Exists(protectedFile) && File.Exists(tooYoung));
            });
        }
        finally
        {
            // This process creates every fixture below a newly allocated, verified temp root.
            if (Policy.Inside(Policy.Canonical(Path.GetTempPath()), Policy.Canonical(root)) &&
                Path.GetFileName(root).StartsWith("AKASS-Tests-", StringComparison.Ordinal) &&
                FileSafety.VerifyDirectory(root)) Directory.Delete(root, true);
        }
        Console.WriteLine(failures.Count + " failed; " + passed + " passed; " + skipped + " skipped");
        return failures.Count == 0 ? 0 : 1;
    }

    private static string Under(string parent, string name) => Directory.CreateDirectory(Path.Combine(parent, name)).FullName;

    private static void Test(string name, Action body)
    {
        int before = skipped;
        try { body(); if (before == skipped) { passed++; Console.WriteLine("PASS " + name); } }
        catch (Exception e) { failures.Add(name); Console.WriteLine("FAIL " + name + ": " + e); }
    }
    private static void Skip(string reason) { skipped++; Console.WriteLine("SKIP " + reason); }
    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException("Expected " + expected + ", received " + actual);
    }
    private static void True(bool condition) => Equal(true, condition);
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string link, string file, IntPtr attributes);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CreateSymbolicLink(string link, string target, int flags);
}
