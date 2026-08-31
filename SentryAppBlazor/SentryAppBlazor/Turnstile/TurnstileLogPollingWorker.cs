using Microsoft.EntityFrameworkCore;
using SentryAppBlazor.Data;
using SentryAppBlazor.Services;

namespace SentryAppBlazor.Turnstile;

public sealed class TurnstileLogPollingWorker : BackgroundService
{
    private readonly IDbContextFactory<AccessControlDbContext> factory;
    private readonly IDbContextFactory<PersonnelsDbContext> personnelsFactory;
    private readonly TurnstilePollingController controller;
    private readonly TurnstileLogState state;
    private readonly PersonnelLookupService lookup;
    private readonly ISmsSender sms;
    private readonly IPhotoUrlBuilder photos;
    private readonly MonitoringSettingsStore settings;
    private readonly IConfiguration configuration;
    private readonly TimeProvider time;
    private readonly ILogger<TurnstileLogPollingWorker> logger;
    private readonly Dictionary<Guid, DateTimeOffset> seen = [];
    private DateTimeOffset lastTimestamp;
    private Guid lastId;
    private volatile bool resetRequested;
    private PeriodicTimer? timer;

    public TurnstileLogPollingWorker(
        IDbContextFactory<AccessControlDbContext> factory, IDbContextFactory<PersonnelsDbContext> personnelsFactory, TurnstilePollingController controller,
        TurnstileLogState state, PersonnelLookupService lookup, ISmsSender sms,
        IPhotoUrlBuilder photos, MonitoringSettingsStore settings,
        IConfiguration configuration, TimeProvider time, ILogger<TurnstileLogPollingWorker> logger)
    {
        this.factory = factory; this.personnelsFactory = personnelsFactory; this.controller = controller; this.state = state;
        this.lookup = lookup; this.sms = sms; this.photos = photos;
        this.settings = settings; this.configuration = configuration;
        this.time = time; this.logger = logger;
        RequestCursorReset();
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

                var options = TurnstilePollingOptions.FromConfiguration(configuration, settings.Current);
                if (timer is null || timerInterval != options.IntervalMs)
                {
                    timer?.Dispose();
                    timerInterval = options.IntervalMs;
                    timer = new PeriodicTimer(TimeSpan.FromMilliseconds(timerInterval), time);
                }

                try
                {
                    // Cursor initialization talks to SQL Server too, so it must use
                    // the same retry boundary as a normal poll. Previously, an
                    // unavailable database here escaped ExecuteAsync and caused the
                    // generic host (and therefore IIS/Blazor) to shut down.
                    if (resetRequested)
                    {
                        ResetCursor(options.LookbackSecondsOnStart);
                        resetRequested = false;
                    }

                    await PollOnceAsync(options, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception exception)
                {
                    logger.LogError(
                        exception,
                        "Turnstile database operation failed; monitoring will retry without stopping the web server");
                }

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
        var monitoring = settings.Current;
        var deviceId = string.IsNullOrWhiteSpace(monitoring.DeviceId) ? "all" : monitoring.DeviceId.Trim();
        var maximumRows = Math.Clamp(options.MaxRowsPerPoll, 1, 500);
        var candidates = await db.DeviceLogs.AsNoTracking()
            .Where(x => !x.IsDeleted && x.TimeLogStamp >= lastTimestamp &&
                        (deviceId.ToLower() == "all" || x.DeviceSerialNumber == deviceId))
            .OrderBy(x => x.TimeLogStamp).ThenBy(x => x.Id)
            .Take(maximumRows * 2)
            .ToListAsync(token);
        var deviceNames = await db.ZkDevices.AsNoTracking().Where(x => !x.IsDeleted)
            .ToDictionaryAsync(x => x.SerialNumber, x => x.Name, token);
        var rows = candidates
            .Where(x => x.TimeLogStamp > lastTimestamp ||
                        (x.TimeLogStamp == lastTimestamp && x.Id.CompareTo(lastId) > 0))
            .Take(maximumRows)
            .Select(x => new TurnstileLogRow
            {
                TimeLogId = x.Id, TimeLogStamp = x.TimeLogStamp, LogType = x.LogType,
                AccessNumber = x.AccessNumber, DeviceSerialNumber = x.DeviceSerialNumber,
                VerifyMode = x.VerifyMode, Event = x.Event, EventAddress = x.EventAddress,
                DeviceName = x.DeviceSerialNumber is not null && deviceNames.TryGetValue(x.DeviceSerialNumber, out var name) ? name : null
            }).ToList();

        var accessNumbers = rows.Select(x => x.AccessNumber).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).Distinct().ToArray();
        if (accessNumbers.Length > 0)
        {
            await using var personnels = await personnelsFactory.CreateDbContextAsync(token);
            var profiles = await personnels.Personnels.AsNoTracking()
                .Where(x => accessNumbers.Contains(x.AccessNumber) && !x.IsDeleted)
                .Select(x => new { x.AccessNumber, x.LastName, x.FirstName, x.PhotoId })
                .ToDictionaryAsync(x => x.AccessNumber, token);
            foreach (var row in rows)
                if (row.AccessNumber is not null && profiles.TryGetValue(row.AccessNumber, out var profile))
                {
                    row.LastName = profile.LastName;
                    row.FirstName = profile.FirstName;
                    row.PhotoId = profile.PhotoId;
                }
        }

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
        var monitoring = settings.Current;
        if (monitoring.SmsEnabled)
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
    private void OnStatusChanged(bool active) { if (active) RequestCursorReset(); }
    private void RequestCursorReset() => resetRequested = true;

    private void ResetCursor(int lookbackSeconds)
    {
        lastTimestamp = time.GetUtcNow().Subtract(TimeSpan.FromSeconds(Math.Max(0, lookbackSeconds)));
        lastId = Guid.Empty;
        logger.LogInformation("Turnstile polling cursor initialized at {Cursor}", lastTimestamp);
    }
    internal (DateTimeOffset Timestamp, Guid Id) Cursor => (lastTimestamp, lastId);

    public override void Dispose()
    {
        controller.StatusChanged -= OnStatusChanged;
        timer?.Dispose();
        base.Dispose();
    }
}
