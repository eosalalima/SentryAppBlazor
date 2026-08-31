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

        // Prefer references that the access-control database has already accepted.
        // References are optional on DeviceLogs, so an empty database must still be
        // able to receive demo events without depending on a second database.
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
            var serialNumber = await SelectDemoReferenceAsync(
                db,
                "SELECT TOP (1) SerialNumber AS Value FROM dbo.ZKDevices WHERE IsDeleted = 0 AND SerialNumber IS NOT NULL AND LTRIM(RTRIM(SerialNumber)) <> '' ORDER BY NEWID()",
                token);

            source = new DemoLogSource
            {
                AccessNumber = null,
                DeviceSerialNumber = serialNumber
            };

            logger.LogInformation(
                "No reusable DeviceLogs references were found; inserting a demo event with nullable references");
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
        public string? AccessNumber { get; set; }
        public string? DeviceSerialNumber { get; set; }
    }

    private static async Task<string?> SelectDemoReferenceAsync(
        DbContext db,
        string sql,
        CancellationToken token) =>
        await db.Database.SqlQueryRaw<string>(sql).FirstOrDefaultAsync(token);

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
