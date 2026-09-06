# Turnstile monitoring implementation

## Architecture

The .NET 10 application is an Interactive Server Blazor Web App. A singleton `TurnstilePollingController` owns the idempotent running state and Demo/Live mode. The two hosted services are created once by DI: `TurnstileLogPollingWorker` is the only reader in both modes, while `DemoDeviceLogGenerator` can write only during an active Demo session. Demo rows therefore reach `TurnstileLogState` only after a database round trip.

The existing monitoring layout and the reference-inspired two-column “history plus current event” flow were retained. The upstream reference repository could not be fetched in this build environment (the HTTPS proxy returned 403), so schema and behavior decisions were also checked against the target's existing EF mappings, prior implementation report, and tests rather than copied files.

## Configuration

`Monitoring` supports the following restart-free persisted values in `sentryconfig.json` (the workers read the store each cycle):

| Setting | Default | Purpose |
| --- | ---: | --- |
| `Mode` | `Demo` | Monitoring operating mode (`Demo` or `Live`), selected in Monitoring Settings |
| `PollingIntervalMs` | `500` | Poll timer period |
| `StartupLookbackSeconds` | `3` | Cursor recovery window on every start |
| `MaximumRowsPerPoll` | `20` | Ordered SQL batch size |
| `DemoMinimumDelaySeconds` | `1` | Minimum random demo delay |
| `DemoMaximumDelaySeconds` | `10` | Maximum inclusive random demo delay |
| `HighlightDurationMs` | `5000` | Spotlight duration per event |
| `FeedItemTtlSeconds` | `10` | Recent-feed lifetime after spotlight |
| `MaximumFeedItemsPerCategory` | `10` | Independent IN and OUT capacity |
| `EnableFlowDiagnostics` | `false` | Detailed flow logging |
| `ExternalPhotoDirectory` | empty | Protected directory behind `/photos` |

The supplied SQL Server connection strings are retained in both configuration sources and remain editable in Monitoring Settings. `PersonnelsDb` defaults to the Access Control connection because `Personnels` is part of that database. Applying settings does not require an application restart.

## Database assumptions

* `dbo.DeviceLogs.Id` is a GUID and `TimeLogStamp`/`DateCreated` are SQL Server `datetimeoffset`-compatible values. SQL Server compares offsets by UTC instant; cursors use `DateTimeOffset` and initialize from UTC.
* The stable cursor is `(TimeLogStamp, Id)`, with filtering and ascending ordering performed by the database. A 100-batch guard bounds immediate backlog draining.
* All requested `DeviceLogs` columns are mapped and demo inserts explicitly store `TEST`, event `20`, address `1`, verification mode `200`, index `0`, and null optional sensor/link values. No migration or schema mutation is performed.
* SQL Server demo personnel are read with the supplied cross-database STAFF/ STUDENT `UNION` statement. Separate-context querying preserves equivalent behavior for SQLite tests or deployments where a cross-database query is unavailable. A personnel and an active Access Control `dbo.ZKDevices` row are both required; one of each and one allowed log type are selected randomly per event.
* Log categories are centralized as `IN`, `OUT`, and `BREAK OUT`; OUT and BREAK OUT share the outgoing feed.
* Application-generated timestamps use local `DateTimeOffset`; cursor comparisons are instant-safe, and the UI calls `ToLocalTime()` for display.
* Missing optional enrichment never suppresses an event. SMS transport remains deliberately deferred; the registered implementation reports that transport is not configured rather than fabricating success.

## Operations and security

Start resets the cursor and activates polling. Demo automatically selects and inserts source values; Live never invokes demo insertion. Stop makes both workers idle, and repeated commands are no-ops. The newest record always replaces the spotlight immediately, then moves to its direction feed after the configured duration. Spotlight/feed state, processed IDs, FIFO eviction, and expiry are lock-protected; callbacks are raised outside locks and Blazor dispatches renders through `InvokeAsync`.

Photo URLs never reveal the configured directory. The catch-all `/photos/{photoId}` endpoint rejects traversal/rooted/separator identifiers, allows only JPEG, PNG, GIF, WebP, and SVG extensions, verifies the canonical path remains beneath the configured root, and falls back to the application-owned SVG.

## Outstanding work

* Validate all EF column types and directory-field semantics against each deployment before production rollout; no live SQL Server was available here.
* Replace the no-op `ISmsSender` in a later phase if SMS delivery is approved. Keep transmission outside UI components and preferably behind its own durable worker/outbox.
* Consider cached device/directory reference data if production volume makes the current batched lookups material.
