# PgServer

## Constructors

| Constructor | |
| --- | --- |
| `new PgServer(string pgVersion)` | A server of that version with default options |
| `new PgServer(PgServerOptions options)` | See [Options](./options) |

## Methods

| Method | Does |
| --- | --- |
| `StartAsync()` | Downloads and installs the binaries if needed, sets up the instance on its first start, and starts the server, or attaches to a running named instance |
| `StopAsync()` | Stops the server cleanly (fast mode, then immediate mode, then killing the process) |
| `KillAsync()` | Force-kills the server: a last resort |
| `DisposeAsync()` | Stops the server if this object started it, and deletes the instance folder unless it is kept |
| `InstallAsync()` | Downloads and extracts the binaries and extensions without creating an instance or starting a server |
| `GetStatusAsync()` | `PgInstanceStatus`: `State`, `ProcessId`, `Port`, `StartedAt`, also for a server another process started |
| `GetAvailableExtensionsAsync()` | Every extension `CREATE EXTENSION` can use in the install, read from its files |

All but `DisposeAsync()` take an optional `CancellationToken`.

## Properties

| Property | |
| --- | --- |
| `ConnectionString` | `Host`, `Port`, `Username` and `Database`, for Npgsql |
| `PgVersion` | The exact version once started (the spec before that) |
| `PgPort`, `PgUser`, `PgDbName` | Port, superuser and default database |
| `IsRunning`, `IsAttached` | Whether the server runs; whether this object attached to one another process started |
| `InstanceName` | The name, for a named instance |
| `DbDir`, `BinariesDir`, `InstanceDir`, `DataDir` | Folders in use |
| `InstallDir`, `PgBinDir` | The shared install of this version and its `bin` folder |
| `LogFile` | The server log |
| `Locale` | The locale passed to initdb |

## Related types

| Type | |
| --- | --- |
| `PgVersions.GetAvailableAsync()` | The versions published for a platform, oldest first |
| `PgCache.DefaultDirectory` | The shared cache folder |
| `PgPlatform.Detect()` | The platform and binaries the library uses on this machine |
| `PgExtensionConfig` | An extension archive's URL and optional `Sha256` |
| `PgInstallProgress` | A progress report: `Step` (`Download`, `Verify`, `Extract`), `Item`, `BytesDone`, `BytesTotal` |
| `PostgresEmbedException` | How failures are reported |
