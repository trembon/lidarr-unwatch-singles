namespace NzbDrone.Core.Plugins.UnwatchSingles.Scanning;

/// <summary>The effective scan settings for one Lidarr metadata provider.</summary>
public sealed record PluginScanSettings(
    bool Enabled,
    bool DryRun,
    bool AutomaticScanningEnabled,
    bool ScheduledScanningEnabled,
    int ScheduledIntervalHours,
    int DurationToleranceMilliseconds,
    int ProviderId = 0);

public interface IUnwatchSinglesConfigurationProvider
{
    PluginScanSettings? GetConfiguration();
}
