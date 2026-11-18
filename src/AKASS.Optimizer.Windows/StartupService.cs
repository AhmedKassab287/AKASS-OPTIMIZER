using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Akass.Optimizer;

public sealed class StartupEntry
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Source { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool Editable { get; set; }
    internal string Item { get; set; } = "";
}

public sealed class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "AKASS Optimizer";
    private readonly string current = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
    private readonly string common = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
    private readonly string disabled = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AKASS", "Startup backups");

    public IReadOnlyList<StartupEntry> List() => List(out _);

    public IReadOnlyList<StartupEntry> List(out bool incomplete)
    {
        incomplete = false;
        var results = new List<StartupEntry>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(RunKey);
                if (key == null) continue;
                foreach (var name in key.GetValueNames())
                {
                    if (!(key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string command))
                        continue;
                    string? executable = ParseExecutable(command);
                    var prior = results.FirstOrDefault(x => hive == RegistryHive.CurrentUser &&
                        x.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && x.Path == (executable ?? command) &&
                        x.Source == "HKCU 64-bit");
                    if (prior == null)
                        results.Add(new StartupEntry {
                            Name = name, Path = executable ?? command, Publisher = Publisher(executable),
                            Source = (hive == RegistryHive.CurrentUser ? "HKCU " : "HKLM ") +
                                (view == RegistryView.Registry64 ? "64-bit" : "32-bit")
                        });
                }
            }
            catch (Exception e) when (FileSafety.ExpectedFailure(e)) { incomplete = true; }
        }
        incomplete |= !ReadFolder(results, current, "UserStartup", true);
        incomplete |= !ReadFolder(results, common, "CommonStartup", true);
        incomplete |= !ReadFolder(results, disabled, "Backup", false);
        return results.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private bool ReadFolder(List<StartupEntry> list, string folder, string source, bool enabled)
    {
        if (!Directory.Exists(folder)) return true;
        if (!FileSafety.VerifyDirectory(folder)) return false;
        try
        {
            int examined = 0;
            foreach (var link in Directory.EnumerateFiles(folder))
            {
                if (++examined > 2000) return false;
                if (Path.GetFileName(link).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                string? target = Path.GetExtension(link).Equals(".lnk", StringComparison.OrdinalIgnoreCase) ? Shortcut(link) : null;
                var publisher = Publisher(target);
                list.Add(new StartupEntry {
                    Name = Path.GetFileName(link), Item = link, Path = target ?? link, Publisher = publisher,
                    Source = source, Enabled = enabled,
                    Editable = target != null && (source == "UserStartup" || source == "Backup") &&
                        (source == "Backup" || OptionalTarget(target, publisher))
                });
            }
            return true;
        }
        catch (Exception e) when (FileSafety.ExpectedFailure(e)) { return false; }
    }

    public void Change(StartupEntry selected)
    {
        string source = selected.Enabled ? current : disabled;
        string destination = selected.Enabled ? disabled : current;
        if (!selected.Editable || Path.GetFileName(selected.Item) != selected.Name ||
            !string.Equals(Path.GetDirectoryName(selected.Item), source, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetExtension(selected.Item).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("This startup item is read-only.");

        if (!Directory.Exists(destination))
        {
            if (destination != disabled) throw new DirectoryNotFoundException();
            var parent = Path.GetDirectoryName(destination)!;
            var appData = Path.GetDirectoryName(parent)!;
            using (new FileSafety.DirectoryChain(appData)) Directory.CreateDirectory(destination);
        }

        using var from = new FileSafety.DirectoryChain(source);
        using var to = new FileSafety.DirectoryChain(destination);
        if (!FileSafety.Probe(selected.Item, out var item) || item.Links != 1)
            throw new IOException("The startup link changed or could not be verified.");
        var target = Shortcut(selected.Item);
        if (target == null || !string.Equals(target, selected.Path, StringComparison.OrdinalIgnoreCase) ||
            (selected.Enabled && !OptionalTarget(target, Publisher(target))))
            throw new IOException("The startup shortcut target changed.");
        File.Move(selected.Item, Path.Combine(destination, selected.Name));
    }

    public bool LaunchesWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return string.Equals(ParseExecutable(key?.GetValue(AppName) as string ?? ""), LocalExecutable.Path,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    public void SetLaunchWithWindows(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        var existing = key.GetValue(AppName) as string;
        if (existing != null && !string.Equals(ParseExecutable(existing), LocalExecutable.Path,
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A startup entry already belongs to another AKASS Optimizer installation.");
        if (enabled) key.SetValue(AppName, "\"" + LocalExecutable.Path + "\" --tray", RegistryValueKind.String);
        else key.DeleteValue(AppName, false);
    }

    public static string? ParseExecutable(string raw)
    {
        var text = Environment.ExpandEnvironmentVariables(raw.Trim());
        if (text.StartsWith("\"", StringComparison.Ordinal))
        {
            int end = text.IndexOf('\"', 1);
            if (end <= 1) return null;
            text = text.Substring(1, end - 1);
        }
        else
        {
            int end = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (end < 0) return null;
            text = text.Substring(0, end + 4);
        }
        try { return Policy.Canonical(text); }
        catch (Exception e) when (FileSafety.ExpectedFailure(e)) { return null; }
    }

    private static bool OptionalTarget(string target, string publisher)
    {
        if (!target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !FileSafety.Probe(target, out _) ||
            !FileSafety.VerifyDirectory(Path.GetDirectoryName(target)!)) return false;
        var localPrograms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
        bool knownFolder = Policy.Inside(localPrograms, target) ||
            Policy.Inside(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), target) ||
            Policy.Inside(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), target);
        if (!knownFolder || publisher.Length == 0 || publisher.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0)
            return false;
        return !new[] { "antivirus", "defender", "security", "crowdstrike", "sentinel", "sophos",
            "kaspersky", "mcafee", "malwarebytes", "eset", "avast", "bitdefender", "cisco",
            "driver", "management", "norton", "fortinet", "trend micro", "symantec" }
            .Any(word => (target + " " + publisher).IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static string? Shortcut(string path)
    {
        if (!FileSafety.Probe(path, out var item) || item.Links != 1) return null;
        object? shell = null, link = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            link = ((dynamic)shell!).CreateShortcut(path);
            string target = ((dynamic)link).TargetPath;
            return Policy.Canonical(target);
        }
        catch (Exception e) when (FileSafety.ExpectedFailure(e) || e is ExternalException) { return null; }
        finally
        {
            if (link != null && Marshal.IsComObject(link)) Marshal.FinalReleaseComObject(link);
            if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
    }

    private static string Publisher(string? path)
    {
        if (path == null || !FileSafety.VerifyDirectory(Path.GetDirectoryName(path)!) || !FileSafety.Probe(path, out _))
            return "";
        try { return System.Diagnostics.FileVersionInfo.GetVersionInfo(path).CompanyName ?? ""; }
        catch (Exception e) when (FileSafety.ExpectedFailure(e)) { return ""; }
    }
}

internal static class LocalExecutable
{
    public static string Path
    {
        get
        {
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            return process.MainModule?.FileName ?? throw new InvalidOperationException("Executable path unavailable.");
        }
    }
}
