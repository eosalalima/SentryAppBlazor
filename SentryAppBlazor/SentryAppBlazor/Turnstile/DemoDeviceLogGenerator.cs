using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SentryAppBlazor.Data;

namespace SentryAppBlazor.Turnstile;
public sealed class DemoDeviceLogGenerator(IDbContextFactory<AccessControlDbContext> factory,DeviceLogWriter writer,TurnstilePollingController controller,IOptionsMonitor<SimulationOptions> settings,TimeProvider time,Random random,ILogger<DemoDeviceLogGenerator> logger):BackgroundService
{
    public static readonly string[] LogTypes=["IN","OUT","BREAK OUT"];
    public static bool ShouldGenerate(SimulationOptions options, bool monitoringActive) => !options.IsLiveMode && options.EnableSimulatedLogs && monitoringActive;
    protected override async Task ExecuteAsync(CancellationToken token) { while(!token.IsCancellationRequested) try { var o=settings.CurrentValue; await Task.Delay(TimeSpan.FromSeconds(random.Next(o.MinimumDelaySeconds,o.MaximumDelaySeconds+1)),time,token); o=settings.CurrentValue; if(!ShouldGenerate(o,controller.IsActive)) continue; await GenerateOnceAsync(token); } catch(OperationCanceledException) when(token.IsCancellationRequested){break;} catch(Exception ex){logger.LogError(ex,"Simulated log generation failed; a later cycle will retry");} }
    internal async Task<bool> GenerateOnceAsync(CancellationToken token) { await using var db=await factory.CreateDbContextAsync(token); var people=await db.Personnels.Where(x=>!x.IsDeleted&&!string.IsNullOrEmpty(x.AccessNumber)).Select(x=>x.AccessNumber).ToListAsync(token); var devices=await db.ZkDevices.Where(x=>!x.IsDeleted&&!string.IsNullOrEmpty(x.SerialNumber)).Select(x=>x.SerialNumber).ToListAsync(token); if(people.Count==0||devices.Count==0){logger.LogWarning("Simulation skipped because no active personnel or device is available");return false;} await writer.InsertAsync(people[random.Next(people.Count)],devices[random.Next(devices.Count)],LogTypes[random.Next(LogTypes.Length)],"TEST",token);return true; }
}
