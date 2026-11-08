# Introduction

MysticMind.PostgresEmbed runs a real PostgreSQL server, extensions included, from .NET code and tests. It
downloads the binaries for your platform, sets up a data folder, starts the server on a free port and stops it
when you are done. There is no Docker to run and no Postgres to install.

## Why

- **Real Postgres.** Tests run against the same server your app uses in production, not an in-memory
  stand-in, so SQL, types, extensions and query plans behave the same.
- **Works where Docker does not.** Laptops and CI runners without Docker, or without the right to install
  packages, only need the .NET SDK.
- **Fast after the first run.** The binaries are downloaded once and shared; a server starts in about a second
  afterwards, and a [named instance](./named-instances) that is already running is attached to in
  milliseconds.

## Requirements

- .NET 8 or later (the package targets .NET 8, 9, 10 and 11).
- Windows x64, Linux x64 or arm64 (glibc or musl), macOS x64 or arm64. Windows on Arm runs the x64 build under
  emulation.
- Postgres refuses to run as root or Administrator, so run your tests as a regular user. This matters in Docker
  containers: add a user and run as that user.

## The binaries

By default the library uses the minimal binaries published by
[zonkyio/embedded-postgres-binaries](https://github.com/zonkyio/embedded-postgres-binaries) to Maven Central.
They hold what a server needs and are much smaller than the official downloads. The full list of versions is on
[mvnrepository.com](https://mvnrepository.com/artifact/io.zonky.test.postgres/embedded-postgres-binaries-bom);
click a version to see the platforms it is published for.

## From the command line too

[pgembed](https://mysticmind.github.io/pgembed-cli/) is a command-line tool built on this library: named
instances, SQL with readable results, schema inspection and an MCP server for AI assistants. It shares the
library's [cache](./cache), so a server started with `pgembed start app` is the one your tests attach to.
