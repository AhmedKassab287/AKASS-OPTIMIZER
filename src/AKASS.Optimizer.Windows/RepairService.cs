using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Akass.Optimizer;

public enum RepairKind { Sfc, DismScan, DismRestore, FlushDns, BatteryReport, ClearStandby }

public sealed class ToolResult
{
    public int ExitCode { get; }
    public string Output { get; }
    public ToolResult(int code, string output) { ExitCode = code; Output = output; }
}

public static class RepairService
{
    private const int OutputLimit = 400000;

    public static async Task<ToolResult> RunAsync(RepairKind kind, CancellationToken token)
    {
        if (kind == RepairKind.BatteryReport)
            throw new ArgumentException("Use the battery report service.");
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        if (principal.IsInRole(WindowsBuiltInRole.Administrator))
            return await Task.Run(() => RunCommand(kind, null, null), token).ConfigureAwait(false);

        if (!Enum.IsDefined(typeof(RepairKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        string name = "AKASS.Optimizer.Repair." + Guid.NewGuid().ToString("N");
        using var pipe = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var helper = Process.Start(new ProcessStartInfo
        {
            FileName = LocalExecutable.Path, Arguments = "--repair-helper " + name + " " + kind,
            UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden
        }) ?? throw new InvalidOperationException("Could not start the repair helper.");

        Task connected = pipe.WaitForConnectionAsync(token);
        while (!connected.IsCompleted && !helper.HasExited)
            await Task.Delay(250, token).ConfigureAwait(false);
        if (!connected.IsCompleted) throw new IOException("The elevated helper could not connect.");
        await connected.ConfigureAwait(false);
        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint client) || client != (uint)helper.Id)
            throw new IOException("Could not verify the elevated helper.");
        using var Reader = new StreamReader(pipe, Encoding.UTF8);
        string? code = await Reader.ReadLineAsync().ConfigureAwait(false);
        string text = await ReadOutput(Reader).ConfigureAwait(false);
        if (!int.TryParse(code, out int exitCode) || code == null)
            throw new IOException("The repair helper did not return an exit status.");
        return new ToolResult(exitCode, text);
    }

    public static int Helper(string name, string action)
    {
        const string prefix = "AKASS.Optimizer.Repair.";
        if (!name.StartsWith(prefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(name.Substring(prefix.Length), "N", out _) ||
            !Enum.TryParse<RepairKind>(action, false, out var kind) || kind.ToString() != action ||
            kind == RepairKind.BatteryReport)
            return 2;
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) return 3;
        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.None,
            TokenImpersonationLevel.Identification);
        try { pipe.Connect(8000); }
        catch (TimeoutException) { return 4; }
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
        ToolResult result;
        try { result = RunCommand(kind, null, null); }
        catch (Exception error) { result = new ToolResult(-1, error.Message); }
        writer.WriteLine(result.ExitCode);
        writer.Write(result.Output);
        return result.ExitCode;
    }

    internal static ToolResult RunCommand(RepairKind kind, string? reportPath, bool? xml)
    {
        if (kind == RepairKind.ClearStandby) return MemoryMaintenance.ClearStandby();
        var (name, args) = kind switch
        {
            RepairKind.Sfc => ("sfc.exe", "/scannow"),
            RepairKind.DismScan => ("dism.exe", "/Online /Cleanup-Image /ScanHealth"),
            RepairKind.DismRestore => ("dism.exe", "/Online /Cleanup-Image /RestoreHealth /NoRestart"),
            RepairKind.FlushDns => ("ipconfig.exe", "/flushdns"),
            RepairKind.BatteryReport when reportPath != null =>
                ("powercfg.exe", "/batteryreport " + (xml == true ? "/xml " : "") + "/output \"" + ValidateReportPath(reportPath) + "\""),
            _ => throw new ArgumentException("Unsupported operation.")
        };
        var path = Path.Combine(Environment.SystemDirectory, name);
        var launch = new ProcessStartInfo
        {
            FileName = path, Arguments = args, WorkingDirectory = Environment.SystemDirectory,
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        // Windows built-ins may use the console's output encoding. SFC uses UTF-16 when redirected.
        if (kind == RepairKind.Sfc)
            launch.StandardOutputEncoding = Encoding.Unicode;
        using var process = Process.Start(launch) ?? throw new InvalidOperationException("Windows tool could not start.");
        var stdout = ReadOutput(process.StandardOutput);
        var stderr = ReadOutput(process.StandardError);
        Task.WhenAll(stdout, stderr).GetAwaiter().GetResult();
        process.WaitForExit();
        return new ToolResult(process.ExitCode, stdout.Result.Trim('\0') + "\n" + stderr.Result);
    }

    private static string ValidateReportPath(string path)
    {
        var full = Policy.Canonical(path);
        if (full.IndexOf('"') >= 0 || !FileSafety.VerifyDirectory(Path.GetDirectoryName(full)!) ||
            File.Exists(full) || Directory.Exists(full))
            throw new IOException("Choose a new file in an accessible local folder.");
        return full;
    }

    private static async Task<string> ReadOutput(StreamReader reader)
    {
        var buffer = new char[4096];
        var text = new StringBuilder();
        int read;
        bool truncated = false;
        while ((read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
        {
            int remaining = OutputLimit - text.Length;
            if (remaining > 0) text.Append(buffer, 0, Math.Min(read, remaining));
            truncated |= read > remaining;
        }
        if (truncated) text.Append("\n[…]");
        return text.ToString();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);
}
