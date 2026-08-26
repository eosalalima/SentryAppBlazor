using Microsoft.Extensions.Options;
using SentryAppBlazor.Services;

namespace SentryAppBlazor.Turnstile;

public sealed class DemoDeviceLogGenerator(
    TurnstileLogState state,
    TurnstilePollingController controller,
    IOptionsMonitor<SimulationOptions> settings,
    IOptionsMonitor<MonitoringOptions> monitoringSettings,
    TimeProvider time,
    Random random,
    ILogger<DemoDeviceLogGenerator> logger) : BackgroundService
{
    private static readonly (string AccessNumber, string Name)[] DemoPeople =
    [
        ("2026-0001", "Maria Santos"),
        ("2026-0002", "John Reyes"),
        ("2026-0003", "Angela Cruz"),
        ("2026-0004", "Miguel Garcia")
    ];

    public static readonly string[] LogTypes = ["IN", "OUT", "BREAK OUT"];

    public static bool ShouldGenerate(SimulationOptions simulation, MonitoringOptions monitoring, bool monitoringActive) =>
        !simulation.IsLiveMode &&
        monitoring.OperatingMode.Equals("Demo", StringComparison.OrdinalIgnoreCase) &&
        monitoring.EnableSimulatedLogs &&
        monitoringActive;

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var simulation = settings.CurrentValue;
                await Task.Delay(
                    TimeSpan.FromSeconds(random.Next(simulation.MinimumDelaySeconds, simulation.MaximumDelaySeconds + 1)),
                    time,
                    token);

                var monitoring = monitoringSettings.CurrentValue;
                if (!ShouldGenerate(settings.CurrentValue, monitoring, controller.IsActive)) continue;

                var entry = CreateEntry(monitoring, time.GetUtcNow(), random);
                state.Add(entry);
                state.Prune(time.GetUtcNow().AddMilliseconds(-monitoring.FeedRetentionDuration));
                logger.LogDebug("Generated demo turnstile event {LogId}", entry.TimeLogId);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Simulated log generation failed; a later cycle will retry");
            }
        }
    }

    public static TurnstileLogEntry CreateEntry(MonitoringOptions monitoring, DateTimeOffset timestamp, Random random)
    {
        var person = DemoPeople[random.Next(DemoPeople.Length)];
        var device = string.IsNullOrWhiteSpace(monitoring.DeviceId) || monitoring.DeviceId.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? "Demo Gate"
            : monitoring.DeviceId.Trim();

        return new TurnstileLogEntry(
            Guid.NewGuid(),
            timestamp,
            LogTypes[random.Next(LogTypes.Length)],
            person.AccessNumber,
            person.Name,
            "/img/avatar-placeholder.svg",
            device,
            device,
            "DEMO",
            "Simulated event",
            "Demo mode",
            "SMS disabled for demo event.");
    }
}
