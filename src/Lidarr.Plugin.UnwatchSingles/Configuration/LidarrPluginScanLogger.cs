using NLog;
using NzbDrone.Core.Plugins.UnwatchSingles.Scanning;

namespace NzbDrone.Core.Plugins.UnwatchSingles.Configuration;

public sealed class LidarrPluginScanLogger(Logger logger) : IPluginScanLogger
{
    public void Information(string message) => logger.Info(message);

    public void Warning(string message) => logger.Warn(message);

    public void Debug(string message) => logger.Debug(message);
}
