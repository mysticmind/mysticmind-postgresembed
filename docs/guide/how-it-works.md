# How It Works

1. A version spec such as `17` is resolved against the versions published for the platform. The binaries for
   that version are downloaded from Maven once, checked against the SHA-256 Maven publishes next to them, and
   cached in `BinariesDir`. Each download is retried 3 times. An unknown version fails fast with a clear error.
2. The binaries, plus the configured extensions, are extracted once into a shared install folder in
   `BinariesDir`, for example `postgres-17.6.0-linux-amd64`. Every instance with the same version and extensions
   reuses it, so later servers start in about a second.
3. `initdb` creates the instance's `data` folder.
4. The port and `ServerParams` are written to `pg_embed.conf`, which is included from `postgresql.conf`.
5. `pg_ctl start -w` starts the server and waits until it accepts connections.
6. On stop, `pg_ctl stop` runs in fast mode, falling back to immediate mode and then to killing the process.

## Where things live

- An instance folder holds the `data` folder and the server log (`server.LogFile`): under `<DbDir>/pg_embed`,
  named after `InstanceId`, or in the [shared cache](./cache#the-shared-cache) for a named instance.
- Instances that are not named are deleted when the server stops (`ClearInstanceDirOnStop`); named ones are
  kept.
- `server.InstallDir` and `server.PgBinDir` point at the shared install, `server.DataDir` at the data.
