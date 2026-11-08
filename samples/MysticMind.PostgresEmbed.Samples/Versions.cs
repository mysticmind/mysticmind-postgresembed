namespace MysticMind.PostgresEmbed.Samples;

public class Versions
{
    [Fact]
    public void Version_specs()
    {
        #region sample_version_specs
        var exact = new PgServer("17.6.0");   // exactly this version
        var major = new PgServer("17");       // newest 17.x
        var minor = new PgServer("17.6");     // newest 17.6.x
        var newest = new PgServer("latest");  // newest of all
        #endregion

        Assert.Equal("17", major.PgVersion);
    }

    [Fact]
    public async Task Published_versions()
    {
        #region sample_list_versions
        IReadOnlyList<string> versions = await PgVersions.GetAvailableAsync();   // for this platform, oldest first
        #endregion

        Assert.Contains(versions, v => v.StartsWith("17."));
    }

    [Fact]
    public async Task The_resolved_version()
    {
        #region sample_resolved_version
        await using var server = new PgServer("17");
        await server.StartAsync();
        Console.WriteLine(server.PgVersion);   // the exact version, such as 17.6.0
        #endregion

        Assert.StartsWith("17.", server.PgVersion);
    }
}
