# Downloads and Cache

## How downloads work

The binaries for a version are downloaded from Maven once, checked against the SHA-256 Maven publishes next to
them, and kept in `BinariesDir`. They are then extracted, with any configured [extensions](./extensions), into
an install folder such as `postgres-17.6.0-linux-amd64`, which every server with the same version and
extensions reuses. Each download is retried 3 times (`DownloadRetryCount`).

When several processes need the same version at the same time, one downloads it and the others wait.

## The shared cache

The shared cache is one folder per user:

- `PGEMBED_CACHE_DIR` if set
- otherwise `~/.cache/pgembed` on Linux (or `$XDG_CACHE_HOME/pgembed`), `~/Library/Caches/pgembed` on macOS
  and `%LOCALAPPDATA%\pgembed` on Windows

snippet: sample_cache_folder

It holds [named instances](./named-instances), and their binaries in `binaries`. Setting `PGEMBED_CACHE_DIR`
also makes its `binaries` folder the default for **every** server, so all projects on the machine download each
version once. Without it, a server that is not named keeps its downloads in `<DbDir>/pg_embed/binaries`; set
`BinariesDir` to share them.

## Warm the cache

`InstallAsync()` downloads and extracts the binaries (and configured extensions) without creating an instance or
starting a server. Run it ahead of time, for example in its own CI step:

snippet: sample_install

## Show progress

A first run is slow because of the download. `InstallProgress` receives what happens, so a test runner or tool
can show it:

snippet: sample_install_progress

Download reports arrive at most ten times a second, plus the start and the end. Nothing is reported when the
version is already cached.

## Mirrors and proxies

Set `MavenRepo` to use a mirror or an internal proxy, and `HttpClient` to configure a proxy or custom handlers.
Maven Central publishes a SHA-256 for every archive; a mirror might not. To refuse a download that cannot be
verified:

snippet: sample_require_checksum
