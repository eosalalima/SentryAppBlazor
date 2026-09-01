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
        var accessNumbers = (await ReadAccessNumbersAsync(staffFactory, "STAFF", token))
            .Concat(await ReadAccessNumbersAsync(studentFactory, "STUDENT", token))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var serialNumbers = await db.ZkDevices.AsNoTracking()
            .Where(x => !x.IsDeleted && x.SerialNumber != "")
            .OrderBy(x => x.SerialNumber)
            .Select(x => x.SerialNumber)
            .ToListAsync(token);

        // Directory databases are enrichment sources, not a prerequisite for a
        // DeviceLogs insert.  A missing/unavailable STAFF or STUDENT database used
        // to make the generator silently produce no row at all.  Preserve nulls
        // when source data is unavailable; those columns are optional in the
        // Access Control schema and the event itself must still be persisted.
        var choices = accessNumbers.Count > 1
            ? accessNumbers.Where(x => x != previousDemoAccessNumber).ToList()
            : accessNumbers;
        var accessNumber = choices.Count == 0 ? null : choices[random.Next(choices.Count)];
        previousDemoAccessNumber = accessNumber;
        var serialNumber = serialNumbers.Count == 0 ? null : serialNumbers[random.Next(serialNumbers.Count)];

        return await InsertAsync(db, accessNumber, serialNumber,
            DemoDeviceLogGenerator.LogTypes[random.Next(DemoDeviceLogGenerator.LogTypes.Length)],
            "Test",
            EventCodes[random.Next(EventCodes.Length)],
            EventAddresses[random.Next(EventAddresses.Length)],
            VerifyModes[random.Next(VerifyModes.Length)], token);
    }

    private async Task<List<string>> ReadAccessNumbersAsync<TContext>(
        IDbContextFactory<TContext> contextFactory,
        string source,
        CancellationToken token) where TContext : DirectoryDbContext
    {
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(token);
            return await context.People.AsNoTracking()
                .Where(x => x.Field15 != "")
                .Select(x => x.Field15)
                .ToListAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception,
                "Unable to read {DirectorySource}; inserting the DeviceLogs record without that optional directory data",
                source);
            return [];
        }
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
        // managed by this application. Set every mapped value explicitly, but let
        // EF build the INSERT. The previous SQL Server-only command hard-coded the
        // complete column list; deployments whose DeviceLogs schema predates one
        // of the optional columns rejected the whole insert with "invalid column".
        // EF uses the configured model and keeps the write path identical for the
        // production and demo providers.
        // Use the application clock for the inserted event as well as the poller.
        // A SQL Server clock behind the web server would otherwise place a brand
        // new row behind the poller's cursor, making the insert invisible forever.
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
