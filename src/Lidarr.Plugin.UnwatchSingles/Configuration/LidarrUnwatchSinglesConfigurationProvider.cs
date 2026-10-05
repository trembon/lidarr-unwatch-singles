using NzbDrone.Core.Extras.Metadata;
using NzbDrone.Core.Plugins.UnwatchSingles.Scanning;

namespace NzbDrone.Core.Plugins.UnwatchSingles.Configuration;

public sealed class LidarrUnwatchSinglesConfigurationProvider : IUnwatchSinglesConfigurationProvider
{
    private readonly Lazy<IMetadataFactory> _metadataFactory;

    public LidarrUnwatchSinglesConfigurationProvider(Lazy<IMetadataFactory> metadataFactory)
    {
        _metadataFactory = metadataFactory;
    }

    public PluginScanSettings? GetConfiguration()
    {
        var provider = _metadataFactory.Value.GetAvailableProviders()
            .OfType<UnwatchSinglesMetadata>()
            .FirstOrDefault();

        if (provider?.Definition is not MetadataDefinition definition ||
            definition.Settings is not UnwatchSinglesSettings settings)
        {
            return null;
        }

        return settings.ToPluginScanSettings(definition.Enable, definition.Id);
    }
}
