using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Plugins.UnwatchSingles.Scanning;

namespace NzbDrone.Core.Plugins.UnwatchSingles.Commands;

/// <summary>Runs requested scans and returns Lidarr's task completion summary.</summary>
public sealed class ScanDuplicateSinglesTask : IExecute<ScanDuplicateSinglesCommand>
{
    private readonly DuplicateSinglesScanner _scanner;

    public ScanDuplicateSinglesTask(DuplicateSinglesScanner scanner)
    {
        _scanner = scanner;
    }

    public void Execute(ScanDuplicateSinglesCommand command)
    {
        command.ResultSummary = _scanner.Scan(command.ArtistId, command.DryRun).CompletionMessage;
    }
}
