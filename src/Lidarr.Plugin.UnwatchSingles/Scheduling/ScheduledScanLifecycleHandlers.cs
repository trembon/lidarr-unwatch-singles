using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Extras.Metadata;
using NzbDrone.Core.Plugins.UnwatchSingles.Configuration;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.Plugins.UnwatchSingles.Scheduling;

public sealed class CaptureScheduledScanBeforeLidarrCleanup(IPluginScheduledTaskCoordinator coordinator)
    : IHandle<ApplicationStartedEvent>
{
    [EventHandleOrder(EventHandleOrder.First)]
    public void Handle(ApplicationStartedEvent message) =>
        coordinator.CaptureBeforeStartupCleanup();
}

public sealed class RegisterScheduledScanAfterLidarrCleanup(IPluginScheduledTaskCoordinator coordinator)
    : IHandle<ApplicationStartedEvent>,
      IHandle<ProviderUpdatedEvent<IMetadata>>,
      IHandle<ProviderDeletedEvent<IMetadata>>
{
    [EventHandleOrder(EventHandleOrder.Last)]
    public void Handle(ApplicationStartedEvent message) =>
        coordinator.ReconcileAfterStartupCleanup();

    public void Handle(ProviderUpdatedEvent<IMetadata> message)
    {
        if (message.Definition is MetadataDefinition definition &&
            definition.Implementation == typeof(UnwatchSinglesMetadata).Name)
        {
            coordinator.UpdateProvider(definition);
        }
    }

    public void Handle(ProviderDeletedEvent<IMetadata> message)
    {
        coordinator.RemoveProvider(message.ProviderId);
    }
}
