using Microsoft.Extensions.Options;
using SentryAppBlazor.Services;

namespace SentryAppBlazor.Turnstile;

public sealed class DemoDeviceLogGenerator(
    DeviceLogWriter deviceLogs,
    TurnstilePollingController controller,
    IOptionsMonitor<SimulationOptions> settings,
    IOptionsMonitor<MonitoringOptions> monitoringSettings,
    IConfiguration configuration,
    TimeProvider time,
    Random random,
    ILogger<DemoDeviceLogGenerator> logger) : BackgroundService
{
    public static readonly string[] LogTypes = ["IN", "OUT", "BREAK OUT"];
    public static readonly string[] Events = ["0", "105", "20", "202", "214", "23", "27", "41", "42"];
    public static readonly string[] EventAddresses = ["0", "1", "105", "2", "20", "214"];
    public static readonly string[] VerifyModes = ["200", "255", "3", "4"];
    private static readonly (string AccessNumber, string Name)[] People =
    [
        ("2026-0001", "Maria Santos"),
        ("2026-0002", "Daniel Reyes"),
        ("2026-0003", "Angela Cruz"),
        ("2026-0004", "Noel Garcia")
    ];
    private static readonly (string SerialNumber, string Name)[] Devices =
    [
        ("DEMO-GATE-1", "Main Gate"),
        ("DEMO-GATE-2", "North Gate")
    ];

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
                var simulation = CurrentSimulation(configuration, settings.CurrentValue);
                await Task.Delay(TimeSpan.FromSeconds(random.Next(1, 11)), time, token);
                var monitoring = monitoringSettings.CurrentValue;
                simulation = CurrentSimulation(configuration, settings.CurrentValue);
                if (!ShouldGenerate(simulation, monitoring, controller.IsActive))
                    continue;

                // Use real active personnel and devices so database constraints are
                // satisfied and the new row matches the poller's device filter.
                var logId = await deviceLogs.InsertDemoAsync(random, token);
                if (logId is not null) logger.LogInformation(
                    "Inserted demo turnstile event {LogId} into DeviceLogs; it will be displayed by the polling worker",
                    logId);

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

    internal static SimulationOptions CurrentSimulation(IConfiguration configuration, SimulationOptions fallback)
    {
        var liveValue = configuration["IsLiveMode"];
        return new SimulationOptions
        {
            IsLiveMode = bool.TryParse(liveValue, out var live) ? live : fallback.IsLiveMode,
            EnableSimulatedLogs = fallback.EnableSimulatedLogs,
            MinimumDelaySeconds = fallback.MinimumDelaySeconds,
            MaximumDelaySeconds = fallback.MaximumDelaySeconds
        };
    }

    internal static bool ShouldStartMonitoring(bool demoWasEnabled, bool demoIsEnabled) =>
        demoIsEnabled && !demoWasEnabled;

    internal static TurnstileLogEntry CreateEntry(Random random, DateTimeOffset timestamp)
    {
        var person = People[random.Next(People.Length)];
        var device = Devices[random.Next(Devices.Length)];
        return new TurnstileLogEntry(
            Guid.NewGuid(), timestamp, LogTypes[random.Next(LogTypes.Length)],
            person.AccessNumber, person.Name, "/img/avatar-placeholder.svg",
            device.SerialNumber, device.Name, VerifyModes[random.Next(VerifyModes.Length)],
            Events[random.Next(Events.Length)], EventAddresses[random.Next(EventAddresses.Length)],
            "Demo event — SMS not sent.");
    }
}
