using Npgsql;

namespace MysticMind.PostgresEmbed.Tests;

public class PgServerTests
{
    private const string PgVersion = "18.6.0";
    private const string CreateTableSql = "CREATE TABLE table1(ID CHAR(256) CONSTRAINT id PRIMARY KEY, Title CHAR)";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // one download and extraction per test run, whatever DbDir a test uses
    private static readonly string SharedBinariesDir = Path.Combine(Directory.GetCurrentDirectory(), "pg_embed", "binaries");

    private static PgServerOptions Options() => new() { PgVersion = PgVersion, BinariesDir = SharedBinariesDir };

    private static async Task<object?> ExecuteScalar(PgServer server, string sql)
    {
        await using var conn = new NpgsqlConnection(server.ConnectionString + ";Pooling=false");
        await conn.OpenAsync(Ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        return await cmd.ExecuteScalarAsync(Ct);
    }

    private static string CreateDir(params string[] parts)
    {
        var path = Path.Combine([Directory.GetCurrentDirectory(), .. parts]);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public async Task starts_server_and_creates_table()
    {
        await using var server = new PgServer(PgVersion);
        await server.StartAsync(Ct);

        Assert.True(server.IsRunning);
        await ExecuteScalar(server, CreateTableSql);
        Assert.StartsWith("PostgreSQL 18.", (string?)await ExecuteScalar(server, "SELECT version()"));
    }

    [Fact]
    public async Task applies_server_params_including_values_with_spaces_and_quotes()
    {
        await using var server = new PgServer(Options() with
        {
            ServerParams = new Dictionary<string, string>
            {
                ["max_connections"] = "300",
                ["timezone"] = "UTC",
                ["application_name"] = "pg embed's test"
            }
        });
        await server.StartAsync(Ct);

        Assert.Equal("300", await ExecuteScalar(server, "SHOW max_connections"));
        Assert.Equal("pg embed's test", await ExecuteScalar(server, "SELECT current_setting('application_name')"));
    }

    [Fact]
    public async Task can_be_stopped_explicitly_and_start_is_idempotent()
    {
        var server = new PgServer(PgVersion);
        try
        {
            await server.StartAsync(Ct);
            await server.StartAsync(Ct);
            await ExecuteScalar(server, CreateTableSql);
        }
        finally
        {
            await server.StopAsync(Ct);
        }

        Assert.False(server.IsRunning);
        await server.DisposeAsync();
    }

    [Fact]
    public async Task ephemeral_instance_folder_is_removed_on_dispose_by_default()
    {
        string instanceDir;
        await using (var server = new PgServer(PgVersion))
        {
            await server.StartAsync(Ct);
            instanceDir = server.InstanceDir;
            Assert.True(Directory.Exists(instanceDir));
        }

        Assert.False(Directory.Exists(instanceDir));
    }

    [Fact]
    public async Task named_instance_keeps_its_data_across_restarts()
    {
        var options = Options() with { InstanceId = Guid.NewGuid() };

        await using (var server = new PgServer(options))
        {
            await server.StartAsync(Ct);
            await ExecuteScalar(server, CreateTableSql);
            await ExecuteScalar(server, "INSERT INTO table1 VALUES ('1', 'a')");
        }

        // a restart picks a new free port; the managed config is rewritten with it
        await using (var server = new PgServer(options with { ClearInstanceDirOnStop = true }))
        {
            await server.StartAsync(Ct);
            Assert.Equal(1L, await ExecuteScalar(server, "SELECT count(*) FROM table1"));
        }
    }

    [Fact]
    public async Task supports_db_dir_with_spaces()
    {
        await using var server = new PgServer(Options() with
        {
            DbDir = CreateDir("folder with spaces", "x -test")
        });
        await server.StartAsync(Ct);
        await ExecuteScalar(server, CreateTableSql);
    }

    private const string NonAsciiFolder = "文件夹 ünïcödé";

    // On Windows, Postgres cannot use non-ASCII paths, so the library falls back to the
    // path's 8.3 short name; that only exists where the volume creates short names
    // (the system drive by default, but not e.g. the D: drive of GitHub's runners).
    private static bool HasShortNames(string dir)
    {
        try
        {
            PathUtils.ToProcessSafePath(dir, nameof(PgServerOptions.DbDir));
            return true;
        }
        catch (PostgresEmbedException)
        {
            return false;
        }
    }

    // https://github.com/mysticmind/mysticmind-postgresembed/issues/29
    [Fact]
    public async Task supports_db_dir_with_non_ascii_characters()
    {
        // the temp folder is on the system drive on Windows, like the paths in #29
        var dbDir = Path.Combine(Path.GetTempPath(), "mysticmind-postgresembed-tests", NonAsciiFolder);
        Directory.CreateDirectory(dbDir);
        Assert.SkipUnless(HasShortNames(dbDir), "8.3 short names are disabled on the temp folder's volume");

        await using var server = new PgServer(Options() with { DbDir = dbDir });
        await server.StartAsync(Ct);
        await ExecuteScalar(server, CreateTableSql);
    }

    [Fact]
    public async Task non_ascii_db_dir_without_short_names_fails_with_a_clear_error()
    {
        var dbDir = CreateDir(NonAsciiFolder);
        Assert.SkipUnless(OperatingSystem.IsWindows() && !HasShortNames(dbDir),
            "only applies on Windows, on a volume without 8.3 short names");

        // an empty binaries folder of its own, to show the check runs before any download
        var binariesDir = CreateDir("fail-fast-binaries-" + Guid.NewGuid().ToString("N"));

        await using var server = new PgServer(Options() with { DbDir = dbDir, BinariesDir = binariesDir });
        var ex = await Assert.ThrowsAsync<PostgresEmbedException>(() => server.StartAsync(Ct));

        Assert.Contains("PgServerOptions.DbDir", ex.Message);
        Assert.Contains("8.3 short names turned off", ex.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(binariesDir));
    }

    [Fact]
    public void non_ascii_path_error_says_what_is_wrong_and_how_to_fix_it()
    {
        var message = PathUtils.NonAsciiPathMessage(@"D:\a\" + NonAsciiFolder + @"\pg_embed", nameof(PgServerOptions.DbDir));

        Assert.Contains(@"cannot use the folder 'D:\a\文件夹 ünïcödé\pg_embed'", message);
        Assert.Contains("non-ASCII characters (文 件 夹 ü ï ö é)", message);
        Assert.Contains("drive D: has 8.3 short names turned off", message);
        Assert.Contains("Set PgServerOptions.DbDir to a folder whose full path is ASCII only", message);
        Assert.Contains("fsutil 8dot3name set D: 0", message);
        Assert.Contains("https://github.com/mysticmind/mysticmind-postgresembed/issues/29", message);
    }

    [Fact]
    public void non_ascii_path_error_on_a_network_share_names_the_option_and_skips_fsutil()
    {
        var message = PathUtils.NonAsciiPathMessage(@"\\server\share\" + NonAsciiFolder, nameof(PgServerOptions.BinariesDir));

        Assert.Contains("set PgServerOptions.BinariesDir to a folder whose full path is ASCII only", message);
        Assert.DoesNotContain("fsutil", message);
    }

    [Fact]
    public void non_ascii_path_error_reports_a_windows_failure_to_resolve_a_short_name()
    {
        var message = PathUtils.NonAsciiPathMessage(@"C:\" + NonAsciiFolder, nameof(PgServerOptions.DbDir), win32Error: 5);

        Assert.Contains("Windows could not provide an ASCII-only (8.3) form of the path (Win32 error 5)", message);
    }

    [Fact]
    public async Task supports_db_dir_with_spaces_and_local_user_permission()
    {
        await using var server = new PgServer(Options() with
        {
            DbDir = CreateDir("folder with spaces", "perm"),
            AddLocalUserAccessPermission = true
        });
        await server.StartAsync(Ct);
        await ExecuteScalar(server, CreateTableSql);
    }

    [Fact]
    public async Task unknown_version_fails_fast_with_a_clear_error()
    {
        await using var server = new PgServer(new PgServerOptions { PgVersion = "9.9.9", DownloadRetryCount = 3 });

        var ex = await Assert.ThrowsAsync<PostgresEmbedException>(() => server.StartAsync(Ct));
        Assert.Contains("404", ex.Message);
    }

    [Fact]
    public async Task installs_postgis_extension()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The PostGIS bundle used here is Windows only");

        await using var server = new PgServer(new PgServerOptions
        {
            PgVersion = "17.6.0",
            Extensions = [new PgExtensionConfig("https://download.osgeo.org/postgis/windows/pg17/postgis-bundle-pg17-3.6.2x64.zip")]
        });
        await server.StartAsync(Ct);

        await ExecuteScalar(server, "CREATE EXTENSION postgis; CREATE EXTENSION fuzzystrmatch");
        Assert.NotNull(await ExecuteScalar(server, "SELECT postgis_version()"));
    }
}
