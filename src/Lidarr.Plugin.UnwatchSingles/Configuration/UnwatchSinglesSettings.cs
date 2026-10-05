using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Plugins.UnwatchSingles.Scanning;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Plugins.UnwatchSingles.Configuration;

/// <summary>User-configurable options for scan behavior.</summary>
public sealed class UnwatchSinglesSettings : IProviderConfig
{
    private static readonly UnwatchSinglesSettingsValidator SettingsValidator = new();

    public UnwatchSinglesSettings()
    {
        DryRun = false;
        AutomaticScanningEnabled = true;
        ScheduledScanningEnabled = true;
        ScheduledIntervalHours = 24;
        DurationToleranceMilliseconds = 3_000;
    }

    [FieldDefinition(0, Label = "Dry run", Type = FieldType.Checkbox,
        HelpText = "Report duplicates but leave all monitoring states unchanged.")]
    public bool DryRun { get; set; }

    [FieldDefinition(1, Label = "Scan after an artist refresh or album import", Type = FieldType.Checkbox,
        HelpText = "Scan the affected artist after its metadata refresh or an album import completes.")]
    public bool AutomaticScanningEnabled { get; set; }

    [FieldDefinition(2, Label = "Run a scheduled full-library scan", Type = FieldType.Checkbox,
        HelpText = "Run a daily full-library scan to catch duplicates missed by artist-scoped events.")]
    public bool ScheduledScanningEnabled { get; set; }

    [FieldDefinition(3, Label = "Full scan interval", Type = FieldType.Number, Unit = "hours",
        HelpText = "How often the scheduled library scan runs. Defaults to 24 hours.")]
    public int ScheduledIntervalHours { get; set; }

    [FieldDefinition(4, Label = "Duration tolerance", Type = FieldType.Number, Unit = "milliseconds",
        HelpText = "Maximum duration difference for title and duration matching. Defaults to 3000 ms.")]
    public int DurationToleranceMilliseconds { get; set; }

    public NzbDroneValidationResult Validate() =>
        new(SettingsValidator.Validate(this));

    public PluginScanSettings ToPluginScanSettings(bool enabled, int providerId = 0) =>
        new(enabled, DryRun, AutomaticScanningEnabled, ScheduledScanningEnabled,
            ScheduledIntervalHours, DurationToleranceMilliseconds, providerId);
}

public sealed class UnwatchSinglesSettingsValidator : AbstractValidator<UnwatchSinglesSettings>
{
    public UnwatchSinglesSettingsValidator()
    {
        RuleFor(settings => settings.ScheduledIntervalHours)
            .InclusiveBetween(1, 8_760)
            .WithMessage("Full scan interval must be between one hour and one year.");

        RuleFor(settings => settings.DurationToleranceMilliseconds)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Duration tolerance cannot be negative.");
    }
}
