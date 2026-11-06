using System.Diagnostics;
using System.Globalization;
using System.Text;
using CliWrap;
using CliWrap.Buffered;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Polly.Retry;

namespace MysticMind.PostgresEmbed;

/// <summary>
/// An embedded Postgres server. Call <see cref="StartAsync"/> to download (once), set up and start the
/// server, and dispose it (<c>await using</c>) or call <see cref="StopAsync"/> to shut it down.
/// </summary>
public sealed class PgServer : IAsyncDisposable
{
    private const string PgHost = "localhost";
    private const string DefaultDatabase = "postgres";
    private const string ManagedConfFile = "pg_embed.conf";
    private const string IncludeManagedConf = $"include_if_exists = '{ManagedConfFile}'";

    private readonly PgServerOptions _options;
    private readonly ILogger _logger;
    private readonly Platform _platform;
    private readonly Architecture _architecture;
    private readonly bool _clearInstanceDirOnStop;
    private readonly ResiliencePipeline _downloadRetry;
    private readonly ResiliencePipeline _deleteRetry;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

    // folders as handed to the Postgres tools (8.3 form on Windows when needed, see PathUtils)
    private string _processInstanceDir;
    private string _processInstallDir;
    private bool _isRunning;
    private bool _disposed;

    /// <summary>Creates a server for <paramref name="pgVersion"/> with default options.</summary>
    public PgServer(string pgVersion) : this(new PgServerOptions { PgVersion = pgVersion })
    {
    }

    public PgServer(PgServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.PgVersion);

        _options = options;
        _logger = options.Logger ?? NullLogger.Instance;
        _platform = options.Platform ?? PlatformInfo.DetectPlatform();
        _architecture = options.Architecture ?? PlatformInfo.DetectArchitecture(_platform);
        _clearInstanceDirOnStop = options.ClearInstanceDirOnStop ?? options.InstanceId is null;

        PgVersion = options.PgVersion;
        PgUser = string.IsNullOrWhiteSpace(options.PgUser) ? "postgres" : options.PgUser;
        PgPort = options.Port == 0 ? PortFinder.GetFreePort() : options.Port;
        Locale = string.IsNullOrWhiteSpace(options.Locale)
            ? (_platform == Platform.Windows ? null : "en_US.UTF-8")
            : options.Locale;

        var instanceId = options.InstanceId ?? Guid.NewGuid();
        DbDir = Path.GetFullPath(Path.Combine(string.IsNullOrWhiteSpace(options.DbDir) ? "." : options.DbDir, "pg_embed"));
        BinariesDir = string.IsNullOrWhiteSpace(options.BinariesDir)
            ? Path.Combine(DbDir, "binaries")
            : Path.GetFullPath(options.BinariesDir);
        InstanceDir = Path.Combine(DbDir, instanceId.ToString());
        InstallDir = Path.Combine(BinariesDir, InstallFolderName(options, _platform, _architecture));
        _processInstanceDir = InstanceDir;
        _processInstallDir = InstallDir;

