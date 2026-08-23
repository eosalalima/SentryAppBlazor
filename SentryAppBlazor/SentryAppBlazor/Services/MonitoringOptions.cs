using System.ComponentModel.DataAnnotations;

namespace SentryAppBlazor.Services;

public sealed class MonitoringOptions
{
    public const string SectionName = "Monitoring";
    public string OperatingMode { get; set; } = "Demo";
    public bool EnableSimulatedLogs { get; set; }
    public string DeviceId { get; set; } = "all";
    public string PhotosPath { get; set; } = @"F:\Projects\Dr. Gloria D Lacson School\Photos\Photos";
    [Range(250, 60_000)] public int PollingInterval { get; set; } = 500;
    [Range(1_000, 60_000)] public int HighlightDisplayDuration { get; set; } = 5_000;
    [Range(1_000, 300_000)] public int FeedRetentionDuration { get; set; } = 10_000;
    [Range(0, 3_600)] public int LookbackSecondsOnStart { get; set; } = 3;
    [Range(1, 500)] public int MaxRowsPerPoll { get; set; } = 20;
    public bool SmsEnabled { get; set; }
    [RegularExpression("^COM[1-9][0-9]*$")] public string SmsComPort { get; set; } = "COM4";
    [Range(5, 120)] public int SmsTimeoutSeconds { get; set; } = 30;
    [Range(0, 5)] public int SmsRetryCount { get; set; } = 2;

    public MonitoringOptions Clone() => new()
    {
        OperatingMode = OperatingMode,
        EnableSimulatedLogs = EnableSimulatedLogs,
        DeviceId = DeviceId,
        PhotosPath = PhotosPath,
        PollingInterval = PollingInterval,
        HighlightDisplayDuration = HighlightDisplayDuration,
        FeedRetentionDuration = FeedRetentionDuration,
        LookbackSecondsOnStart = LookbackSecondsOnStart,
        MaxRowsPerPoll = MaxRowsPerPoll,
        SmsEnabled = SmsEnabled,
        SmsComPort = SmsComPort,
        SmsTimeoutSeconds = SmsTimeoutSeconds,
        SmsRetryCount = SmsRetryCount
    };

}
