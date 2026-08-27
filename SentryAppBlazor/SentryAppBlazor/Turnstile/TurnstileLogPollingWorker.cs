using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SentryAppBlazor.Data;
using SentryAppBlazor.Services;

namespace SentryAppBlazor.Turnstile;
public sealed class TurnstileLogPollingWorker : BackgroundService
{
    private readonly IDbContextFactory<AccessControlDbContext> factory; private readonly TurnstilePollingController controller; private readonly TurnstileLogState state; private readonly PersonnelLookupService lookup; private readonly ISmsSender sms; private readonly IPhotoUrlBuilder photos; private readonly ILogger<TurnstileLogPollingWorker> logger; private readonly IOptionsMonitor<MonitoringOptions> settings; private readonly RecentlySeenIds seen; private readonly TimeProvider time;
    private DateTimeOffset lastTimestamp; private Guid lastId;
    public TurnstileLogPollingWorker(IDbContextFactory<AccessControlDbContext> factory, TurnstilePollingController controller, TurnstileLogState state, PersonnelLookupService lookup, ISmsSender sms, IPhotoUrlBuilder photos, IOptionsMonitor<MonitoringOptions> settings, IOptions<TurnstilePollingOptions> pollingOptions, TimeProvider time, ILogger<TurnstileLogPollingWorker> logger)
    { this.factory=factory; this.controller=controller; this.state=state; this.lookup=lookup; this.sms=sms; this.photos=photos; this.logger=logger; this.settings=settings; this.time=time; seen=new(pollingOptions.Value.RecentlySeenCapacity); lastTimestamp=time.GetUtcNow().AddSeconds(-settings.CurrentValue.LookbackSecondsOnStart); }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested) { try { await controller.WaitUntilActiveAsync(stoppingToken); if (!controller.IsActive) continue; var options=settings.CurrentValue; if (ShouldPollDatabase(options)) await PollOnceAsync(stoppingToken); await Task.Delay(options.PollingInterval, stoppingToken); } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; } catch (Exception ex) { logger.LogError(ex, "Turnstile poll failed; it will be retried"); await Task.Delay(settings.CurrentValue.PollingInterval, stoppingToken); } }
    }
    public static bool ShouldPollDatabase(MonitoringOptions options) => true;
    internal async Task PollOnceAsync(CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var options = settings.CurrentValue;
        var deviceId = string.IsNullOrWhiteSpace(options.DeviceId) ? "all" : options.DeviceId.Trim();
        FormattableString query = $@"SELECT TOP ({options.MaxRowsPerPoll}) dl.Id AS TimeLogId, dl.TimeLogStamp, dl.LogType, dl.AccessNumber, dl.DeviceSerialNumber, dl.VerifyMode,
p.LastName, p.FirstName, p.PhotoId, dl.Event, dl.EventAddress, zk.Name AS DeviceName
FROM dbo.DeviceLogs dl
LEFT JOIN dbo.Personnels p ON p.AccessNumber = dl.AccessNumber AND p.IsDeleted = 0
LEFT JOIN dbo.ZKDevices zk ON zk.SerialNumber = dl.DeviceSerialNumber AND zk.IsDeleted = 0
WHERE dl.IsDeleted = 0
AND (LOWER({deviceId}) = 'all' OR dl.DeviceSerialNumber = {deviceId})
AND (dl.TimeLogStamp > {lastTimestamp} OR (dl.TimeLogStamp = {lastTimestamp} AND dl.Id > {lastId}))
ORDER BY dl.TimeLogStamp ASC, dl.Id ASC";
        var rows = await db.TurnstileLogRows.FromSqlInterpolated(query).AsNoTracking().ToListAsync(token);
        // Keep SQL Server's uniqueidentifier ordering intact. Guid.CompareTo uses a
        // different byte ordering, so sorting this page again in .NET can move the
        // watermark backwards and repeatedly fetch (or skip) rows that share a
        // timestamp, especially once a poll is limited by TOP.
        foreach (var row in rows)
        {
            if (seen.Add(row.TimeLogId)) await ProcessAsync(row, token);
            lastTimestamp=row.TimeLogStamp; lastId=row.TimeLogId;
        }
        state.Prune(time.GetUtcNow().AddMilliseconds(-options.FeedRetentionDuration));
    }
    private async Task ProcessAsync(TurnstileLogRow row, CancellationToken token)
    {
        var name=string.Join(' ', new[]{row.FirstName?.Trim(),row.LastName?.Trim()}.Where(x=>!string.IsNullOrWhiteSpace(x))); if (name.Length==0) name=row.AccessNumber ?? "Unknown personnel";
        var device=!string.IsNullOrWhiteSpace(row.DeviceName)?row.DeviceName.Trim():!string.IsNullOrWhiteSpace(row.DeviceSerialNumber)?row.DeviceSerialNumber:"Unknown device";
        string status;
        if (!settings.CurrentValue.SmsEnabled) { status="SMS disabled in monitoring settings."; state.Add(new(row.TimeLogId,row.TimeLogStamp,row.LogType,row.AccessNumber,name,photos.Build(row.PhotoId),row.DeviceSerialNumber,device,row.VerifyMode,row.Event,row.EventAddress,status)); return; }
        var mobile=await lookup.FindMobileAsync(row.AccessNumber, token);
        if (mobile is null) status="SMS not sent: missing mobile number.";
        else try { var message=$"{name} has logged {row.LogType ?? "an event"} on {row.TimeLogStamp:yyyy-MM-dd HH:mm:ss} at {device}. Auto-generated SMS — do not reply."; var result=await sms.SendAsync(mobile,message,token); status=result.Success?"SMS sent successfully.":$"SMS failed: {result.Message ?? "unknown error"}."; } catch (OperationCanceledException) when(token.IsCancellationRequested){throw;} catch(Exception ex){logger.LogWarning(ex,"SMS delivery failed for log {LogId}",row.TimeLogId);status=$"SMS failed: {ex.Message}.";}
        state.Add(new(row.TimeLogId,row.TimeLogStamp,row.LogType,row.AccessNumber,name,photos.Build(row.PhotoId),row.DeviceSerialNumber,device,row.VerifyMode,row.Event,row.EventAddress,status));
    }
}
