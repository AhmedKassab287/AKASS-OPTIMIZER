using System;
#if !NETFRAMEWORK
using System.Linq;
using LibreHardwareMonitor.Hardware;
#endif

namespace Akass.Optimizer;

public sealed class HardwareSensors : IDisposable
{
#if !NETFRAMEWORK
    private Computer? computer;
    private DateTime retryAfter;
    private readonly object gate = new object();
    private bool disposed;
#endif

    public (double? Cpu, double? Gpu, string Adapter) Read()
    {
#if !NETFRAMEWORK
        lock (gate)
        {
            if (disposed || DateTime.UtcNow < retryAfter) return (null, null, "");
            try
            {
                if (computer == null)
                {
                    computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true };
                    computer.Open();
                }
                double? cpu = null, gpu = null;
                string adapter = "";
                foreach (var device in computer.Hardware)
                {
                    device.Update();
                    var temperatures = device.Sensors.Where(s => s.SensorType == SensorType.Temperature &&
                        s.Value is float value && value > 0 && value < 130).ToArray();
                    bool processor = device.HardwareType == HardwareType.Cpu;
                    if (!processor && device.HardwareType is not (HardwareType.GpuAmd or HardwareType.GpuNvidia or HardwareType.GpuIntel))
                        continue;
                    var preferred = temperatures.Where(s => processor
                        ? HasName(s, "Package", "Tdie", "Tctl")
                        : HasName(s, "GPU Core", "GPU Temperature"));
                    double? value = preferred.DefaultIfEmpty().Max(s => (double?)s?.Value) ??
                        temperatures.DefaultIfEmpty().Max(s => (double?)s?.Value);
                    if (processor && value.HasValue && (!cpu.HasValue || value > cpu)) cpu = value;
                    if (!processor && value.HasValue && (!gpu.HasValue || value > gpu))
                    {
                        gpu = value;
                        adapter = device.Name;
                    }
                }
                return (cpu, gpu, adapter);
            }
            catch (Exception)
            {
                CloseDevice();
                retryAfter = DateTime.UtcNow.AddMinutes(1);
            }
        }
#endif
        return (null, null, "");
    }

#if !NETFRAMEWORK
    private static bool HasName(ISensor sensor, params string[] names) =>
        names.Any(name => sensor.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);

    private void CloseDevice()
    {
        try { computer?.Close(); }
        catch (Exception) { }
        finally { computer = null; }
    }
#endif

    public void Dispose()
    {
#if !NETFRAMEWORK
        lock (gate)
        {
            disposed = true;
            CloseDevice();
        }
#endif
    }
}
