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
    public static readonly string[] LogTypes = ["IN", "OUT", "BREAK OUT"];
    public static bool IsDemoMode(MonitoringOptions options) =>
        string.Equals(options.Mode, "Demo", StringComparison.OrdinalIgnoreCase);
    public static bool ShouldGenerate(MonitoringOptions options) => IsDemoMode(options);

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        logger.LogInformation("Demo DeviceLogs generator started");
        try
        {
            while (!token.IsCancellationRequested)
            {
                await controller.WaitUntilActiveAsync(token);
                if (!CanGenerate(controller)) continue;

                try
                {
                    var id = await writer.InsertDemoAsync(controller.Selection, token);
                    if (id is null)
                    {
                        controller.ReportStatus("Demo data was not inserted. Select an existing access number and device.");
                        logger.LogWarning("Demo insert skipped because its selected personnel or device is not valid");
                    }
                    else
                    {
                        controller.ReportStatus(null);
                        logger.LogInformation("Inserted demo DeviceLogs row {LogId}", id);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception exception)
                {
                    controller.ReportStatus("The demo event could not be saved. Monitoring will retry.");
                    logger.LogError(exception, "Demo DeviceLogs insert failed; a later cycle will retry");
                }

                await DelayWhileEligibleAsync(GetDelay(settings.Current, random), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { logger.LogInformation("Demo DeviceLogs generator stopped"); }
    }

    public static bool CanGenerate(TurnstilePollingController controller) =>
        controller.IsActive && controller.Mode == MonitoringMode.Demo;

    public static TimeSpan GetDelay(MonitoringOptions options, Random random)
    {
        var minimum = Math.Max(1, options.DemoMinimumDelaySeconds);
        var maximum = Math.Max(minimum, options.DemoMaximumDelaySeconds);
        return TimeSpan.FromSeconds(minimum == maximum ? minimum : random.Next(minimum, maximum + 1));
    }

    public static TimeSpan GetDelay(MonitoringOptions options) =>
        TimeSpan.FromSeconds(Math.Max(1, options.DemoMaximumDelaySeconds));

    private async Task DelayWhileEligibleAsync(TimeSpan delay, CancellationToken token)
    {
        var end = time.GetUtcNow() + delay;
        while (CanGenerate(controller) && time.GetUtcNow() < end)
        {
            var remaining = end - time.GetUtcNow();
            await Task.Delay(remaining < TimeSpan.FromMilliseconds(200) ? remaining : TimeSpan.FromMilliseconds(200), time, token);
        }
    }
}
