using NzbDrone.Core.Plugins.UnwatchSingles.Matching;
using NzbDrone.Core.Plugins.UnwatchSingles.Scanning;

namespace Lidarr.Plugin.UnwatchSingles.Tests.Scanning;

public sealed class DuplicateSinglesScannerTests
{
    [Fact]
    public void DryRunReportsMatchesWithoutChangingMonitoringState()
    {
        var library = Library(OneConfidentPair());
        var monitoring = new RecordingMonitoringService(library);
        var scanner = Scanner(library, monitoring, Configuration());

        var summary = scanner.Scan(forceDryRun: true);

        Assert.Equal(1, summary.DuplicatesFound);
        Assert.Equal(1, summary.WouldUnmonitor);
        Assert.Equal(1, summary.Unchanged);
        Assert.Equal(0, summary.Unmonitored);
        Assert.Empty(monitoring.ChangedAlbumIds);
    }

    [Fact]
    public void DisabledProviderDoesNotInspectOrChangeTheLibrary()
    {
        var library = Library(OneConfidentPair());
        var monitoring = new RecordingMonitoringService(library);
        var scanner = Scanner(library, monitoring, Configuration(enabled: false));

        var summary = scanner.Scan();

        Assert.True(summary.IsDisabled);
        Assert.Equal(0, library.ArtistIdReadCount);
        Assert.Empty(monitoring.ChangedAlbumIds);
    }

    [Fact]
    public void ConfidentMatchChangesOnlyTheSingleAndRepeatedScansAreIdempotent()
    {
        var library = Library(OneConfidentPair());
        var monitoring = new RecordingMonitoringService(library);
        var scanner = Scanner(library, monitoring, Configuration());

        var firstScan = scanner.Scan();
        var secondScan = scanner.Scan();

        Assert.Equal(1, firstScan.Unmonitored);
        Assert.Equal(new[] { 10 }, monitoring.ChangedAlbumIds);
        Assert.Equal(0, secondScan.Unmonitored);
        Assert.Equal(0, secondScan.SinglesInspected);
        Assert.True(library.Current.Single(release => release.AlbumId == 10).AlbumMonitored is false);
        Assert.True(library.Current.Single(release => release.AlbumId == 11).AlbumMonitored);
        Assert.All(library.Current, release => Assert.True(release.EditionMonitored));
    }

    [Fact]
    public void ArtistFilterOnlyInspectsTheRequestedArtist()
    {
        var library = Library(OneConfidentPair(artistId: 7), OneConfidentPair(artistId: 8, singleAlbumId: 20, targetAlbumId: 21));
        var monitoring = new RecordingMonitoringService(library);
        var scanner = Scanner(library, monitoring, Configuration());

        var summary = scanner.Scan(artistId: 8);

        Assert.Equal(1, summary.SinglesInspected);
        Assert.Equal(new[] { 20 }, monitoring.ChangedAlbumIds);
    }

    [Fact]
    public void AmbiguousMatchesAreLoggedAndRemainMonitored()
    {
        var releases = new[]
        {
            Edition(10, 7, "Single", "Song", "record-a", 200_000),
            Edition(11, 7, "Album", "Song", "record-b", 203_001)
        };
        var library = Library(releases);
        var monitoring = new RecordingMonitoringService(library);
        var logger = new RecordingLogger();
        var scanner = Scanner(library, monitoring, Configuration(), logger);

        var summary = scanner.Scan();

        Assert.Equal(1, summary.AmbiguousSingles);
        Assert.Equal(1, summary.Unchanged);
        Assert.Empty(monitoring.ChangedAlbumIds);
        Assert.Contains(logger.Warnings, message => message.Contains("Possible duplicate Single", StringComparison.Ordinal));
    }

    [Fact]
    public void FailingArtistDoesNotPreventTheRemainingArtistsFromBeingScanned()
    {
        var library = Library(OneConfidentPair(artistId: 7), OneConfidentPair(artistId: 8, singleAlbumId: 20, targetAlbumId: 21));
        library.FailingArtistId = 7;
        var monitoring = new RecordingMonitoringService(library);
        var logger = new RecordingLogger();
        var scanner = Scanner(library, monitoring, Configuration(), logger);

        var summary = scanner.Scan();

        Assert.Equal(1, summary.Failed);
        Assert.Equal(1, summary.Unmonitored);
        Assert.Equal(new[] { 20 }, monitoring.ChangedAlbumIds);
        Assert.Contains(logger.Warnings, message => message.Contains("continuing with other artists", StringComparison.Ordinal));
    }

    [Fact]
    public void AFailedMonitoringWriteDoesNotStopOtherSinglesForThatArtist()
    {
        var releases = OneConfidentPair()
            .Concat(OneConfidentPair(singleAlbumId: 20, targetAlbumId: 21))
            .ToArray();
        var library = Library(releases);
        var monitoring = new RecordingMonitoringService(library) { FailingAlbumId = 10 };
        var scanner = Scanner(library, monitoring, Configuration());

        var summary = scanner.Scan();

        Assert.Equal(1, summary.Failed);
        Assert.Equal(1, summary.Unmonitored);
        Assert.Equal(new[] { 20 }, monitoring.ChangedAlbumIds);
    }

