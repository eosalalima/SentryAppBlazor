using Microsoft.EntityFrameworkCore;
using SentryAppBlazor.Data;

namespace SentryAppBlazor.Turnstile;
public sealed class DeviceLogWriter(
    IDbContextFactory<AccessControlDbContext> factory,
    IDbContextFactory<PersonnelsDbContext> personnelsFactory,
    ILogger<DeviceLogWriter> logger)
{
    public async Task<Guid?> InsertDemoAsync(Random random, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var personnels = await personnelsFactory.CreateDbContextAsync(token);

        // Resolve references with narrow SQL queries rather than materializing
        // mapped entities. Access-control databases vary between deployments and
        // may not contain every column represented by our read models.
        var accessNumber = await SelectDemoReferenceAsync(
            personnels, "SELECT TOP (1) AccessNumber AS Value FROM dbo.Personnels WHERE IsDeleted = 0 AND AccessNumber <> '' ORDER BY NEWID()", token);
        var serialNumber = await SelectDemoReferenceAsync(
            db, "SELECT TOP (1) SerialNumber AS Value FROM dbo.ZKDevices WHERE IsDeleted = 0 AND SerialNumber <> '' ORDER BY NEWID()", token);

        if (accessNumber is null || serialNumber is null)
        {
            logger.LogWarning(
                "Skipping demo DeviceLogs insert because no active {MissingReferences} records exist",
                accessNumber is null && serialNumber is null ? "Personnel or ZKDevice" : accessNumber is null ? "Personnel" : "ZKDevice");
            return null;
        }

        logger.LogDebug(
            "Creating demo DeviceLogs record for personnel {AccessNumber} at device {SerialNumber}",
            accessNumber,
            serialNumber);

        return await InsertAsync(
            db,
            accessNumber,
            serialNumber,
            DemoDeviceLogGenerator.LogTypes[random.Next(DemoDeviceLogGenerator.LogTypes.Length)],
            "TEST",
            "20",
            "1",
            "200",
            token);
    }

    private static async Task<string?> SelectDemoReferenceAsync(
        DbContext db, string sql, CancellationToken token) =>
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
