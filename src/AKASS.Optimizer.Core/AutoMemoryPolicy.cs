using System;

namespace Akass.Optimizer;

public sealed class AutoMemoryPolicy
{
    public const int DefaultLimitMiB = 512;
    public const int MinimumLimitMiB = 128;
    public const int MaximumLimitMiB = 16384;
    public static readonly TimeSpan IdlePeriod = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan SustainedPeriod = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(10);
    private TimeSpan? lowSince, lastSample, lastAttempt;
    private bool episodeHandled;

    public bool ShouldOptimize(bool enabled, int limitMiB, ulong available, uint load, bool? windowsLow,
        TimeSpan now, TimeSpan? idle)
    {
        var limit = (ulong)Math.Max(MinimumLimitMiB, Math.Min(MaximumLimitMiB, limitMiB)) * Policy.MiB;
        if (!enabled || available >= limit || !(load >= 80 || windowsLow == true))
        {
            lowSince = lastSample = null;
            if (enabled && available >= limit + 128 * Policy.MiB) episodeHandled = false;
            return false;
        }
        if (!lastSample.HasValue || now - lastSample.Value > TimeSpan.FromSeconds(35)) lowSince = now;
        lastSample = now;
        if (episodeHandled || now - lowSince < SustainedPeriod || idle == null || idle < IdlePeriod ||
            (lastAttempt.HasValue && now - lastAttempt.Value < Cooldown)) return false;

        lastAttempt = now;
        episodeHandled = true;
        return true;
    }
}
