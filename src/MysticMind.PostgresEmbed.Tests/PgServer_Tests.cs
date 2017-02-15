using System;
using System.Collections.Generic;
using System.Diagnostics;

using Xunit;
using Xunit.Abstractions;

using Polly;

namespace MysticMind.PostgresEmbed.Tests
{
    public class PgServer_Tests
    {
        [Fact]
        public void create_server_and_table_test()
        {
            using (var server = new MysticMind.PostgresEmbed.PgServer("9.5.5.1"))
            {
                server.Start();
                
                // Note: set pooling to false to prevent connecting issues
                // https://github.com/npgsql/npgsql/issues/939
                string connStr = $"Server=localhost;Port={server.Port};User Id=postgres;Password=test;Database=postgres;Pooling=false";
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

        [Fact]
        public void create_server_and_pass_server_params()
        {
            var serverParams = new Dictionary<string, string>();

            // set generic query optimizer to off
            serverParams.Add("geqo", "off");

            // set timezone as UTC
            serverParams.Add("timezone", "UTC");

            // switch off synchronous commit
            serverParams.Add("synchronous_commit", "off");

            // set max connections
            serverParams.Add("max_connections", "300");

            using (var server = new MysticMind.PostgresEmbed.PgServer("9.5.5.1", pgServerParams: serverParams))
            {
                server.Start();

                // Note: set pooling to false to prevent connecting issues
                // https://github.com/npgsql/npgsql/issues/939
                string connStr = $"Server=localhost;Port={server.Port};User Id=postgres;Password=test;Database=postgres;Pooling=false";
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

        [Fact]
        public void create_server_without_using_block()
        {
            var server = new MysticMind.PostgresEmbed.PgServer("9.5.5.1");

            try
            {    
                server.Start();
                string connStr = $"Server=localhost;Port={server.Port};User Id=postgres;Password=test;Database=postgres";
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
            var extensions = new List<PgExtensionConfig>();
            // plv8 extension
            extensions.Add(new PgExtensionConfig(
                    "http://www.postgresonline.com/downloads/pg95plv8jsbin_w64.zip",
                    new List<string> { "CREATE EXTENSION plv8" }
                ));

            using (var server = new MysticMind.PostgresEmbed.PgServer("9.5.5.1", pgExtensions: extensions))
            {
                server.Start();
            }
        }

        [Fact]
        public void create_server_with_postgis_extension_test()
        {
            var extensions = new List<PgExtensionConfig>();
            
            extensions.Add(new PgExtensionConfig(
                    "http://download.osgeo.org/postgis/windows/pg96/postgis-bundle-pg96-2.3.2x64.zip",
                    new List<string>
                        {
                            "CREATE EXTENSION postgis",
                            "CREATE EXTENSION fuzzystrmatch"
                        }
                ));

            using (var server = new MysticMind.PostgresEmbed.PgServer("9.6.2.1", pgExtensions: extensions))
            {
                server.Start();
            }
        }

    }
}