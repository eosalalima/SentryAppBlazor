using SentryAppBlazor.Services;

namespace SentryAppBlazor.Turnstile;

public sealed class DemoDeviceLogGenerator(
    DeviceLogWriter writer,
    MonitoringSettingsStore settings,
    TimeProvider time,
    Random random,
    ILogger<DemoDeviceLogGenerator> logger) : BackgroundService
{
    public static readonly string[] LogTypes = ["IN", "OUT"];

    public static bool ShouldGenerate(MonitoringOptions monitoring) => IsDemoMode(monitoring);

    public static bool IsDemoMode(MonitoringOptions monitoring) =>
        monitoring.OperatingMode.Equals("Demo", StringComparison.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var monitoring = settings.Current;

                if (!ShouldGenerate(monitoring))
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), time, token);
                    continue;
                }

                // Demo mode is a standalone database-data generator. It does not
                // depend on the monitor page, a connected browser, or the polling
                // controller: insert once immediately and then use the configured
                // interval between subsequent rows.
                var logId = await writer.InsertDemoAsync(random, token);
                if (logId.HasValue)
                    logger.LogInformation("Inserted demo turnstile event {LogId} into DeviceLogs", logId.Value);

                await Task.Delay(GetDelay(monitoring), time, token);
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

}
