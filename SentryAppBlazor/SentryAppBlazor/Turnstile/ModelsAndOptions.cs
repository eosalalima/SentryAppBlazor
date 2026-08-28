using System.ComponentModel.DataAnnotations;

namespace SentryAppBlazor.Turnstile;
public sealed class TurnstilePollingOptions
{
    public const int DefaultIntervalMs = 500, DefaultLookbackSeconds = 3, DefaultMaxRows = 20;
    [Range(50, 60000)] public int IntervalMs { get; set; } = DefaultIntervalMs;
    [Range(0, 3600)] public int LookbackSecondsOnStart { get; set; } = DefaultLookbackSeconds;
    [Range(1, 500)] public int MaxRowsPerPoll { get; set; } = DefaultMaxRows;
    public bool FlowDiagnosticsEnabled { get; set; }

    public static TurnstilePollingOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("TurnstilePolling");
        return new()
        {
            IntervalMs = Valid(section["IntervalMs"] ?? section["IntervalsMs"], 50, 60000, DefaultIntervalMs),
            LookbackSecondsOnStart = Valid(section["LookbackSecondsOnStart"] ?? section["LookbackSecondsOntart"] ?? section["InitialLookbackSeconds"], 0, 3600, DefaultLookbackSeconds),
            MaxRowsPerPoll = Valid(section["MaxRowsPerPoll"], 1, 500, DefaultMaxRows),
            FlowDiagnosticsEnabled = bool.TryParse(section["FlowDiagnosticsEnabled"], out var diagnostics) && diagnostics
        };
    }

    private static int Valid(string? value, int minimum, int maximum, int fallback) =>
        int.TryParse(value, out var parsed) && parsed >= minimum && parsed <= maximum ? parsed : fallback;
}
public sealed class SimulationOptions
{
    public bool IsLiveMode { get; set; } = true;
    public bool EnableSimulatedLogs { get; set; }
    public bool EnableManualTestLogs { get; set; }
    public string? AdministrationKey { get; set; }
    [Range(1, 3600)] public int MinimumDelaySeconds { get; set; } = 1;
    [Range(1, 3600)] public int MaximumDelaySeconds { get; set; } = 10;

    public SimulationOptions Clone() => new()
    {
        IsLiveMode = IsLiveMode,
        EnableSimulatedLogs = EnableSimulatedLogs,
        EnableManualTestLogs = EnableManualTestLogs,
        AdministrationKey = AdministrationKey,
        MinimumDelaySeconds = MinimumDelaySeconds,
        MaximumDelaySeconds = MaximumDelaySeconds
    };
}
public sealed record TurnstileLogEntry(Guid TimeLogId, DateTimeOffset TimeLogStamp, string? LogType, string? AccessNumber, string PersonnelName, string PhotoUrl, string? DeviceSerialNumber, string DeviceName, string? VerifyMode, string? Event, string? EventAddress, string SmsStatusMessage);
public readonly record struct SmsSendResult(bool Success, string? Message = null);
public interface ISmsSender { Task<SmsSendResult> SendAsync(string mobileNumber, string message, CancellationToken cancellationToken); }
public sealed class LoggingSmsSender(ILogger<LoggingSmsSender> logger) : ISmsSender { public Task<SmsSendResult> SendAsync(string mobileNumber, string message, CancellationToken cancellationToken) { logger.LogInformation("SMS transport is not configured; recipient ending {Suffix}", mobileNumber.Length > 4 ? mobileNumber[^4..] : "****"); return Task.FromResult(new SmsSendResult(false, "transport not configured")); } }
public interface IPhotoUrlBuilder { string Build(string? photoId); }
public sealed class PhotoUrlBuilder : IPhotoUrlBuilder { public string Build(string? id) { if (string.IsNullOrWhiteSpace(id)) return "/img/avatar-placeholder.svg"; var safe = Path.GetFileName(id.Trim()); return safe == id.Trim() ? $"/photos/{Uri.EscapeDataString(safe)}" : "/img/avatar-placeholder.svg"; } }
