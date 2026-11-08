# In Tests and CI

## Share a server with a fixture

Starting a server takes about a second once the binaries are cached, so share one across the tests in a class
(or a whole assembly) with a fixture. This is xUnit v3, where `IAsyncLifetime` returns `ValueTask`; with xUnit
v2, return `Task` from both methods instead:

snippet: sample_xunit_fixture

For a whole assembly, use an assembly fixture (`[assembly: AssemblyFixture(typeof(DatabaseFixture))]` in xUnit
v3) or a collection fixture. Keep tests independent by giving each its own database or schema, or by rolling
back a transaction at the end of each test.

## Reuse a server across runs

A [named instance](./named-instances) keeps running between test runs when you start it yourself, for example
with the [pgembed CLI](https://mysticmind.github.io/pgembed-cli/) (`pgembed start app --version 17`). Tests then
attach to it in milliseconds and leave it running.

## In CI

- **Cache the binaries.** Set `PGEMBED_CACHE_DIR` to a folder your CI caches between runs, or set
  `BinariesDir`, so the first test does not pay for the download.
- **Warm the cache in its own step** with [`InstallAsync()`](./cache#warm-the-cache), so download time shows up
  there and not in a test's timeout.
- **Run as a regular user.** Postgres refuses to run as root; in a container, add a user.
- **Windows agents** may need `AddLocalUserAccessPermission = true`; see
  [Troubleshooting](./troubleshooting#initdb-failure-while-starting-the-server).
