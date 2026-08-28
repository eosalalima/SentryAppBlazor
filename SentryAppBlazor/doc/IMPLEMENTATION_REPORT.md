# Turnstile monitoring architecture and operations

The Interactive Server UI reads only `TurnstileLogState`. `TurnstileLogPollingWorker` creates a fresh Access Control context per cycle, performs the ordered left-join query with a `(TimeLogStamp, Id)` watermark, enriches each result using independent STAFF and STUDENT contexts, attempts the replaceable SMS transport, and publishes the event even when lookup or delivery fails. The feed and recently-seen-ID cache are bounded and thread safe.

`DemoDeviceLogGenerator` is a separate hosted service. It uses active personnel and devices when available, otherwise marked demo identifiers, and always inserts a record through the parameterized `DeviceLogWriter`; the polling worker—not the simulator—publishes it. Insert failures are logged and retried on a later cycle and are never replaced with an in-memory event. The same writer backs `POST /admin/test-logs`. That endpoint returns 404 unless both non-live mode and `EnableManualTestLogs` are configured. Deployments should additionally place this administrative route behind their established authentication gateway/policy.

## Configuration and safe demo enablement

Supply secrets with user-secrets, a secret store, or environment variables (`ConnectionStrings__AccessControlDb`, `ConnectionStrings__StaffDb`, and `ConnectionStrings__StudentDb`). Committed values are intentionally blank. Production defaults are `Simulation:IsLiveMode=true`, with automatic and manual generation false.

For an isolated demo database only, choose **Demo**, enable **Automatic demo logs**, apply the settings, verify the SQL identity cannot reach production, then start monitoring in the UI. Applying settings writes both safety controls (`IsLiveMode=false` and `EnableSimulatedLogs=true`) to `sentryconfig.json`. The generator inserts a marked DeviceLogs row and the normal polling worker reads, enriches, and displays it. Turn automatic logs off before changing the connection. Manual insertion additionally requires `Simulation__EnableManualTestLogs=true`, a secret `Simulation__AdministrationKey`, the matching `X-Test-Log-Key` header, and a JSON request containing `accessNumber`, `deviceSerialNumber`, and one of `IN`, `OUT`, or `BREAK OUT`.

## SQL permissions

Use separate least-privilege identities where possible. Monitoring needs `SELECT` on Access Control `dbo.DeviceLogs`, `dbo.Personnels`, and `dbo.ZKDevices`; lookup needs `SELECT` on each directory's `dbo.MyDataTable`. Demo/manual writing needs only `INSERT` on Access Control `dbo.DeviceLogs`. Do not grant schema modification, broad database roles, or directory writes.

## Run and verify

Install the .NET 10 SDK, then run `dotnet restore SentryAppBlazor/SentryAppBlazor.slnx`, `dotnet format SentryAppBlazor/SentryAppBlazor.slnx --verify-no-changes`, `dotnet build SentryAppBlazor/SentryAppBlazor.slnx -c Release --no-restore`, and `dotnet test SentryAppBlazor/SentryAppBlazor.slnx -c Release --no-build`. Start with `dotnet run --project SentryAppBlazor/SentryAppBlazor/SentryAppBlazor.csproj`. SQL transient errors are logged and retried on later hosted-service cycles; cancellation is propagated through delays, EF calls, and SMS calls.
