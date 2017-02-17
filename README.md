# MysticMind.PostgresEmbed _Postgres embedded database equivalent for .Net applications_

This is a library for running a Postgres server embedded equivalent including extensions targeting Windows x64. This project also handles Postgres extensions very well with a neat way to configure and use it.

This project uses the binaries published in Nuget package [PostgreSql.Binaries.Lite](https://www.nuget.org/packages/PostgreSql.Binaries.Lite/). Note that this is a minimal set of binaries which can be quickly downloaded (less than 20MB) for use rather than the official downloads which are pegged at around 100MB.

## Usage

### Example of using Postgres binary
```
// using Postgres 9.5.5.1 with a using block
using (var server = new MysticMind.PostgresEmbed.PgServer("9.5.5.1"))
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

### Example of using Postgres and extensions
```
// Example of using Postgres 9.6.2.1 with extension PostGIS 2.3.2
// you can add multiple create extension sql statements to be run
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
```

```
// Example of using Postgres 9.5.5.1 with extension PLV8
var extensions = new List<PgExtensionConfig>();

extensions.Add(new PgExtensionConfig(
    "http://www.postgresonline.com/downloads/pg95plv8jsbin_w64.zip",
    new List<string> { "CREATE EXTENSION plv8" }
));

using (var server = new MysticMind.PostgresEmbed.PgServer("9.5.5.1", pgExtensions: extensions))
{
    server.Start();
}
```

### Example of passing additional server parameters
```
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

    // do operations here
}
```

## Few gotchas
- You can pass a port parameter while creating instance. If you don't pass one, system will use a free port to start the server. Use `server.PgPort` to fetch the port used by the embedded server
- `postgres` is the default database created
- `postgres` is the default user (super user) to be used for connection
- Trust authentication is the default authentication. You can pass any password in connection string which will be ignored by the server. Since our primary motivation is to use the server for unit tests on localhost, this is pretty fine to keep it simple.
- If you has passed `DbDir` path while creating the server then it will be used as the working directory else it will use the current directory. You will find a folder named `pg_embed` within which the `binaries` and instance folders are created.
- If you would want to validate something on an instance, you could pass `clearInstanceDir=false` while creating the server. This won't delete the folder after the execution and tear down of the server. You can manually start the server using `pg_ctl.exe` available within the pgsql bin directory and connect using any Postgres client.  

## How it works
The following steps are done when you run an embedded server:
- Binaries of configured Postgres version and the extensions are downloaded. 
- For Postgres binary, nupkg of the published nuget package version is downloaded and the binary zip file is extracted from it
- For Postgres extensions, file is downloaded from the configured url. You have to choose the right version of extension compatible with the Postgres version.
- Several steps are executed in order once you start the server 
- All binaries zip files once downloaded are stored under `[specified db dir]\pg_embed\binaries` and reused on further runs.
- Since each run of embedded server can possibly use a combination of Postgres version and extensions. Hence implemented a concept of an instance containing the extracted Postgres binary, extensions and db data. 
- Each instance has a instance folder (guid) which contains the `pgsql` and `data` folders. Instance folder is created and removed for each embedded server setup and tear down. 
- Binary files of Postgres and the extensions are extracted into the instance `pgsql` folder
- InitDb is run on the `data` folder calling `initdb.exe` in a `Process`
- Server is started by instantiating a new process on a free port (in the range of 5500 or above) using `pg_ctl.exe`. 
- System will wait and check (fires a sql query using `psql.exe` at a set  interval) if the server has been started till a defined wait timeout.
- Create extensions sql commands configured are run to install the extensions. All sql statements are combined together and run as a single query via a new process and psql.exe
- After using the server, system will tear down by running a fast stop Process, kill the server process and clear the instance folder.
- Server  implements `IDisposable` to call Stop automatically within the context of a `using(..){...}` block. If using an unit test setup and teardown at the class level, you will call `Start()` and `Stop()` appropriately.

## Known Issues

### Npgsql exception
If you are using [Npgsql](https://github.com/npgsql), when you execute the server, you may sporadically notice the following exception

> Npgsql.NpgsqlException : Unable to write data to the transport connection: An existing connection was forcibly closed by the remote host.

Refer https://github.com/npgsql/npgsql/issues/939 to know details. Resolution is to use `Pooling=false` in connection string.

### InitDb failure while starting embedded server

> fixing permissions on existing directory ./pg_embed/aa60c634-fa20-4fa8-b4fc-a43a3b08aa99/data ... initdb: could not change permissions of directory "./pg_embed/aa60c634-fa20-4fa8-b4fc-a43a3b08aa99/data": Permission denied

All processes run from within the embedded server runs under local account. Postgres expects that the parent folder of the data directory has full access permission for the local account.

The fix is to pass a flag `addLocalUserAccessPermission` as `true` and the system will attempt to add full access before the InitDb step as below:

```
icacls.exe c:\pg_embed\aa60c634-fa20-4fa8-b4fc-a43a3b08aa99 /t /grant:r <user>:(OI)(OC)F
```

Note: 
1. The local account should have rights to change folder permissions otherwise the operation will result in an exception.
2. You may not face this issue in development environments.
1. This step was required to be enabled for Appveyor CI builds to succeed.

## Pending Tasks
Wire up CI via Appveyor and releasing Nuget package.

## Acknowledgements
- This project uses the Postgres binaries published via [PostgreSql.Binaries.Lite](https://github.com/mihasic/PostgreSql.Binaries.Lite).

- Looked at projects [Yandex Embedded PostgresSQL](https://github.com/yandex-qatools/postgresql-embedded) and [OpenTable Embedded PostgreSQL Component](https://github.com/opentable/otj-pg-embedded) while brainstorming the implementation.

Note that the above projects had only dealt with Postgres binary and none had options to deal with the Postgres extensions.
 
## License
MysticMind.PostgresEmbed is licensed under [MIT License](http://www.opensource.org/licenses/mit-license.php). Refer to [License file](https://github.com/mysticmind/mysticmind-postgresembed/blob/master/LICENSE) for more information.

Copyright © 2017 Babu Annamalai


