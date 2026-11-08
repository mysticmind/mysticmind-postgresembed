# Versions

`PgVersion` takes an exact version or a spec that resolves to the newest matching version published for your
platform:

snippet: sample_version_specs

A spec is resolved when the server starts, and `server.PgVersion` then holds the exact version:

snippet: sample_resolved_version

Without network access, a spec falls back to the newest matching version already downloaded. An unknown version
fails fast with a clear error.

## What is published

snippet: sample_list_versions

This reads one small metadata file from the Maven repository. Versions such as `13.1.0-1` are repackaged builds
of `13.1.0` and sort after it.

## Pin the major for kept data

Postgres cannot open data written by another major version. For a server whose data you keep (a
[named instance](./named-instances)), pin at least the major (`"17"`, not `"latest"`); the library refuses a
mismatch with a clear error before downloading anything.
