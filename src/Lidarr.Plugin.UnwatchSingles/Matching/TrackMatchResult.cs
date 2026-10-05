namespace NzbDrone.Core.Plugins.UnwatchSingles.Matching;

public enum TrackMatchType
{
    None,
    TitleOnly,
    TitleAndDuration,
    RecordingId
}

public enum DuplicateConfidence
{
    None,
    Ambiguous,
    TitleAndDuration,
    RecordingId
}

public sealed record TrackMatchResult(
    TrackSnapshot SingleTrack,
    TrackSnapshot? MatchingTrack,
    TrackMatchType MatchType,
    int? DurationDifferenceMilliseconds);

public sealed record DuplicateSingleResult(
    ReleaseSnapshot Single,
    ReleaseSnapshot? MatchingRelease,
    bool IsDuplicate,
    DuplicateConfidence Confidence,
    IReadOnlyList<TrackMatchResult> TrackMatches)
{
    public static DuplicateSingleResult NoMatch(ReleaseSnapshot single) =>
        new(single, null, false, DuplicateConfidence.None,
            single.Tracks.Select(track =>
                new TrackMatchResult(track, null, TrackMatchType.None, null)).ToArray());
}
