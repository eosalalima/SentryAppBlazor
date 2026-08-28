using Microsoft.EntityFrameworkCore;
using SentryAppBlazor.Data;

namespace SentryAppBlazor.Turnstile;
public sealed class DeviceLogWriter(IDbContextFactory<AccessControlDbContext> factory, TimeProvider time, ILogger<DeviceLogWriter> logger)
{
    public async Task<Guid> InsertDemoAsync(Random random, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var accessNumbers = await db.Personnels.AsNoTracking()
            .Where(person => !person.IsDeleted)
            .Select(person => person.AccessNumber)
            .ToListAsync(token);
        var serialNumbers = await db.ZkDevices.AsNoTracking()
            .Where(device => !device.IsDeleted)
            .Select(device => device.SerialNumber)
            .ToListAsync(token);

        // Reference rows improve the display, but are not required by the polling
        // query (it deliberately uses LEFT JOINs). Always insert a DeviceLogs row
        // so demo events exercise exactly the same database path as real events.
        var accessNumber = accessNumbers.Count == 0
            ? $"DEMO-{random.Next(1, 10_000):0000}"
            : accessNumbers[random.Next(accessNumbers.Count)];
        var serialNumber = serialNumbers.Count == 0
            ? "DEMO-GATE-1"
            : serialNumbers[random.Next(serialNumbers.Count)];

        if (accessNumbers.Count == 0)
            logger.LogWarning("No active personnel were found; inserting the demo DeviceLogs row with access number {AccessNumber}", accessNumber);
        if (serialNumbers.Count == 0)
            logger.LogWarning("No active devices were found; inserting the demo DeviceLogs row with serial number {SerialNumber}", serialNumber);

        return await InsertAsync(
            db,
            accessNumber,
            serialNumber,
            DemoDeviceLogGenerator.LogTypes[random.Next(DemoDeviceLogGenerator.LogTypes.Length)],
            "TEST", "20", "1", "200",
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
