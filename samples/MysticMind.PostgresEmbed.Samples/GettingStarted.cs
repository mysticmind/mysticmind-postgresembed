namespace MysticMind.PostgresEmbed.Samples;

public class GettingStarted
{
    [Fact]
    public async Task Start_a_server_and_connect()
    {
        #region sample_start_server
        await using var server = new PgServer("17");
        await server.StartAsync();

        // trust authentication: no password needed
        await using var conn = new NpgsqlConnection(server.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand("CREATE TABLE books(id int PRIMARY KEY, title text)", conn);
        await cmd.ExecuteNonQueryAsync();
        #endregion

        Assert.True(server.IsRunning);
    }

    [Fact]
    public void Which_binaries_this_machine_uses()
    {
        #region sample_platform
        PgPlatformInfo platform = PgPlatform.Detect();
        Console.WriteLine($"{platform.Platform} {platform.Architecture}: {platform.ArtifactName}");
        #endregion

        Assert.NotEmpty(platform.ArtifactName);
    }
}
