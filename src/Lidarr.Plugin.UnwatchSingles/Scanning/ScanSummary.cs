namespace NzbDrone.Core.Plugins.UnwatchSingles.Scanning;

public sealed record ScanSummary(
    int SinglesInspected,
    int DuplicatesFound,
    int AmbiguousSingles,
    int Unmonitored,
    int WouldUnmonitor,
    int Unchanged,
    int Failed,
    bool IsDryRun,
    bool IsDisabled = false)
{
    public string CompletionMessage => IsDisabled
        ? "Unwatch Singles scan skipped: enable the Unwatch Singles provider under Settings > Metadata."
        : $"Unwatch Singles scan complete. Singles inspected: {SinglesInspected}; " +
          $"duplicates found: {DuplicatesFound}; ambiguous: {AmbiguousSingles}; " +
          $"unmonitored: {Unmonitored}; would unmonitor: {WouldUnmonitor}; " +
          $"unchanged: {Unchanged}; failed: {Failed}; dry run: {IsDryRun}.";
}

internal sealed class ScanSummaryBuilder(bool isDryRun)
{
    public bool IsDryRun { get; } = isDryRun;

    public int SinglesInspected { get; set; }

    public int DuplicatesFound { get; set; }

    public int AmbiguousSingles { get; set; }

    public int Unmonitored { get; set; }

    public int WouldUnmonitor { get; set; }

    public int Unchanged { get; set; }

    public int Failed { get; set; }

    public ScanSummary ToSummary() => new(
        SinglesInspected,
        DuplicatesFound,
        AmbiguousSingles,
        Unmonitored,
        WouldUnmonitor,
        Unchanged,
        Failed,
        IsDryRun);
}
