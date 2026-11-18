using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Akass.Optimizer;

[DataContract]
public sealed class Preferences
{
    [DataMember] public string Language { get; set; } = "en";
    [DataMember] public string Theme { get; set; } = "Dark";
    [DataMember] public bool Tray { get; set; } = true;
    [DataMember] public bool Background { get; set; } = true;
    [DataMember] public int Refresh { get; set; } = 2;
    [DataMember] public bool Notifications { get; set; } = true;
    [DataMember] public bool Advanced { get; set; }
    [DataMember] public int Sensitivity { get; set; } = 2;
    [DataMember] public bool AutoMemory { get; set; }
    [DataMember] public int AutoMemoryLimitMiB { get; set; } = AutoMemoryPolicy.DefaultLimitMiB;
    [DataMember] public string[] CleanupDefaults { get; set; } = new[] { "UserTemp" };
    [DataMember] public bool ScheduledConsent { get; set; }
    [DataMember] public Guid OriginalPlan { get; set; }
    [DataMember] public Guid GamingPlan { get; set; }

    [OnDeserializing]
    private void Defaults(StreamingContext context)
    {
        Language = "en";
        Theme = "Dark";
        Tray = Background = Notifications = true;
        Refresh = Sensitivity = 2;
        AutoMemoryLimitMiB = AutoMemoryPolicy.DefaultLimitMiB;
        CleanupDefaults = new[] { "UserTemp" };
    }

    public void Validate()
    {
        if (Language != "ar" && Language != "tr") Language = "en";
        if (Theme != "Light" && Theme != "System") Theme = "Dark";
        Refresh = Math.Max(1, Math.Min(30, Refresh));
        Sensitivity = Math.Max(1, Math.Min(3, Sensitivity));
        AutoMemoryLimitMiB = Math.Max(AutoMemoryPolicy.MinimumLimitMiB,
            Math.Min(AutoMemoryPolicy.MaximumLimitMiB, AutoMemoryLimitMiB));
        CleanupDefaults = (CleanupDefaults ?? Array.Empty<string>())
            .Where(id => id == "UserTemp" || id == "WindowsTemp" || id == "Chrome" || id == "Edge" ||
                id == "Firefox" || id == "Thumbnails" || id == "Reports" || id == "Dumps")
            .Distinct().ToArray();
    }
}

[DataContract]
public sealed class ActivityRecord
{
    [DataMember] public DateTime Time { get; set; } = DateTime.UtcNow;
    [DataMember] public string Action { get; set; } = "";
    [DataMember] public string Detail { get; set; } = "";
}

public sealed class StateStore
{
    private readonly object gate = new object();
    public string DirectoryPath { get; }
    private string SettingsPath => Path.Combine(DirectoryPath, "settings.json");
    private string HistoryPath => Path.Combine(DirectoryPath, "history.json");

    public StateStore(string? location = null) => DirectoryPath = location ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AKASS", "Optimizer");

    public Preferences ReadSettings()
    {
        var value = Read(SettingsPath, new Preferences());
        value.Validate();
        return value;
    }

    public void Save(Preferences value)
    {
        value.Validate();
        Write(SettingsPath, value);
    }

    public IReadOnlyList<ActivityRecord> History() => Read(HistoryPath, new List<ActivityRecord>());

    public void Log(string action, string detail)
    {
        using (EnterHistory())
        lock (gate)
        {
            var recent = History().Where(x => x.Time >= DateTime.UtcNow.AddDays(-90)).Take(249).ToList();
            recent.Insert(0, new ActivityRecord { Action = action, Detail = detail.Length <= 600 ? detail : detail.Substring(0, 600) });
            Write(HistoryPath, recent);
        }
    }

    public void ClearHistory()
    {
        using (EnterHistory())
        lock (gate) File.Delete(HistoryPath);
    }

    private IDisposable EnterHistory()
    {
        using var hash = SHA256.Create();
        var id = Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(DirectoryPath.ToUpperInvariant())))
            .Replace('/', '_').Replace('+', '-').TrimEnd('=');
        var mutex = new Mutex(false, @"Local\AKASS.Optimizer.History." + id);
        try
        {
            try
            {
                if (!mutex.WaitOne(TimeSpan.FromSeconds(5))) throw new IOException("The activity log is busy.");
            }
            catch (AbandonedMutexException) { }
            return new HistoryLease(mutex);
        }
        catch { mutex.Dispose(); throw; }
    }

    private sealed class HistoryLease : IDisposable
    {
        private readonly Mutex mutex;
        public HistoryLease(Mutex mutex) => this.mutex = mutex;
        public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
    }

    private T Read<T>(string path, T fallback)
    {
        lock (gate)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                if (stream.Length > 2 * 1024 * 1024) return fallback;
                return (T?)new DataContractJsonSerializer(typeof(T)).ReadObject(stream) ?? fallback;
            }
            catch (FileNotFoundException) { return fallback; }
            catch (DirectoryNotFoundException) { return fallback; }
            catch (SerializationException) { return fallback; }
        }
    }

    private void Write<T>(string path, T value)
    {
        lock (gate)
        {
            Directory.CreateDirectory(DirectoryPath);
            string pending = Path.Combine(DirectoryPath, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = File.Create(pending))
                {
                    new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(pending, path, null);
                else File.Move(pending, path);
            }
            finally { if (File.Exists(pending)) File.Delete(pending); }
        }
    }
}