        _downloadRetry = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                // a PostgresEmbedException (such as a 404 for an unknown version) is not transient
                ShouldHandle = new PredicateBuilder()
                    .Handle<Exception>(ex => ex is not (PostgresEmbedException or OperationCanceledException)),
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                MaxRetryAttempts = Math.Max(1, options.DownloadRetryCount)
            })
            .Build();

        _deleteRetry = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<IOException>().Handle<UnauthorizedAccessException>(),
                Delay = options.DeleteFolderInitialDelay,
                BackoffType = DelayBackoffType.Exponential,
                MaxRetryAttempts = Math.Max(1, options.DeleteFolderRetryCount)
            })
            .Build();
    }

    public string PgVersion { get; }

    public string PgUser { get; }

    public int PgPort { get; }

    public string PgDbName => DefaultDatabase;

    public string? Locale { get; }

    /// <summary>The <c>pg_embed</c> working folder.</summary>
    public string DbDir { get; }

    /// <summary>Cache of downloaded archives and extracted installs, shared by all instances under <see cref="DbDir"/>.</summary>
    public string BinariesDir { get; }

    /// <summary>
    /// Extracted Postgres install (binaries plus configured extensions) used by this server. It is shared,
    /// read only, by every instance with the same version, platform and extensions.
    /// </summary>
    public string InstallDir { get; }

    /// <summary>Instance folder holding the data directory and the server log.</summary>
    public string InstanceDir { get; }

    public string PgBinDir => Path.Combine(InstallDir, "bin");

    public string DataDir => Path.Combine(InstanceDir, "data");

    /// <summary>Server log file, useful when diagnosing startup failures.</summary>
    public string LogFile => Path.Combine(InstanceDir, "postgres.log");

    public bool IsRunning => _isRunning;

    /// <summary>
    /// A connection string for the default database, for example
    /// <c>Host=localhost;Port=5432;Username=postgres;Database=postgres</c>. Trust authentication is used,
    /// so no password is needed.
    /// </summary>
    public string ConnectionString => $"Host={PgHost};Port={PgPort};Username={PgUser};Database={PgDbName}";

    private string ProcessBinDir => Path.Combine(_processInstallDir, "bin");

    private string ProcessDataDir => Path.Combine(_processInstanceDir, "data");

    private string ProcessLogFile => Path.Combine(_processInstanceDir, "postgres.log");

    /// <summary>
    /// Downloads the binaries and extensions when they are not cached, sets up the instance when it does
    /// not exist yet, and starts the server. Calling it on a running server does nothing.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isRunning) return;

            if (_options.ClearWorkingDirOnStart)
            {
                DeleteDirectory(DbDir);
            }

            Directory.CreateDirectory(BinariesDir);
            Directory.CreateDirectory(InstanceDir);

            if (!File.Exists(Path.Combine(PgBinDir, ExecutableName("pg_ctl"))))
            {
                await InstallBinariesAsync(cancellationToken).ConfigureAwait(false);
            }

            _processInstanceDir = PathUtils.ToProcessSafePath(InstanceDir);
            _processInstallDir = PathUtils.ToProcessSafePath(InstallDir);

            if (_options.AddLocalUserAccessPermission)
            {
                await AddLocalUserAccessPermissionAsync(cancellationToken).ConfigureAwait(false);
            }

            if (!File.Exists(Path.Combine(DataDir, "PG_VERSION")))
            {
                await InitDbAsync(cancellationToken).ConfigureAwait(false);
            }

            WriteServerConfig();
            await StartServerAsync(cancellationToken).ConfigureAwait(false);

            _isRunning = true;
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            _logger.LogInformation("Postgres {Version} is running on port {Port} ({InstanceDir})", PgVersion, PgPort, InstanceDir);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>
    /// Stops the server and, depending on <see cref="PgServerOptions.ClearInstanceDirOnStop"/>, deletes the
    /// instance folder. Calling it on a stopped server only performs the cleanup.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isRunning)
            {
                AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
                await StopServerAsync(cancellationToken).ConfigureAwait(false);
                _isRunning = false;
                _logger.LogInformation("Postgres on port {Port} stopped", PgPort);
            }

            if (_clearInstanceDirOnStop)
            {
                DeleteDirectory(InstanceDir);
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        await StopAsync().ConfigureAwait(false);
        _disposed = true;
        _lifecycleLock.Dispose();
    }

    private async Task InstallBinariesAsync(CancellationToken cancellationToken)
    {
        var downloader = new PostgresBinaryDownloader(
            PgVersion, BinariesDir, _platform, _architecture, _options.MavenRepo, _options.HttpClient, _logger);

        string archivePath;
        try
        {
            archivePath = await _downloadRetry
                .ExecuteAsync(async ct => await downloader.DownloadAsync(ct).ConfigureAwait(false), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or PostgresEmbedException))
        {
            throw new PostgresEmbedException($"Failed to download Postgres {PgVersion} binaries.", ex);
        }

        var extensions = _options.Extensions.Select(config => new PgExtension(BinariesDir, config)).ToList();

        foreach (var extension in extensions)
        {
            await _downloadRetry
                .ExecuteAsync(
                    async ct => await extension.DownloadAsync(_options.HttpClient, _logger, ct).ConfigureAwait(false),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        // extract into a temporary folder and rename it into place, so concurrent servers (parallel
        // tests, several test processes) never run from a half extracted install
        var stagingDir = $"{InstallDir}.{Guid.NewGuid():N}.tmp";
        try
        {
            _logger.LogInformation("Extracting {Archive} to {InstallDir}", archivePath, InstallDir);
            ArchiveExtractor.ExtractTarArchive(archivePath, stagingDir);

            foreach (var extension in extensions)
            {
                extension.Extract(stagingDir);
            }

            SetBinariesAsExecutable(Path.Combine(stagingDir, "bin"));

            try
            {
                Directory.Move(stagingDir, InstallDir);
            }
            catch (IOException) when (File.Exists(Path.Combine(PgBinDir, ExecutableName("pg_ctl"))))
            {
                // another server finished the same install first
            }
        }
        finally
        {
            if (Directory.Exists(stagingDir)) DeleteDirectory(stagingDir);
        }
    }

    private static void SetBinariesAsExecutable(string binDir)
    {
        if (OperatingSystem.IsWindows()) return;

        const UnixFileMode executable =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

        foreach (var file in Directory.EnumerateFiles(binDir))
        {
            File.SetUnixFileMode(file, executable);
        }
    }

    // for example "postgres-18.6.0-darwin-arm64v8", plus a hash of the extension urls when there are any
    private static string InstallFolderName(PgServerOptions options, Platform platform, Architecture architecture)
    {
        var name = $"postgres-{options.PgVersion}-{PlatformInfo.ArtifactSuffix(platform, architecture)}";
        if (options.Extensions.Count == 0) return name;

        var urls = string.Join('\n', options.Extensions.Select(e => e.DownloadUrl));
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(urls)));
        return $"{name}-ext-{hash[..12].ToLowerInvariant()}";
    }

    // In some environments (CI agents in particular) the current account lacks write access on the
    // instance folder, and initdb fails with "could not change permissions of directory".
    // The account needs rights to change folder permissions for this to work.
    private async Task AddLocalUserAccessPermissionAsync(CancellationToken cancellationToken)
    {
        if (_platform != Platform.Windows) return;

        var result = await Cli.Wrap("icacls.exe")
            .WithArguments([InstanceDir, "/t", "/grant:r", $"{Environment.UserName}:(OI)(CI)F"])
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(cancellationToken)
            .ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new PostgresEmbedException(
                $"Granting full access on '{InstanceDir}' to '{Environment.UserName}' failed with exit code {result.ExitCode}. " +
                $"{result.StandardOutput} {result.StandardError}".TrimEnd());
        }
    }

    private async Task InitDbAsync(CancellationToken cancellationToken)
    {
        // each argument is passed (and escaped) individually, so paths with spaces work (issue #29)
        var args = new List<string>
        {
            "-D", ProcessDataDir,
            "-U", PgUser,
            "-E", "UTF8",
            "-A", "trust",
            "--no-sync"
        };

        if (!string.IsNullOrEmpty(Locale))
        {
            args.AddRange(["--locale", Locale]);
        }

        var result = await Cli.Wrap(Path.Combine(ProcessBinDir, ExecutableName("initdb")))
            .WithArguments(args)
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(cancellationToken)
            .ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new PostgresEmbedException(
                $"initdb failed with exit code {result.ExitCode}.{Environment.NewLine}{result.StandardOutput}{result.StandardError}");
        }
    }

    /// <summary>
    /// Writes port and server parameters to a config file included from postgresql.conf. Unlike passing
    /// them on the pg_ctl command line, this needs no shell quoting, so values with spaces or quotes work.
    /// </summary>
    private void WriteServerConfig()
    {
        var conf = new StringBuilder()
            .AppendLine("# Managed by MysticMind.PostgresEmbed and rewritten on every start.")
            .AppendLine(CultureInfo.InvariantCulture, $"port = {PgPort}")
            .AppendLine("fsync = off");

        foreach (var (name, value) in _options.ServerParams)
        {
            conf.AppendLine(CultureInfo.InvariantCulture, $"{name} = '{value.Replace("'", "''", StringComparison.Ordinal)}'");
        }

        File.WriteAllText(Path.Combine(DataDir, ManagedConfFile), conf.ToString());

        var mainConf = Path.Combine(DataDir, "postgresql.conf");
        if (!File.ReadLines(mainConf).Any(line => line.Trim() == IncludeManagedConf))
        {
            File.AppendAllText(mainConf, $"{Environment.NewLine}{IncludeManagedConf}{Environment.NewLine}");
        }
    }

    private async Task StartServerAsync(CancellationToken cancellationToken)
    {
        var timeoutSeconds = Math.Max(1, (int)Math.Ceiling(_options.StartupTimeout.TotalSeconds));

        // pg_ctl -w returns once the server accepts connections. It is run without redirected output:
        // on Windows the server is launched through cmd.exe, which would inherit redirected handles and
        // keep them open for the lifetime of the server. Server output goes to the log file instead.
        var startInfo = new ProcessStartInfo(Path.Combine(ProcessBinDir, ExecutableName("pg_ctl")))
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in new[]
                 {
                     "start", "-D", ProcessDataDir, "-l", ProcessLogFile, "-w", "-t",
                     timeoutSeconds.ToString(CultureInfo.InvariantCulture), "-s"
                 })
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)
                            ?? throw new PostgresEmbedException("Failed to launch pg_ctl.");

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await StopServerAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        if (process.ExitCode != 0)
        {
            throw new PostgresEmbedException(
                $"Postgres did not start within {timeoutSeconds}s on port {PgPort} (pg_ctl exit code {process.ExitCode})." +
                $"{Environment.NewLine}{ReadLogTail()}");
        }
    }

    private async Task StopServerAsync(CancellationToken cancellationToken)
    {
        var timeoutSeconds = Math.Max(1, (int)Math.Ceiling(_options.ShutdownTimeout.TotalSeconds));

        if (await RunPgCtlStopAsync("fast", timeoutSeconds, cancellationToken).ConfigureAwait(false)) return;

        _logger.LogWarning("Fast shutdown of Postgres on port {Port} failed, trying immediate shutdown", PgPort);
        if (await RunPgCtlStopAsync("immediate", timeoutSeconds, cancellationToken).ConfigureAwait(false)) return;

        KillPostmaster();
    }

    private async Task<bool> RunPgCtlStopAsync(string mode, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var result = await Cli.Wrap(Path.Combine(ProcessBinDir, ExecutableName("pg_ctl")))
            .WithArguments(["stop", "-D", ProcessDataDir, "-m", mode, "-w", "-t", timeoutSeconds.ToString(CultureInfo.InvariantCulture), "-s"])
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(cancellationToken)
            .ConfigureAwait(false);

        return result.ExitCode == 0;
    }

    // last resort, and the handler for a test host exiting without disposing the server
    private void KillPostmaster()
    {
        var pidFile = Path.Combine(DataDir, "postmaster.pid");
        try
        {
            if (!File.Exists(pidFile)) return;

            var firstLine = File.ReadLines(pidFile).FirstOrDefault();
            if (!int.TryParse(firstLine, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid)) return;

            using var postmaster = Process.GetProcessById(pid);
            postmaster.Kill(entireProcessTree: true);
            postmaster.WaitForExit(5000);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            _logger.LogDebug(ex, "Could not kill the Postgres server process");
        }
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        if (_isRunning) KillPostmaster();
    }

    private string ReadLogTail()
    {
        try
        {
            return File.Exists(LogFile) ? string.Join(Environment.NewLine, File.ReadLines(LogFile).TakeLast(20)) : "";
        }
        catch (IOException)
        {
            return "";
        }
    }

    private void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;

        _deleteRetry.Execute(() =>
        {
            if (OperatingSystem.IsWindows())
            {
                // Directory.Delete fails on read-only files on Windows
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
            }

            Directory.Delete(path, recursive: true);
        });
    }

    private string ExecutableName(string name) => _platform == Platform.Windows ? $"{name}.exe" : name;
}
