using NzbDrone.Core.Plugins.UnwatchSingles.Matching;

namespace NzbDrone.Core.Plugins.UnwatchSingles.Scanning;

/// <summary>Coordinates scans and is the only component that changes monitoring state.</summary>
public sealed class DuplicateSinglesScanner
{
    private readonly IUnwatchSinglesLibrary _library;
    private readonly IAlbumMonitoringService _monitoringService;
    private readonly IUnwatchSinglesConfigurationProvider _configurationProvider;
    private readonly IPluginScanLogger _logger;
    private readonly DuplicateSingleDetector _detector;
    private readonly SemaphoreSlim _scanGate = new(1, 1);

    public DuplicateSinglesScanner(
        IUnwatchSinglesLibrary library,
        IAlbumMonitoringService monitoringService,
        IUnwatchSinglesConfigurationProvider configurationProvider,
        IPluginScanLogger logger,
        DuplicateSingleDetector detector)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _monitoringService = monitoringService ?? throw new ArgumentNullException(nameof(monitoringService));
        _configurationProvider = configurationProvider ?? throw new ArgumentNullException(nameof(configurationProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
    }

    public ScanSummary Scan(int? artistId = null, bool forceDryRun = false)
    {
        _scanGate.Wait();

        try
        {
            var configuration = _configurationProvider.GetConfiguration();

            if (configuration is null || !configuration.Enabled)
            {
                return new ScanSummary(0, 0, 0, 0, 0, 0, 0, true, IsDisabled: true);
            }

            var isDryRun = forceDryRun || configuration.DryRun;
            var summary = new ScanSummaryBuilder(isDryRun);
            IReadOnlyList<int> artistIds;

            try
            {
                artistIds = artistId is null
                    ? _library.GetMonitoredArtistIds()
                    : new[] { artistId.Value };
            }
            catch (Exception exception)
            {
                summary.Failed++;
                _logger.Warning($"Unable to list monitored artists: {exception.Message}");
                var failedResult = summary.ToSummary();
                _logger.Information(failedResult.CompletionMessage);
                return failedResult;
            }

            foreach (var currentArtistId in artistIds.Distinct().Order())
            {
                ScanArtist(currentArtistId, configuration, summary);
            }

            var result = summary.ToSummary();
            _logger.Information(result.CompletionMessage);
            return result;
        }
        finally
        {
            _scanGate.Release();
        }
    }

    private void ScanArtist(int artistId, PluginScanSettings configuration, ScanSummaryBuilder summary)
    {
        IReadOnlyList<ReleaseSnapshot> releases;

        try
        {
            releases = _library.GetReleasesByArtist(artistId);
        }
        catch (Exception exception)
        {
            summary.Failed++;
            _logger.Warning($"Unable to load artist {artistId}; continuing with other artists: {exception.Message}");
            return;
        }

        var comparisonReleases = releases
            .Where(release =>
                release.IsSelectedAndMonitored &&
                IsComparisonType(release.ReleaseType))
            .OrderBy(release => release.AlbumId)
            .ThenBy(release => release.ReleaseId)
            .ToArray();

        var singles = releases
            .Where(release =>
                release.IsSelectedAndMonitored &&
                IsSingleType(release.ReleaseType))
            .OrderBy(release => release.AlbumId)
            .ThenBy(release => release.ReleaseId)
            .ToArray();

        foreach (var single in singles)
        {
            summary.SinglesInspected++;

            try
            {
                var result = _detector.Analyze(single, comparisonReleases,
                    configuration.DurationToleranceMilliseconds);

                if (result.Confidence == DuplicateConfidence.Ambiguous)
                {
                    summary.AmbiguousSingles++;
                    summary.Unchanged++;
                    LogAmbiguousMatch(result);
                    continue;
                }

                if (!result.IsDuplicate || result.MatchingRelease is null)
                {
                    summary.Unchanged++;
                    continue;
                }

                summary.DuplicatesFound++;
                LogTrackMatches(result);

                if (summary.IsDryRun)
                {
                    summary.WouldUnmonitor++;
                    summary.Unchanged++;
                    _logger.Information(
                        $"Dry run: would unmonitor Single '{single.AlbumTitle}' by '{single.ArtistName}'; " +
                        $"all {single.Tracks.Count} tracks match '{result.MatchingRelease.AlbumTitle}' " +
                        $"[{result.Confidence}].");
                    continue;
                }

                if (!RevalidateAndUnmonitor(single, result.MatchingRelease, configuration.DurationToleranceMilliseconds))
                {
                    summary.Unchanged++;
                    continue;
                }

                summary.Unmonitored++;
                _logger.Information(
                    $"Unmonitored Single '{single.AlbumTitle}' by '{single.ArtistName}'; " +
                    $"all {single.Tracks.Count} tracks match '{result.MatchingRelease.AlbumTitle}' " +
                    $"[{result.Confidence}].");
            }
            catch (Exception exception)
            {
                summary.Failed++;
                _logger.Warning(
                    $"Failed to analyze Single '{single.AlbumTitle}' by '{single.ArtistName}': {exception.Message}");
            }
        }
    }

    private bool RevalidateAndUnmonitor(
        ReleaseSnapshot originalSingle,
        ReleaseSnapshot originalTarget,
        int durationToleranceMilliseconds)
    {
        var latestReleases = _library.GetReleasesByArtist(originalSingle.ArtistId);
        var latestSingle = latestReleases.FirstOrDefault(release =>
            release.AlbumId == originalSingle.AlbumId &&
            release.ReleaseId == originalSingle.ReleaseId);
        var latestTarget = latestReleases.FirstOrDefault(release =>
            release.AlbumId == originalTarget.AlbumId &&
            release.ReleaseId == originalTarget.ReleaseId);

        if (latestSingle is not { IsSelectedAndMonitored: true } ||
            latestTarget is not { IsSelectedAndMonitored: true } ||
            !IsSingleType(latestSingle.ReleaseType) ||
            !IsComparisonType(latestTarget.ReleaseType))
        {
            return false;
        }

        var latestResult = _detector.Analyze(latestSingle, [latestTarget], durationToleranceMilliseconds);

        if (!latestResult.IsDuplicate || latestResult.MatchingRelease?.ReleaseId != originalTarget.ReleaseId)
        {
            return false;
        }

        _monitoringService.SetAlbumMonitored(originalSingle.AlbumId, false);
        return true;
    }

    private void LogAmbiguousMatch(DuplicateSingleResult result)
    {
        var matchingTracks = result.TrackMatches
            .Where(match => match.MatchType != TrackMatchType.None)
            .Select(match =>
            {
                var difference = match.DurationDifferenceMilliseconds is null
                    ? "duration unavailable"
                    : $"duration differs by {match.DurationDifferenceMilliseconds.Value} ms";
                return $"'{match.SingleTrack.Title}' on '{result.MatchingRelease?.AlbumTitle}' ({difference})";
            });

        _logger.Warning(
            $"Possible duplicate Single '{result.Single.AlbumTitle}' by '{result.Single.ArtistName}': " +
            $"{string.Join("; ", matchingTracks)}. No monitoring state changed.");
    }

    private void LogTrackMatches(DuplicateSingleResult result)
    {
        foreach (var match in result.TrackMatches)
        {
            _logger.Debug(
                $"Single '{result.Single.AlbumTitle}' track '{match.SingleTrack.Title}' matched " +
                $"'{match.MatchingTrack?.Title}' on '{result.MatchingRelease?.AlbumTitle}' " +
                $"using {match.MatchType}; duration difference " +
                $"{match.DurationDifferenceMilliseconds?.ToString() ?? "unavailable"} ms.");
        }
    }

    private static bool IsSingleType(string releaseType) => HasType(releaseType, "Single");

    private static bool IsComparisonType(string releaseType) =>
        HasType(releaseType, "Album") || HasType(releaseType, "EP");

    private static bool HasType(string releaseType, string expected) =>
        releaseType.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(type => type.Equals(expected, StringComparison.OrdinalIgnoreCase));
}
