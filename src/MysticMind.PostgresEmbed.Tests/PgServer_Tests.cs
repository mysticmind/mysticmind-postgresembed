using System;
using System.Collections.Generic;
using System.Diagnostics;

using Xunit;
using Xunit.Abstractions;

using Polly;

namespace MysticMind.PostgresEmbed.Tests
{
    using NuGet.Versioning;

    public class PgServer_Tests
    {
        private const string PG_USER = "postgres";
        private const string CONN_STR = "Server=localhost;Port={0};User Id={1};Password=test;Database=postgres;Pooling=false";

        // this required for the appveyor CI build to set full access for appveyor user on instance folder
        private const bool ADD_LOCAL_USER_ACCESS_PERMISSION = true;

        [Fact]
        public void create_server_and_table_test()
        {
            using (var server = new MysticMind.PostgresEmbed.PgServer(
                "9.5.5.1", 
                PG_USER, 
                addLocalUserAccessPermission: ADD_LOCAL_USER_ACCESS_PERMISSION))
            {
                server.Start();
                
                // Note: set pooling to false to prevent connecting issues
                // https://github.com/npgsql/npgsql/issues/939
                string connStr = string.Format(CONN_STR, server.PgPort, PG_USER);
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

            using (var server = new MysticMind.PostgresEmbed.PgServer(
                "9.5.5.1", 
                PG_USER, 
                pgServerParams: serverParams, 
                addLocalUserAccessPermission: ADD_LOCAL_USER_ACCESS_PERMISSION))
            {
                server.Start();

                // Note: set pooling to false to prevent connecting issues
                // https://github.com/npgsql/npgsql/issues/939
                string connStr = string.Format(CONN_STR, server.PgPort, PG_USER);
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
            var server = new MysticMind.PostgresEmbed.PgServer(
                "9.5.5.1", 
                PG_USER,
                addLocalUserAccessPermission: ADD_LOCAL_USER_ACCESS_PERMISSION);

            try
            {    
                server.Start();
                string connStr = string.Format(CONN_STR, server.PgPort, PG_USER);
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

            using (var server = new MysticMind.PostgresEmbed.PgServer(
                "9.5.5.1", 
                PG_USER, 
                pgExtensions: extensions,
                addLocalUserAccessPermission: ADD_LOCAL_USER_ACCESS_PERMISSION))
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

            using (var server = new MysticMind.PostgresEmbed.PgServer(
                "9.6.2.1", 
                PG_USER, 
                pgExtensions: extensions,
                addLocalUserAccessPermission: ADD_LOCAL_USER_ACCESS_PERMISSION))
            {
                server.Start();
            }
        }

    }
}