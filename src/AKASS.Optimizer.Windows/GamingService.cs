using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Akass.Optimizer;

public sealed class PowerPlan
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}

public static class GamingService
{
    public static Guid Active()
    {
        uint result = PowerGetActiveScheme(IntPtr.Zero, out var pointer);
        if (result != 0) throw new Win32Exception((int)result);
        try { return (Guid)Marshal.PtrToStructure(pointer, typeof(Guid))!; }
        finally { LocalFree(pointer); }
    }

    public static IReadOnlyList<PowerPlan> List()
    {
        var results = new List<PowerPlan>();
        for (uint index = 0; index < 128; index++)
        {
            uint size = 16;
            uint error = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 16, index, out var id, ref size);
            if (error == 259) break;
            if (error != 0) throw new Win32Exception((int)error);
            results.Add(new PowerPlan { Id = id, Name = FriendlyName(id) });
        }
        return results;
    }

    private static string FriendlyName(Guid id)
    {
        uint size = 0;
        PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, null, ref size);
        if (size == 0 || size > 65536) return id.ToString();
        var data = new byte[size];
        if (PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, data, ref size) != 0)
            return id.ToString();
        return System.Text.Encoding.Unicode.GetString(data, 0, (int)size).TrimEnd('\0');
    }

    public static void Begin(StateStore store, Preferences preferences, Guid selected)
    {
        if (preferences.OriginalPlan != Guid.Empty) throw new InvalidOperationException("Restore the prior session first.");
        Guid original = Active();
        if (original == selected) return;
        preferences.OriginalPlan = original;
        preferences.GamingPlan = selected;
        try { store.Save(preferences); }
        catch { preferences.OriginalPlan = preferences.GamingPlan = Guid.Empty; throw; }
        Set(selected);
    }

    public static bool Restore(StateStore store, Preferences preferences)
    {
        Guid original = preferences.OriginalPlan;
        Guid gaming = preferences.GamingPlan;
        if (original == Guid.Empty) return false;
        bool changed = Active() == gaming;
        if (changed) Set(original);
        preferences.OriginalPlan = preferences.GamingPlan = Guid.Empty;
        try { store.Save(preferences); }
        catch { preferences.OriginalPlan = original; preferences.GamingPlan = gaming; throw; }
        return changed;
    }

    private static void Set(Guid id)
    {
        uint result = PowerSetActiveScheme(IntPtr.Zero, ref id);
        if (result != 0) throw new Win32Exception((int)result);
    }

    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr key, out IntPtr id);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr key, ref Guid id);
    [DllImport("powrprof.dll")] private static extern uint PowerEnumerate(IntPtr key, IntPtr scheme, IntPtr subgroup,
        uint kind, uint index, out Guid id, ref uint size);
    [DllImport("powrprof.dll")] private static extern uint PowerReadFriendlyName(IntPtr key, ref Guid scheme,
        IntPtr subgroup, IntPtr setting, byte[]? buffer, ref uint size);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
