# Sentry Turnstile Monitoring System — implementation report

## Implemented architecture

The repository is now one .NET 10 Blazor Web App using Interactive Server rendering. A singleton hosted `MonitoringState` owns the single event coordinator, bounded ID deduplication set, FIFO queue, Spotlight lifecycle, recent-feed retention, and cancellation-aware timing. Demo events enter this same pipeline and never access a database. The responsive monitoring screen includes lifecycle controls, explicit status text, current-event Spotlight, recent activity, protected photo URLs, and a server-only settings summary.

`PhotoService` accepts extension-free photo IDs, reduces them with `Path.GetFileName`, verifies the canonical result remains below the configured root, serves JPG content from the server, and otherwise returns the SVG placeholder. The physical directory is never rendered.

## Configuration

`Monitoring` settings are bound and data-annotation validated at startup. Defaults are Demo mode, all devices, 500 ms polling, 5 second Spotlight, 10 second feed retention, 3 second startup lookback, 20 rows per poll, COM4, 30 second SMS timeout, two retries, and SMS disabled. Values currently require an application restart. Override secrets and environment-specific paths with IIS environment variables such as `Monitoring__PhotosPath`; do not store a production connection string in this repository.

## Assumptions and deliberately deferred integration

No database schema or anonymized rows were available. Consequently no table/column names, SQL query, device source, direction rule, personnel relationship, unique-log mapping, or mobile-number mapping has been invented. Live mode visibly reports that schema mapping is required. Likewise, modem model, serial parameters, recipient source, authentication policy, and administrator authorization policy are unconfirmed, so SMS transmission and mutable browser settings are intentionally disabled. Demo photo IDs fall back to the placeholder unless matching files are installed.

Before Live mode can be completed, provide the `CREATE TABLE` definitions for `DeviceLogs` and related device/personnel tables, anonymized rows, stable unique ID, timestamp, PhotoId type, confirmed IN/OUT rule, device key/name source, personnel name/mobile source, modem configuration, and administrator policy.

## IIS deployment

1. Install IIS and the .NET 10 Hosting Bundle, then reboot or restart IIS.
2. Run `dotnet publish SentryAppBlazor/SentryAppBlazor/SentryAppBlazor.csproj -c Release -r win-x64 --self-contained false -o <publish-folder>`.
3. Back up the current site files and configuration. Copy the publish output to a versioned deployment folder.
4. Create an IIS application pool using **No Managed Code**. Point the site/application at the deployment folder.
5. Set environment-specific `Monitoring__*` values in protected IIS configuration. Never place the production connection string in source control.
6. Grant the application-pool identity read/execute access to the deployment folder and read-only access to the configured photo folder. Later, grant only the confirmed read-only SQL permissions and COM-port access required by the final integrations.
7. Configure HTTPS bindings, stdout/event logging, rapid-fail protection, and an intentional recycle schedule. Verify `/`, `/settings`, and a missing `/photos/test` request (which must return the placeholder).
8. For rollback, stop the site, restore the prior versioned physical path and protected configuration backup, start the site, and repeat the health checks.

## Verification status and known limitations

Static JSON validation and Git whitespace validation were completed. The container does not include the .NET SDK, so compilation, automated tests, runtime smoke tests, and browser screenshots could not be performed here. Live SQL, modem/SMS behavior, authentication, durable checkpoints across process restarts, multi-instance coordination, and editable persistent settings remain blocked on the required source information above.
