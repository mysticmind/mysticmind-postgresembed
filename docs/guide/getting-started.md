# Getting Started

## Install

```console
dotnet add package MysticMind.PostgresEmbed
```

## Start a server

snippet: sample_start_server

The first start downloads and extracts the binaries, which takes a little while; later starts take about a
second. Disposing the server stops it and, unless it is a [named instance](./named-instances), deletes its data.
You can also call `StopAsync()` yourself.

`server.ConnectionString` holds the host, port, user and database. The server listens on localhost only and
uses trust authentication, so no password is needed and any password in a connection string is ignored.

## Good to know

- `postgres` is both the default database and the superuser.
- The server gets a free port unless you set `Port`; read it from `server.PgPort`.
- Each server has an instance folder with its `data` folder and its log (`server.LogFile`). Startup errors
  include the end of that log.
- If the process exits without disposing the server, the library makes a best effort to kill the server, so
  tests never leave Postgres behind.
- To see which platform and binaries the library uses on this machine:

snippet: sample_platform

## Next

- [Choose a version](./versions)
- [Keep a server between runs](./named-instances)
- [Share it across the tests in a class or assembly](./testing)
- [Every option](../reference/options)
