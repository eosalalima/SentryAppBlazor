using SentryAppBlazor.Services;

namespace SentryAppBlazor.Turnstile;

public sealed class DemoDeviceLogGenerator(
    DeviceLogWriter writer,
    MonitoringSettingsStore settings,
    TurnstilePollingController controller,
    TimeProvider time,
    Random random,
    ILogger<DemoDeviceLogGenerator> logger) : BackgroundService
{
    public static readonly string[] LogTypes = ["IN", "OUT"];

    public static bool ShouldGenerate(MonitoringOptions monitoring) => IsDemoMode(monitoring);

    public static bool IsDemoMode(MonitoringOptions monitoring) =>
        string.Equals(monitoring.OperatingMode, "Demo", StringComparison.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                // "Start" controls the complete monitoring pipeline.  Do not
                // generate database rows merely because the application is
                // running with Demo selected in sentryconfig.json.
                await controller.WaitUntilActiveAsync(token);
                if (!controller.IsActive)
                    continue;
                var activeSession = controller.ActiveSession;

                var monitoring = settings.Current;

                if (!ShouldGenerate(monitoring))
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), time, token);
                    continue;
                }

                // The writer's AccessControlDbContext factory resolves the
                // AccessControlDb value from sentryconfig.json for every insert.
                // Starting in Demo therefore creates a real DeviceLogs row in the
                // configured database immediately, not in a local demo database.
                var logId = await writer.InsertDemoAsync(random, token);
                if (logId.HasValue)
                    logger.LogInformation("Inserted demo turnstile event {LogId} into DeviceLogs", logId.Value);

                await WaitForNextInsertAsync(GetDelay(monitoring), activeSession, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Demo event generation failed; a later cycle will retry");
                await Task.Delay(TimeSpan.FromSeconds(1), time, token);
            }
        }
    }

    internal static TimeSpan GetDelay(MonitoringOptions monitoring) =>
        TimeSpan.FromSeconds(Math.Max(1, monitoring.DemoLogIntervalSeconds));

    private async Task WaitForNextInsertAsync(
        TimeSpan delay,
        long activeSession,
        CancellationToken token)
    {
        var remaining = delay;
        while (remaining > TimeSpan.Zero && controller.IsActive &&
               controller.ActiveSession == activeSession && IsDemoMode(settings.Current))
        {
            var slice = remaining > TimeSpan.FromSeconds(1)
                ? TimeSpan.FromSeconds(1)
                : remaining;
            await Task.Delay(slice, time, token);
            remaining -= slice;
        }
    }

}
