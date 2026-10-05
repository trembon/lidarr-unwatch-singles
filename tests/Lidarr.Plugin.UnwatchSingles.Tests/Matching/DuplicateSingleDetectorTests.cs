using NzbDrone.Core.Plugins.UnwatchSingles.Matching;

namespace Lidarr.Plugin.UnwatchSingles.Tests.Matching;

public sealed class DuplicateSingleDetectorTests
{
    private readonly DuplicateSingleDetector _detector = new();

    [Fact]
    public void IdenticalRecordingIdIsAnExactMatchEvenWhenTitlesAndDurationsDiffer()
    {
        var single = Single(Track(1, "same-recording", "Album version", 200_000));
        var target = Album(Track(2, "SAME-RECORDING", "Radio edit", 180_000));

        var result = _detector.Analyze(single, [target]);

        Assert.True(result.IsDuplicate);
        Assert.Equal(DuplicateConfidence.RecordingId, result.Confidence);
        Assert.Equal(TrackMatchType.RecordingId, Assert.Single(result.TrackMatches).MatchType);
    }

    [Fact]
    public void DifferentRecordingIdsCanMatchByTitleAndDuration()
    {
        var single = Single(Track(1, "recording-a", "Song", 200_000));
        var target = Album(Track(2, "recording-b", "SONG!", 202_000));

        var result = _detector.Analyze(single, [target]);

        Assert.True(result.IsDuplicate);
        Assert.Equal(DuplicateConfidence.TitleAndDuration, result.Confidence);
        Assert.Equal(TrackMatchType.TitleAndDuration, Assert.Single(result.TrackMatches).MatchType);
        Assert.Equal(2_000, result.TrackMatches[0].DurationDifferenceMilliseconds);
    }

    [Fact]
    public void RecordingIdMatchIsPreferredAcrossEditions()
    {
        var single = Single(Track(1, "recording-a", "Song", 200_000));
        var titleDurationEdition = Album(Track(2, "recording-b", "Song", 200_000));
        var recordingIdEdition = new ReleaseSnapshot(12, 120, 7, "Artist", "Exact edition", "EP",
            true, true, 1, [Track(3, "recording-a", "Different title", 180_000)]);

        var result = _detector.Analyze(single, [titleDurationEdition, recordingIdEdition]);

        Assert.True(result.IsDuplicate);
        Assert.Equal(120, result.MatchingRelease!.ReleaseId);
        Assert.Equal(DuplicateConfidence.RecordingId, result.Confidence);
    }

    [Fact]
    public void BestConfidentEditionUsesTheMostRecordingIdMatches()
    {
        var single = Single(
            Track(1, "recording-a", "Song A", 200_000),
            Track(2, "recording-b", "Song B", 220_000));
        var titleDurationEdition = Album(
            Track(3, "recording-c", "Song A", 200_000),
            Track(4, "recording-d", "Song B", 220_000));
        var strongerEdition = new ReleaseSnapshot(12, 120, 7, "Artist", "Stronger edition", "EP",
            true, true, 2,
            [Track(5, "recording-a", "Alternate title", 150_000), Track(6, "recording-e", "Song B", 220_000)]);

        var result = _detector.Analyze(single, [titleDurationEdition, strongerEdition]);

        Assert.True(result.IsDuplicate);
        Assert.Equal(120, result.MatchingRelease!.ReleaseId);
        Assert.Equal(1, result.TrackMatches.Count(match => match.MatchType == TrackMatchType.RecordingId));
    }

    [Fact]
    public void DurationToleranceIsInclusiveAtBothBoundaries()
    {
        var single = Single(Track(1, null, "Song", 200_000));
        var shorter = Album(Track(2, null, "Song", 197_000));
        var longer = Ep(Track(3, null, "Song", 203_000));

        Assert.True(_detector.Analyze(single, [shorter]).IsDuplicate);
        Assert.True(_detector.Analyze(single, [longer]).IsDuplicate);
    }

    [Fact]
    public void SameTitleOutsideDurationToleranceIsOnlyAmbiguous()
    {
        var single = Single(Track(1, null, "Song", 200_000));
        var target = Album(Track(2, null, "Song", 203_001));

        var result = _detector.Analyze(single, [target]);

        Assert.False(result.IsDuplicate);
        Assert.Equal(DuplicateConfidence.Ambiguous, result.Confidence);
        Assert.Equal(TrackMatchType.TitleOnly, Assert.Single(result.TrackMatches).MatchType);
        Assert.Equal(3_001, result.TrackMatches[0].DurationDifferenceMilliseconds);
    }

