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
        var source = await db.DeviceLogs.AsNoTracking()
            .Where(x => !x.IsDeleted && x.AccessNumber != null && x.AccessNumber != "" &&
                        x.DeviceSerialNumber != null && x.DeviceSerialNumber != "")
            .OrderByDescending(x => x.TimeLogStamp).ThenByDescending(x => x.Id)
            .Select(x => new DemoLogSource { AccessNumber = x.AccessNumber, DeviceSerialNumber = x.DeviceSerialNumber })
            .FirstOrDefaultAsync(token);

        if (source is null)
        {
            var personnel = db.Database.IsSqlite()
                ? await db.Set<Personnel>().AsNoTracking().OrderBy(x => x.AccessNumber)
                    .Select(x => x.AccessNumber).FirstOrDefaultAsync(token)
                : null;
            var serial = await db.ZkDevices.AsNoTracking().Where(x => !x.IsDeleted).OrderBy(x => x.SerialNumber)
                .Select(x => x.SerialNumber).FirstOrDefaultAsync(token);
            source = new DemoLogSource { AccessNumber = personnel, DeviceSerialNumber = serial };
        }

        return await InsertAsync(db, source.AccessNumber, source.DeviceSerialNumber,
            DemoDeviceLogGenerator.LogTypes[random.Next(DemoDeviceLogGenerator.LogTypes.Length)],
            "TEST", "20", "1", "200", token);
    }

    private sealed class DemoLogSource
    {
        public string? AccessNumber { get; set; }
        public string? DeviceSerialNumber { get; set; }
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
        var now = DateTimeOffset.UtcNow;
        var row = new DeviceLog
        {
            Id = Guid.NewGuid(), DateCreated = now, RecordDate = now.UtcDateTime.Date,
            TimeLogStamp = now, AccessNumber = accessNumber, DeviceSerialNumber = serial,
            CardNo = cardNo, Event = eventCode, EventAddress = eventAddress,
            LogType = logType, VerifyMode = verifyMode
        };
        db.DeviceLogs.Add(row);
        await db.SaveChangesAsync(token);
        return row.Id;
    }

}
