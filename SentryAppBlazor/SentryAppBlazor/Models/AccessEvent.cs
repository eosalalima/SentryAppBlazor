namespace SentryAppBlazor.Models;

public enum AccessDirection { Entry, Exit }

public sealed record AccessEvent(
    long UniqueLogId,
    DateTimeOffset Timestamp,
    string PhotoId,
    string PersonnelId,
    string PersonnelName,
    string DeviceId,
    string DeviceName,
    AccessDirection Direction,
    string? MobileNumber = null);
