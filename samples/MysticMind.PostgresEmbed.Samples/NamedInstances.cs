namespace MysticMind.PostgresEmbed.Samples;

[Collection("named instance app")]
public class NamedInstances
{
    [Fact]
    public async Task Keep_and_reuse_a_server()
    {
        #region sample_named_instance
        await using var server = new PgServer(new PgServerOptions { PgVersion = "17", InstanceName = "app" });
        await server.StartAsync();   // attaches in milliseconds when "app" is already running
        #endregion

        await using var again = new PgServer(new PgServerOptions { PgVersion = "17", InstanceName = "app" });
        await again.StartAsync();
        Assert.True(again.IsAttached);
        await server.StopAsync();
    }

    [Fact]
    public async Task Check_and_control_a_server()
    {
        // running, as if another process had started it (disposing this one would stop it)
        var started = new PgServer(new PgServerOptions { PgVersion = "17", InstanceName = "app" });
        await started.StartAsync();
        Assert.Equal(PgInstanceState.Running, (await new PgServer(new PgServerOptions { PgVersion = "17", InstanceName = "app" }).GetStatusAsync()).State);

        #region sample_instance_status
        await using var app = new PgServer(new PgServerOptions { PgVersion = "17", InstanceName = "app" });
        PgInstanceStatus status = await app.GetStatusAsync();   // State, ProcessId, Port, StartedAt
        await app.StopAsync();                                   // clean shutdown
        await app.KillAsync();                                   // last resort: force-kill
        #endregion

        Assert.NotEqual(PgInstanceState.Running, (await app.GetStatusAsync()).State);
    }
}
