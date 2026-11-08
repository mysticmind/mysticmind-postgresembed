<img src="https://raw.githubusercontent.com/mysticmind/mysticmind-postgresembed/master/assets/icon.png" alt="MysticMind.PostgresEmbed icon: an elephant standing on a window of code" width="96">

# MysticMind.PostgresEmbed _Postgres embedded database equivalent for .Net applications_ [![Build status](https://github.com/mysticmind/mysticmind-postgresembed/actions/workflows/ci.yaml/badge.svg)](https://github.com/mysticmind/mysticmind-postgresembed/actions/workflows/ci.yaml) [![NuGet Version](https://badgen.net/nuget/v/mysticmind.postgresembed)](https://www.nuget.org/packages/MysticMind.PostgresEmbed/)

Run a real PostgreSQL server, extensions included, from .NET code and tests on Windows, Linux (glibc and
Alpine/musl) and macOS. No Docker and no local Postgres install required. The library downloads minimal, small
sized binaries for your platform once, verifies them, and shares them across every project on the machine.

**Documentation: [mysticmind.github.io/mysticmind-postgresembed](https://mysticmind.github.io/mysticmind-postgresembed/)**

If you have benefitted from this library and it has saved you a bunch of time, please feel free to sponsor my work!

[![GitHub Sponsor](https://img.shields.io/static/v1?label=GitHub%20Sponsor&message=%E2%9D%A4&logo=GitHub)](https://github.com/sponsors/mysticmind)

## Requirements

- .NET 8 or later (the package targets .NET 8, 9, 10 and 11)
- Windows x64, Linux x64/arm64 (glibc or musl), macOS x64/arm64. Windows on Arm runs the x64 build under emulation.
- Postgres refuses to run as root/Administrator, so run your tests as a regular user (this matters in Docker containers)

## Getting started

```console
dotnet add package MysticMind.PostgresEmbed
```

```csharp
await using var server = new PgServer("17");
await server.StartAsync();

// trust authentication: no password needed
await using var conn = new NpgsqlConnection(server.ConnectionString);
await conn.OpenAsync();

await using var cmd = new NpgsqlCommand("CREATE TABLE books(id int PRIMARY KEY, title text)", conn);
await cmd.ExecuteNonQueryAsync();
```

The first start downloads the binaries; later starts take about a second. Disposing the server stops it.
See [Getting Started](https://mysticmind.github.io/mysticmind-postgresembed/guide/getting-started).

## What it does

- **[Versions](https://mysticmind.github.io/mysticmind-postgresembed/guide/versions)**: an exact version
  (`17.6.0`) or a spec (`17`, `17.6`, `latest`) resolved to the newest one published for your platform.
- **[Named instances](https://mysticmind.github.io/mysticmind-postgresembed/guide/named-instances)**: keep a
  server and its data between runs; another process, or the [pgembed CLI](https://mysticmind.github.io/pgembed-cli/),
  attaches to it in milliseconds.
- **[Downloads and cache](https://mysticmind.github.io/mysticmind-postgresembed/guide/cache)**: one shared cache
  per user, checksum-verified downloads, warming the cache ahead of time, progress reports, mirrors.
- **[Extensions](https://mysticmind.github.io/mysticmind-postgresembed/guide/extensions)**: the bundled ones, and
  extension archives the library installs for you.
- **[Tests and CI](https://mysticmind.github.io/mysticmind-postgresembed/guide/testing)**: an xUnit fixture that
  shares one server, and caching the binaries in CI.

Every option is in the [Options reference](https://mysticmind.github.io/mysticmind-postgresembed/reference/options).
Upgrading from v4? See [Upgrading to v5](https://mysticmind.github.io/mysticmind-postgresembed/guide/upgrading-to-v5).
Something not working? See [Troubleshooting](https://mysticmind.github.io/mysticmind-postgresembed/guide/troubleshooting).

## Good to know

- `postgres` is the default database and the default superuser.
- The server listens on localhost only and uses trust authentication: it is made for local development and tests.
- If the test process exits without disposing the server, the library makes a best effort to kill the server, so
  tests do not leave Postgres behind.

## Acknowledgements

- This project uses the minimal Postgres binaries published by [zonkyio/embedded-postgres-binaries](https://github.com/zonkyio/embedded-postgres-binaries).
- [Yandex Embedded PostgreSQL](https://github.com/yandex-qatools/postgresql-embedded) and [OpenTable Embedded PostgreSQL Component](https://github.com/opentable/otj-pg-embedded) were useful references while designing the implementation.

## License

MysticMind.PostgresEmbed is licensed under the [MIT License](http://www.opensource.org/licenses/mit-license.php). Refer to the [License file](https://github.com/mysticmind/mysticmind-postgresembed/blob/master/LICENSE) for more information.

Copyright © 2023-2026 Babu Annamalai
