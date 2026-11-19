using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Akass.Optimizer;

internal static class FileSafety
{
    internal static void MarkInternetDownload(string path, Uri source)
    {
        const uint writeAccess = 0x40000000;
        const uint createAlways = 2;
        using var handle = CreateFile(@"\\?\" + path + ":Zone.Identifier:$DATA", writeAccess, 0, IntPtr.Zero,
            createAlways, 0, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        using var writer = new StreamWriter(new FileStream(handle, FileAccess.Write));
        writer.Write("[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=" + source.AbsoluteUri + "\r\n");
    }

    internal struct Identity
    {
        public uint Volume;
        public ulong Index, Length, Modified, Created;
        public uint Links;
        public string Key => Volume.ToString("X8") + ":" + Index.ToString("X16");
        public bool Matches(Identity other) =>
            Volume == other.Volume && Index == other.Index && Length == other.Length &&
            Modified == other.Modified && Created == other.Created && Links == 1 && other.Links == 1;
    }

    public static bool ExpectedFailure(Exception e) =>
        e is IOException || e is UnauthorizedAccessException || e is ArgumentException ||
        e is Win32Exception || e is System.Security.SecurityException || e is NotSupportedException;

    public static bool Probe(string path, out Identity identity)
    {
        using var handle = Open(path, 0x80, 7, false);
        return TryInspect(handle, path, false, out identity);
    }

    public static bool VerifyDirectory(string path)
    {
        try { using var chain = new DirectoryChain(path); return true; }
        catch (Exception e) when (ExpectedFailure(e)) { return false; }
    }

    public static bool Delete(string root, string path, Identity expected)
    {
        if (!Policy.Inside(root, path)) return false;
        using var chain = new DirectoryChain(Path.GetDirectoryName(path)!);
        using var handle = Open(path, 0x10000 | 0x80, 1, false);
        if (!TryInspect(handle, path, false, out var actual) || !expected.Matches(actual)) return false;
        var disposition = new FileDisposition { Delete = true };
        if (!SetFileInformationByHandle(handle, 4, ref disposition, 1))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return true;
    }

    internal sealed class DirectoryChain : IDisposable
    {
        private readonly List<SafeFileHandle> handles = new List<SafeFileHandle>();

        public DirectoryChain(string directory)
        {
            directory = Policy.Canonical(directory);
            var current = Path.GetPathRoot(directory)!;
            try
            {
                Hold(current);
                foreach (var part in directory.Substring(current.Length).Split(
                    new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    current = Path.Combine(current, part);
                    Hold(current);
                }
            }
            catch { Dispose(); throw; }
        }

        private void Hold(string path)
        {
            var handle = Open(path, 0x80, 3, true);
            handles.Add(handle);
            if (!TryInspect(handle, path, true, out _) ||
                (GetFileInformationByHandleEx(handle, 23, out uint caseFlags, 4) && (caseFlags & 1) != 0))
                throw new IOException("A directory was inaccessible or could not be verified.");
        }

        public void Dispose()
        {
            for (int i = handles.Count - 1; i >= 0; i--) handles[i].Dispose();
        }
    }

    private static SafeFileHandle Open(string path, uint access, uint share, bool directory) =>
        CreateFile(@"\\?\" + path, access, share, IntPtr.Zero, 3,
            0x00200000 | (directory ? 0x02000000u : 0), IntPtr.Zero);

    private static bool TryInspect(SafeFileHandle handle, string path, bool directory, out Identity identity)
    {
        identity = default;
        if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var data) ||
            (data.Attributes & 0x400) != 0 || ((data.Attributes & 0x10) != 0) != directory) return false;
        var actual = new StringBuilder(Math.Min(32768, path.Length + 64));
        var size = GetFinalPathNameByHandle(handle, actual, (uint)actual.Capacity, 0);
        if (size == 0 || size >= actual.Capacity) return false;
        // DirectoryChain rejects case-sensitive directories before mutation.
        if (!string.Equals(actual.ToString().TrimEnd('\\'), (@"\\?\" + path).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            return false;
        identity = new Identity
        {
            Volume = data.Volume,
            Index = ((ulong)data.IndexHigh << 32) | data.IndexLow,
            Length = ((ulong)data.SizeHigh << 32) | data.SizeLow,
            Modified = ((ulong)data.ModifiedHigh << 32) | data.ModifiedLow,
            Created = ((ulong)data.CreatedHigh << 32) | data.CreatedLow,
            Links = data.Links
        };
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInfoByHandle
    {
        public uint Attributes, CreatedLow, CreatedHigh, AccessLow, AccessHigh, ModifiedLow, ModifiedHigh,
            Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDisposition { [MarshalAs(UnmanagedType.U1)] public bool Delete; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string file, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInfoByHandle info);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint length, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int kind, out uint info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int kind, ref FileDisposition info, uint size);
}
