namespace NzbDrone.Core.Plugins.UnwatchSingles.Matching;

/// <summary>A Lidarr track reduced to the fields used for safe comparisons.</summary>
public sealed record TrackSnapshot(
    int Id,
    string? RecordingId,
    string? Title,
    int DurationMilliseconds);
