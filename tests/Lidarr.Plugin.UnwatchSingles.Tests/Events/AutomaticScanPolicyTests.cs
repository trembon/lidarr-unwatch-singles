using NzbDrone.Core.Plugins.UnwatchSingles.Events;

namespace Lidarr.Plugin.UnwatchSingles.Tests.Events;

public sealed class AutomaticScanPolicyTests
{
    [Theory]
    [InlineData("Album", true)]
    [InlineData("EP", true)]
    [InlineData("Album, EP", true)]
    [InlineData("Single", false)]
    [InlineData("Compilation", false)]
    [InlineData(null, false)]
    public void OnlyAlbumAndEpEditsTriggerArtistRescans(string? albumType, bool expected)
    {
        Assert.Equal(expected, AutomaticScanPolicy.ShouldScanAfterAlbumEdit(albumType));
    }
}
