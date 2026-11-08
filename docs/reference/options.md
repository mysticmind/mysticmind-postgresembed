# Options

A server is configured with `PgServerOptions`:

snippet: sample_options

| Option | Default | Description |
| --- | --- | --- |
| `PgVersion` | required | Exact version (`17.6.0`) or a spec (`17`, `17.6`, `latest`); see [Versions](../guide/versions) |
| `PgUser` | `postgres` | Superuser created by initdb |
| `Port` | free port | TCP port; read the actual one from `server.PgPort` |
| `DbDir` | current directory | A `pg_embed` folder is created under it |
| `BinariesDir` | `<DbDir>/pg_embed/binaries`, or `binaries` in the [shared cache](../guide/cache#the-shared-cache) for named instances and when `PGEMBED_CACHE_DIR` is set | Cache of downloads and extracted installs |
| `InstanceName` | none | Keep and share an instance under a name; see [Named Instances](../guide/named-instances) |
| `AttachIfRunning` | `true` | Attach to the instance's server when it is already running |
| `InstanceId` | new Guid | Older way to reuse an instance: prefer `InstanceName` (set one or the other) |
| `ClearInstanceDirOnStop` | `true` for a new instance, `false` for a named one | Delete the instance folder when the server stops |
| `ClearWorkingDirOnStart` | `false` | Delete the `pg_embed` folder before starting |
| `KeepRunningAfterExit` | `false` | Keep the server running when the process exits without stopping it (by default it is killed, so tests never leave Postgres behind) |
| `ServerParams` | none | Postgres settings, written to a config file (values may contain spaces and quotes) |
| `Extensions` | none | Extension archives to install; see [Extensions](../guide/extensions) |
| `Locale` | `en_US.UTF-8` (system locale on Windows) | Locale passed to initdb |
| `AddLocalUserAccessPermission` | `false` | Windows only; see [Troubleshooting](../guide/troubleshooting#initdb-failure-while-starting-the-server) |
| `StartupTimeout` / `ShutdownTimeout` | 30 s / 10 s | |
| `MavenRepo` | Maven Central | A mirror or an internal proxy |
| `RequireChecksum` | `false` | Fail a download whose repository publishes no SHA-256, instead of installing it unverified |
| `DownloadRetryCount` | 3 | Retries for each download |
| `DeleteFolderRetryCount` / `DeleteFolderInitialDelay` | 5 / 16 ms | Retries when deleting folders that are briefly locked; the delay doubles each time |
| `HttpClient` | shared client | Configure proxies or custom handlers |
| `Logger` | none | Any `Microsoft.Extensions.Logging` logger |
| `InstallProgress` | none | `IProgress<PgInstallProgress>`: download bytes, checksum check and extraction; see [Show progress](../guide/cache#show-progress) |
| `Platform` / `Architecture` | detected | Override detection |

## Environment

| Variable | Effect |
| --- | --- |
| `PGEMBED_CACHE_DIR` | The [shared cache](../guide/cache#the-shared-cache) folder; also makes its `binaries` folder the default `BinariesDir` for every server |
| `XDG_CACHE_HOME` | On Linux, the shared cache is `$XDG_CACHE_HOME/pgembed` when `PGEMBED_CACHE_DIR` is not set |
