using Microsoft.EntityFrameworkCore;
using SentryAppBlazor.Data;

namespace SentryAppBlazor.Turnstile;
public sealed class DeviceLogWriter(
    IDbContextFactory<AccessControlDbContext> factory,
    ILogger<DeviceLogWriter> logger)
{
    public async Task<Guid?> InsertDemoAsync(Random random, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);

        // Base simulation on a record that the live DeviceLogs pipeline has
        // already accepted. This keeps the access-number/device pair valid for
        // this installation and avoids making demo generation depend on a
        // separately configured Personnels database.
        var source = await db.Database.SqlQueryRaw<DemoLogSource>(
            @"SELECT TOP (1) AccessNumber, DeviceSerialNumber
              FROM dbo.DeviceLogs
              WHERE IsDeleted = 0
                AND AccessNumber IS NOT NULL AND LTRIM(RTRIM(AccessNumber)) <> ''
                AND DeviceSerialNumber IS NOT NULL AND LTRIM(RTRIM(DeviceSerialNumber)) <> ''
              ORDER BY TimeLogStamp DESC, Id DESC")
            .FirstOrDefaultAsync(token);

        if (source is null)
        {
            logger.LogWarning(
                "Skipping demo DeviceLogs insert because no live DeviceLogs record with personnel and device references exists");
            return null;
        }

        logger.LogDebug(
            "Creating demo DeviceLogs record for personnel {AccessNumber} at device {SerialNumber}",
            source.AccessNumber,
            source.DeviceSerialNumber);

        return await InsertAsync(
            db,
            source.AccessNumber,
            source.DeviceSerialNumber,
            DemoDeviceLogGenerator.LogTypes[random.Next(DemoDeviceLogGenerator.LogTypes.Length)],
            "TEST",
            "20",
            "1",
            "200",
            token);
    }

    private sealed class DemoLogSource
    {
        public string AccessNumber { get; set; } = string.Empty;
        public string DeviceSerialNumber { get; set; } = string.Empty;
    }

    public async Task<Guid> InsertAsync(string accessNumber,string serial,string logType,string marker,CancellationToken token)
        => await InsertAsync(accessNumber, serial, logType, marker, "20", "1", "200", token);

    private async Task<Guid> InsertAsync(string accessNumber, string serial, string logType, string cardNo, string eventCode, string eventAddress, string verifyMode, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(accessNumber)||string.IsNullOrWhiteSpace(serial)||!DemoDeviceLogGenerator.LogTypes.Contains(logType)) throw new ArgumentException("Valid personnel, device, and log type are required.");
        await using var db=await factory.CreateDbContextAsync(token);
        return await InsertAsync(db, accessNumber, serial, logType, cardNo, eventCode, eventAddress, verifyMode, token);
    }

    private async Task<Guid> InsertAsync(AccessControlDbContext db, string? accessNumber, string? serial, string logType, string cardNo, string eventCode, string eventAddress, string verifyMode, CancellationToken token)
    {
        if (!DemoDeviceLogGenerator.LogTypes.Contains(logType)) throw new ArgumentException("A valid log type is required.", nameof(logType));
        var id=Guid.NewGuid();
        // Use the database clock for the row's watermark columns.  The polling
        // cursor is also based on the database clock, so a clock/time-zone skew
        // between IIS and SQL Server cannot make a newly inserted row look old.
        await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO dbo.DeviceLogs (Id,DateCreated,IsDeleted,RecordDate,TimeLogStamp,AccessNumber,DeviceSerialNumber,CardNo,SiteCode,LinkId,Event,EventAddress,LogType,VerifyMode,[Index],HasMask,Temperature,IsNotified)
VALUES ({id},SYSDATETIMEOFFSET(),0,CONVERT(date,SYSDATETIMEOFFSET()),SYSDATETIMEOFFSET(),{accessNumber},{serial},{cardNo},NULL,NULL,{eventCode},{eventAddress},{logType},{verifyMode},0,NULL,NULL,NULL)",token); return id;
    }
}
