# Troubleshooting

## initdb failure while starting the server

> initdb: could not change permissions of directory "./pg_embed/aa60c634-fa20-4fa8-b4fc-a43a3b08aa99/data":
> Permission denied

All Postgres processes run under the current account, and Postgres needs full access to the parent folder of
the data directory. On Windows, set `AddLocalUserAccessPermission = true`. The library then runs the following
before initdb:

```console
icacls.exe <instance folder> /t /grant:r <user>:(OI)(CI)F
```

The account needs the right to change folder permissions. You usually only need this on CI agents.

## initdb failure with a large negative number

If `initdb` fails on Windows with a large negative exit code, install the latest
[Microsoft Visual C++ Redistributable](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist).

## Non-ASCII paths on Windows

Postgres on Windows cannot use folder paths with characters outside the system code page, such as Chinese
folder names. The library works around this by handing Postgres the 8.3 short form of the path, which is ASCII
only. Short names are on by default for the system drive, but are often turned off on other drives.

Without them, startup stops before downloading anything, with an error that lists the problem characters and the
two ways out:

- Set `DbDir` (or `BinariesDir`, whichever the error names) to a folder whose full path is ASCII only, such as
  `C:\pg-embed`.
- Turn on short names for that drive as an administrator (`fsutil 8dot3name set D: 0`), then delete and
  recreate the folder. Folders created while short names were off never get one.

## Running as root

Postgres refuses to run as root or Administrator. In a Docker container, add a user and run the tests as that
user.

## The server does not start

Startup errors include the end of the server log; the whole log is at `server.LogFile`. Increase
`StartupTimeout` on slow machines (the default is 30 seconds).
