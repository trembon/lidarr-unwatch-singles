using System.Reflection;
using NzbDrone.Core.Plugins;

namespace NzbDrone.Core.Plugins.UnwatchSingles;

/// <summary>Entry point Lidarr uses to identify and update this plugin.</summary>
public sealed class UnwatchSinglesPlugin : Plugin
{
    private const string GithubBase = "https://github.com/";

    public override string Name => "Unwatch Singles";

    public override string Owner => Metadata("RepositoryOwner", "trembon");

    public override string GithubUrl => $"{GithubBase}{Owner}/{Metadata("RepositoryName", "lidarr-unwatch-singles")}";

    private static string Metadata(string name, string fallback) =>
        typeof(UnwatchSinglesPlugin).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == name)?.Value ?? fallback;
}
