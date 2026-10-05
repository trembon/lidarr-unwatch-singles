using NLog;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music.Events;
using NzbDrone.Core.Plugins.UnwatchSingles.Scanning;

namespace NzbDrone.Core.Plugins.UnwatchSingles.Events;

public sealed class UnwatchSinglesEventHandler :
    IHandle<ArtistRefreshCompleteEvent>,
    IHandle<AlbumImportedEvent>,
    IHandle<AlbumEditedEvent>
{
    private readonly DuplicateSinglesScanner _scanner;
    private readonly IUnwatchSinglesConfigurationProvider _configurationProvider;
    private readonly Logger _logger;

    public UnwatchSinglesEventHandler(
        DuplicateSinglesScanner scanner,
        IUnwatchSinglesConfigurationProvider configurationProvider,
        Logger logger)
    {
        _scanner = scanner;
        _configurationProvider = configurationProvider;
        _logger = logger;
    }

    [EventHandleOrder(EventHandleOrder.Last)]
    public void Handle(ArtistRefreshCompleteEvent message)
    {
        ScanAutomatically(message.Artist.Id);
    }

    [EventHandleOrder(EventHandleOrder.Last)]
    public void Handle(AlbumImportedEvent message)
    {
        if (message.Artist is not null)
        {
            ScanAutomatically(message.Artist.Id);
        }
    }

    [EventHandleOrder(EventHandleOrder.Last)]
    public void Handle(AlbumEditedEvent message)
    {
        // The plugin edits only Single monitoring. Restricting these rescans to Album/EP
        // edits ignores the event caused by our own Single unmonitoring update.
        if (!AutomaticScanPolicy.ShouldScanAfterAlbumEdit(message.Album.AlbumType))
        {
            return;
        }

        ScanAutomatically(message.Album.ArtistId);
    }

    private void ScanAutomatically(int artistId)
    {
        try
        {
            if (_configurationProvider.GetConfiguration() is { Enabled: true, AutomaticScanningEnabled: true })
            {
                _scanner.Scan(artistId);
            }
        }
        catch (Exception exception)
        {
            _logger.Warn(exception, "Unwatch Singles automatic scan failed for artist {0}.", artistId);
        }
    }

}
