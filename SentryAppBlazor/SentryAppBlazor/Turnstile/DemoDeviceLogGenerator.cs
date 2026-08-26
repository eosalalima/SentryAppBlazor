using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SentryAppBlazor.Data;
using SentryAppBlazor.Services;

namespace SentryAppBlazor.Turnstile;
public sealed class DemoDeviceLogGenerator(IDbContextFactory<AccessControlDbContext> factory,DeviceLogWriter writer,TurnstilePollingController controller,IOptionsMonitor<SimulationOptions> settings,IOptionsMonitor<MonitoringOptions> monitoringSettings,TimeProvider time,Random random,ILogger<DemoDeviceLogGenerator> logger):BackgroundService
{
    public static readonly string[] LogTypes=["IN","OUT","BREAK OUT"];
    public static bool ShouldGenerate(SimulationOptions simulation, MonitoringOptions monitoring, bool monitoringActive) =>
        !simulation.IsLiveMode &&
        monitoring.OperatingMode.Equals("Demo", StringComparison.OrdinalIgnoreCase) &&
        monitoring.EnableSimulatedLogs &&
        monitoringActive;

    protected override async Task ExecuteAsync(CancellationToken token) { while(!token.IsCancellationRequested) try { var o=settings.CurrentValue; await Task.Delay(TimeSpan.FromSeconds(random.Next(o.MinimumDelaySeconds,o.MaximumDelaySeconds+1)),time,token); if(!ShouldGenerate(settings.CurrentValue,monitoringSettings.CurrentValue,controller.IsActive)) continue; await GenerateOnceAsync(token); } catch(OperationCanceledException) when(token.IsCancellationRequested){break;} catch(Exception ex){logger.LogError(ex,"Simulated log generation failed; a later cycle will retry");} }
    internal async Task<bool> GenerateOnceAsync(CancellationToken token) { await using var db=await factory.CreateDbContextAsync(token); var people=await db.Personnels.Where(x=>!x.IsDeleted&&!string.IsNullOrEmpty(x.AccessNumber)).Select(x=>x.AccessNumber).ToListAsync(token); var deviceId=monitoringSettings.CurrentValue.DeviceId?.Trim(); var deviceQuery=db.ZkDevices.Where(x=>!x.IsDeleted&&!string.IsNullOrEmpty(x.SerialNumber)); if(!string.IsNullOrEmpty(deviceId)&&!deviceId.Equals("all",StringComparison.OrdinalIgnoreCase)) deviceQuery=deviceQuery.Where(x=>x.SerialNumber==deviceId); var devices=await deviceQuery.Select(x=>x.SerialNumber).ToListAsync(token); if(people.Count==0||devices.Count==0){logger.LogWarning("Simulation skipped because no active personnel or monitored device is available");return false;} await writer.InsertAsync(people[random.Next(people.Count)],devices[random.Next(devices.Count)],LogTypes[random.Next(LogTypes.Length)],"TEST",token);return true; }
}
