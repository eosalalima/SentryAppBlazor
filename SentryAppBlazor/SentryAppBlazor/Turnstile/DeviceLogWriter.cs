using Microsoft.EntityFrameworkCore;
using SentryAppBlazor.Data;

namespace SentryAppBlazor.Turnstile;
public sealed class DeviceLogWriter(IDbContextFactory<AccessControlDbContext> factory, TimeProvider time)
{
    public Task<Guid> InsertDemoAsync(string accessNumber, string serial, Random random, CancellationToken token) =>
        InsertAsync(accessNumber, serial,
            DemoDeviceLogGenerator.LogTypes[random.Next(DemoDeviceLogGenerator.LogTypes.Length)],
            "Test",
            DemoDeviceLogGenerator.Events[random.Next(DemoDeviceLogGenerator.Events.Length)],
            DemoDeviceLogGenerator.EventAddresses[random.Next(DemoDeviceLogGenerator.EventAddresses.Length)],
            DemoDeviceLogGenerator.VerifyModes[random.Next(DemoDeviceLogGenerator.VerifyModes.Length)],
            token);

    public async Task<Guid> InsertAsync(string accessNumber,string serial,string logType,string marker,CancellationToken token)
        => await InsertAsync(accessNumber, serial, logType, marker, "20", "1", "200", token);

    private async Task<Guid> InsertAsync(string accessNumber, string serial, string logType, string cardNo, string eventCode, string eventAddress, string verifyMode, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(accessNumber)||string.IsNullOrWhiteSpace(serial)||!DemoDeviceLogGenerator.LogTypes.Contains(logType)) throw new ArgumentException("Valid personnel, device, and log type are required.");
        var now=time.GetLocalNow(); var id=Guid.NewGuid(); await using var db=await factory.CreateDbContextAsync(token);
        await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO dbo.DeviceLogs (Id,DateCreated,IsDeleted,RecordDate,TimeLogStamp,AccessNumber,DeviceSerialNumber,CardNo,SiteCode,LinkId,Event,EventAddress,LogType,VerifyMode,[Index],HasMask,Temperature,IsNotified)
VALUES ({id},{now},0,{now.DateTime},{now},{accessNumber},{serial},{cardNo},NULL,NULL,{eventCode},{eventAddress},{logType},{verifyMode},0,NULL,NULL,NULL)",token); return id;
    }
}
