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
    public static readonly string[] Events = ["0", "105", "20", "202", "214", "23", "27", "41", "42"];
    public static readonly string[] EventAddresses = ["0", "1", "105", "2", "20", "214"];
    public static readonly string[] VerifyModes = ["200", "255", "3", "4"];

    public static bool ShouldGenerate(SimulationOptions simulation, MonitoringOptions monitoring, bool monitoringActive) =>
        IsDemoEnabled(simulation, monitoring) && monitoringActive;

    public static bool IsDemoEnabled(SimulationOptions simulation, MonitoringOptions monitoring) =>
        !simulation.IsLiveMode &&
        monitoring.OperatingMode.Equals("Demo", StringComparison.OrdinalIgnoreCase) &&
        monitoring.EnableSimulatedLogs;

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                // Waiting on the controller makes clicking Start the trigger for
                // demo generation.  In particular, do not delay before the first
                // insert: operators should be able to verify the new DeviceLogs
                // row immediately after starting the monitor.
                await controller.WaitUntilActiveAsync(token);

                // Read the persisted file directly. Relying on IOptionsMonitor
                // made Apply dependent on the platform file watcher noticing an
                // atomic file replacement.
                var simulation = settings.CurrentSimulation;
                var monitoring = settings.Current;

                if (!ShouldGenerate(simulation, monitoring, controller.IsActive))
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), time, token);
                    continue;
                }

                // Persist demo traffic to DeviceLogs. The polling worker then
                // discovers and publishes it through the same path as a real
                // turnstile event instead of bypassing the database.
                var logId = await writer.InsertDemoAsync(random, token);
                logger.LogInformation(
                    "Inserted demo turnstile event {LogId} into DeviceLogs",
                    logId);

                // Only subsequent records use the configured random interval.
                // The safety conditions are evaluated again on the next loop.
                await Task.Delay(GetDelay(simulation, random), time, token);
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

    internal static TimeSpan GetDelay(SimulationOptions simulation, Random random)
    {
        var minimum = Math.Max(1, simulation.MinimumDelaySeconds);
        var maximum = Math.Max(minimum, simulation.MaximumDelaySeconds);
        return TimeSpan.FromSeconds(random.Next(minimum, maximum + 1));
    }

}
