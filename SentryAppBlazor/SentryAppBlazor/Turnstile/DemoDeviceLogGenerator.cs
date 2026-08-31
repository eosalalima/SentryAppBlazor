using SentryAppBlazor.Services;

namespace SentryAppBlazor.Turnstile;

public sealed class DemoDeviceLogGenerator(
    DeviceLogWriter writer,
    TurnstilePollingController controller,
    MonitoringSettingsStore settings,
    TimeProvider time,
    Random random,
    ILogger<DemoDeviceLogGenerator> logger) : BackgroundService
{
    public static readonly string[] LogTypes = ["IN", "OUT", "BREAK OUT"];

    public static bool ShouldGenerate(MonitoringOptions monitoring, bool monitoringActive) =>
        IsDemoMode(monitoring) && monitoringActive;

    public static bool IsDemoMode(MonitoringOptions monitoring) =>
        monitoring.OperatingMode.Equals("Demo", StringComparison.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                // The operator's Start button is the only action that makes demo
                // generation active. OperatingMode controls what Start does.
                await controller.WaitUntilActiveAsync(token);

                var monitoring = settings.Current;

                if (!ShouldGenerate(monitoring, controller.IsActive))
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), time, token);
                    continue;
                }

                // Wait between every attempt, including the first after Start.
                await Task.Delay(GetDelay(monitoring), time, token);

                // Do not write if settings or controller state changed while waiting.
                monitoring = settings.Current;
                if (!ShouldGenerate(monitoring, controller.IsActive))
                    continue;

                var logId = await writer.InsertDemoAsync(random, token);
                if (logId.HasValue)
                    logger.LogInformation("Inserted demo turnstile event {LogId} into DeviceLogs", logId.Value);
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
