using Microsoft.EntityFrameworkCore;
using SentryAppBlazor.Data;

namespace SentryAppBlazor.Turnstile;
public sealed class DeviceLogWriter(IDbContextFactory<AccessControlDbContext> factory, TimeProvider time)
{
    public async Task<Guid> InsertDemoAsync(Random random, string? monitoredDevice, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var accessNumbers = await db.Personnels.AsNoTracking()
            .Where(person => !person.IsDeleted)
            .Select(person => person.AccessNumber)
            .ToListAsync(token);
        var deviceFilter = string.IsNullOrWhiteSpace(monitoredDevice) ||
            monitoredDevice.Equals("all", StringComparison.OrdinalIgnoreCase)
                ? null
                : monitoredDevice.Trim();
        var serialNumbers = await db.ZkDevices.AsNoTracking()
            .Where(device => !device.IsDeleted &&
                (deviceFilter == null || device.SerialNumber == deviceFilter))
            .Select(device => device.SerialNumber)
            .ToListAsync(token);

        if (accessNumbers.Count == 0)
            throw new InvalidOperationException("Demo logs require at least one active personnel record.");
        if (serialNumbers.Count == 0)
            throw new InvalidOperationException("Demo logs require an active device matching the monitoring filter.");

        return await InsertAsync(
            db,
            accessNumbers[random.Next(accessNumbers.Count)],
            serialNumbers[random.Next(serialNumbers.Count)],
            DemoDeviceLogGenerator.LogTypes[random.Next(DemoDeviceLogGenerator.LogTypes.Length)],
            "DEMO",
            DemoDeviceLogGenerator.Events[random.Next(DemoDeviceLogGenerator.Events.Length)],
            DemoDeviceLogGenerator.EventAddresses[random.Next(DemoDeviceLogGenerator.EventAddresses.Length)],
            DemoDeviceLogGenerator.VerifyModes[random.Next(DemoDeviceLogGenerator.VerifyModes.Length)],
            token);
    }

    public async Task<Guid> InsertAsync(string accessNumber,string serial,string logType,string marker,CancellationToken token)
        => await InsertAsync(accessNumber, serial, logType, marker, "20", "1", "200", token);

    private async Task<Guid> InsertAsync(string accessNumber, string serial, string logType, string cardNo, string eventCode, string eventAddress, string verifyMode, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(accessNumber)||string.IsNullOrWhiteSpace(serial)||!DemoDeviceLogGenerator.LogTypes.Contains(logType)) throw new ArgumentException("Valid personnel, device, and log type are required.");
        await using var db=await factory.CreateDbContextAsync(token);
        return await InsertAsync(db, accessNumber, serial, logType, cardNo, eventCode, eventAddress, verifyMode, token);
    }

    private async Task<Guid> InsertAsync(AccessControlDbContext db, string accessNumber, string serial, string logType, string cardNo, string eventCode, string eventAddress, string verifyMode, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(accessNumber)||string.IsNullOrWhiteSpace(serial)||!DemoDeviceLogGenerator.LogTypes.Contains(logType)) throw new ArgumentException("Valid personnel, device, and log type are required.");
        var now=time.GetLocalNow(); var id=Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO dbo.DeviceLogs (Id,DateCreated,IsDeleted,RecordDate,TimeLogStamp,AccessNumber,DeviceSerialNumber,CardNo,SiteCode,LinkId,Event,EventAddress,LogType,VerifyMode,[Index],HasMask,Temperature,IsNotified)
VALUES ({id},{now},0,{now.DateTime},{now},{accessNumber},{serial},{cardNo},NULL,NULL,{eventCode},{eventAddress},{logType},{verifyMode},0,NULL,NULL,NULL)",token); return id;
    }
}
