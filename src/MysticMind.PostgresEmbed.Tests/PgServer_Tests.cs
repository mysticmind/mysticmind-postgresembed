using System;
using System.Collections.Generic;
using Xunit;
using System.IO;
using System.Runtime.InteropServices;

namespace MysticMind.PostgresEmbed.Tests;

[CollectionDefinition("Non-Parallel Collection", DisableParallelization = true)]
public class NonParallelCollectionDefinitionClass
{
}

[Collection("Non-Parallel Collection")]
public class PgServerTests
{
    private const string PgUser = "postgres";
    private const string ConnStr = "Server=localhost;Port={0};User Id={1};Password=test;Database=postgres;Pooling=false";

    // this required for the appveyor CI build to set full access for appveyor user on instance folder on Windows
    private const bool AddLocalUserAccessPermission = false;

    [Fact]
    public void create_server_and_table_test()
    {
        using var server = new PgServer(
                
            "15.3.0",
            addLocalUserAccessPermission: AddLocalUserAccessPermission,
            clearInstanceDirOnStop:true);
        server.Start();
                
        // Note: set pooling to false to prevent connecting issues
        // https://github.com/npgsql/npgsql/issues/939
        var connStr = string.Format(ConnStr, server.PgPort, PgUser);
        var conn = new Npgsql.NpgsqlConnection(connStr);
        var cmd =
            new Npgsql.NpgsqlCommand(
                "CREATE TABLE table1(ID CHAR(256) CONSTRAINT id PRIMARY KEY, Title CHAR)",
                conn);

        conn.Open();
        cmd.ExecuteNonQuery();
        conn.Close();
    }

    [Fact]
    public void create_server_and_pass_server_params()
    {
        var serverParams = new Dictionary<string, string>
        {
            // set generic query optimizer to off
            { "geqo", "off" },
            // set timezone as UTC
            { "timezone", "UTC" },
            // switch off synchronous commit
            { "synchronous_commit", "off" },
            // set max connections
            { "max_connections", "300" }
        };

        using var server = new PgServer(
            "15.3.0",
            pgServerParams: serverParams, 
            addLocalUserAccessPermission: AddLocalUserAccessPermission,
            clearInstanceDirOnStop: true);
        server.Start();

        // Note: set pooling to false to prevent connecting issues
        // https://github.com/npgsql/npgsql/issues/939
        var connStr = string.Format(ConnStr, server.PgPort, PgUser);
        var conn = new Npgsql.NpgsqlConnection(connStr);
        var cmd =
            new Npgsql.NpgsqlCommand(
                "CREATE TABLE table1(ID CHAR(256) CONSTRAINT id PRIMARY KEY, Title CHAR)",
                conn);

        conn.Open();
        cmd.ExecuteNonQuery();
        conn.Close();
    }

    [Fact]
    public void create_server_without_using_block()
    {
        var server = new PgServer(
            "15.3.0",
            addLocalUserAccessPermission: AddLocalUserAccessPermission,
            clearInstanceDirOnStop: true);

        try
        {    
            server.Start();
            var connStr = string.Format(ConnStr, server.PgPort, PgUser);
            var conn = new Npgsql.NpgsqlConnection(connStr);
            var cmd =
                new Npgsql.NpgsqlCommand(
                    "CREATE TABLE table1(ID CHAR(256) CONSTRAINT id PRIMARY KEY, Title CHAR)",
                    conn);

            conn.Open();
            cmd.ExecuteNonQuery();
            conn.Close();
        }
        finally
        {
            server.Stop();
        }
    }

    [SkippableFact]
    public void create_server_with_postgis_extension_test()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Test supported only on Windows");
        var extensions = new List<PgExtensionConfig>
        {
            new PgExtensionConfig(
                "https://download.osgeo.org/postgis/windows/pg15/archive/postgis-bundle-pg15-3.3.3x64.zip"
            )
        };
        
        using var server = new PgServer(
            "15.3.0",
            pgExtensions: extensions,
            addLocalUserAccessPermission: AddLocalUserAccessPermission,
            clearInstanceDirOnStop: true);
        server.Start();
        var connStr = string.Format(ConnStr, server.PgPort, PgUser);
        var conn = new Npgsql.NpgsqlConnection(connStr);
        var cmd =
            new Npgsql.NpgsqlCommand(
                "CREATE EXTENSION postgis;CREATE EXTENSION fuzzystrmatch",
                conn);

        conn.Open();
        cmd.ExecuteNonQuery();
        conn.Close();
    }

    [Fact]
    public void create_server_with_user_defined_instance_id_and_table_test()
    {
        using var server = new PgServer(
            "15.3.0",
            addLocalUserAccessPermission: AddLocalUserAccessPermission,
            instanceId: Guid.NewGuid(),
            clearInstanceDirOnStop: true);
        server.Start();

        // assert if instance id directory exists
        Assert.True(Directory.Exists(server.InstanceDir));

        // Note: set pooling to false to prevent connecting issues
        // https://github.com/npgsql/npgsql/issues/939
        var connStr = string.Format(ConnStr, server.PgPort, PgUser);
        var conn = new Npgsql.NpgsqlConnection(connStr);
        var cmd =
            new Npgsql.NpgsqlCommand(
                "CREATE TABLE table1(ID CHAR(256) CONSTRAINT id PRIMARY KEY, Title CHAR)",
                conn);

        conn.Open();
        cmd.ExecuteNonQuery();
        conn.Close();
    }

    [Fact]
    public void create_server_with_existing_instance_id_and_table_test()
    {
        var instanceId = Guid.NewGuid();

        using (var server = new PgServer(
                         "15.3.0",
                         addLocalUserAccessPermission: AddLocalUserAccessPermission,
                         instanceId: instanceId))
        {
            server.Start();

            // assert if instance id directory exists
            Assert.True(Directory.Exists(server.InstanceDir));

            // Note: set pooling to false to prevent connecting issues
            // https://github.com/npgsql/npgsql/issues/939
            var connStr = string.Format(ConnStr, server.PgPort, PgUser);
            var conn = new Npgsql.NpgsqlConnection(connStr);
            var cmd =
                new Npgsql.NpgsqlCommand(
                    "CREATE TABLE table1(ID CHAR(256) CONSTRAINT id PRIMARY KEY, Title CHAR)",
                    conn);

            conn.Open();
            cmd.ExecuteNonQuery();
            conn.Close();
        }

        using (
            var server = new PgServer(
                "15.3.0",
                addLocalUserAccessPermission: AddLocalUserAccessPermission,
                instanceId: instanceId,
                clearInstanceDirOnStop:true,
                startupWaitTime:20*1000
            )
        )
        {
            server.Start();

            // assert if instance id directory exists
            Assert.True(Directory.Exists(server.InstanceDir));
        }
    }

    [Fact]
    public void create_server_without_version_suffix()
    {
        using var server = new PgServer(
            "15.3.0",
            addLocalUserAccessPermission: AddLocalUserAccessPermission,
            clearInstanceDirOnStop: true);
        server.Start();

        // Note: set pooling to false to prevent connecting issues
        // https://github.com/npgsql/npgsql/issues/939
        var connStr = string.Format(ConnStr, server.PgPort, PgUser);
        var conn = new Npgsql.NpgsqlConnection(connStr);
        var cmd =
            new Npgsql.NpgsqlCommand(
                "CREATE TABLE table1(ID CHAR(256) CONSTRAINT id PRIMARY KEY, Title CHAR)",
                conn);

        conn.Open();
        cmd.ExecuteNonQuery();
        conn.Close();
    }
}