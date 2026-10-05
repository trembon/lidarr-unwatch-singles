using NzbDrone.Core.Extras.Metadata;
using NzbDrone.Core.Extras.Metadata.Files;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.Plugins.UnwatchSingles.Configuration;

/// <summary>Settings and scan controls presented in Lidarr's Metadata section.</summary>
public sealed class UnwatchSinglesMetadata : MetadataBase<UnwatchSinglesSettings>
{
    public override string Name => "Unwatch Singles";

    public override MetadataFile FindMetadataFile(Artist artist, string path) => null!;

    public override MetadataFileResult ArtistMetadata(Artist artist) => null!;

    public override MetadataFileResult AlbumMetadata(Artist artist, Album album, string albumPath) => null!;

    public override MetadataFileResult TrackMetadata(Artist artist, TrackFile trackFile) => null!;

    public override List<ImageFileResult> ArtistImages(Artist artist) => [];

    public override List<ImageFileResult> AlbumImages(Artist artist, Album album, string albumPath) => [];

    public override List<ImageFileResult> TrackImages(Artist artist, TrackFile trackFile) => [];
}
