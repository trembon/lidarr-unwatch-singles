using NzbDrone.Core.Plugins.UnwatchSingles.Scanning;
using NzbDrone.Core.Plugins.UnwatchSingles.Scheduling;

namespace Lidarr.Plugin.UnwatchSingles.Tests.Scheduling;

public sealed class ScheduledScanPlanTests
{
    [Fact]
    public void EnabledScheduleUsesTheConfiguredIntervalAndStartsDueImmediately()
    {
        var now = DateTime.SpecifyKind(new DateTime(2026, 10, 5, 10, 0, 0), DateTimeKind.Utc);

        var plan = ScheduledScanPlan.Create(Configuration(hours: 24), null, null, now);

        Assert.Equal(1_440, plan.IntervalMinutes);
        Assert.Equal(now.AddDays(-1), plan.LastExecution);
        Assert.Equal(plan.LastExecution, plan.LastStartTime);
    }

    [Fact]
    public void ChangingTheIntervalPreservesLastRunTimes()
    {
        var now = DateTime.SpecifyKind(new DateTime(2026, 10, 5, 10, 0, 0), DateTimeKind.Utc);
        var previousLastExecution = now.AddHours(-4);
        var previousLastStart = previousLastExecution.AddMinutes(-1);

        var plan = ScheduledScanPlan.Create(Configuration(hours: 6),
            previousLastExecution, previousLastStart, now);

        Assert.Equal(360, plan.IntervalMinutes);
        Assert.Equal(previousLastExecution, plan.LastExecution);
        Assert.Equal(previousLastStart, plan.LastStartTime);
    }

    [Fact]
    public void RestartAndDisableKeepTaskProgressButTurnSchedulingOff()
    {
        var now = DateTime.SpecifyKind(new DateTime(2026, 10, 5, 10, 0, 0), DateTimeKind.Utc);
        var previousLastExecution = now.AddHours(-26);
        var previousLastStart = previousLastExecution.AddMinutes(-1);

        var plan = ScheduledScanPlan.Create(Configuration(enabled: false, hours: 24),
            previousLastExecution, previousLastStart, now);

        Assert.Equal(0, plan.IntervalMinutes);
        Assert.Equal(previousLastExecution, plan.LastExecution);
        Assert.Equal(previousLastStart, plan.LastStartTime);
    }

    private static PluginScanSettings Configuration(bool enabled = true, int hours = 24) =>
        new(enabled, false, true, true, hours, 3_000);
}
