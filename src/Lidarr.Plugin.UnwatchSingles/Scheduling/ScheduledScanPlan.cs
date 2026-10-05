using NzbDrone.Core.Plugins.UnwatchSingles.Scanning;

namespace NzbDrone.Core.Plugins.UnwatchSingles.Scheduling;

/// <summary>Pure rules for task enablement, interval changes, and preserved scan timestamps.</summary>
public sealed record ScheduledScanPlan(int IntervalMinutes, DateTime LastExecution, DateTime LastStartTime)
{
    public static ScheduledScanPlan Create(
        PluginScanSettings? configuration,
        DateTime? previousLastExecution,
        DateTime? previousLastStartTime,
        DateTime nowUtc)
    {
        var intervalMinutes = configuration is { Enabled: true, ScheduledScanningEnabled: true }
            ? checked(configuration.ScheduledIntervalHours * 60)
            : 0;

        var lastExecution = previousLastExecution.GetValueOrDefault();
        if (lastExecution == default)
        {
            lastExecution = nowUtc.AddMinutes(-intervalMinutes);
        }

        var lastStartTime = previousLastStartTime.GetValueOrDefault();
        if (lastStartTime == default)
        {
            lastStartTime = lastExecution;
        }

        return new ScheduledScanPlan(intervalMinutes, lastExecution, lastStartTime);
    }
}
