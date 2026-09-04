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
    public const string PersonnelSql = """
        SELECT [Id], [Field01] AS IDNumber, [Field02] AS LastName,
               [Field03] AS FirstName, [Field04] AS MiddleInitial,
               [Field13] AS MobileNumber, [Field15] AS AccessNumber,
               'STAFF' AS PersonnelType
        FROM [STAFF].[dbo].[MyDataTable]
        UNION
        SELECT [Id], [Field01] AS IDNumber, [Field02] AS LastName,
               [Field03] AS FirstName, [Field04] AS MiddleInitial,
               [Field13] AS MobileNumber, [Field15] AS AccessNumber,
               'STUDENT' AS PersonnelType
        FROM [STUDENT].[dbo].[MyDataTable]
        ORDER BY [Field02], [Field03]
        """;
    private string? previousDemoAccessNumber;

    public async Task<Guid?> InsertDemoAsync(DemoSelection selection, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(selection.AccessNumber) ||
            string.IsNullOrWhiteSpace(selection.DeviceSerialNumber) ||
            !DemoDeviceLogGenerator.LogTypes.Contains(selection.LogType, StringComparer.OrdinalIgnoreCase))
            return null;

        var accessNumber = selection.AccessNumber.Trim();
        var serial = selection.DeviceSerialNumber.Trim();
        var personnelExists = (await ReadAccessNumbersAsync(staffFactory, "STAFF", token))
            .Concat(await ReadAccessNumbersAsync(studentFactory, "STUDENT", token))
            .Contains(accessNumber, StringComparer.OrdinalIgnoreCase);
        var deviceExists = (await ReadSerialNumbersAsync(token)).Contains(serial, StringComparer.OrdinalIgnoreCase);
        if (!personnelExists || !deviceExists) return null;

        return await InsertAsync(accessNumber, serial, selection.LogType, "TEST", "20", "1", "200", token);
    }

    public async Task<Guid?> InsertDemoAsync(Random random, CancellationToken token)
    {
        var accessNumbers = (await ReadCombinedAccessNumbersAsync(token))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var serialNumbers = await ReadSerialNumbersAsync(token);

        if (accessNumbers.Count == 0 || serialNumbers.Count == 0)
            return null;

        var choices = accessNumbers.Count > 1
            ? accessNumbers.Where(x => x != previousDemoAccessNumber).ToList()
            : accessNumbers;
        var accessNumber = choices[random.Next(choices.Count)];
        previousDemoAccessNumber = accessNumber;
        var serialNumber = serialNumbers[random.Next(serialNumbers.Count)];

        // Discovery queries are deliberately isolated from the write context. A
        // missing legacy ZKDevices column or unavailable directory must not leave
        // the context in a failed state and suppress an otherwise valid insert.
        await using var db = await factory.CreateDbContextAsync(token);
        return await InsertAsync(db, accessNumber, serialNumber,
            DemoDeviceLogGenerator.LogTypes[random.Next(DemoDeviceLogGenerator.LogTypes.Length)],
            "TEST", "20", "1", "200", token);
    }

    private async Task<List<string>> ReadCombinedAccessNumbersAsync(CancellationToken token)
    {
        try
        {
            await using var staff = await staffFactory.CreateDbContextAsync(token);
            if (staff.Database.IsSqlServer())
            {
                var connection = staff.Database.GetDbConnection();
                await connection.OpenAsync(token);
                await using var command = connection.CreateCommand();
                command.CommandText = PersonnelSql;
                await using var reader = await command.ExecuteReaderAsync(token);
                var accessNumberOrdinal = reader.GetOrdinal("AccessNumber");
                var accessNumbers = new List<string>();
                while (await reader.ReadAsync(token))
                    if (!reader.IsDBNull(accessNumberOrdinal) && reader.GetString(accessNumberOrdinal) is { Length: > 0 } accessNumber)
                        accessNumbers.Add(accessNumber);
                return accessNumbers;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Unable to execute the combined STAFF/STUDENT personnel query; trying the individual configured databases");
        }

        // SQLite test/demo stores and installations on different SQL Server
        // instances cannot execute a three-part cross-database query. Preserve
        // the same UNION semantics through the two configured contexts.
        return (await ReadAccessNumbersAsync(staffFactory, "STAFF", token))
            .Concat(await ReadAccessNumbersAsync(studentFactory, "STUDENT", token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<List<string>> ReadSerialNumbersAsync(CancellationToken token)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(token);
            return await db.ZkDevices.AsNoTracking()
                .Where(x => !x.IsDeleted && x.SerialNumber != "")
                .OrderBy(x => x.SerialNumber)
                .Select(x => x.SerialNumber)
                .ToListAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception,
                "Unable to read ZKDevices; demo insertion will be skipped");
            return [];
        }
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
                "Unable to read configured {DirectorySource} database",
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
        // managed by this application. Set every required and nullable value
        // explicitly and let EF build the parameterized INSERT. Use the application
        // clock for the inserted event as well as the poller.
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
