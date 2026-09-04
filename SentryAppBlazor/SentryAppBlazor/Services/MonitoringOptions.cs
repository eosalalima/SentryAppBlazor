using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SentryAppBlazor.Services;

public sealed class MonitoringOptions : IValidatableObject
{
    public const string SectionName = "Monitoring";

    [RegularExpression("^(Demo|Live)$", ErrorMessage = "Mode must be Demo or Live.")]
    public string Mode { get; set; } = "Demo";
    [Range(50, 60_000)] public int PollingIntervalMs { get; set; } = 500;
    [Range(0, 3_600)] public int StartupLookbackSeconds { get; set; } = 3;
    [Range(1, 500)] public int MaximumRowsPerPoll { get; set; } = 20;
    [Range(1, 3_600)] public int DemoMinimumDelaySeconds { get; set; } = 1;
    [Range(1, 3_600)] public int DemoMaximumDelaySeconds { get; set; } = 10;
    [Range(1, 300_000)] public int HighlightDurationMs { get; set; } = 5_000;
    [Range(1, 3_600)] public int FeedItemTtlSeconds { get; set; } = 10;
    [Range(1, 100)] public int MaximumFeedItemsPerCategory { get; set; } = 10;
    public bool EnableFlowDiagnostics { get; set; }
    public string ExternalPhotoDirectory { get; set; } = string.Empty;
    public string DeviceId { get; set; } = "all";

    // Existing SMS fields are retained as an extension point; the supplied sender is a no-op.
    public bool SmsEnabled { get; set; }
    [RegularExpression("^COM[1-9][0-9]*$")] public string SmsComPort { get; set; } = "COM4";
    [Range(5, 120)] public int SmsTimeoutSeconds { get; set; } = 30;
    [Range(0, 5)] public int SmsRetryCount { get; set; } = 2;

    // Compatibility aliases for configuration files written by earlier releases.
    [JsonIgnore] public string OperatingMode { get => Mode; set => Mode = value; }
    [JsonIgnore] public int DemoLogIntervalSeconds { get => DemoMaximumDelaySeconds; set => DemoMinimumDelaySeconds = DemoMaximumDelaySeconds = value; }
    [JsonIgnore] public string PhotosPath { get => ExternalPhotoDirectory; set => ExternalPhotoDirectory = value; }
    [JsonIgnore] public int PollingInterval { get => PollingIntervalMs; set => PollingIntervalMs = value; }
    [JsonIgnore] public int HighlightDisplayDuration { get => HighlightDurationMs; set => HighlightDurationMs = value; }
    [JsonIgnore] public int FeedRetentionDuration { get => checked(FeedItemTtlSeconds * 1000); set => FeedItemTtlSeconds = Math.Max(1, value / 1000); }
    [JsonIgnore] public int LookbackSecondsOnStart { get => StartupLookbackSeconds; set => StartupLookbackSeconds = value; }
    [JsonIgnore] public int MaxRowsPerPoll { get => MaximumRowsPerPoll; set => MaximumRowsPerPoll = value; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DemoMinimumDelaySeconds > DemoMaximumDelaySeconds)
            yield return new ValidationResult("The minimum demo delay cannot exceed the maximum demo delay.",
                [nameof(DemoMinimumDelaySeconds), nameof(DemoMaximumDelaySeconds)]);
    }

    public MonitoringOptions Clone() => (MonitoringOptions)MemberwiseClone();
}
