# Turnstile monitoring architecture and operations

The Interactive Server UI reads only `TurnstileLogState`. `TurnstileLogPollingWorker` creates a fresh Access Control context per cycle, performs the ordered left-join query with a `(TimeLogStamp, Id)` watermark, enriches each result using independent STAFF and STUDENT contexts, attempts the replaceable SMS transport, and publishes the event even when lookup or delivery fails. The feed and recently-seen-ID cache are bounded and thread safe.

`DemoDeviceLogGenerator` is a separate hosted service. It publishes clearly marked, temporary sample events directly to `TurnstileLogState`, allowing Demo mode to run without SQL Server and without writing to any database. Live mode continues to use the polling worker and the configured databases. `DeviceLogWriter` backs only the explicit `POST /admin/test-logs` administrative endpoint, which returns 404 unless both non-live mode and `EnableManualTestLogs` are configured. Deployments should additionally place this route behind their established authentication gateway/policy.

## Configuration and safe demo enablement

Supply secrets with user-secrets, a secret store, or environment variables (`ConnectionStrings__AccessControlDb`, `ConnectionStrings__StaffDb`, and `ConnectionStrings__StudentDb`). Committed values are intentionally blank. Production defaults are `Simulation:IsLiveMode=true`, with automatic and manual generation false.

To run without a database, choose **Demo**, enable **Automatic demo logs**, and apply the settings. Applying settings writes both safety controls (`IsLiveMode=false` and `EnableSimulatedLogs=true`) to `sentryconfig.json`; the generator then publishes temporary marked events to the normal UI state. Manual database insertion is separate and requires `Simulation__EnableManualTestLogs=true`, a secret `Simulation__AdministrationKey`, the matching `X-Test-Log-Key` header, and a JSON request containing `accessNumber`, `deviceSerialNumber`, and one of `IN`, `OUT`, or `BREAK OUT`.

## SQL permissions

Use separate least-privilege identities where possible. Monitoring needs `SELECT` on Access Control `dbo.DeviceLogs`, `dbo.Personnels`, and `dbo.ZKDevices`; lookup needs `SELECT` on each directory's `dbo.MyDataTable`. Demo/manual writing needs only `INSERT` on Access Control `dbo.DeviceLogs`. Do not grant schema modification, broad database roles, or directory writes.

## Run and verify

Install the .NET 10 SDK, then run `dotnet restore SentryAppBlazor/SentryAppBlazor.slnx`, `dotnet format SentryAppBlazor/SentryAppBlazor.slnx --verify-no-changes`, `dotnet build SentryAppBlazor/SentryAppBlazor.slnx -c Release --no-restore`, and `dotnet test SentryAppBlazor/SentryAppBlazor.slnx -c Release --no-build`. Start with `dotnet run --project SentryAppBlazor/SentryAppBlazor/SentryAppBlazor.csproj`. SQL transient errors are logged and retried on later hosted-service cycles; cancellation is propagated through delays, EF calls, and SMS calls.
