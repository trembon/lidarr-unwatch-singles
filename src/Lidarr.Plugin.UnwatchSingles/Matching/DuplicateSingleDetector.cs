namespace NzbDrone.Core.Plugins.UnwatchSingles.Matching;

/// <summary>Finds a complete one-to-one match on a single monitored edition.</summary>
public sealed class DuplicateSingleDetector
{
    public DuplicateSingleResult Analyze(
        ReleaseSnapshot single,
        IEnumerable<ReleaseSnapshot> comparisonReleases,
        int durationToleranceMilliseconds = 3_000)
    {
        ArgumentNullException.ThrowIfNull(single);
        ArgumentNullException.ThrowIfNull(comparisonReleases);
        ArgumentOutOfRangeException.ThrowIfNegative(durationToleranceMilliseconds);

        if (!IsType(single.ReleaseType, "Single") ||
            !single.IsSelectedAndMonitored ||
            !single.HasCompleteTrackList)
        {
            return DuplicateSingleResult.NoMatch(single);
        }

        DuplicateSingleResult? bestConfident = null;
        DuplicateSingleResult? bestAmbiguous = null;

        foreach (var target in comparisonReleases)
        {
            if (target.ArtistId != single.ArtistId ||
                target.AlbumId == single.AlbumId ||
                !target.IsSelectedAndMonitored ||
                !target.HasCompleteTrackList ||
                !IsComparisonType(target.ReleaseType))
            {
                continue;
            }

            var comparisons = BuildComparisonMatrix(single.Tracks, target.Tracks, durationToleranceMilliseconds);
            var confidentAssignment = FindAssignment(comparisons, includeTitleOnly: false);

            if (confidentAssignment is not null)
            {
                var trackMatches = confidentAssignment
                    .Select((targetTrack, sourceIndex) => comparisons[sourceIndex][targetTrack]!)
                    .ToArray();
                var confidence = trackMatches.All(match => match.MatchType == TrackMatchType.RecordingId)
                    ? DuplicateConfidence.RecordingId
                    : DuplicateConfidence.TitleAndDuration;

                var confidentCandidate = new DuplicateSingleResult(single, target, true, confidence, trackMatches);
                if (confidence == DuplicateConfidence.RecordingId)
                {
                    return confidentCandidate;
                }

                if (bestConfident is null ||
                    CountRecordingIdMatches(confidentCandidate) > CountRecordingIdMatches(bestConfident))
                {
                    bestConfident = confidentCandidate;
                }
                continue;
            }

            var ambiguousAssignment = FindAssignment(comparisons, includeTitleOnly: true);

            if (ambiguousAssignment is null)
            {
                continue;
            }

            var ambiguousMatches = ambiguousAssignment
                .Select((targetTrack, sourceIndex) => comparisons[sourceIndex][targetTrack]!)
                .ToArray();
            var candidate = new DuplicateSingleResult(
                single,
                target,
                false,
                DuplicateConfidence.Ambiguous,
                ambiguousMatches);

            if (bestAmbiguous is null ||
                ambiguousMatches.Count(match => match.MatchType == TrackMatchType.TitleOnly) <
                bestAmbiguous.TrackMatches.Count(match => match.MatchType == TrackMatchType.TitleOnly))
            {
                bestAmbiguous = candidate;
            }
        }

        return bestConfident ?? bestAmbiguous ?? DuplicateSingleResult.NoMatch(single);
    }

    private static int CountRecordingIdMatches(DuplicateSingleResult result) =>
        result.TrackMatches.Count(match => match.MatchType == TrackMatchType.RecordingId);

