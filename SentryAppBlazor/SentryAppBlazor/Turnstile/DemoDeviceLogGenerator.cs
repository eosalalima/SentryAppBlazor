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

    public static bool ShouldGenerate(SimulationOptions simulation, MonitoringOptions monitoring, bool monitoringActive) =>
        IsDemoEnabled(simulation, monitoring) && monitoringActive;

    public static bool IsDemoEnabled(SimulationOptions simulation, MonitoringOptions monitoring) =>
        !simulation.IsLiveMode &&
        monitoring.OperatingMode.Equals("Demo", StringComparison.OrdinalIgnoreCase) &&
        monitoring.EnableSimulatedLogs;

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        var demoWasEnabled = false;

        while (!token.IsCancellationRequested)
        {
            try
            {
                var simulation = settings.CurrentSimulation;
                var monitoring = settings.Current;
                var demoIsEnabled = IsDemoEnabled(simulation, monitoring);

                // A persisted demo configuration must work after an application
                // restart, before a browser is connected to click Start. Only
                // start on the disabled-to-enabled edge so an operator can still
                // stop monitoring without the worker immediately starting it again.
                if (ShouldStartMonitoring(demoWasEnabled, demoIsEnabled, controller.IsActive))
                    controller.TryStart();
                demoWasEnabled = demoIsEnabled;

                if (!ShouldGenerate(simulation, monitoring, controller.IsActive))
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), time, token);
                    continue;
                }

                // Wait between every attempt, including the first after Start.
                await Task.Delay(GetDelay(simulation, random), time, token);

                // Do not write if settings or controller state changed while waiting.
                simulation = settings.CurrentSimulation;
                monitoring = settings.Current;
                if (!ShouldGenerate(simulation, monitoring, controller.IsActive))
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

    internal static TimeSpan GetDelay(SimulationOptions simulation, Random random)
    {
        var minimum = Math.Max(1, simulation.MinimumDelaySeconds);
        var maximum = Math.Max(minimum, simulation.MaximumDelaySeconds);
        return TimeSpan.FromSeconds(random.Next(minimum, maximum + 1));
    }

    internal static bool ShouldStartMonitoring(
        bool demoWasEnabled,
        bool demoIsEnabled,
        bool monitoringActive) =>
        !demoWasEnabled && demoIsEnabled && !monitoringActive;

}
