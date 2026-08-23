namespace SentryAppBlazor.Data;

public sealed class DeviceLog
{
    public Guid Id { get; set; }
    public DateTimeOffset DateCreated { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime RecordDate { get; set; }
    public DateTimeOffset TimeLogStamp { get; set; }
    public string? AccessNumber { get; set; }
    public string? DeviceSerialNumber { get; set; }
    public string? CardNo { get; set; }
    public string? SiteCode { get; set; }
    public string? LinkId { get; set; }
    public string? Event { get; set; }
    public string? EventAddress { get; set; }
    public string? LogType { get; set; }
    public string? VerifyMode { get; set; }
    public int Index { get; set; }
    public bool? HasMask { get; set; }
    public decimal? Temperature { get; set; }
    public bool? IsNotified { get; set; }
}
public sealed class Personnel { public string AccessNumber { get; set; } = ""; public bool IsDeleted { get; set; } public string? LastName { get; set; } public string? FirstName { get; set; } public string? PhotoId { get; set; } }
public sealed class ZkDevice { public string SerialNumber { get; set; } = ""; public string? Name { get; set; } public bool IsDeleted { get; set; } }
public sealed class DirectoryPerson { public string? Field01 { get; set; } public string? Field02 { get; set; } public string? Field03 { get; set; } public string? Field04 { get; set; } public string? Field13 { get; set; } public string Field15 { get; set; } = ""; }
public sealed class TurnstileLogRow
{
    public Guid TimeLogId { get; set; } public DateTimeOffset TimeLogStamp { get; set; }
    public string? LogType { get; set; } public string? AccessNumber { get; set; } public string? DeviceSerialNumber { get; set; } public string? VerifyMode { get; set; }
    public string? LastName { get; set; } public string? FirstName { get; set; } public string? PhotoId { get; set; }
    public string? Event { get; set; } public string? EventAddress { get; set; } public string? DeviceName { get; set; }
}