    private static TrackMatchResult?[][] BuildComparisonMatrix(
        IReadOnlyList<TrackSnapshot> singleTracks,
        IReadOnlyList<TrackSnapshot> targetTracks,
        int toleranceMilliseconds)
    {
        var matrix = new TrackMatchResult?[singleTracks.Count][];

        for (var singleIndex = 0; singleIndex < singleTracks.Count; singleIndex++)
        {
            matrix[singleIndex] = new TrackMatchResult?[targetTracks.Count];

            for (var targetIndex = 0; targetIndex < targetTracks.Count; targetIndex++)
            {
                var singleTrack = singleTracks[singleIndex];
                var targetTrack = targetTracks[targetIndex];

                if (HasRecordingId(singleTrack.RecordingId) &&
                    HasRecordingId(targetTrack.RecordingId) &&
                    string.Equals(singleTrack.RecordingId!.Trim(), targetTrack.RecordingId!.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    var idMatchDurationDifference = HasPositiveDurations(singleTrack, targetTrack)
                        ? (int?)DurationDifference(singleTrack, targetTrack)
                        : null;
                    matrix[singleIndex][targetIndex] = new TrackMatchResult(
                        singleTrack, targetTrack, TrackMatchType.RecordingId, idMatchDurationDifference);
                    continue;
                }

                var singleTitle = TrackTitleNormalizer.Normalize(singleTrack.Title);
                var targetTitle = TrackTitleNormalizer.Normalize(targetTrack.Title);

                if (singleTitle.Length == 0 ||
                    !string.Equals(singleTitle, targetTitle, StringComparison.Ordinal))
                {
                    continue;
                }

                var difference = HasPositiveDurations(singleTrack, targetTrack)
                    ? DurationDifference(singleTrack, targetTrack)
                    : (int?)null;
                var matchType = difference is not null && difference <= toleranceMilliseconds
                    ? TrackMatchType.TitleAndDuration
                    : TrackMatchType.TitleOnly;

                matrix[singleIndex][targetIndex] = new TrackMatchResult(
                    singleTrack, targetTrack, matchType, difference);
            }
        }

        return matrix;
    }

    private static int[]? FindAssignment(TrackMatchResult?[][] matrix, bool includeTitleOnly)
    {
        if (matrix.Length == 0)
        {
            return null;
        }

        var sourceCount = matrix.Length;
        var targetCount = matrix[0].Length;
        var sink = sourceCount + targetCount + 1;
        var graph = Enumerable.Range(0, sink + 1)
            .Select(_ => new List<ResidualEdge>())
            .ToArray();
        var candidateEdges = new List<(int Source, int Target, ResidualEdge Edge)>();

        for (var source = 0; source < sourceCount; source++)
        {
            AddEdge(graph, 0, source + 1, 1, 0);

            for (var target = 0; target < targetCount; target++)
            {
                var match = matrix[source][target];

                if (match is null || (!includeTitleOnly && match.MatchType == TrackMatchType.TitleOnly))
                {
                    continue;
                }

                var cost = match.MatchType switch
                {
                    TrackMatchType.RecordingId => 0,
                    TrackMatchType.TitleAndDuration => 1,
                    TrackMatchType.TitleOnly => sourceCount + 1,
                    _ => throw new InvalidOperationException($"Unexpected match type {match.MatchType}.")
                };
                var edge = AddEdge(graph, source + 1, sourceCount + target + 1, 1, cost);
                candidateEdges.Add((source, target, edge));
            }
        }

        for (var target = 0; target < targetCount; target++)
        {
            AddEdge(graph, sourceCount + target + 1, sink, 1, 0);
        }

        for (var flow = 0; flow < sourceCount; flow++)
        {
            var distances = Enumerable.Repeat(int.MaxValue, graph.Length).ToArray();
            var previousNode = Enumerable.Repeat(-1, graph.Length).ToArray();
            var previousEdge = Enumerable.Repeat(-1, graph.Length).ToArray();
            distances[0] = 0;

            // Residual reverse edges have negative costs, so Bellman-Ford finds the
            // lowest-cost augmenting path and preserves the strongest available matches.
            for (var pass = 0; pass < graph.Length - 1; pass++)
            {
                var changed = false;

                for (var node = 0; node < graph.Length; node++)
                {
                    if (distances[node] == int.MaxValue)
                    {
                        continue;
                    }

                    for (var edgeIndex = 0; edgeIndex < graph[node].Count; edgeIndex++)
                    {
                        var edge = graph[node][edgeIndex];
                        if (edge.Capacity == 0 || distances[edge.To] <= distances[node] + edge.Cost)
                        {
                            continue;
                        }

                        distances[edge.To] = distances[node] + edge.Cost;
                        previousNode[edge.To] = node;
                        previousEdge[edge.To] = edgeIndex;
                        changed = true;
                    }
                }

                if (!changed)
                {
                    break;
                }
            }

            if (previousNode[sink] < 0)
            {
                return null;
            }

            for (var node = sink; node != 0; node = previousNode[node])
            {
                var edge = graph[previousNode[node]][previousEdge[node]];
                edge.Capacity--;
                graph[node][edge.ReverseIndex].Capacity++;
            }
        }

        var assignment = Enumerable.Repeat(-1, sourceCount).ToArray();
        foreach (var (source, target, edge) in candidateEdges)
        {
            if (edge.Capacity == 0)
            {
                assignment[source] = target;
            }
        }

        return assignment.All(target => target >= 0) ? assignment : null;
    }

    private static ResidualEdge AddEdge(
        IReadOnlyList<List<ResidualEdge>> graph,
        int from,
        int to,
        int capacity,
        int cost)
    {
        var forward = new ResidualEdge(to, graph[to].Count, capacity, cost);
        var reverse = new ResidualEdge(from, graph[from].Count, 0, -cost);
        graph[from].Add(forward);
        graph[to].Add(reverse);
        return forward;
    }

    private sealed class ResidualEdge(int to, int reverseIndex, int capacity, int cost)
    {
        public int To { get; } = to;

        public int ReverseIndex { get; } = reverseIndex;

        public int Capacity { get; set; } = capacity;

        public int Cost { get; } = cost;
    }

    private static bool IsComparisonType(string releaseType) =>
        IsType(releaseType, "Album") || IsType(releaseType, "EP");

    private static bool IsType(string releaseType, string expected) =>
        releaseType.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(type => type.Equals(expected, StringComparison.OrdinalIgnoreCase));

    private static bool HasRecordingId(string? value) => !string.IsNullOrWhiteSpace(value);

    private static bool HasPositiveDurations(TrackSnapshot first, TrackSnapshot second) =>
        first.DurationMilliseconds > 0 && second.DurationMilliseconds > 0;

    private static int DurationDifference(TrackSnapshot first, TrackSnapshot second) =>
        checked((int)Math.Abs((long)first.DurationMilliseconds - second.DurationMilliseconds));
}
