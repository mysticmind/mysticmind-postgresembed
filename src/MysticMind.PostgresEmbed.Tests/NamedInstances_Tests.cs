using Npgsql;

namespace MysticMind.PostgresEmbed.Tests;

// Named instances keep their data on purpose, so every folder a test creates is deleted after it
// (xunit disposes the test class after each test, once the servers in it are disposed).
public sealed class NamedInstancesTests : IDisposable
{
    private const string PgVersion = "18.6.0";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly string SharedBinariesDir = Path.Combine(Directory.GetCurrentDirectory(), "pg_embed", "binaries");

    private readonly List<string> _folders = [];

    private string NewFolder(string prefix)
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), $"{prefix}-{Guid.NewGuid():N}");
        _folders.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var folder in _folders)
        {
            for (var attempt = 0; attempt < 5 && Directory.Exists(folder); attempt++)
            {
                try { Directory.Delete(folder, recursive: true); }
                catch (IOException) { Thread.Sleep(200); } // Windows can hold files briefly after a server exits
                catch (UnauthorizedAccessException) { Thread.Sleep(200); }
            }
        }
    }

    // an explicit DbDir per test keeps named instances out of the real per-user cache
    private PgServerOptions Named(string name, string? dbDir = null) => new()
    {
        PgVersion = PgVersion,
        InstanceName = name,
        DbDir = dbDir ?? NewFolder("named"),
        BinariesDir = SharedBinariesDir
    };

    private static async Task<object?> Scalar(PgServer server, string sql)
    {
        await using var conn = new NpgsqlConnection(server.ConnectionString + ";Pooling=false");
        await conn.OpenAsync(Ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        return await cmd.ExecuteScalarAsync(Ct);
    }

    [Fact]
    public void a_named_instance_lives_in_the_shared_cache_unless_a_db_dir_is_given()
    {
        var shared = new PgServer(new PgServerOptions { PgVersion = PgVersion, InstanceName = "app" });
        Assert.Equal(Path.Combine(PgCache.DefaultDirectory, "instances", "app"), shared.InstanceDir);
        Assert.Equal(Path.Combine(PgCache.DefaultDirectory, "binaries"), shared.BinariesDir);
        Assert.Equal("app", shared.InstanceName);

        var local = new PgServer(new PgServerOptions { PgVersion = PgVersion, InstanceName = "app", DbDir = "/tmp/x" });
        Assert.Equal(Path.Combine(Path.GetFullPath("/tmp/x"), "pg_embed", "app"), local.InstanceDir);
        Assert.Equal(Path.Combine(Path.GetFullPath("/tmp/x"), "pg_embed", "binaries"), local.BinariesDir);
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("-starts-with-dash")]
    [InlineData("../escape")]
    [InlineData("")]
    public void invalid_names_are_rejected(string name) =>
        Assert.Throws<ArgumentException>(() => new PgServer(new PgServerOptions { PgVersion = PgVersion, InstanceName = name }));

    [Fact]
    public void a_name_and_an_id_cannot_both_be_set() =>
        Assert.Throws<ArgumentException>(() => new PgServer(new PgServerOptions
        {
            PgVersion = PgVersion, InstanceName = "app", InstanceId = Guid.NewGuid()
        }));

    [Theory]
    [InlineData(false, false, "/custom/cache", null, null, null, "/custom/cache")]
    [InlineData(true, false, null, @"C:\Users\me\AppData\Local", null, null, @"C:\Users\me\AppData\Local/pgembed")]
    [InlineData(false, true, null, null, "/Users/me", null, "/Users/me/Library/Caches/pgembed")]
    [InlineData(false, false, null, null, "/home/me", "/home/me/.xdg", "/home/me/.xdg/pgembed")]
    [InlineData(false, false, null, null, "/home/me", null, "/home/me/.cache/pgembed")]
    public void the_cache_folder_follows_each_platforms_convention(
        bool windows, bool macOS, string? configured, string? localAppData, string? home, string? xdg, string expected)
    {
        var env = new Dictionary<string, string?>
        {
            [PgCache.DirectoryVariable] = configured, ["LOCALAPPDATA"] = localAppData, ["HOME"] = home, ["XDG_CACHE_HOME"] = xdg
        };

        var actual = PgCache.Resolve(name => env.GetValueOrDefault(name), windows, macOS);

        // PGEMBED_CACHE_DIR is made absolute (on Windows "/custom/cache" gains a drive letter);
        // separators differ by host, so compare with forward slashes
        static string Normalize(string path) => path.Replace('\\', '/');
        Assert.Equal(Normalize(configured is null ? expected : Path.GetFullPath(configured)), Normalize(actual));
    }

    [Fact]
    public void platforms_map_to_runtime_identifiers_and_artifacts()
    {
        var current = PgPlatform.Detect();
        Assert.Contains(current.RuntimeIdentifier, PgPlatform.SupportedRuntimeIdentifiers);

        Assert.True(PgPlatform.TryParse("LINUX-musl-arm64", out var alpine));
        Assert.Equal((Platform.AlpineLinux, Architecture.Arm64V8, "linux-arm64v8-alpine"), (alpine.Platform, alpine.Architecture, alpine.ArtifactName));
        Assert.Equal("osx-x64", PgPlatform.From(Platform.Darwin, Architecture.Amd64).RuntimeIdentifier);
        Assert.False(PgPlatform.TryParse("win-arm64", out _));
    }

    [Fact]
    public async Task a_second_server_attaches_to_a_running_named_instance_and_leaves_it_running()
    {
        var options = Named("attach");
        await using var owner = new PgServer(options);
        await owner.StartAsync(Ct);
        await Scalar(owner, "CREATE TABLE seen(id int); INSERT INTO seen VALUES (42)");

        await using (var visitor = new PgServer(options))
        {
            await visitor.StartAsync(Ct);

            Assert.True(visitor.IsAttached);
            Assert.False(owner.IsAttached);
            Assert.Equal(owner.PgPort, visitor.PgPort);
            Assert.Equal(42, await Scalar(visitor, "SELECT id FROM seen"));
        }

        // disposing the visitor did not stop the owner's server
        Assert.Equal(PgInstanceState.Running, (await owner.GetStatusAsync(Ct)).State);
        Assert.Equal(42, await Scalar(owner, "SELECT id FROM seen"));
    }

    [Fact]
    public async Task status_reports_process_port_and_start_time_and_ignores_a_stale_pid_file()
    {
        var options = Named("status");
        await using var server = new PgServer(options);
        Assert.Equal(PgInstanceState.Stopped, (await server.GetStatusAsync(Ct)).State);

        await server.StartAsync(Ct);
        var running = await server.GetStatusAsync(Ct);
        Assert.Equal(PgInstanceState.Running, running.State);
        Assert.Equal(server.PgPort, running.Port);
        Assert.NotNull(running.ProcessId);
        Assert.NotNull(running.StartedAt);

        await server.StopAsync(Ct);
        Assert.Equal(PgInstanceState.Stopped, (await server.GetStatusAsync(Ct)).State);

        File.WriteAllText(Path.Combine(server.DataDir, "postmaster.pid"), "999999\n/data\n1700000000\n5432\n\n\n\nready\n");
        Assert.Equal(PgInstanceState.Stopped, (await server.GetStatusAsync(Ct)).State);
    }

    [Fact]
    public async Task stop_works_from_an_object_that_did_not_start_the_server()
    {
        var options = Named("stop-elsewhere");
        await using var owner = new PgServer(options);
        await owner.StartAsync(Ct);

        await using var other = new PgServer(options); // never started: like `pgembed stop` in a new process
        await other.StopAsync(Ct);

        Assert.Equal(PgInstanceState.Stopped, (await owner.GetStatusAsync(Ct)).State);
    }

    [Fact]
    public async Task disposing_an_object_that_only_queried_leaves_the_server_running()
    {
        var options = Named("query-only");
        await using var owner = new PgServer(options);
        await owner.StartAsync(Ct);

        await using (var observer = new PgServer(options))
        {
            Assert.Equal(PgInstanceState.Running, (await observer.GetStatusAsync(Ct)).State);
        }

        Assert.Equal(PgInstanceState.Running, (await owner.GetStatusAsync(Ct)).State);
    }

    [Fact]
    public async Task kill_stops_the_server_and_a_restart_recovers_the_data()
    {
        var options = Named("kill");
        await using (var server = new PgServer(options))
        {
            await server.StartAsync(Ct);
            await Scalar(server, "CREATE TABLE kept(id int); INSERT INTO kept VALUES (7)");

            await server.KillAsync(Ct);
            Assert.Equal(PgInstanceState.Stopped, (await server.GetStatusAsync(Ct)).State);
        }

        await using var restarted = new PgServer(options);
        await restarted.StartAsync(Ct);
        Assert.False(restarted.IsAttached);
        Assert.Equal(7, await Scalar(restarted, "SELECT id FROM kept"));
    }

    [Fact]
    public async Task kill_releases_the_servers_shared_memory()
    {
        // Linux and macOS: a killed server's System V segment outlives it unless removed, and macOS
        // allows only 32 on the whole machine
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows uses no System V shared memory");

        await using var server = new PgServer(Named("kill-shm"));
        await server.StartAsync(Ct);
        var segment = SharedMemory.Read(Path.Combine(server.DataDir, "postmaster.pid"));
        Assert.NotNull(segment);
        Assert.True(SharedMemory.Exists(segment.Value.Key));

        await server.KillAsync(Ct);

        Assert.False(SharedMemory.Exists(segment.Value.Key));
    }

    [Fact]
    public async Task two_concurrent_starts_of_one_name_start_one_server()
    {
        var options = Named("concurrent");
        await using var a = new PgServer(options);
        await using var b = new PgServer(options);

        await Task.WhenAll(a.StartAsync(Ct), b.StartAsync(Ct));

        Assert.Equal(1, new[] { a, b }.Count(s => s.IsAttached));
        Assert.Equal(a.PgPort, b.PgPort);
    }

    [Fact]
    public async Task data_from_another_major_version_is_refused_before_any_download()
    {
        var dbDir = NewFolder("named");
        await using (var v18 = new PgServer(Named("major", dbDir)))
        {
            await v18.StartAsync(Ct);
        }

        var emptyBinaries = NewFolder("empty-binaries");
        Directory.CreateDirectory(emptyBinaries);
        await using var v17 = new PgServer(Named("major", dbDir) with { PgVersion = "17", BinariesDir = emptyBinaries });

        var ex = await Assert.ThrowsAsync<PostgresEmbedException>(() => v17.StartAsync(Ct));

        Assert.Contains("created by Postgres 18", ex.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(emptyBinaries));
    }

    [Fact]
    public async Task without_attach_a_running_instance_is_reported_not_restarted()
    {
        var options = Named("no-attach");
        await using var owner = new PgServer(options);
        await owner.StartAsync(Ct);

        await using var second = new PgServer(options with { AttachIfRunning = false });

        var ex = await Assert.ThrowsAsync<PostgresEmbedException>(() => second.StartAsync(Ct));
        Assert.Contains("already running", ex.Message);
    }

    [Fact]
    public async Task concurrent_installs_of_one_version_download_it_once()
    {
        var binaries = NewFolder("download-once"); // a full download and install: removed after the test
        var counting = new FakeHttpHandler(); // no routes: real network, but every request is recorded
        var client = new HttpClient(counting);

        PgServer Make() => new(new PgServerOptions { PgVersion = PgVersion, BinariesDir = binaries, HttpClient = client });
        await using var a = Make();
        await using var b = Make();

        await Task.WhenAll(a.InstallAsync(Ct), b.InstallAsync(Ct));

        Assert.Single(counting.Requests, r => r.EndsWith(".jar", StringComparison.Ordinal));
        Assert.Single(Directory.EnumerateDirectories(binaries));
    }
}
