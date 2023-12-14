# MysticMind.PostgresEmbed _Postgres embedded database equivalent for .Net applications_ [![Build status](https://github.com/mysticmind/mysticmind-postgresembed/actions/workflows/ci.yaml/badge.svg)](https://github.com/mysticmind/mysticmind-postgresembed/actions/workflows/ci.yaml) [![NuGet Version](https://badgen.net/nuget/v/mysticmind.postgresembed)](https://www.nuget.org/packages/MysticMind.PostgresEmbed/)

This is a library for running a Postgres server embedded equivalent including extensions targeting Windows, Linux and OSX (including Silicon - M1/M2) available since v3.x or above. This project also handles Postgres extensions very well with a neat way to configure and use it.

Note that until v2.x, this library was only supporting Windows.

By default, this project uses the minimum binaries published by [zonkyio/embedded-postgres-binaries](https://github.com/zonkyio/embedded-postgres-binaries). Note that this is a minimal set of binaries which can be quickly downloaded (around 10MB) for use rather than the official downloads which are pegged at around 100MB. A list of all available versions of postgres binaries is here: https://mvnrepository.com/artifact/io.zonky.test.postgres/embedded-postgres-binaries-bom. If you click on a specific version, you can lookup the OS platforms for which packages are published.

Library automatically detects the OS environment and architecture to setup the library for use accordingly.

If you have benefitted from this library and has saved you a bunch of time, please feel free to sponsor my work!<br>
<a href="https://github.com/sponsors/mysticmind" target="_blank"><img height="30" style="border:0px;height:36px;" src="https://img.shields.io/static/v1?label=GitHub Sponsor&message=%E2%9D%A4&logo=GitHub" border="0" alt="GitHub Sponsor" /></a> <!--<a href="https://ko-fi.com/babuannamalai" target="_blank"><img height="36" style="border:0px;height:36px;" src="https://cdn.ko-fi.com/cdn/kofi4.png?v=3" border="0" alt="Buy Me a Coffee at ko-fi.com" /></a> <a href="https://www.buymeacoffee.com/babuannamalai" target="_blank"><img src="https://cdn.buymeacoffee.com/buttons/default-orange.png" alt="Buy Me A Coffee" height="36" width="174"></a>-->

## Usage
Install the package from Nuget using `Install-Package MysticMind.PostgresEmbed` or clone the repository and build it.

### Example of using Postgres binary
```csharp
// using Postgres 15.3.0 with a using block
using (var server = new MysticMind.PostgresEmbed.PgServer("15.3.0"))
{
    // start the server
    server.Start();
    
    // using Npgsql to connect the server
    string connStr = $"Server=localhost;Port={server.PgPort};User Id=postgres;Password=test;Database=postgres";
    
    var conn = new Npgsql.NpgsqlConnection(connStr);
    
    var cmd =
        new Npgsql.NpgsqlCommand(
            "CREATE TABLE table1(ID CHAR(256) CONSTRAINT id PRIMARY KEY, Title CHAR)",
            conn);

    conn.Open();
    cmd.ExecuteNonQuery();
    conn.Close();
}
```

### Example of using Postgres binary with StartAsync
```csharp
// using Postgres 15.3.0 with a using block
using (var server = new MysticMind.PostgresEmbed.PgServer("15.3.0"))
{
    // start the server
    await server.StartAsync();
    
    // using Npgsql to connect the server
    string connStr = $"Server=localhost;Port={server.PgPort};User Id=postgres;Password=test;Database=postgres";
    
    var conn = new Npgsql.NpgsqlConnection(connStr);
    
    var cmd =
        new Npgsql.NpgsqlCommand(
            "CREATE TABLE table1(ID CHAR(256) CONSTRAINT id PRIMARY KEY, Title CHAR)",
            conn);

    await conn.OpenAsync();
    await cmd.ExecuteNonQueryAsync();
    await conn.CloseAsync();
}
```


### Example of using Postgres and extensions
```csharp
// Example of using Postgres 15.3.0 with extension PostGIS 3.3.3
// you can add multiple create extension sql statements to be run
var extensions = new List<PgExtensionConfig>();
            
extensions.Add(new PgExtensionConfig(
        "https://download.osgeo.org/postgis/windows/pg15/postgis-bundle-pg15-3.3.3x64.zip"));

using (var server = new MysticMind.PostgresEmbed.PgServer("15.3.0", pgExtensions: extensions))
{
    server.Start();
    var connStr = string.Format(ConnStr, server.PgPort, PgUser);
    var conn = new Npgsql.NpgsqlConnection(connStr);
    var cmd = new Npgsql.NpgsqlCommand("CREATE EXTENSION postgis;CREATE EXTENSION fuzzystrmatch", conn);
    conn.Open();
    cmd.ExecuteNonQuery();
    conn.Close(); 
}
```

### Example of passing additional server parameters
```csharp
var serverParams = new Dictionary<string, string>();
            
// set generic query optimizer to off
serverParams.Add("geqo", "off");

// set timezone as UTC
serverParams.Add("timezone", "UTC");

// switch off synchronous commit
serverParams.Add("synchronous_commit", "off");

// set max connections
serverParams.Add("max_connections", "300");

using (var server = new MysticMind.PostgresEmbed.PgServer("15.3.0", pgServerParams: serverParams))
{
    server.Start();

    // do operations here
}
```

### Example of usage in unit tests (xUnit)

Since download and extraction of binaries take time, it would be good strategy to setup and teardown the server for each unit tests class instance.

With xUnit, you will need to create a fixture and wire it as a class fixture. See code below:

```csharp
// this example demonstrates writing an xUnit class fixture
// implements IDisposable to help with the teardown logic.
public class DatabaseServerFixture : IDisposable
    {
        private static PgServer _pgServer;

        public DatabaseServerFixture()
        {
            var pgExtensions = new List<PgExtensionConfig>();
            pgExtensions.Add(
                new PgExtensionConfig(
                    "https://download.osgeo.org/postgis/windows/pg15/postgis-bundle-pg15-3.3.3x64.zip"));

            _pgServer = new PgServer("15.3.0", port: 5432, pgExtensions: pgExtensions);
            _pgServer.Start();
            var connStr = string.Format(ConnStr, server.PgPort, PgUser);
            var conn = new Npgsql.NpgsqlConnection(connStr);
            var cmd = new Npgsql.NpgsqlCommand("CREATE EXTENSION postgis", conn);
            conn.Open();
            cmd.ExecuteNonQuery();
            conn.Close();
        }

        public void Dispose()
        {
            if (_pgServer != null)
            {
                _pgServer.Stop();
            }
        }
    }
    
    // wire DatabaseServerFixture fixture as a class fixture 
    // so that it is created once for the whole class 
    // and shared across all unit tests within the class
    public class my_db_tests : IClassFixture<DatabaseServerFixture>
    {
        [Fact]
        public void your_test()
        {
            // add your test code
        }
    }
```

## Few gotchas
- You can pass a port parameter while creating instance. If you don't pass one, system will use a free port to start the server. Use `server.PgPort` to fetch the port used by the embedded server
- `postgres` is the default database created
- `postgres` is the default user (super user) to be used for connection
- Trust authentication is the default authentication. You can pass any password in connection string which will be ignored by the server. Since our primary motivation is to use the server for unit tests on localhost, this is pretty fine to keep it simple.
- If you pass `DbDir` path while creating the server then it will be used as the working directory else it will use the current directory. You will find a folder named `pg_embed` within which the `binaries` and instance folders are created.
- If you would want to clear the whole root working directory prior to start of server(clear the all the folders from prior runs), you can pass `clearWorkingDirOnStart=true` in the constuctor while creating the server. By default this value is `false`.
- If you would want to clear the instance directory on stopping the server, you could pass `clearInstanceDirOnStop=true` in the constuctor while creating the server. By default this value is `false`.
- If you would want to run a named instance, you can pass a guid value for `instanceId` in the constructor. This will be helpful in scenarios where you would want to rerun the same named instance already setup. In this case, if the named directory exists, system will skip the setup process and start the server. Note that `clearInstanceDirOnStop` and `clearWorkingDirOnStart` should be `false` (this is the default as well).
- If you don't pass a `instanceId`, system will create a new instance by running the whole setup process for every server start.

## How it works
The following steps are done when you run an embedded server:
- Binaries of configured Postgres version and the extensions are downloaded. 
- For Postgres binary, nupkg of the published nuget package version is downloaded and the binary zip file is extracted from it
- For Postgres extensions, file is downloaded from the configured url. You have to choose the right version of extension compatible with the Postgres version.
- Since downloads from http endpoints can be flaky, retry logic is implemented with 3 retries for every file being downloaded.
- Several steps are executed in order once you start the server 
- All binaries zip files once downloaded are stored under `[specified db dir]\pg_embed\binaries` and reused on further runs.
- Since each run of embedded server can possibly use a combination of Postgres version and extensions. Hence implemented a concept of an instance containing the extracted Postgres binary, extensions and db data. 
- Each instance has a instance folder (guid) which contains the `pgsql` and `data` folders. Instance folder is created and removed for each embedded server setup and tear down. 
- Binary files of Postgres and the extensions are extracted into the instance `pgsql` folder
- InitDb is run on the `data` folder calling `initdb` in a `Process`
- Server is started by instantiating a new process on a free port (in the range of 5500 or above) using `pg_ctl`. 
- System will wait and check (fires a sql query using `psql` at a set  interval) if the server has been started till a defined wait timeout.
- Create extensions sql commands configured are run to install the extensions. All sql statements are combined together and run as a single query via a new process and psql.exe
- After using the server, system will tear down by running a fast stop Process, kill the server process and clear the instance folder.
- Server  implements `IDisposable` to call Stop automatically within the context of a `using(..){...}` block. If using an unit test setup and teardown at the class level, you will call `Start()` and `Stop()` appropriately.

## Breaking changes in v3.x
- `PgServer` class constructor signatures have changed.
- Lib no more uses [PostgreSql.Binaries.Lite](https://github.com/mihasic/PostgreSql.Binaries.Lite)
- With regards to postgres extensions, end-users will need to run `create extension <extn_name>;` to install the extension. Library will only download and extract the extension based on the url provided.

## Known Issues
- Some test tend to fail when running all at once in Rider with the exception message: "the database system is starting up". Just rerun that specific test and it will pass.

### Npgsql exception
If you are using [Npgsql](https://github.com/npgsql), when you execute the server, you may sporadically notice the following exception

> Npgsql.NpgsqlException : Unable to write data to the transport connection: An existing connection was forcibly closed by the remote host.

Refer https://github.com/npgsql/npgsql/issues/939 to know details. Resolution is to use `Pooling=false` in connection string.

### InitDb failure while starting embedded server

> fixing permissions on existing directory ./pg_embed/aa60c634-fa20-4fa8-b4fc-a43a3b08aa99/data ... initdb: could not change permissions of directory "./pg_embed/aa60c634-fa20-4fa8-b4fc-a43a3b08aa99/data": Permission denied

All processes run from within the embedded server runs under local account. Postgres expects that the parent folder of the data directory has full access permission for the local account.

The fix is to pass a flag `addLocalUserAccessPermission` as `true` and the system will attempt to add full access before the InitDb step as below for the case of Windows:

```
icacls.exe c:\pg_embed\aa60c634-fa20-4fa8-b4fc-a43a3b08aa99 /t /grant:r <user>:(OI)(OC)F
```

For the case of *nix, all the binaries in bin folder are set to `755` by the library to execute.

### InitDb failure with a large negative number

If you are seeing failures with `initdb` with a large negative number then it could be a dependency library issue for Postgres itself, you would need to install [Visual C++ Redistributable Packages for Visual Studio 2013](https://www.microsoft.com/en-us/download/details.aspx?id=40784) to make `MSVCR120.dll` available for Postgres to use.

Note:
1. The local account should have rights to change folder permissions otherwise the operation will result in an exception.
2. You may not face this issue in development environments.
3. This step was required to be enabled for Appveyor CI builds to succeed.

## Acknowledgements
- This project uses the minimal Postgres binaries published via [zonkyio/embedded-postgres-binaries](https://github.com/zonkyio/embedded-postgres-binaries).

- Looked at projects [Yandex Embedded PostgresSQL](https://github.com/yandex-qatools/postgresql-embedded) and [OpenTable Embedded PostgreSQL Component](https://github.com/opentable/otj-pg-embedded) while brainstorming the implementation.

Note that the above projects had only dealt with Postgres binary and none had options to deal with the Postgres extensions.
 
## License
MysticMind.PostgresEmbed is licensed under [MIT License](http://www.opensource.org/licenses/mit-license.php). Refer to [License file](https://github.com/mysticmind/mysticmind-postgresembed/blob/master/LICENSE) for more information.

Copyright © 2023 Babu Annamalai


