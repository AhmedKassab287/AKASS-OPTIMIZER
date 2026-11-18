using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml;

namespace Akass.Optimizer;

public sealed class BatteryStatus
{
    public bool? Present { get; set; }
    public int? Percent { get; set; }
    public bool? Charging { get; set; }
    public bool? AcOnline { get; set; }
}

public sealed class BatteryCapacity
{
    public string Name { get; set; } = "";
    public int? Design { get; set; }
    public int? Full { get; set; }
    public double? Wear => Design > 0 && Full > 0 ? Math.Max(0, 1 - Full.Value / (double)Design.Value) : (double?)null;
}

public static class BatteryService
{
    public static BatteryStatus Status()
    {
        var status = new BatteryStatus();
        if (!GetSystemPowerStatus(out var data)) return status;
        status.Present = data.Flags == 255 ? (bool?)null : (data.Flags & 128) == 0;
        status.Percent = data.Percent <= 100 && status.Present != false ? data.Percent : (int?)null;
        status.Charging = status.Present == true ? (data.Flags & 8) != 0 : (bool?)null;
        status.AcOnline = data.Ac <= 1 ? data.Ac == 1 : (bool?)null;
        return status;
    }

    public static IReadOnlyList<BatteryCapacity> Capacity(StateStore store)
    {
        Directory.CreateDirectory(store.DirectoryPath);
        string location = Path.Combine(store.DirectoryPath, Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            var result = RepairService.RunCommand(RepairKind.BatteryReport, location, true);
            if (result.ExitCode != 0 || !File.Exists(location)) return Array.Empty<BatteryCapacity>();
            return ParseReport(location);
        }
        finally { if (File.Exists(location)) File.Delete(location); }
    }

    public static ToolResult Report(string location) => RepairService.RunCommand(RepairKind.BatteryReport, location, false);

    public static IReadOnlyList<BatteryCapacity> ParseReport(string path)
    {
        var limits = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null, MaxCharactersInDocument = 12 * 1024 * 1024 };
        using var reader = XmlReader.Create(path, limits);
        var doc = new XmlDocument { XmlResolver = null };
        doc.Load(reader);
        var nodes = doc.SelectNodes("//*[local-name()='Batteries']/*[local-name()='Battery']");
        if (nodes == null) return Array.Empty<BatteryCapacity>();
        return nodes.Cast<XmlNode>().Select(node => new BatteryCapacity
        {
            Name = Field(node, "Id") ?? Field(node, "Name") ?? "",
            Design = CapacityValue(Field(node, "DesignCapacity")),
            Full = CapacityValue(Field(node, "FullChargeCapacity"))
        }).ToArray();
    }

    private static int? CapacityValue(string? raw) => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value > 0
        ? value : (int?)null;

    private static string? Field(XmlNode node, string name) => node.SelectSingleNode("*[local-name()='" + name + "']")?.InnerText;

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerStatus { public byte Ac, Flags, Percent, Saver; public uint Lifetime, FullLifetime; }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out PowerStatus state);
}