    [Fact]
    public void MissingDurationLeavesAComparableTitleAmbiguous()
    {
        var single = Single(Track(1, null, "Song", 0));
        var target = Album(Track(2, null, "Song", 200_000));

        var result = _detector.Analyze(single, [target]);

        Assert.False(result.IsDuplicate);
        Assert.Equal(DuplicateConfidence.Ambiguous, result.Confidence);
        Assert.Null(result.TrackMatches[0].DurationDifferenceMilliseconds);
    }

    [Fact]
    public void DifferentTitlesDoNotMatch()
    {
        var single = Single(Track(1, null, "Song One", 200_000));
        var target = Album(Track(2, null, "Song Two", 200_000));

        var result = _detector.Analyze(single, [target]);

        Assert.False(result.IsDuplicate);
        Assert.Equal(DuplicateConfidence.None, result.Confidence);
    }

    [Fact]
    public void UnicodeCaseWhitespaceAndPunctuationAreNormalized()
    {
        var single = Single(Track(1, null, "  Ｃafé — SONG! ", 200_000));
        var target = Album(Track(2, null, "café song", 200_000));

        Assert.True(_detector.Analyze(single, [target]).IsDuplicate);
    }

    [Theory]
    [InlineData("Song (Acoustic)", "Song")]
    [InlineData("Song (Live)", "Song")]
    [InlineData("Song (Remix)", "Song")]
    public void MeaningfulWordsAreNotRemoved(string singleTitle, string targetTitle)
    {
        var single = Single(Track(1, null, singleTitle, 200_000));
        var target = Album(Track(2, null, targetTitle, 200_000));

        Assert.False(_detector.Analyze(single, [target]).IsDuplicate);
    }

    [Fact]
    public void NonpositiveDurationDoesNotEnableFallbackMatching()
    {
        var single = Single(Track(1, null, "Song", 200_000));
        var target = Album(Track(2, null, "Song", -1));

        Assert.Equal(DuplicateConfidence.Ambiguous, _detector.Analyze(single, [target]).Confidence);
    }

    [Fact]
    public void SameArtistSingleTrackCanMatchAnAlbumOrAnEp()
    {
        var single = Single(Track(1, "same-recording", "Song", 200_000));

        Assert.True(_detector.Analyze(single,
            [Album(Track(2, "same-recording", "Song", 200_000))]).IsDuplicate);
        Assert.True(_detector.Analyze(single,
            [Ep(Track(3, "same-recording", "Song", 200_000))]).IsDuplicate);
    }

    [Fact]
    public void ATrackOnAnotherSingleDoesNotQualify()
    {
        var single = Single(Track(1, "same-recording", "Song", 200_000));
        var otherSingle = new ReleaseSnapshot(12, 120, single.ArtistId, single.ArtistName,
            "Other single", "Single", true, true, 1,
            [Track(2, "same-recording", "Song", 200_000)]);

        Assert.False(_detector.Analyze(single, [otherSingle]).IsDuplicate);
    }

    [Fact]
    public void EveryTrackMustMatchOnTheSameTargetEdition()
    {
        var single = Single(
            Track(1, "song-a", "Song A", 200_000),
            Track(2, "song-b", "Song B", 220_000));
        var albumWithOneTrack = Album(Track(3, "song-a", "Song A", 200_000));
        var splitAlbum = Album(Track(4, "song-b", "Song B", 220_000), albumId: 11);

        var result = _detector.Analyze(single, [albumWithOneTrack, splitAlbum]);

        Assert.False(result.IsDuplicate);
        Assert.Equal(DuplicateConfidence.None, result.Confidence);
    }

    [Fact]
    public void MultiTrackSingleUsesACompleteOneToOneAssignment()
    {
        var single = Single(
            Track(1, "shared", "First", 200_000),
            Track(2, "shared", "Second", 220_000));
        var target = Album(
            Track(3, "shared", "Different name one", 190_000),
            Track(4, "shared", "Different name two", 210_000));

        var result = _detector.Analyze(single, [target]);

        Assert.True(result.IsDuplicate);
        Assert.Equal(2, result.TrackMatches.Count);
        Assert.Equal(2, result.TrackMatches.Select(match => match.MatchingTrack!.Id).Distinct().Count());
    }

