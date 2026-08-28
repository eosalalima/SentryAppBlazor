using SentryAppBlazor.Services;

namespace SentryAppBlazor.Turnstile;

public sealed class DemoDeviceLogGenerator(
    TurnstileLogState state,
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
        var demoWasEnabled = false;

        while (!token.IsCancellationRequested)
        {
            try
            {
                // Read the persisted file directly. Relying on IOptionsMonitor here
                // made Apply dependent on the platform file-watcher noticing an
                // atomic file replacement, so the hosted service could keep using
                // its startup settings indefinitely.
                var simulation = settings.CurrentSimulation;
                var monitoring = settings.Current;
                var demoIsEnabled = IsDemoEnabled(simulation, monitoring);

                // Hosted services must not depend on a browser clicking Start. Wake
                // both the generator and poller when the application starts in demo
                // mode, and when sentryconfig.json is later changed into demo mode.
                if (ShouldStartMonitoring(demoWasEnabled, demoIsEnabled))
                    controller.TryStart();
                demoWasEnabled = demoIsEnabled;

                if (!ShouldGenerate(simulation, monitoring, controller.IsActive))
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), time, token);
                    continue;
                }

                await Task.Delay(GetDelay(simulation, random), time, token);

                // The operator may stop monitoring or enable live mode while the
                // delay is in progress, so recheck every write-safety condition.
                monitoring = settings.Current;
                simulation = settings.CurrentSimulation;
                if (!ShouldGenerate(simulation, monitoring, controller.IsActive))
                    continue;

                var entry = CreateEntry(random, time.GetLocalNow());
                state.Add(entry);
                logger.LogInformation(
                    "Published in-memory demo turnstile event {LogId}",
                    entry.TimeLogId);

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

    internal static bool ShouldStartMonitoring(bool demoWasEnabled, bool demoIsEnabled) =>
        demoIsEnabled && !demoWasEnabled;

    internal static TimeSpan GetDelay(SimulationOptions simulation, Random random)
    {
        var minimum = Math.Max(1, simulation.MinimumDelaySeconds);
        var maximum = Math.Max(minimum, simulation.MaximumDelaySeconds);
        return TimeSpan.FromSeconds(random.Next(minimum, maximum + 1));
    }

    internal static TurnstileLogEntry CreateEntry(Random random, DateTimeOffset timestamp)
    {
        string[] names = ["Maria Santos", "Daniel Reyes", "Angela Cruz", "Noel Garcia"];
        var logType = LogTypes[random.Next(LogTypes.Length)];
        var deviceNumber = random.Next(1, 4);
        return new(
            Guid.NewGuid(), timestamp, logType,
            $"DEMO-{random.Next(1, 10_000):0000}", names[random.Next(names.Length)],
            "/img/avatar-placeholder.svg", $"DEMO-GATE-{deviceNumber}", $"Demo Gate {deviceNumber}",
            VerifyModes[random.Next(VerifyModes.Length)], Events[random.Next(Events.Length)],
            EventAddresses[random.Next(EventAddresses.Length)], "SMS disabled for demo events.");
    }

}
