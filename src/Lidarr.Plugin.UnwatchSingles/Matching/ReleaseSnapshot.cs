namespace NzbDrone.Core.Plugins.UnwatchSingles.Matching;

/// <summary>One monitored edition with its owning release group and full track list.</summary>
public sealed record ReleaseSnapshot(
    int AlbumId,
    int ReleaseId,
    int ArtistId,
    string ArtistName,
    string AlbumTitle,
    string ReleaseType,
    bool AlbumMonitored,
    bool EditionMonitored,
    int ExpectedTrackCount,
    IReadOnlyList<TrackSnapshot> Tracks)
{
    public bool IsSelectedAndMonitored => AlbumMonitored && EditionMonitored;

    public bool HasCompleteTrackList =>
        ExpectedTrackCount > 0 &&
        Tracks.Count == ExpectedTrackCount &&
        Tracks.All(track => track.Id > 0);
}
