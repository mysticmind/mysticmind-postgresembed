# MysticMind.PostgresEmbed _Postgres embedded database equivalent for .Net applications_ [![Build status](https://github.com/mysticmind/mysticmind-postgresembed/actions/workflows/ci.yaml/badge.svg)](https://github.com/mysticmind/mysticmind-postgresembed/actions/workflows/ci.yaml) [![NuGet Version](https://badgen.net/nuget/v/mysticmind.postgresembed)](https://www.nuget.org/packages/MysticMind.PostgresEmbed/)

Run a real PostgreSQL server, extensions included, from .NET code and tests on Windows, Linux (glibc and Alpine/musl) and macOS, on both x64 and arm64. No Docker and no local Postgres install required.

By default, this library uses the minimal binaries published by [zonkyio/embedded-postgres-binaries](https://github.com/zonkyio/embedded-postgres-binaries). They are much smaller than the official downloads. A list of all available Postgres versions is here: https://mvnrepository.com/artifact/io.zonky.test.postgres/embedded-postgres-binaries-bom. Click a version to see the platforms it is published for.

The library detects the OS and CPU architecture and downloads the matching binaries.

If you have benefitted from this library and it has saved you a bunch of time, please feel free to sponsor my work!<br>
<a href="https://github.com/sponsors/mysticmind" target="_blank"><img height="30" style="border:0px;height:36px;" src="https://img.shields.io/static/v1?label=GitHub Sponsor&message=%E2%9D%A4&logo=GitHub" border="0" alt="GitHub Sponsor" /></a>

## Requirements

- .NET 8 or later (the package targets .NET 8, 9, 10 and 11)
- Windows x64, Linux x64/arm64 (glibc or musl), macOS x64/arm64
- Postgres refuses to run as root/Administrator, so run your tests as a regular user (this matters in Docker containers)

## Usage

Install the package from NuGet:

```
dotnet add package MysticMind.PostgresEmbed
```

### Start a server

```csharp
await using var server = new PgServer("17.6.0");
await server.StartAsync();

// trust authentication is used, so no password is needed
await using var conn = new NpgsqlConnection(server.ConnectionString);
await conn.OpenAsync();

await using var cmd = new NpgsqlCommand("CREATE TABLE table1(id int PRIMARY KEY, title text)", conn);
await cmd.ExecuteNonQueryAsync();
```

Disposing the server stops it. You can also call `StopAsync()` yourself.

### Options

Everything else is configured with `PgServerOptions`:

```csharp
await using var server = new PgServer(new PgServerOptions
{
    PgVersion = "17.6.0",
    Port = 5432,                    // default: a free port, see server.PgPort
    DbDir = "/tmp/my-tests",        // default: the current directory
    BinariesDir = "/tmp/pg-cache",  // share downloads across projects (see below)
    ServerParams = new Dictionary<string, string>
    {
        ["max_connections"] = "300",
        ["timezone"] = "UTC",
        ["synchronous_commit"] = "off"
    },
    StartupTimeout = TimeSpan.FromSeconds(60),
    Logger = loggerFactory.CreateLogger("Postgres")
});
await server.StartAsync();
```

| Option | Default | Description |
| --- | --- | --- |
| `PgVersion` | required | Postgres version, for example `17.6.0` |
| `PgUser` | `postgres` | Superuser created by initdb |
| `Port` | free port | TCP port; read the actual one from `server.PgPort` |
| `DbDir` | current directory | A `pg_embed` folder is created under it |
| `BinariesDir` | `<DbDir>/pg_embed/binaries` | Cache of downloads and extracted installs |
| `InstanceId` | new Guid | Pass a fixed Guid to reuse an instance and its data across runs |
| `ClearInstanceDirOnStop` | `true` for a new instance, `false` for a named one | Delete the instance folder when the server stops |
| `ClearWorkingDirOnStart` | `false` | Delete the `pg_embed` folder before starting |
| `ServerParams` | none | Postgres settings, written to a config file (values may contain spaces and quotes) |
| `Extensions` | none | Extension archives to install, see below |
| `Locale` | `en_US.UTF-8` (system locale on Windows) | Locale passed to initdb |
| `AddLocalUserAccessPermission` | `false` | Windows only, see [InitDb failure](#initdb-failure-while-starting-embedded-server) |
| `StartupTimeout` / `ShutdownTimeout` | 30s / 10s | |
| `MavenRepo` | Maven Central | Use a mirror or an internal proxy |
| `HttpClient` | shared client | Configure proxies or custom handlers |
| `Logger` | none | Any `Microsoft.Extensions.Logging` logger |
| `Platform` / `Architecture` | detected | Override detection |

### Extensions

The library downloads and extracts extension archives into the Postgres install. Then run `CREATE EXTENSION` yourself. Pick an extension build that matches the Postgres version and platform.

```csharp
await using var server = new PgServer(new PgServerOptions
{
    PgVersion = "17.6.0",
    Extensions =
    [
        new PgExtensionConfig("https://download.osgeo.org/postgis/windows/pg17/postgis-bundle-pg17-3.6.2x64.zip")
    ]
});
await server.StartAsync();

await using var conn = new NpgsqlConnection(server.ConnectionString);
await conn.OpenAsync();
await using var cmd = new NpgsqlCommand("CREATE EXTENSION postgis", conn);
await cmd.ExecuteNonQueryAsync();
```

### Using it in xUnit tests

Downloading and extracting binaries takes time on the first run, so share one server across the tests in a class (or a whole assembly) with a fixture:

```csharp
public sealed class DatabaseFixture : IAsyncLifetime
{
    public PgServer Server { get; } = new("17.6.0");

    public async ValueTask InitializeAsync() => await Server.StartAsync();

    public async ValueTask DisposeAsync() => await Server.DisposeAsync();
}

public class MyDbTests(DatabaseFixture db) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task your_test()
    {
        await using var conn = new NpgsqlConnection(db.Server.ConnectionString);
        // ...
    }
}
```

## Good to know

- `postgres` is the default database and the default superuser.
- Trust authentication is used. Any password in the connection string is ignored. The server only listens on localhost, since the main use case is local tests.
- Each server gets an instance folder (named after `InstanceId`) under `<DbDir>/pg_embed`, holding the `data` folder and the server log (`server.LogFile`). New instances are deleted when the server stops.
- Pass a fixed `InstanceId` to keep the data and reuse it on the next run. The instance is set up on the first run and only started afterwards.
- If the test process exits without disposing the server, the library makes a best effort to kill the server on process exit.

## How it works

1. The zonky binaries for the version and platform are downloaded from Maven once, and cached in `BinariesDir`. Each download is retried 3 times. An unknown version fails fast with a clear error.
2. The binaries, plus the configured extensions, are extracted once into a shared install folder in `BinariesDir`, for example `postgres-17.6.0-linux-amd64`. Every instance with the same version and extensions reuses it, so later servers start in about a second.
3. `initdb` creates the instance `data` folder.
4. The port and `ServerParams` are written to `pg_embed.conf`, which is included from `postgresql.conf`.
5. `pg_ctl start -w` starts the server and waits until it accepts connections.
6. On stop, `pg_ctl stop` runs in fast mode, falling back to immediate mode and then to killing the process.

## Breaking changes in v5

v5 modernizes the library. Summary of what changed for callers:

- **Target frameworks:** .NET 8, 9, 10 and 11. .NET 6 and 7 (out of support) are dropped.
- **Async only:** `Start()`, `Stop()` and `Dispose()` are removed. Use `await StartAsync()`, `await StopAsync()` and `await using`. `PgServer` implements `IAsyncDisposable` only.
- **Options object:** the long constructor is replaced by `new PgServer(pgVersion)` and `new PgServer(PgServerOptions)`. The parameters map to option properties of the same name. `startupWaitTime` (ms) is now `StartupTimeout` (`TimeSpan`), `pgServerParams` is `ServerParams`, `pgExtensions` is `Extensions`, and the `deleteFolder*` parameters are `DeleteFolderRetryCount` and `DeleteFolderInitialDelay`.
- **Instance cleanup:** instances without an `InstanceId` are now deleted on stop by default. Set `ClearInstanceDirOnStop = false` to keep them.
- **Folder layout:** binaries now live in a shared install folder under `BinariesDir`, so `PgBinDir` points there instead of `<instance>/bin`. Existing named instances keep their data.
- **Removed:** `PgExtensionConfig.CreateExtensionSqlList` (it was never executed). Run `CREATE EXTENSION` yourself.
- **Errors:** failures are reported as `PostgresEmbedException`, and startup errors include the end of the server log.
- **New:** `ConnectionString`, `IsRunning`, `LogFile`, `InstallDir`, `CancellationToken` support, logging, proxy support through `HttpClient`, Linux arm64 and Alpine support, and support for paths with spaces and non-ASCII characters.

## Known issues

### InitDb failure while starting embedded server

> initdb: could not change permissions of directory "./pg_embed/aa60c634-fa20-4fa8-b4fc-a43a3b08aa99/data": Permission denied

All Postgres processes run under the current account, and Postgres needs full access to the parent folder of the data directory. On Windows, set `AddLocalUserAccessPermission = true`. The library then runs the following before initdb:

```
icacls.exe <instance folder> /t /grant:r <user>:(OI)(CI)F
```

The account needs the right to change folder permissions. You usually only need this on CI agents.

### Non-ASCII paths on Windows

Postgres on Windows cannot use folder paths with characters outside the system code page, such as Chinese folder names. The library works around this by using the 8.3 short form of the path. If 8.3 names are disabled on the volume, startup fails with a clear error; use an ASCII only `DbDir` in that case.

### InitDb failure with a large negative number

If `initdb` fails on Windows with a large negative exit code, install the latest [Microsoft Visual C++ Redistributable](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist).

## Acknowledgements

- This project uses the minimal Postgres binaries published by [zonkyio/embedded-postgres-binaries](https://github.com/zonkyio/embedded-postgres-binaries).
- [Yandex Embedded PostgreSQL](https://github.com/yandex-qatools/postgresql-embedded) and [OpenTable Embedded PostgreSQL Component](https://github.com/opentable/otj-pg-embedded) were useful references while designing the implementation.

## License

MysticMind.PostgresEmbed is licensed under the [MIT License](http://www.opensource.org/licenses/mit-license.php). Refer to the [License file](https://github.com/mysticmind/mysticmind-postgresembed/blob/master/LICENSE) for more information.

Copyright © 2023 Babu Annamalai
