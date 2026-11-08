# Upgrading to v5

v5 modernizes the library. What changed for callers:

- **Target frameworks:** .NET 8, 9, 10 and 11. .NET 6 and 7 (out of support) are dropped.
- **Async only:** `Start()`, `Stop()` and `Dispose()` are removed. Use `await StartAsync()`, `await StopAsync()`
  and `await using`. `PgServer` implements `IAsyncDisposable` only.
- **Options object:** the long constructor is replaced by `new PgServer(pgVersion)` and
  `new PgServer(PgServerOptions)`. The parameters map to option properties of the same name:

  | v4 parameter | v5 option |
  | --- | --- |
  | `startupWaitTime` (ms) | `StartupTimeout` (`TimeSpan`) |
  | `pgServerParams` | `ServerParams` |
  | `pgExtensions` | `Extensions` |
  | `deleteFolderRetryCount` | `DeleteFolderRetryCount` |
  | `deleteFolderInitialTimeout` (ms) | `DeleteFolderInitialDelay` (`TimeSpan`) |
  | `deleteFolderTimeoutFactor` | gone: the delay always doubles |

- **Instance cleanup:** instances without an `InstanceId` are now deleted on stop by default. Set
  `ClearInstanceDirOnStop = false` to keep them.
- **Folder layout:** binaries now live in a shared install folder under `BinariesDir`, so `PgBinDir` points there
  instead of `<instance>/bin`. Existing named instances keep their data.
- **Removed:** `PgExtensionConfig.CreateExtensionSqlList` (it was never executed). Run `CREATE EXTENSION`
  yourself.
- **Errors:** failures are reported as `PostgresEmbedException`, and startup errors include the end of the server
  log.
- **New:** `ConnectionString`, `IsRunning`, `LogFile`, `InstallDir`, `CancellationToken` support, logging, proxy
  support through `HttpClient`, Linux arm64 and Alpine support, and support for paths with spaces and non-ASCII
  characters.
