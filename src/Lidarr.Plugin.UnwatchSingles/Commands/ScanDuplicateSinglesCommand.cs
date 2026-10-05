using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Plugins.UnwatchSingles.Commands;

/// <summary>Requests a full-library or artist-scoped single scan.</summary>
public sealed class ScanDuplicateSinglesCommand : Command
{
    public ScanDuplicateSinglesCommand()
    {
        SendUpdatesToClient = true;
    }

    public int? ArtistId { get; set; }

    public bool DryRun { get; set; }

    public string ResultSummary { get; set; } = string.Empty;

    public override string CompletionMessage => ResultSummary;

    public override bool UpdateScheduledTask => !DryRun;

    public override bool IsExclusive => true;
}