    [Fact]
    public async Task ConcurrentScanRequestsAreSerialized()
    {
        var library = Library(OneConfidentPair());
        using var initialReadStarted = new ManualResetEventSlim();
        using var releaseInitialRead = new ManualResetEventSlim();
        library.OnFirstRead = () =>
        {
            initialReadStarted.Set();
            releaseInitialRead.Wait(TimeSpan.FromSeconds(5));
        };
        var monitoring = new RecordingMonitoringService(library);
        var scanner = Scanner(library, monitoring, Configuration());

        var firstScan = Task.Run(() => scanner.Scan(forceDryRun: true));
        Assert.True(await Task.Run(() => initialReadStarted.Wait(TimeSpan.FromSeconds(5))));
        var secondScan = Task.Run(() => scanner.Scan(forceDryRun: true));
        releaseInitialRead.Set();

        await Task.WhenAll(firstScan, secondScan);

        Assert.Equal(2, firstScan.Result.DuplicatesFound + secondScan.Result.DuplicatesFound);
        Assert.Empty(monitoring.ChangedAlbumIds);
    }

    private static DuplicateSinglesScanner Scanner(
        RecordingLibrary library,
        RecordingMonitoringService monitoring,
        PluginScanSettings? configuration,
        RecordingLogger? logger = null) =>
        new(library, monitoring, new ConfigurationProvider(configuration), logger ?? new RecordingLogger(),
            new DuplicateSingleDetector());

    private static PluginScanSettings Configuration(bool enabled = true) =>
        new(enabled, false, true, true, 24, 3_000);

    private static RecordingLibrary Library(params IEnumerable<ReleaseSnapshot>[] groups) =>
        new(groups.SelectMany(group => group).ToArray());

    private static ReleaseSnapshot[] OneConfidentPair(int artistId = 7, int singleAlbumId = 10, int targetAlbumId = 11) =>
    [
        Edition(singleAlbumId, artistId, "Single", "Song", "recording", 200_000),
        Edition(targetAlbumId, artistId, "Album", "Different album title", "recording", 200_000)
    ];

    private static ReleaseSnapshot Edition(
        int albumId,
        int artistId,
        string type,
        string trackTitle,
        string? recordingId,
        int duration) =>
        new(albumId, albumId * 10, artistId, $"Artist {artistId}", $"Album {albumId}", type,
            true, true, 1, [new TrackSnapshot(albumId * 100, recordingId, trackTitle, duration)]);

    private sealed class ConfigurationProvider(PluginScanSettings? configuration)
        : IUnwatchSinglesConfigurationProvider
    {
        public PluginScanSettings? GetConfiguration() => configuration;
    }

    private sealed class RecordingLibrary(ReleaseSnapshot[] releases) : IUnwatchSinglesLibrary
    {
        private readonly object _sync = new();
        private readonly List<ReleaseSnapshot> _releases = releases.ToList();
        private int _firstRead = 1;

        public int ArtistIdReadCount { get; private set; }

        public int? FailingArtistId { get; set; }

        public Action? OnFirstRead { get; set; }

        public ReleaseSnapshot[] Current
        {
            get
            {
                lock (_sync)
                {
                    return [.. _releases];
                }
            }
        }

        public IReadOnlyList<int> GetMonitoredArtistIds()
        {
            ArtistIdReadCount++;
            return Current.Where(release => release.ArtistId > 0)
                .Select(release => release.ArtistId).Distinct().Order().ToArray();
        }

        public IReadOnlyList<ReleaseSnapshot> GetReleasesByArtist(int artistId)
        {
            if (Interlocked.Exchange(ref _firstRead, 0) == 1)
            {
                OnFirstRead?.Invoke();
            }

            if (FailingArtistId == artistId)
            {
                throw new InvalidOperationException("test failure");
            }

            return Current.Where(release => release.ArtistId == artistId).ToArray();
        }

        public void SetAlbumMonitored(int albumId, bool monitored)
        {
            lock (_sync)
            {
                for (var index = 0; index < _releases.Count; index++)
                {
                    if (_releases[index].AlbumId == albumId)
                    {
                        _releases[index] = _releases[index] with { AlbumMonitored = monitored };
                    }
                }
            }
        }
    }

    private sealed class RecordingMonitoringService(RecordingLibrary library) : IAlbumMonitoringService
    {
        public List<int> ChangedAlbumIds { get; } = [];

        public int? FailingAlbumId { get; set; }

        public void SetAlbumMonitored(int albumId, bool monitored)
        {
            if (FailingAlbumId == albumId)
            {
                throw new InvalidOperationException("test failure");
            }

            ChangedAlbumIds.Add(albumId);
            library.SetAlbumMonitored(albumId, monitored);
        }
    }

    private sealed class RecordingLogger : IPluginScanLogger
    {
        public List<string> Warnings { get; } = [];

        public void Information(string message)
        {
        }

        public void Warning(string message) => Warnings.Add(message);

        public void Debug(string message)
        {
        }
    }
}
