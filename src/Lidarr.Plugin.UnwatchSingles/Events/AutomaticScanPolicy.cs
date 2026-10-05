namespace NzbDrone.Core.Plugins.UnwatchSingles.Events;

/// <summary>Pure event rules for deciding which library changes can affect a Single match.</summary>
public static class AutomaticScanPolicy
{
    public static bool ShouldScanAfterAlbumEdit(string? albumType) =>
        HasType(albumType, "Album") || HasType(albumType, "EP");

    private static bool HasType(string? albumType, string expected) =>
        !string.IsNullOrWhiteSpace(albumType) &&
        albumType.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(type => type.Equals(expected, StringComparison.OrdinalIgnoreCase));
}
