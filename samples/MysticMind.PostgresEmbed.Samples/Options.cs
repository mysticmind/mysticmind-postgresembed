using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MysticMind.PostgresEmbed.Samples;

public class Options
{
    [Fact]
    public async Task Configure_a_server()
    {
        ILoggerFactory loggerFactory = NullLoggerFactory.Instance;
        var dbDir = Path.Combine(Path.GetTempPath(), $"postgresembed-options-{Guid.NewGuid():N}");

        #region sample_options
        await using var server = new PgServer(new PgServerOptions
        {
            PgVersion = "17",
            DbDir = dbDir,                  // default: the current directory
            ServerParams = new Dictionary<string, string>
            {
                ["max_connections"] = "300",
                ["timezone"] = "UTC",
                ["synchronous_commit"] = "off"
            },
            StartupTimeout = TimeSpan.FromSeconds(60),
            Logger = loggerFactory.CreateLogger("Postgres")
        });
        await server.StartAsync();
        Console.WriteLine(server.PgPort);   // a free port unless you set Port
        #endregion

        await using var conn = new NpgsqlConnection(server.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SHOW max_connections", conn);
        Assert.Equal("300", await cmd.ExecuteScalarAsync());
    }
}
