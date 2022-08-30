using System;
using System.Collections.Generic;
using Xunit;
using System.IO;
using System.Threading.Tasks;

namespace MysticMind.PostgresEmbed.Tests
{
    public class PgServerTests
    {
        private const string PgUser = "postgres";
        private const string ConnStr = "Server=localhost;Port={0};User Id={1};Password=test;Database=postgres;Pooling=false";

        // this required for the appveyor CI build to set full access for appveyor user on instance folder
        private const bool AddLocalUserAccessPermission = true;

        [Fact]
        public void create_server_and_table_test()
        {
            using var server = new PgServer(
                "9.5.5.1", 
                PgUser, 
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
                "9.5.5.1", 
                PgUser, 
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
                "9.5.5.1", 
                PgUser,
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

        [Fact]
        public void create_server_with_plv8_extension_test()
        {
            var extensions = new List<PgExtensionConfig>
            {
                // plv8 extension
                new PgExtensionConfig(
                    "http://www.postgresonline.com/downloads/pg95plv8jsbin_w64.zip",
                    new List<string> { "CREATE EXTENSION plv8" }
                )
            };

            using var server = new PgServer(
                "9.5.5.1", 
                PgUser, 
                pgExtensions: extensions,
                addLocalUserAccessPermission: AddLocalUserAccessPermission,
                clearInstanceDirOnStop: true);
            server.Start();
        }

        [Fact]
        public void create_server_with_postgis_extension_test()
        {
            var extensions = new List<PgExtensionConfig>();
            
            extensions.Add(new PgExtensionConfig(
                    "http://download.osgeo.org/postgis/windows/pg96/archive/postgis-bundle-pg96-2.5.1x64.zip",
                    new List<string>
                        {
                            "CREATE EXTENSION postgis",
                            "CREATE EXTENSION fuzzystrmatch"
                        }
                ));

            using var server = new PgServer(
                "9.6.2.1", 
                PgUser, 
                pgExtensions: extensions,
                addLocalUserAccessPermission: AddLocalUserAccessPermission,
                clearInstanceDirOnStop: true);
            server.Start();
        }

        [Fact]
        public void create_server_with_user_defined_instance_id_and_table_test()
        {
            using var server = new PgServer(
                "9.5.5.1",
                PgUser,
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
                "9.5.5.1",
                PgUser,
                addLocalUserAccessPermission: AddLocalUserAccessPermission,
                instanceId: instanceId))
            {
                server.Start();

                // assert if instance id drectory exists
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

            using (var server = new PgServer(
                "9.5.5.1",
                PgUser,
                addLocalUserAccessPermission: AddLocalUserAccessPermission,
                instanceId: instanceId,
                clearInstanceDirOnStop:true))
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
                "10.5.1",
                PgUser,
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
        public async Task create_server_async_and_table_test()
        {
            using var server = new PgServer(
                "9.5.5.1",
                PgUser,
                addLocalUserAccessPermission: AddLocalUserAccessPermission,
                clearInstanceDirOnStop:true);
            await server.StartAsync();

            // Note: set pooling to false to prevent connecting issues
            // https://github.com/npgsql/npgsql/issues/939
            var connStr = string.Format(ConnStr, server.PgPort, PgUser);
            var conn = new Npgsql.NpgsqlConnection(connStr);
            var cmd =
                new Npgsql.NpgsqlCommand(
                    "CREATE TABLE table1(ID CHAR(256) CONSTRAINT id PRIMARY KEY, Title CHAR)",
                    conn);

            await conn.OpenAsync();
            await cmd.ExecuteNonQueryAsync();
            await conn.CloseAsync();
        }
        
        [Fact]
        public async Task create_server_async_without_using_block()
        {
            var server = new PgServer(
                "9.5.5.1", 
                PgUser,
                addLocalUserAccessPermission: AddLocalUserAccessPermission,
                clearInstanceDirOnStop: true);

            try
            {    
                await server.StartAsync();
                var connStr = string.Format(ConnStr, server.PgPort, PgUser);
                var conn = new Npgsql.NpgsqlConnection(connStr);
                var cmd =
                    new Npgsql.NpgsqlCommand(
                        "CREATE TABLE table1(ID CHAR(256) CONSTRAINT id PRIMARY KEY, Title CHAR)",
                        conn);

                await conn.OpenAsync();
                await cmd.ExecuteNonQueryAsync();
                await conn.CloseAsync();
            }
            finally
            {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Bug_19_authors_md_file_already_exists()
        {
            var extensions = new List<PgExtensionConfig>();

            extensions.Add(new PgExtensionConfig(
                "https://download.osgeo.org/postgis/windows/pg96/postgis-bundle-pg96-3.2.3x64.zip",
                new List<string>
                {
                    "CREATE EXTENSION postgis"
                }
            ));

            using var server = new PgServer(
                "9.6.2.1",
                PgUser,
                pgExtensions: extensions,
                addLocalUserAccessPermission: AddLocalUserAccessPermission,
                clearInstanceDirOnStop: true);
            await server.StartAsync();
        }
    }
}