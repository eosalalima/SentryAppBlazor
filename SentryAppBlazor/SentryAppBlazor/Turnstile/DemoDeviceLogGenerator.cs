using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SentryAppBlazor.Data;
using SentryAppBlazor.Services;

namespace SentryAppBlazor.Turnstile;

public sealed class DemoDeviceLogGenerator(
    IDbContextFactory<StaffDbContext> staffFactory,
    IDbContextFactory<StudentDbContext> studentFactory,
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

                var accessNumbers = await LoadAccessNumbersAsync(token);
                var serialNumbers = await LoadDeviceSerialNumbersAsync(token);
                if (accessNumbers.Count == 0 || serialNumbers.Count == 0)
                {
                    logger.LogWarning("Demo log was not inserted because no directory access numbers or device serial numbers are available");
                    continue;
                }

                var id = await writer.InsertDemoAsync(
                    accessNumbers[random.Next(accessNumbers.Count)],
                    serialNumbers[random.Next(serialNumbers.Count)],
                    random,
                    token);
                logger.LogInformation("Inserted demo DeviceLogs record {LogId}", id);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Demo DeviceLogs insertion failed; a later cycle will retry");
            }
        }
    }

    private async Task<List<string>> LoadAccessNumbersAsync(CancellationToken token)
    {
        await using var staff = await staffFactory.CreateDbContextAsync(token);
        await using var students = await studentFactory.CreateDbContextAsync(token);
        var staffNumbers = await staff.People.AsNoTracking().Select(x => x.Field15).Where(x => x != "").ToListAsync(token);
        var studentNumbers = await students.People.AsNoTracking().Select(x => x.Field15).Where(x => x != "").ToListAsync(token);
        return staffNumbers.Concat(studentNumbers).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<List<string>> LoadDeviceSerialNumbersAsync(CancellationToken token)
    {
        await using var db = await accessControlFactory.CreateDbContextAsync(token);
        return await db.ZkDevices.AsNoTracking().Select(x => x.SerialNumber).Where(x => x != "").ToListAsync(token);
    }
}