    [Fact]
    public void OneToOneAssignmentKeepsRecordingIdMatchesWhenFallbacksCouldDisplaceThem()
    {
        var single = Single(
            Track(1, "recording-a", "Song", 200_000),
            Track(2, "recording-b", "Song", 200_000));
        var target = Album(
            Track(3, "recording-a", "Song", 200_000),
            Track(4, "recording-c", "Song", 200_000));

        var result = _detector.Analyze(single, [target]);

        Assert.True(result.IsDuplicate);
        Assert.Equal(1, result.TrackMatches.Count(match => match.MatchType == TrackMatchType.RecordingId));
        Assert.Equal(3, result.TrackMatches[0].MatchingTrack!.Id);
    }

    [Fact]
    public void OneTargetTrackCannotRepresentTwoSingleTracks()
    {
        var single = Single(
            Track(1, "one-recording", "Song", 200_000),
            Track(2, "one-recording", "Song", 200_000));
        var target = Album(Track(3, "one-recording", "Song", 200_000));

        var result = _detector.Analyze(single, [target]);

        Assert.False(result.IsDuplicate);
        Assert.Equal(DuplicateConfidence.None, result.Confidence);
    }

    [Fact]
    public void AlreadyUnmonitoredOrUnselectedEditionsAreIgnored()
    {
        var single = Single(Track(1, "same-recording", "Song", 200_000));
        var unselectedAlbumEdition = Album(Track(2, "same-recording", "Song", 200_000))
            with { EditionMonitored = false };

        Assert.False(_detector.Analyze(single with { AlbumMonitored = false }, [unselectedAlbumEdition]).IsDuplicate);
        Assert.False(_detector.Analyze(single with { EditionMonitored = false }, [unselectedAlbumEdition]).IsDuplicate);
    }

    [Fact]
    public void IncompleteTrackListsAndDifferentArtistsAreIgnored()
    {
        var single = Single(Track(1, "same-recording", "Song", 200_000));
        var incompleteAlbum = Album(Track(2, "same-recording", "Song", 200_000))
            with { ExpectedTrackCount = 2 };
        var otherArtistAlbum = Album(Track(3, "same-recording", "Song", 200_000))
            with { ArtistId = single.ArtistId + 1 };

        Assert.False(_detector.Analyze(single, [incompleteAlbum, otherArtistAlbum]).IsDuplicate);
        Assert.False(_detector.Analyze(single with { ExpectedTrackCount = 2 }, [Album(Track(4, "same-recording", "Song", 200_000))]).IsDuplicate);
    }

    [Fact]
    public void ComparisonReleaseMustBelongToAnAlbumOrAnEp()
    {
        var single = Single(Track(1, "same-recording", "Song", 200_000));
        var compilation = Album(Track(2, "same-recording", "Song", 200_000))
            with { ReleaseType = "Compilation" };

        Assert.False(_detector.Analyze(single, [compilation]).IsDuplicate);
    }

    [Fact]
    public void RecordingIdFallbackRequiresAtLeastOneAvailableId()
    {
        var single = Single(Track(1, " ", "Song", 200_000));
        var target = Album(Track(2, "\t", "song", 200_000));

        Assert.True(_detector.Analyze(single, [target]).IsDuplicate);
        Assert.Equal(TrackMatchType.TitleAndDuration,
            _detector.Analyze(single, [target]).TrackMatches[0].MatchType);
    }

    private static ReleaseSnapshot Single(params TrackSnapshot[] tracks) =>
        new(10, 100, 7, "Artist", "Single", "Single", true, true, tracks.Length, tracks);

    private static ReleaseSnapshot Album(TrackSnapshot track, int albumId = 11) =>
        new(albumId, albumId * 10, 7, "Artist", "Album", "Album", true, true, 1, [track]);

    private static ReleaseSnapshot Album(TrackSnapshot first, TrackSnapshot second, int albumId = 11) =>
        new(albumId, albumId * 10, 7, "Artist", "Album", "Album", true, true, 2, [first, second]);

    private static ReleaseSnapshot Ep(TrackSnapshot track) =>
        new(12, 120, 7, "Artist", "EP", "EP", true, true, 1, [track]);

    private static TrackSnapshot Track(int id, string? recordingId, string title, int durationMilliseconds) =>
        new(id, recordingId, title, durationMilliseconds);
}
