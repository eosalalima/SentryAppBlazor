using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SentryAppBlazor.Data;
using SentryAppBlazor.Services;

namespace SentryAppBlazor.Turnstile;

public sealed class DemoDeviceLogGenerator(
    IDbContextFactory<AccessControlDbContext> accessControlFactory,
    DeviceLogWriter writer,
    TurnstilePollingController controller,
    IOptionsMonitor<SimulationOptions> settings,
    IOptionsMonitor<MonitoringOptions> monitoringSettings,
    TimeProvider time,
    Random random,
    ILogger<DemoDeviceLogGenerator> logger) : BackgroundService
{
    public static readonly string[] LogTypes = ["IN", "OUT"];
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
        // Demo mode is expected to be immediately usable. Starting the shared
        // controller here wakes both this generator and the database poller; it
        // also avoids relying on a Blazor circuit/button click to start hosted
        // services that belong to the application rather than to one browser.
        if (IsDemoEnabled(settings.CurrentValue, monitoringSettings.CurrentValue))
        {
            controller.Start();
        }

        while (!token.IsCancellationRequested)
        {
            try
            {
                await controller.WaitUntilActiveAsync(token);
                var monitoring = monitoringSettings.CurrentValue;
                var simulation = settings.CurrentValue;
                if (!ShouldGenerate(simulation, monitoring, controller.IsActive))
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), time, token);
                    continue;
                }

                var accessNumbers = await LoadAccessNumbersAsync(token);
                var serialNumbers = await LoadDeviceSerialNumbersAsync(token);
                if (accessNumbers.Count == 0 || serialNumbers.Count == 0)
                {
                    logger.LogWarning("Demo log was not inserted because no directory access numbers or device serial numbers are available");
                    await Task.Delay(TimeSpan.FromSeconds(1), time, token);
                    continue;
                }

                var id = await writer.InsertDemoAsync(
                    accessNumbers[random.Next(accessNumbers.Count)],
                    serialNumbers[random.Next(serialNumbers.Count)],
                    random,
                    token);
                logger.LogInformation("Inserted demo DeviceLogs record {LogId}", id);

                await Task.Delay(
                    TimeSpan.FromSeconds(random.Next(simulation.MinimumDelaySeconds, simulation.MaximumDelaySeconds + 1)),
                    time,
                    token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Demo DeviceLogs insertion failed; a later cycle will retry");
                await Task.Delay(TimeSpan.FromSeconds(1), time, token);
            }
        }
    }

    private async Task<List<string>> LoadAccessNumbersAsync(CancellationToken token)
    {
        await using var db = await accessControlFactory.CreateDbContextAsync(token);
        return await db.Personnels.AsNoTracking()
            .Where(x => !x.IsDeleted && x.AccessNumber != "")
            .Select(x => x.AccessNumber)
            .ToListAsync(token);
    }

    private async Task<List<string>> LoadDeviceSerialNumbersAsync(CancellationToken token)
    {
        await using var db = await accessControlFactory.CreateDbContextAsync(token);
        return await db.ZkDevices.AsNoTracking().Select(x => x.SerialNumber).Where(x => x != "").ToListAsync(token);
    }
}
