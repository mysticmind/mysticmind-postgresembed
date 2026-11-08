namespace MysticMind.PostgresEmbed.Samples;

#region sample_xunit_fixture
public sealed class DatabaseFixture : IAsyncLifetime
{
    public PgServer Server { get; } = new("17");

    public async ValueTask InitializeAsync() => await Server.StartAsync();

    public async ValueTask DisposeAsync() => await Server.DisposeAsync();
}

public class BookTests(DatabaseFixture db) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task Connects()
    {
        await using var conn = new NpgsqlConnection(db.Server.ConnectionString);
        await conn.OpenAsync();
        // ...
    }
}
#endregion
