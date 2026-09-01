using Microsoft.EntityFrameworkCore;
using SentryAppBlazor.Data;

namespace SentryAppBlazor.Turnstile;

public sealed record DeviceLogInsertRequest(
    string AccessNumber,
    string DeviceSerialNumber,
    string LogType,
    string CardNo);

public sealed class DeviceLogWriter(
    IDbContextFactory<AccessControlDbContext> factory,
    IDbContextFactory<StaffDbContext> staffFactory,
    IDbContextFactory<StudentDbContext> studentFactory,
    ILogger<DeviceLogWriter> logger)
{
    internal static readonly string[] EventCodes = ["0", "105", "20", "202", "214", "23", "27", "41", "42"];
    internal static readonly string[] EventAddresses = ["0", "1", "105", "2", "20", "214"];
    internal static readonly string[] VerifyModes = ["200", "255", "3", "4"];
    private string? previousDemoAccessNumber;

    public async Task<Guid?> InsertDemoAsync(Random random, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var staff = await staffFactory.CreateDbContextAsync(token);
        await using var students = await studentFactory.CreateDbContextAsync(token);

        var staffAccessNumbers = await staff.People.AsNoTracking()
            .Where(x => x.Field15 != "")
            .Select(x => x.Field15)
            .ToListAsync(token);
        var studentAccessNumbers = await students.People.AsNoTracking()
            .Where(x => x.Field15 != "")
            .Select(x => x.Field15)
            .ToListAsync(token);
        var accessNumbers = staffAccessNumbers.Concat(studentAccessNumbers)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var serialNumbers = await db.ZkDevices.AsNoTracking()
            .Where(x => !x.IsDeleted && x.SerialNumber != "")
            .OrderBy(x => x.SerialNumber)
            .Select(x => x.SerialNumber)
            .ToListAsync(token);

        if (accessNumbers.Count == 0 || serialNumbers.Count == 0)
        {
            logger.LogWarning("Cannot insert a demo DeviceLogs record because STAFF/STUDENT MyDataTable has no Field15 value or ZKDevices has no active serial number");
            return null;
        }

        // Do not clone the most recent DeviceLogs row: doing so permanently shows
        // the same person. Choose from the STAFF/STUDENT union and, when possible,
        // avoid repeating the person selected by the preceding demo insert.
        var choices = accessNumbers.Count > 1
            ? accessNumbers.Where(x => x != previousDemoAccessNumber).ToList()
            : accessNumbers;
        var accessNumber = choices[random.Next(choices.Count)];
        previousDemoAccessNumber = accessNumber;
        var serialNumber = serialNumbers[random.Next(serialNumbers.Count)];

        return await InsertAsync(db, accessNumber, serialNumber,
            DemoDeviceLogGenerator.LogTypes[random.Next(DemoDeviceLogGenerator.LogTypes.Length)],
            "Test",
            EventCodes[random.Next(EventCodes.Length)],
            EventAddresses[random.Next(EventAddresses.Length)],
            VerifyModes[random.Next(VerifyModes.Length)], token);
    }

    public Task<Guid> InsertAsync(
        string accessNumber,
        string serial,
        string logType,
        string cardNo,
        CancellationToken token) =>
        InsertAsync(accessNumber, serial, logType, cardNo, "20", "1", "200", token);

    private async Task<Guid> InsertAsync(string accessNumber, string serial, string logType, string cardNo, string eventCode, string eventAddress, string verifyMode, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(accessNumber) ||
            string.IsNullOrWhiteSpace(serial) ||
            string.IsNullOrWhiteSpace(cardNo) ||
            !DemoDeviceLogGenerator.LogTypes.Contains(logType, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Valid personnel, device, card number, and log type are required.");
        }

        await using var db = await factory.CreateDbContextAsync(token);
        return await InsertAsync(
            db,
            accessNumber,
            serial,
            logType.ToUpperInvariant(),
            cardNo,
            eventCode,
            eventAddress,
            verifyMode,
            token);
    }

    private async Task<Guid> InsertAsync(AccessControlDbContext db, string? accessNumber, string? serial, string logType, string cardNo, string eventCode, string eventAddress, string verifyMode, CancellationToken token)
    {
        if (!DemoDeviceLogGenerator.LogTypes.Contains(logType)) throw new ArgumentException("A valid log type is required.", nameof(logType));
        var id = Guid.NewGuid();
        var now = DateTimeOffset.Now;

        // DeviceLogs is an existing Access Control table rather than a schema
        // managed by this application.  Write every production column
        // explicitly so SQL Server defaults, missing defaults, and EF's value
        // generation conventions cannot turn demo generation into a no-op.
        // Use the application clock for the inserted event as well as the poller.
        // A SQL Server clock behind the web server would otherwise place a brand
        // new row behind the poller's cursor, making the insert invisible forever.
        if (db.Database.IsSqlServer())
        {
            var affectedRows = await db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO dbo.DeviceLogs
    (Id, DateCreated, IsDeleted, RecordDate, TimeLogStamp, AccessNumber,
     DeviceSerialNumber, CardNo, SiteCode, LinkId, Event, EventAddress,
     LogType, VerifyMode, [Index], HasMask, Temperature, IsNotified)
VALUES
    ({id}, {now}, {false}, {now.DateTime},
     {now}, {accessNumber}, {serial}, {cardNo}, NULL, NULL,
     {eventCode}, {eventAddress}, {logType}, {verifyMode}, {0}, NULL, NULL, NULL)", token);

            if (affectedRows != 1)
                throw new DbUpdateException($"Expected to insert one DeviceLogs row, but inserted {affectedRows}.");

            return id;
        }

        var row = new DeviceLog
        {
            Id = id, DateCreated = now, IsDeleted = false, RecordDate = now.DateTime,
            TimeLogStamp = now, AccessNumber = accessNumber, DeviceSerialNumber = serial,
            CardNo = cardNo, SiteCode = null, LinkId = null,
            Event = eventCode, EventAddress = eventAddress,
            LogType = logType, VerifyMode = verifyMode, Index = 0,
            HasMask = null, Temperature = null, IsNotified = null
        };
        db.DeviceLogs.Add(row);
        await db.SaveChangesAsync(token);
        return row.Id;
    }

}
