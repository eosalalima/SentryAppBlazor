# Turnstile monitoring architecture and operations

The Interactive Server UI reads only `TurnstileLogState`. `TurnstileLogPollingWorker` creates a fresh Access Control context per cycle, performs the ordered left-join query with a `(TimeLogStamp, Id)` watermark, enriches each result using independent STAFF and STUDENT contexts, attempts the replaceable SMS transport, and publishes the event even when lookup or delivery fails. The feed and recently-seen-ID cache are bounded and thread safe.

`DemoDeviceLogGenerator` is a separate hosted service. In Demo mode it creates clearly marked sample records in Access Control `dbo.DeviceLogs`. `TurnstileLogPollingWorker` polls those records and publishes them to `TurnstileLogState`, so demo and live traffic use the same database-to-screen pipeline. `DeviceLogWriter` also backs the explicit `POST /admin/test-logs` administrative endpoint, which returns 404 unless both non-live mode and `EnableManualTestLogs` are configured. Deployments should additionally place this route behind their established authentication gateway/policy.

## Configuration and safe demo enablement

Supply secrets with user-secrets, a secret store, or environment variables (`ConnectionStrings__AccessControlDb`, `ConnectionStrings__PersonnelsDb`, `ConnectionStrings__StaffDb`, and `ConnectionStrings__StudentDb`). Committed appsettings values are intentionally blank. Access Control supplies devices and receives `DeviceLogs`; Personnels supplies identity and demo access numbers; STAFF and STUDENT supply directory/mobile data. All four settings are resolved at operation time, so values applied in the settings page do not require an application restart. Production defaults are `Simulation:IsLiveMode=true`, with automatic and manual generation false.

To exercise the end-to-end database pipeline, choose **Demo**, enable **Automatic demo logs**, disable **Live database mode**, and apply the settings. Applying settings writes both safety controls (`IsLiveMode=false` and `EnableSimulatedLogs=true`) to `sentryconfig.json`; the generator selects an active access number through the Personnels connection, selects a device and inserts the marked event through the Access Control connection, and the poller displays it with identity data read from Personnels. Manual database insertion is separate and requires `Simulation__EnableManualTestLogs=true`, a secret `Simulation__AdministrationKey`, the matching `X-Test-Log-Key` header, and a JSON request containing `accessNumber`, `deviceSerialNumber`, and one of `IN`, `OUT`, or `BREAK OUT`.

## SQL permissions

Use separate least-privilege identities where possible. Monitoring needs `SELECT` on Access Control `dbo.DeviceLogs`, `dbo.Personnels`, and `dbo.ZKDevices`; lookup needs `SELECT` on each directory's `dbo.MyDataTable`. Demo/manual writing needs only `INSERT` on Access Control `dbo.DeviceLogs`. Do not grant schema modification, broad database roles, or directory writes.

## Run and verify

Install the .NET 10 SDK, then run `dotnet restore SentryAppBlazor/SentryAppBlazor.slnx`, `dotnet format SentryAppBlazor/SentryAppBlazor.slnx --verify-no-changes`, `dotnet build SentryAppBlazor/SentryAppBlazor.slnx -c Release --no-restore`, and `dotnet test SentryAppBlazor/SentryAppBlazor.slnx -c Release --no-build`. Start with `dotnet run --project SentryAppBlazor/SentryAppBlazor/SentryAppBlazor.csproj`. SQL transient errors are logged and retried on later hosted-service cycles; cancellation is propagated through delays, EF calls, and SMS calls.
