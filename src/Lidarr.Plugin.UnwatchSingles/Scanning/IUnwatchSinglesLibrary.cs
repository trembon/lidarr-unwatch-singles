using NzbDrone.Core.Plugins.UnwatchSingles.Matching;

namespace NzbDrone.Core.Plugins.UnwatchSingles.Scanning;

public interface IUnwatchSinglesLibrary
{
    IReadOnlyList<int> GetMonitoredArtistIds();

    IReadOnlyList<ReleaseSnapshot> GetReleasesByArtist(int artistId);
}

public interface IAlbumMonitoringService
{
    void SetAlbumMonitored(int albumId, bool monitored);
}

public interface IPluginScanLogger
{
    void Information(string message);

    void Warning(string message);

    void Debug(string message);
}
