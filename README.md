<img src="https://raw.githubusercontent.com/mysticmind/mysticmind-postgresembed/master/assets/icon.png" alt="MysticMind.PostgresEmbed icon: an elephant standing on a window of code" width="96">

# MysticMind.PostgresEmbed _Postgres embedded database equivalent for .Net applications_ [![Build status](https://github.com/mysticmind/mysticmind-postgresembed/actions/workflows/ci.yaml/badge.svg)](https://github.com/mysticmind/mysticmind-postgresembed/actions/workflows/ci.yaml) [![NuGet Version](https://badgen.net/nuget/v/mysticmind.postgresembed)](https://www.nuget.org/packages/MysticMind.PostgresEmbed/)

Run a real PostgreSQL server, extensions included, from .NET code and tests on Windows, Linux (glibc and Alpine/musl) and macOS. No Docker and no local Postgres install required.

By default, this library uses the minimal binaries published by [zonkyio/embedded-postgres-binaries](https://github.com/zonkyio/embedded-postgres-binaries). They are much smaller than the official downloads. A list of all available Postgres versions is here: https://mvnrepository.com/artifact/io.zonky.test.postgres/embedded-postgres-binaries-bom. Click a version to see the platforms it is published for.

The library detects the OS and CPU architecture and downloads the matching binaries.

If you have benefitted from this library and it has saved you a bunch of time, please feel free to sponsor my work!<br>
<a href="https://github.com/sponsors/mysticmind" target="_blank"><img height="30" style="border:0px;height:36px;" src="https://img.shields.io/static/v1?label=GitHub Sponsor&message=%E2%9D%A4&logo=GitHub" border="0" alt="GitHub Sponsor" /></a>

## Requirements

- .NET 8 or later (the package targets .NET 8, 9, 10 and 11)
- Windows x64, Linux x64/arm64 (glibc or musl), macOS x64/arm64. Windows on Arm runs the x64 build under emulation.
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

### Choosing a version

`PgVersion` takes an exact version or a spec that resolves to the newest matching version published for your platform:

```csharp
new PgServer("17.6.0");  // exactly this version
new PgServer("17");      // newest 17.x
new PgServer("17.6");    // newest 17.6.x
new PgServer("latest");  // newest of all
```

A spec is resolved when the server starts (`server.PgVersion` then holds the exact version). Without network access, it falls back to the newest matching version already downloaded. To see what is published:

```csharp
IReadOnlyList<string> versions = await PgVersions.GetAvailableAsync();   // for this platform, oldest first
```

### Warming the cache, for example in CI

`InstallAsync()` downloads and extracts the binaries (and configured extensions) without creating an instance or starting a server. Run it in its own CI step, cached between runs, so the first test does not pay for the download:

```csharp
await new PgServer(new PgServerOptions { PgVersion = "17", BinariesDir = "/ci-cache/pg" }).InstallAsync();
```

### Named instances: keep a server around and reuse it

Give an instance a name to keep it, data included, and share it across runs, test projects and tools:

```csharp
await using var server = new PgServer(new PgServerOptions { PgVersion = "17", InstanceName = "app" });
await server.StartAsync();
```

- A named instance lives in the **shared cache** (`instances/app`), so every test run, app and tool on the machine finds the same one. Pass `DbDir` to keep it somewhere else instead.
- **If it is already running**, for example from an earlier run, another test project or the `pgembed` CLI, `StartAsync()` **attaches** to it in milliseconds instead of starting another server (`server.IsAttached`). Set `AttachIfRunning = false` to get an error instead.
- **Only the process that started a server stops it on dispose.** A server you attached to keeps running for whoever started it. `StopAsync()` and `KillAsync()` stop it either way.
- Two processes starting the same name at once get one server: a lock per instance makes the second attach.
- Its data is kept when the server stops. Pin at least the major version (`"17"`, not `"latest"`): Postgres cannot open data from another major version, and the library refuses with a clear error before downloading anything.

Check or control a server from anywhere, including one another process started:

```csharp
await using var app = new PgServer(new PgServerOptions { PgVersion = "17", InstanceName = "app" });
PgInstanceStatus status = await app.GetStatusAsync();   // State, ProcessId, Port, StartedAt
await app.StopAsync();                                   // clean shutdown
await app.KillAsync();                                   // last resort: force-kill
```

### The shared cache

The shared cache is one folder per user: `PGEMBED_CACHE_DIR` if set, otherwise `~/.cache/pgembed` on Linux (or `$XDG_CACHE_HOME/pgembed`), `~/Library/Caches/pgembed` on macOS and `%LOCALAPPDATA%\pgembed` on Windows (`PgCache.DefaultDirectory`). It holds named instances, and their binaries in `binaries`.

Setting `PGEMBED_CACHE_DIR` also makes its `binaries` folder the default for **every** server, so all projects on the machine download each Postgres version once. Downloads are shared safely: when several processes need the same version at the same time, one downloads it and the others wait.

### Available extensions

`GetAvailableExtensionsAsync()` lists everything `CREATE EXTENSION` can use in that install: the extensions bundled with Postgres plus any you configured. It reads the install folder, so the server does not need to be running:

```csharp
foreach (var ext in await server.GetAvailableExtensionsAsync())
    Console.WriteLine($"{ext.Name} {ext.DefaultVersion}: {ext.Comment}");
```

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
| `PgVersion` | required | Exact version (`17.6.0`) or a spec (`17`, `17.6`, `latest`), see [Choosing a version](#choosing-a-version) |
| `PgUser` | `postgres` | Superuser created by initdb |
| `Port` | free port | TCP port; read the actual one from `server.PgPort` |
| `DbDir` | current directory | A `pg_embed` folder is created under it |
| `BinariesDir` | `<DbDir>/pg_embed/binaries`, or `binaries` in the [shared cache](#the-shared-cache) for named instances and when `PGEMBED_CACHE_DIR` is set | Cache of downloads and extracted installs |
| `InstanceName` | none | Keep and share an instance under a name, see [Named instances](#named-instances-keep-a-server-around-and-reuse-it) |
| `AttachIfRunning` | `true` | Attach to the instance's server when it is already running |
| `InstanceId` | new Guid | Older way to reuse an instance: prefer `InstanceName` (set one or the other) |
| `ClearInstanceDirOnStop` | `true` for a new instance, `false` for a named one | Delete the instance folder when the server stops |
| `ClearWorkingDirOnStart` | `false` | Delete the `pg_embed` folder before starting |
| `KeepRunningAfterExit` | `false` | Keep the server running when the process exits without stopping it (by default it is killed, so tests never leave Postgres behind) |
| `ServerParams` | none | Postgres settings, written to a config file (values may contain spaces and quotes) |
| `Extensions` | none | Extension archives to install, see below |
| `Locale` | `en_US.UTF-8` (system locale on Windows) | Locale passed to initdb |
| `AddLocalUserAccessPermission` | `false` | Windows only, see [InitDb failure](#initdb-failure-while-starting-embedded-server) |
| `StartupTimeout` / `ShutdownTimeout` | 30s / 10s | |
| `MavenRepo` | Maven Central | Use a mirror or an internal proxy |
| `DownloadRetryCount` | 3 | Retries for each download |
| `DeleteFolderRetryCount` / `DeleteFolderInitialDelay` | 5 / 16 ms | Retries when deleting folders that are briefly locked; the delay doubles each time |
| `HttpClient` | shared client | Configure proxies or custom handlers |
| `Logger` | none | Any `Microsoft.Extensions.Logging` logger |
| `Platform` / `Architecture` | detected | Override detection |

### Extensions

The library downloads extension archives and extracts them into the Postgres install. Then run `CREATE EXTENSION` yourself.

Each archive must be a **zip** built for the **same platform and Postgres major version** as the server. A folder at the top of the zip that holds `bin`, `lib` and `share` is handled. For example, the PostGIS bundle below is a Windows build for Postgres 17: it extracts on Linux or macOS too, but `CREATE EXTENSION` then fails, so choose the extension URL per platform.

```csharp
// Windows: PostGIS bundle for Postgres 17
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

To make sure the archive is exactly the one you expect, give its SHA-256. A mismatch fails the start and the archive is not installed:

```csharp
new PgExtensionConfig("https://example.com/my-extension.zip") { Sha256 = "9f86d081884c7d65..." }
```

### Using it in xUnit tests

Downloading and extracting binaries takes time on the first run, so share one server across the tests in a class (or a whole assembly) with a fixture. This is xUnit v3, where `IAsyncLifetime` returns `ValueTask`; with xUnit v2, return `Task` from both methods instead:

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
- Each server gets an instance folder holding the `data` folder and the server log (`server.LogFile`): under `<DbDir>/pg_embed`, named after `InstanceId`, or in the shared cache for a named instance. Unnamed instances are deleted when the server stops; named ones are kept.
- To reuse an instance and its data, prefer `InstanceName`. A fixed `InstanceId` still works. Either way the instance is set up on the first run and only started afterwards, and should pin at least the major version.
- `PgPlatform.Detect()` tells which platform and binaries the library uses on this machine.
- If the test process exits without disposing the server, the library makes a best effort to kill the server on process exit.

## How it works

1. A version spec such as `17` is resolved against the versions published for the platform. The zonky binaries for that version are downloaded from Maven once, checked against the SHA-256 Maven publishes next to them, and cached in `BinariesDir`. Each download is retried 3 times. An unknown version fails fast with a clear error.
2. The binaries, plus the configured extensions, are extracted once into a shared install folder in `BinariesDir`, for example `postgres-17.6.0-linux-amd64`. Every instance with the same version and extensions reuses it, so later servers start in about a second.
3. `initdb` creates the instance `data` folder.
4. The port and `ServerParams` are written to `pg_embed.conf`, which is included from `postgresql.conf`.
5. `pg_ctl start -w` starts the server and waits until it accepts connections.
6. On stop, `pg_ctl stop` runs in fast mode, falling back to immediate mode and then to killing the process.

## Breaking changes in v5

v5 modernizes the library. Summary of what changed for callers:

- **Target frameworks:** .NET 8, 9, 10 and 11. .NET 6 and 7 (out of support) are dropped.
- **Async only:** `Start()`, `Stop()` and `Dispose()` are removed. Use `await StartAsync()`, `await StopAsync()` and `await using`. `PgServer` implements `IAsyncDisposable` only.
- **Options object:** the long constructor is replaced by `new PgServer(pgVersion)` and `new PgServer(PgServerOptions)`. The parameters map to option properties of the same name. `startupWaitTime` (ms) is now `StartupTimeout` (`TimeSpan`), `pgServerParams` is `ServerParams`, `pgExtensions` is `Extensions`, `deleteFolderRetryCount` is `DeleteFolderRetryCount`, and `deleteFolderInitialTimeout` (ms) is `DeleteFolderInitialDelay` (`TimeSpan`). `deleteFolderTimeoutFactor` is gone: the delay always doubles.
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

Postgres on Windows cannot use folder paths with characters outside the system code page, such as Chinese folder names. The library works around this by handing Postgres the 8.3 short form of the path, which is ASCII only. Short names are on by default for the system drive, but are often turned off on other drives.

Without them, startup stops before downloading anything, with an error that lists the problem characters and the two ways out:

- Set `DbDir` (or `BinariesDir`, whichever the error names) to a folder whose full path is ASCII only, such as `C:\pg-embed`.
- Turn on short names for that drive as an administrator (`fsutil 8dot3name set D: 0`), then delete and recreate the folder. Folders created while short names were off never get one.

### InitDb failure with a large negative number

If `initdb` fails on Windows with a large negative exit code, install the latest [Microsoft Visual C++ Redistributable](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist).

## Acknowledgements

- This project uses the minimal Postgres binaries published by [zonkyio/embedded-postgres-binaries](https://github.com/zonkyio/embedded-postgres-binaries).
- [Yandex Embedded PostgreSQL](https://github.com/yandex-qatools/postgresql-embedded) and [OpenTable Embedded PostgreSQL Component](https://github.com/opentable/otj-pg-embedded) were useful references while designing the implementation.

## License

MysticMind.PostgresEmbed is licensed under the [MIT License](http://www.opensource.org/licenses/mit-license.php). Refer to the [License file](https://github.com/mysticmind/mysticmind-postgresembed/blob/master/LICENSE) for more information.

Copyright © 2023-2026 Babu Annamalai
