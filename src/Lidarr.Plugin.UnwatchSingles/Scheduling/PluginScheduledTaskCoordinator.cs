using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Extras.Metadata;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Plugins.UnwatchSingles.Commands;
using NzbDrone.Core.Plugins.UnwatchSingles.Configuration;
using NzbDrone.Core.Plugins.UnwatchSingles.Scanning;

namespace NzbDrone.Core.Plugins.UnwatchSingles.Scheduling;

public interface IPluginScheduledTaskCoordinator
{
    void CaptureBeforeStartupCleanup();

    void ReconcileAfterStartupCleanup();

    void UpdateProvider(MetadataDefinition definition);

    void RemoveProvider(int providerId);
}

/// <summary>Preserves the scheduled scan while Lidarr rebuilds its native task list.</summary>
public sealed class PluginScheduledTaskCoordinator : IPluginScheduledTaskCoordinator
{
    private readonly IScheduledTaskRepository _taskRepository;
    private readonly ICacheManager _cacheManager;
    private readonly IUnwatchSinglesConfigurationProvider _configurationProvider;
    private readonly Logger _logger;
    private readonly object _sync = new();

    private DateTime _lastExecution;
    private DateTime _lastStartTime;
    private bool _stateCaptured;
    private bool _providerRemoved;
    private int _pluginProviderId;

    public PluginScheduledTaskCoordinator(
        IScheduledTaskRepository taskRepository,
        ICacheManager cacheManager,
        IUnwatchSinglesConfigurationProvider configurationProvider,
        Logger logger)
    {
        _taskRepository = taskRepository;
        _cacheManager = cacheManager;
        _configurationProvider = configurationProvider;
        _logger = logger;
    }

    public void CaptureBeforeStartupCleanup()
    {
        lock (_sync)
        {
            var task = FindExistingTask();
            _lastExecution = task?.LastExecution ?? default;
            _lastStartTime = task?.LastStartTime ?? default;
            _stateCaptured = true;
            _providerRemoved = false;
        }
    }

    public void ReconcileAfterStartupCleanup()
    {
        lock (_sync)
        {
            if (!_stateCaptured)
            {
                var task = FindExistingTask();
                _lastExecution = task?.LastExecution ?? default;
                _lastStartTime = task?.LastStartTime ?? default;
                _stateCaptured = true;
            }

            Reconcile(_providerRemoved ? null : _configurationProvider.GetConfiguration());
        }
    }

    public void UpdateProvider(MetadataDefinition definition)
    {
        lock (_sync)
        {
            _providerRemoved = false;
            _pluginProviderId = definition.Id;

            var settings = definition.Settings as UnwatchSinglesSettings;
            var configuration = settings?.ToPluginScanSettings(definition.Enable, definition.Id);
            Reconcile(configuration);
        }
    }

    public void RemoveProvider(int providerId)
    {
        lock (_sync)
        {
            if (_pluginProviderId == 0 || _pluginProviderId != providerId)
            {
                return;
            }

            _providerRemoved = true;
            Reconcile(null);
            _logger.Info("Disabled the Unwatch Singles scheduled scan after metadata provider {0} was removed.", providerId);
        }
    }

    private void Reconcile(PluginScanSettings? configuration)
    {
        var typeName = typeof(ScanDuplicateSinglesCommand).FullName!;
        var existing = FindExistingTask();
        if (configuration is { ProviderId: > 0 })
        {
            _pluginProviderId = configuration.ProviderId;
        }

        var schedule = ScheduledScanPlan.Create(configuration, _lastExecution, _lastStartTime, DateTime.UtcNow);

        var task = existing ?? new ScheduledTask { TypeName = typeName };
        task.Interval = schedule.IntervalMinutes;
        task.LastExecution = schedule.LastExecution;
        task.LastStartTime = schedule.LastStartTime;
        task.Priority = CommandPriority.Low;

        if (existing is null)
        {
            _taskRepository.Insert(task);
        }
        else
        {
            _taskRepository.Update(task);
        }

        // TaskManager owns this cache and recreates it before our Last-ordered startup hook.
        _cacheManager.GetCache<ScheduledTask>(typeof(TaskManager)).Set(typeName, task);
        _lastExecution = schedule.LastExecution;
        _lastStartTime = schedule.LastStartTime;
        _logger.Debug("Registered Unwatch Singles scheduled task with interval {0} minutes.", schedule.IntervalMinutes);
    }

    private ScheduledTask? FindExistingTask() =>
        _taskRepository.All().FirstOrDefault(task =>
            task.TypeName == typeof(ScanDuplicateSinglesCommand).FullName);
}
