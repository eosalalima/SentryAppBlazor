using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SentryAppBlazor.Data;
using SentryAppBlazor.Services;

namespace SentryAppBlazor.Turnstile;

public sealed class TurnstileLogPollingWorker : BackgroundService
{
    private readonly IDbContextFactory<AccessControlDbContext> factory;
    private readonly TurnstilePollingController controller;
    private readonly TurnstileLogState state;
    private readonly PersonnelLookupService lookup;
    private readonly ISmsSender sms;
    private readonly IPhotoUrlBuilder photos;
    private readonly IOptionsMonitor<MonitoringOptions> monitoring;
    private readonly IConfiguration configuration;
    private readonly TimeProvider time;
    private readonly ILogger<TurnstileLogPollingWorker> logger;
    private readonly Dictionary<Guid, DateTimeOffset> seen = [];
    private DateTimeOffset lastTimestamp;
    private Guid lastId;
    private volatile bool resetRequested;
    private PeriodicTimer? timer;

    public TurnstileLogPollingWorker(
        IDbContextFactory<AccessControlDbContext> factory, TurnstilePollingController controller,
        TurnstileLogState state, PersonnelLookupService lookup, ISmsSender sms,
        IPhotoUrlBuilder photos, IOptionsMonitor<MonitoringOptions> monitoring,
        IConfiguration configuration, TimeProvider time, ILogger<TurnstileLogPollingWorker> logger)
    {
        this.factory = factory; this.controller = controller; this.state = state;
        this.lookup = lookup; this.sms = sms; this.photos = photos;
        this.monitoring = monitoring; this.configuration = configuration;
        this.time = time; this.logger = logger;
        ResetCursor(TurnstilePollingOptions.FromConfiguration(configuration, monitoring.CurrentValue).LookbackSecondsOnStart);
        controller.StatusChanged += OnStatusChanged;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timerInterval = 0;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await controller.WaitUntilActiveAsync(stoppingToken);
                if (!controller.IsActive) continue;

                var options = TurnstilePollingOptions.FromConfiguration(configuration, monitoring.CurrentValue);
                if (resetRequested)
                {
                    ResetCursor(options.LookbackSecondsOnStart);
                    resetRequested = false;
                }
                if (timer is null || timerInterval != options.IntervalMs)
                {
                    timer?.Dispose();
                    timerInterval = options.IntervalMs;
                    timer = new PeriodicTimer(TimeSpan.FromMilliseconds(timerInterval), time);
                }

                try { await PollOnceAsync(options, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception exception) { logger.LogError(exception, "Turnstile poll failed; the unacknowledged row will be retried"); }

                if (controller.IsActive)
                    await timer.WaitForNextTickAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { timer?.Dispose(); timer = null; }
    }

    internal async Task PollOnceAsync(TurnstilePollingOptions options, CancellationToken token)
    {
        var now = time.GetUtcNow();
        foreach (var expired in seen.Where(item => now - item.Value >= TimeSpan.FromMinutes(1)).Select(item => item.Key).ToArray())
            seen.Remove(expired);

        await using var db = await factory.CreateDbContextAsync(token);
        var deviceId = string.IsNullOrWhiteSpace(monitoring.CurrentValue.DeviceId) ? "all" : monitoring.CurrentValue.DeviceId.Trim();
        var maximumRows = Math.Clamp(options.MaxRowsPerPoll, 1, 500);
        FormattableString query = $@"SELECT TOP ({maximumRows}) dl.Id AS TimeLogId, dl.TimeLogStamp, dl.LogType, dl.AccessNumber, dl.DeviceSerialNumber, dl.VerifyMode,
p.LastName, p.FirstName, p.PhotoId, dl.Event, dl.EventAddress, zk.Name AS DeviceName
FROM dbo.DeviceLogs dl
LEFT JOIN dbo.Personnels p ON p.AccessNumber = dl.AccessNumber AND p.IsDeleted = 0
LEFT JOIN dbo.ZKDevices zk ON zk.SerialNumber = dl.DeviceSerialNumber AND zk.IsDeleted = 0
WHERE dl.IsDeleted = 0
AND (LOWER({deviceId}) = 'all' OR dl.DeviceSerialNumber = {deviceId})
AND (dl.TimeLogStamp > {lastTimestamp} OR (dl.TimeLogStamp = {lastTimestamp} AND dl.Id > {lastId}))
ORDER BY dl.TimeLogStamp ASC, dl.Id ASC";
        var rows = await db.TurnstileLogRows.FromSqlInterpolated(query).AsNoTracking().ToListAsync(token);

        foreach (var row in rows)
        {
            if (seen.ContainsKey(row.TimeLogId))
            {
                if (options.FlowDiagnosticsEnabled) logger.LogDebug("Ignoring duplicate log {LogId}", row.TimeLogId);
                Advance(row);
                continue;
            }

            await ProcessAsync(row, token);
            seen[row.TimeLogId] = now;
            Advance(row);
            if (options.FlowDiagnosticsEnabled) logger.LogDebug("Processed new log {LogId}", row.TimeLogId);
        }
    }

    private async Task ProcessAsync(TurnstileLogRow row, CancellationToken token)
    {
        var name = FormatPersonnelName(row.FirstName, row.LastName);
        var device = string.IsNullOrWhiteSpace(row.DeviceName) ? row.DeviceSerialNumber ?? "Unknown device" : row.DeviceName.Trim();
        var status = "SMS disabled in monitoring settings.";
        if (monitoring.CurrentValue.SmsEnabled)
        {
            var mobile = await lookup.FindMobileAsync(row.AccessNumber, token);
            if (mobile is null) status = "SMS not sent: missing mobile number.";
            else
            {
                var message = $"{name} has logged {row.LogType ?? "an event"} on {row.TimeLogStamp:yyyy-MM-dd HH:mm:ss} at {device}. Auto-generated SMS — do not reply.";
                var result = await sms.SendAsync(mobile, message, token);
                status = result.Success ? "SMS sent successfully." : $"SMS failed: {result.Message ?? "unknown error"}.";
            }
        }

        if (!state.Add(new(row.TimeLogId, row.TimeLogStamp, row.LogType, row.AccessNumber, name,
            photos.Build(row.PhotoId), row.DeviceSerialNumber, device, row.VerifyMode, row.Event, row.EventAddress, status)))
            logger.LogDebug("Log {LogId} was already present in monitoring state", row.TimeLogId);
    }

    public static string FormatPersonnelName(string? firstName, string? lastName)
    {
        var first = firstName?.Trim(); var last = lastName?.Trim();
        if (string.IsNullOrWhiteSpace(first)) return string.IsNullOrWhiteSpace(last) ? "UNKNOWN" : last;
        return string.IsNullOrWhiteSpace(last) ? first : $"{last.ToUpperInvariant()}, {first}";
    }

    private void Advance(TurnstileLogRow row) { lastTimestamp = row.TimeLogStamp; lastId = row.TimeLogId; }
    private void OnStatusChanged(bool active) { if (active) resetRequested = true; }
    internal void ResetCursor(int lookbackSeconds) { lastTimestamp = time.GetUtcNow().AddSeconds(-lookbackSeconds); lastId = Guid.Empty; }
    internal (DateTimeOffset Timestamp, Guid Id) Cursor => (lastTimestamp, lastId);

    public override void Dispose()
    {
        controller.StatusChanged -= OnStatusChanged;
        timer?.Dispose();
        base.Dispose();
    }
}
