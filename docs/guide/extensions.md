# Extensions

## Bundled extensions

The binaries include the extensions that ship with Postgres, such as `pgcrypto`, `hstore` and `pg_trgm`. Create
one as usual:

snippet: sample_create_extension

`GetAvailableExtensionsAsync()` lists everything `CREATE EXTENSION` can use in an install: the bundled
extensions plus any you configured. It reads the install folder, so the server does not need to be running:

snippet: sample_available_extensions

## Extension archives

The library downloads extension archives and extracts them into the Postgres install; then run
`CREATE EXTENSION` yourself.

Each archive must be a **zip** built for the **same platform and Postgres major version** as the server. A
folder at the top of the zip that holds `bin`, `lib` and `share` is handled. For example, the PostGIS bundle
below is a Windows build for Postgres 17: it extracts on Linux or macOS too, but `CREATE EXTENSION` then fails,
so choose the extension URL per platform.

snippet: sample_extension_archive

## Verify an archive

Give its SHA-256 to make sure the archive is exactly the one you expect. A mismatch fails the start and the
archive is not installed:

snippet: sample_extension_sha256
