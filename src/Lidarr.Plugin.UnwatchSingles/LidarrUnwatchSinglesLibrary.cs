using NzbDrone.Core.Music;
using NzbDrone.Core.Plugins.UnwatchSingles.Matching;
using NzbDrone.Core.Plugins.UnwatchSingles.Scanning;

namespace NzbDrone.Core.Plugins.UnwatchSingles;

/// <summary>Adapts Lidarr's artist, album, release, and track services to the pure scanner.</summary>
public sealed class LidarrUnwatchSinglesLibrary : IUnwatchSinglesLibrary, IAlbumMonitoringService
{
    private readonly IArtistService _artistService;
    private readonly IAlbumService _albumService;
    private readonly IReleaseService _releaseService;
    private readonly ITrackService _trackService;

    public LidarrUnwatchSinglesLibrary(
        IArtistService artistService,
        IAlbumService albumService,
        IReleaseService releaseService,
        ITrackService trackService)
    {
        _artistService = artistService;
        _albumService = albumService;
        _releaseService = releaseService;
        _trackService = trackService;
    }

    public IReadOnlyList<int> GetMonitoredArtistIds() =>
        _artistService.GetAllArtists()
            .Where(artist => artist.Monitored)
            .Select(artist => artist.Id)
            .Distinct()
            .Order()
            .ToArray();

    public IReadOnlyList<ReleaseSnapshot> GetReleasesByArtist(int artistId)
    {
        var artist = _artistService.GetArtist(artistId);
        var releases = new List<ReleaseSnapshot>();

        foreach (var album in _albumService.GetAlbumsByArtist(artistId)
                     .OrderBy(album => album.Id))
        {
            if (!IsRelevantType(album.AlbumType))
            {
                continue;
            }

            var selectedEditions = _releaseService.GetReleasesByAlbum(album.Id)
                .Where(release => release.Monitored)
                .ToArray();

            // Lidarr normally selects a single edition. If metadata or configuration leaves
            // multiple editions selected, skip this release group rather than mixing editions.
            if (selectedEditions.Length != 1 || !album.Monitored)
            {
                continue;
            }

            var edition = selectedEditions[0];
            var tracks = _trackService.GetTracksByRelease(edition.Id)
                .OrderBy(track => track.Id)
                .Select(track => new TrackSnapshot(
                    track.Id,
                    track.ForeignRecordingId,
                    track.Title,
                    track.Duration))
                .ToArray();

            releases.Add(new ReleaseSnapshot(
                album.Id,
                edition.Id,
                artistId,
                artist.Name,
                album.Title,
                album.AlbumType,
                album.Monitored,
                edition.Monitored,
                edition.TrackCount,
                tracks));
        }

        return releases;
    }

    public void SetAlbumMonitored(int albumId, bool monitored) =>
        _albumService.SetAlbumMonitored(albumId, monitored);

    private static bool IsRelevantType(string albumType) =>
        HasType(albumType, "Single") || HasType(albumType, "Album") || HasType(albumType, "EP");

    private static bool HasType(string albumType, string expected) =>
        albumType.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(type => type.Equals(expected, StringComparison.OrdinalIgnoreCase));
}
