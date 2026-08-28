using Microsoft.EntityFrameworkCore;
using SentryAppBlazor.Data;

namespace SentryAppBlazor.Turnstile;
public sealed class DeviceLogWriter(
    IDbContextFactory<AccessControlDbContext> factory,
    ILogger<DeviceLogWriter> logger)
{
    public async Task<Guid> InsertDemoAsync(Random random, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);

        // DeviceLogs installations commonly enforce foreign keys to Personnels
        // and ZKDevices. Synthetic DEMO-* identifiers therefore make the INSERT
        // fail even though the polling query uses LEFT JOINs. Select real active
        // keys so the generated row is valid for the deployed schema.
        var accessNumbers = await db.Personnels.AsNoTracking()
            .Where(person => !person.IsDeleted && person.AccessNumber != "")
            .Select(person => person.AccessNumber)
            .ToListAsync(token);
        var serialNumbers = await db.ZkDevices.AsNoTracking()
            .Where(device => !device.IsDeleted && device.SerialNumber != "")
            .Select(device => device.SerialNumber)
            .ToListAsync(token);

        if (accessNumbers.Count == 0 || serialNumbers.Count == 0)
            throw new InvalidOperationException(
                "Demo DeviceLogs require at least one active Personnel and ZKDevice record.");

        var accessNumber = accessNumbers[random.Next(accessNumbers.Count)];
        var serialNumber = serialNumbers[random.Next(serialNumbers.Count)];
        logger.LogDebug(
            "Creating demo DeviceLogs record for personnel {AccessNumber} at device {SerialNumber}",
            accessNumber,
            serialNumber);

        return await InsertAsync(
            db,
            accessNumber,
            serialNumber,
            DemoDeviceLogGenerator.LogTypes[random.Next(DemoDeviceLogGenerator.LogTypes.Length)],
            random.NextInt64(1, 10_000_000_000).ToString(),
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
        var id=Guid.NewGuid();
        // Use the database clock for the row's watermark columns.  The polling
        // cursor is also based on the database clock, so a clock/time-zone skew
        // between IIS and SQL Server cannot make a newly inserted row look old.
        await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO dbo.DeviceLogs (Id,DateCreated,IsDeleted,RecordDate,TimeLogStamp,AccessNumber,DeviceSerialNumber,CardNo,SiteCode,LinkId,Event,EventAddress,LogType,VerifyMode,[Index],HasMask,Temperature,IsNotified)
VALUES ({id},SYSDATETIMEOFFSET(),0,CONVERT(date,SYSDATETIMEOFFSET()),SYSDATETIMEOFFSET(),{accessNumber},{serial},{cardNo},NULL,NULL,{eventCode},{eventAddress},{logType},{verifyMode},0,NULL,NULL,NULL)",token); return id;
    }
}
