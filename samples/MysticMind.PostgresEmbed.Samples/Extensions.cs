namespace MysticMind.PostgresEmbed.Samples;

public class Extensions
{
    [Fact]
    public async Task List_available_extensions()
    {
        await using var server = new PgServer("17");
        await server.InstallAsync();

        #region sample_available_extensions
        foreach (var ext in await server.GetAvailableExtensionsAsync())
            Console.WriteLine($"{ext.Name} {ext.DefaultVersion}: {ext.Comment}");
        #endregion

        Assert.Contains(await server.GetAvailableExtensionsAsync(), e => e.Name == "pgcrypto");
    }

    [Fact]
    public async Task Use_a_bundled_extension()
    {
        await using var server = new PgServer("17");
        await server.StartAsync();

        #region sample_create_extension
        await using var conn = new NpgsqlConnection(server.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("CREATE EXTENSION pgcrypto", conn);
        await cmd.ExecuteNonQueryAsync();
        #endregion
    }

    [Fact]
    public async Task Install_an_extension_archive()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the PostGIS bundle in this sample is a Windows build");

        #region sample_extension_archive
        // Windows: the PostGIS bundle for Postgres 17
        await using var server = new PgServer(new PgServerOptions
        {
            PgVersion = "17",
            Extensions =
            [
                new PgExtensionConfig("https://download.osgeo.org/postgis/windows/pg17/postgis-bundle-pg17-3.6.2x64.zip")
            ]
        });
        await server.StartAsync();

        await using var conn = new NpgsqlConnection(server.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("CREATE EXTENSION postgis", conn);
        await cmd.ExecuteNonQueryAsync();
        #endregion
    }

    [Fact]
    public void Verify_an_extension_archive()
    {
        #region sample_extension_sha256
        var postgis = new PgExtensionConfig("https://example.com/my-extension.zip")
        {
            Sha256 = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08"
        };
        #endregion

        Assert.NotNull(postgis.Sha256);
    }
}
