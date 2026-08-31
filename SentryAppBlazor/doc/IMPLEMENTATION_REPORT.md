# Turnstile monitoring architecture and operations

The Interactive Server UI reads only `TurnstileLogState`. `TurnstileLogPollingWorker` creates a fresh Access Control context per cycle, performs the ordered left-join query with a `(TimeLogStamp, Id)` watermark, enriches each result using independent STAFF and STUDENT contexts, attempts the replaceable SMS transport, and publishes the event even when lookup or delivery fails. The feed and recently-seen-ID cache are bounded and thread safe.

`DemoDeviceLogGenerator` is a separate hosted service. When the operator presses **Start** in Demo mode, it creates clearly marked sample records directly in Access Control `dbo.DeviceLogs` at the configured interval. Pressing **Stop** prevents further inserts. The generator does not create or enqueue an in-memory substitute for a database record.

## Configuration and demo operation

Supply secrets with user-secrets, a secret store, or environment variables (`ConnectionStrings__AccessControlDb`, `ConnectionStrings__PersonnelsDb`, `ConnectionStrings__StaffDb`, and `ConnectionStrings__StudentDb`). Committed appsettings values are intentionally blank. Access Control supplies the live `DeviceLogs` source and receives demo `DeviceLogs`; Personnels supplies identity; STAFF and STUDENT supply directory/mobile data. All four settings are resolved at operation time, so values applied in the settings page do not require an application restart. `Monitoring:OperatingMode` is the sole mode switch, and `Monitoring:DemoLogIntervalSeconds` sets the fixed delay between inserts.

To generate records, choose **Demo**, set the demo log interval, apply the settings, and press **Start**. The generator prefers the personnel/device references from the newest valid `DeviceLogs` row. If no such row exists, it selects an active access number from Personnels and an active device from Access Control instead, allowing a new or empty `DeviceLogs` table to be seeded. It then inserts a newly timestamped event through the configured Access Control connection. Press **Stop** to end generation.

## SQL permissions

Use separate least-privilege identities where possible. Monitoring needs `SELECT` on Access Control `dbo.DeviceLogs` and `dbo.ZKDevices`, plus `SELECT` on Personnels `dbo.Personnels`; lookup needs `SELECT` on each directory's `dbo.MyDataTable`. Demo writing needs only `INSERT` on Access Control `dbo.DeviceLogs`. Do not grant schema modification, broad database roles, or directory writes.

## Run and verify

Install the .NET 10 SDK, then run `dotnet restore SentryAppBlazor/SentryAppBlazor.slnx`, `dotnet format SentryAppBlazor/SentryAppBlazor.slnx --verify-no-changes`, `dotnet build SentryAppBlazor/SentryAppBlazor.slnx -c Release --no-restore`, and `dotnet test SentryAppBlazor/SentryAppBlazor.slnx -c Release --no-build`. Start with `dotnet run --project SentryAppBlazor/SentryAppBlazor/SentryAppBlazor.csproj`. SQL transient errors are logged and retried on later hosted-service cycles; cancellation is propagated through delays, EF calls, and SMS calls.
