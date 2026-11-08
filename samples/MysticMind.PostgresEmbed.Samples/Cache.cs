namespace MysticMind.PostgresEmbed.Samples;

public class Cache
{
    [Fact]
    public async Task Warm_the_cache()
    {
        var folder = Path.Combine(PgCache.DefaultDirectory, "binaries");

        #region sample_install
        await new PgServer(new PgServerOptions { PgVersion = "17", BinariesDir = folder }).InstallAsync();
        #endregion

        Assert.NotEmpty(Directory.GetDirectories(folder, "postgres-17*"));
    }

    [Fact]
    public async Task Show_install_progress()
    {
        #region sample_install_progress
        await using var server = new PgServer(new PgServerOptions
        {
            PgVersion = "17",
            InstallProgress = new Progress<PgInstallProgress>(p =>
                Console.WriteLine($"{p.Step} {p.Item}: {p.BytesDone} of {p.BytesTotal} bytes")),
        });
        await server.StartAsync();   // reports only when something is downloaded or extracted
        #endregion

        Assert.True(server.IsRunning);
    }

    [Fact]
    public void Where_the_cache_is()
    {
        #region sample_cache_folder
        Console.WriteLine(PgCache.DefaultDirectory);   // PGEMBED_CACHE_DIR, or the per-user cache folder
        #endregion

        Assert.NotEmpty(PgCache.DefaultDirectory);
    }

    [Fact]
    public void Require_a_checksum()
    {
        #region sample_require_checksum
        var options = new PgServerOptions
        {
            PgVersion = "17",
            MavenRepo = "https://maven.example.com/releases",   // a mirror
            RequireChecksum = true,                             // fail if it publishes no SHA-256
        };
        #endregion

        Assert.True(options.RequireChecksum);
    }
}
