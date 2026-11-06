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
    private bool _ownsServer;
    private bool _disposed;

    // generous: a start can include the first download of the binaries
    private static readonly TimeSpan LockTimeout = TimeSpan.FromMinutes(10);

    // PgVersion as given ("17", "latest", "17.6.0"), and the exact version it resolved to
    private readonly string _requestedVersion;
    private string? _resolvedVersion;

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
        if (options.InstanceName is not null)
        {
            if (options.InstanceId is not null)
            {
                throw new ArgumentException("Set InstanceName or InstanceId, not both.", nameof(options));
            }

            if (!System.Text.RegularExpressions.Regex.IsMatch(options.InstanceName, "^[A-Za-z0-9][A-Za-z0-9._-]{0,62}$"))
            {
                throw new ArgumentException(
                    $"Invalid InstanceName '{options.InstanceName}': use letters, digits, '.', '_' and '-', " +
                    "starting with a letter or digit, up to 63 characters.", nameof(options));
            }
        }

        InstanceName = options.InstanceName;
        _clearInstanceDirOnStop = options.ClearInstanceDirOnStop ?? (options.InstanceId is null && options.InstanceName is null);

        _requestedVersion = options.PgVersion.Trim();
        if (PgVersions.IsExact(_requestedVersion)) _resolvedVersion = _requestedVersion;

        PgUser = string.IsNullOrWhiteSpace(options.PgUser) ? "postgres" : options.PgUser;
        PgPort = options.Port == 0 ? PortFinder.GetFreePort() : options.Port;
        Locale = string.IsNullOrWhiteSpace(options.Locale)
            ? (_platform == Platform.Windows ? null : "en_US.UTF-8")
            : options.Locale;

        // Folders. Unchanged from 5.0 unless PGEMBED_CACHE_DIR is set or the instance is named: a named
        // instance lives in the shared cache (unless DbDir is given), so tests, apps and the pgembed CLI meet.
        var explicitDbDir = !string.IsNullOrWhiteSpace(options.DbDir);
        DbDir = Path.GetFullPath(Path.Combine(explicitDbDir ? options.DbDir! : ".", "pg_embed"));
        var useSharedCache = PgCache.IsConfigured || (options.InstanceName is not null && !explicitDbDir);

        BinariesDir = !string.IsNullOrWhiteSpace(options.BinariesDir)
            ? Path.GetFullPath(options.BinariesDir)
            : useSharedCache
                ? Path.Combine(PgCache.DefaultDirectory, "binaries")
                : Path.Combine(DbDir, "binaries");

        InstanceDir = options.InstanceName is null
            ? Path.Combine(DbDir, (options.InstanceId ?? Guid.NewGuid()).ToString())
            : explicitDbDir
                ? Path.Combine(DbDir, options.InstanceName)
                : Path.Combine(PgCache.DefaultDirectory, "instances", options.InstanceName);
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

    /// <summary>
    /// The exact Postgres version. For a spec such as <c>"17"</c> or <c>"latest"</c>, this is the spec until
    /// <see cref="StartAsync"/> or <see cref="InstallAsync"/> resolves it, and the resolved version after.
    /// </summary>
    public string PgVersion => _resolvedVersion ?? _requestedVersion;

    public string PgUser { get; }

    /// <summary>The port. When attaching to a server that was already running, the port that server uses.</summary>
    public int PgPort { get; private set; }

    /// <summary>The instance name, when <see cref="PgServerOptions.InstanceName"/> is set.</summary>
    public string? InstanceName { get; }

    /// <summary>
    /// True when <see cref="StartAsync"/> attached to a server that was already running instead of starting
    /// one. Such a server is not stopped on dispose, only by <see cref="StopAsync"/> or <see cref="KillAsync"/>.
    /// </summary>
    public bool IsAttached => _isRunning && !_ownsServer;

    public string PgDbName => DefaultDatabase;

    public string? Locale { get; }

    /// <summary>The <c>pg_embed</c> working folder.</summary>
    public string DbDir { get; }

    /// <summary>Cache of downloaded archives and extracted installs, shared by all instances under <see cref="DbDir"/>.</summary>
    public string BinariesDir { get; }

    /// <summary>
    /// Extracted Postgres install (binaries plus configured extensions) used by this server. It is shared,
    /// read only, by every instance with the same version, platform and extensions. Final once
    /// <see cref="PgVersion"/> is resolved.
    /// </summary>
    public string InstallDir => Path.Combine(BinariesDir, InstallFolderName(PgVersion));

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

            // Check before downloading anything that Postgres can use the instance folder (non-ASCII
            // paths on Windows, issue #29); EnsureInstalledAsync does the same for the binaries folder.
            Directory.CreateDirectory(InstanceDir);
            _processInstanceDir = PathUtils.ToProcessSafePath(InstanceDir, nameof(PgServerOptions.DbDir));

            // one process at a time sets up, starts or stops an instance
            using (await AcquireInstanceLockAsync(cancellationToken).ConfigureAwait(false))
            {
                CheckDataMajorVersion();

                var running = PgInstanceStatus.Read(DataDir);
                if (running.State != PgInstanceState.Stopped && _options.AttachIfRunning && running.Port is { } port)
                {
                    PgPort = port;
                    _isRunning = true;
                    _ownsServer = false;
                    _logger.LogInformation("Attached to the Postgres server already running on port {Port} ({InstanceDir})", PgPort, InstanceDir);
                    return;
                }

                if (running.State != PgInstanceState.Stopped)
                {
                    throw new PostgresEmbedException(
                        $"A Postgres server (process {running.ProcessId}) is already running from '{DataDir}'. " +
                        "Stop it first, or set AttachIfRunning to use it.");
                }

                await EnsureInstalledAsync(cancellationToken).ConfigureAwait(false);
                CheckDataMajorVersion(); // again, now that a spec such as "latest" is resolved

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
            }

            _isRunning = true;
            _ownsServer = true;
            if (!_options.KeepRunningAfterExit)
            {
                AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            }

            _logger.LogInformation("Postgres {Version} is running on port {Port} ({InstanceDir})", PgVersion, PgPort, InstanceDir);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>
    /// Stops the instance's server, whichever process started it, and then, depending on
    /// <see cref="PgServerOptions.ClearInstanceDirOnStop"/>, deletes the instance folder. Calling it when no
    /// server runs only performs the cleanup.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken = default) =>
        StopCoreAsync(stopServer: true, cancellationToken);

    /// <summary>
    /// Stops the server only if this object started it: a server it attached to, or one it merely queried,
    /// keeps running.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        await StopCoreAsync(stopServer: _ownsServer, CancellationToken.None).ConfigureAwait(false);
        _disposed = true;
        _lifecycleLock.Dispose();
    }

    /// <summary>
    /// Force-kills the instance's server and its child processes, whichever process started it, without a
    /// clean shutdown. A last resort: Postgres recovers on the next start, but open transactions are lost.
    /// The instance folder is kept.
    /// </summary>
    public async Task KillAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(InstanceDir)) return;

            using (await AcquireInstanceLockAsync(cancellationToken).ConfigureAwait(false))
            {
                AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
                KillPostmaster();
                _isRunning = false;
                _ownsServer = false;

                // Once the server is dead, remove its postmaster.pid: where the killed process lingers as an
                // unreaped zombie (containers without an init process), Postgres would otherwise refuse to
                // start again, believing the server still runs.
                var pidFile = Path.Combine(DataDir, "postmaster.pid");
                if (File.Exists(pidFile) && PgInstanceStatus.Read(DataDir).State == PgInstanceState.Stopped)
                {
                    try
                    {
                        File.Delete(pidFile);
                    }
                    catch (IOException ex)
                    {
                        _logger.LogDebug(ex, "Could not remove {PidFile}", pidFile);
                    }
                }
            }

            _logger.LogWarning("Postgres for {InstanceDir} was killed", InstanceDir);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>
    /// Whether a server is running from this instance's data folder, whichever process started it, with its
    /// process id, port and start time. Reads Postgres's own <c>postmaster.pid</c>; a file left over from a
    /// server that died counts as stopped.
    /// </summary>
    public Task<PgInstanceStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(PgInstanceStatus.Read(DataDir));
    }

    private async Task StopCoreAsync(bool stopServer, CancellationToken cancellationToken)
    {
        if (_disposed) return;

        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (stopServer && Directory.Exists(InstanceDir))
            {
                _processInstanceDir = PathUtils.ToProcessSafePath(InstanceDir, nameof(PgServerOptions.DbDir));

                using (await AcquireInstanceLockAsync(cancellationToken).ConfigureAwait(false))
                {
                    AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;

                    if (PgInstanceStatus.Read(DataDir).State != PgInstanceState.Stopped)
                    {
                        // a server this object did not start needs the binaries located first
                        if (!_ownsServer) await LocateBinariesForStopAsync(cancellationToken).ConfigureAwait(false);

                        await StopServerAsync(cancellationToken).ConfigureAwait(false);
                        _logger.LogInformation("Postgres on port {Port} stopped", PgPort);
                    }
                }
            }

            _isRunning = false;
            _ownsServer = false;

            // never delete the folder of a server that still runs (one this object only attached to)
            if (_clearInstanceDirOnStop && PgInstanceStatus.Read(DataDir).State == PgInstanceState.Stopped)
            {
                DeleteDirectory(InstanceDir);
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private Task<FileLock> AcquireInstanceLockAsync(CancellationToken cancellationToken) =>
        FileLock.AcquireAsync(Path.Combine(InstanceDir, ".lock"), LockTimeout, $"instance {InstanceName ?? InstanceDir}",
            _logger, cancellationToken);

    // Postgres cannot open data created by another major version. Checked before downloading anything.
    private void CheckDataMajorVersion()
    {
        var versionFile = Path.Combine(DataDir, "PG_VERSION");
        if (!File.Exists(versionFile)) return;
        if (!PgVersionNumber.TryParse(PgVersion, out var wanted)) return; // "latest", until resolved

        var dataMajor = File.ReadAllText(versionFile).Trim();
        var wantedMajor = wanted.Parts.Length >= (wanted.Parts[0] >= 10 ? 1 : 2) ? wanted.Major : null;
        if (wantedMajor is not null && wantedMajor != dataMajor)
        {
            throw new PostgresEmbedException(
                $"The instance in '{InstanceDir}' holds data created by Postgres {dataMajor}, which Postgres " +
                $"{wantedMajor} cannot open. Use version {dataMajor} for it, or remove the instance.");
        }
    }

    // To stop a server started by another process, pg_ctl from an install of the data's major version is
    // needed. Prefer one already in the cache, so stopping never waits on a download.
    private async Task LocateBinariesForStopAsync(CancellationToken cancellationToken)
    {
        var dataMajor = File.Exists(Path.Combine(DataDir, "PG_VERSION"))
            ? File.ReadAllText(Path.Combine(DataDir, "PG_VERSION")).Trim()
            : null;
        var artifact = PlatformInfo.ArtifactSuffix(_platform, _architecture);
        var cached = dataMajor is null ? null : PgVersions.Match(dataMajor, CachedVersions(artifact)
            .Where(v => File.Exists(Path.Combine(BinariesDir, $"postgres-{v}-{artifact}", "bin", ExecutableName("pg_ctl"))))
            .ToList());

        if (cached is not null)
        {
            _processInstallDir = Path.Combine(PathUtils.ToProcessSafePath(BinariesDir, nameof(PgServerOptions.BinariesDir)),
                $"postgres-{cached}-{artifact}");
            return;
        }

        await EnsureInstalledAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves <see cref="PgVersion"/>, then downloads and extracts the binaries and configured extensions
    /// into <see cref="InstallDir"/> when they are not there yet, without creating an instance or starting a
    /// server. Use it to warm the cache ahead of tests, for example as a separate CI step.
    /// </summary>
    public async Task InstallAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInstalledAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>
    /// The extensions available to <c>CREATE EXTENSION</c> in this server's install: the ones bundled with
    /// Postgres plus any configured <see cref="PgServerOptions.Extensions"/>. Read from the install folder,
    /// installing first when needed; the server does not have to be running.
    /// </summary>
    public async Task<IReadOnlyList<PgAvailableExtension>> GetAvailableExtensionsAsync(CancellationToken cancellationToken = default)
    {
        await InstallAsync(cancellationToken).ConfigureAwait(false);
        return PgAvailableExtension.ReadFrom(InstallDir);
    }

    private async Task EnsureInstalledAsync(CancellationToken cancellationToken)
    {
        // Check before downloading anything that Postgres can use the binaries folder (issue #29). The
        // install folder's own name is always ASCII, so checking its parent is enough.
        Directory.CreateDirectory(BinariesDir);
        var processBinariesDir = PathUtils.ToProcessSafePath(BinariesDir,
            string.IsNullOrWhiteSpace(_options.BinariesDir) ? nameof(PgServerOptions.DbDir) : nameof(PgServerOptions.BinariesDir));

        await ResolveVersionAsync(cancellationToken).ConfigureAwait(false);
        _processInstallDir = Path.Combine(processBinariesDir, Path.GetFileName(InstallDir));

        if (File.Exists(Path.Combine(PgBinDir, ExecutableName("pg_ctl")))) return;

        // One process downloads and extracts an install while others wait, then find it in place.
        using (await FileLock.AcquireAsync($"{InstallDir}.lock", LockTimeout, $"the download of Postgres {PgVersion}",
                   _logger, cancellationToken).ConfigureAwait(false))
        {
            if (!File.Exists(Path.Combine(PgBinDir, ExecutableName("pg_ctl"))))
            {
                await InstallBinariesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // Resolves a spec such as "17" or "latest" against the versions published for this platform, or,
    // when they cannot be fetched, against the versions already in BinariesDir.
    private async Task ResolveVersionAsync(CancellationToken cancellationToken)
    {
        if (_resolvedVersion is not null) return;

        var artifact = PlatformInfo.ArtifactSuffix(_platform, _architecture);
        IReadOnlyList<string> available;
        try
        {
            available = await PgVersions
                .GetAvailableAsync(_platform, _architecture, _options.MavenRepo, _options.HttpClient, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException or System.Xml.XmlException)
                                   && !cancellationToken.IsCancellationRequested)
        {
            var fallback = PgVersions.Match(_requestedVersion, CachedVersions(artifact));
            if (fallback is null)
            {
                throw new PostgresEmbedException(
                    $"Could not look up the Postgres versions published for {artifact} to resolve '{_requestedVersion}', " +
                    $"and no matching version is cached in '{BinariesDir}'. Use an exact version such as \"17.6.0\" " +
                    "or check network access.", ex);
            }

            _logger.LogWarning(ex, "Could not look up published Postgres versions; using {Version} from {BinariesDir}",
                fallback, BinariesDir);
            _resolvedVersion = fallback;
            return;
        }

        _resolvedVersion = PgVersions.Match(_requestedVersion, available)
            ?? throw new PostgresEmbedException(
                $"No Postgres version matching '{_requestedVersion}' is published for {artifact}. " +
                $"Available majors: {PgVersions.DescribeMajors(available)}.");

        _logger.LogInformation("Resolved Postgres version '{Spec}' to {Version}", _requestedVersion, _resolvedVersion);
    }

    // Versions present in BinariesDir for this platform: extracted installs, cached archives and jars
    private IEnumerable<string> CachedVersions(string artifact)
    {
        if (!Directory.Exists(BinariesDir)) yield break;

        foreach (var path in Directory.EnumerateFileSystemEntries(BinariesDir))
        {
            var name = Path.GetFileName(path);
            string? version = null;

            // postgres-<version>-<artifact>[-ext-<hash>], an extracted install
            if (name.StartsWith("postgres-", StringComparison.Ordinal) && Directory.Exists(path))
            {
                var marker = name.IndexOf("-" + artifact, StringComparison.Ordinal);
                var rest = marker > 0 ? name[(marker + artifact.Length + 1)..] : null;
                if (rest is "" || rest?.StartsWith("-ext-", StringComparison.Ordinal) == true)
                {
                    version = name["postgres-".Length..marker];
                }
            }
            // embedded-postgres-binaries-<artifact>-<version>.txz or .jar, a cached download
            else if (name.StartsWith($"embedded-postgres-binaries-{artifact}-", StringComparison.Ordinal)
                     && (name.EndsWith(".txz", StringComparison.Ordinal) || name.EndsWith(".jar", StringComparison.Ordinal)))
            {
                version = Path.GetFileNameWithoutExtension(name)[$"embedded-postgres-binaries-{artifact}-".Length..];
            }

            if (version is not null && PgVersionNumber.TryParse(version, out _)) yield return version;
        }
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
    private string InstallFolderName(string version)
    {
        var name = $"postgres-{version}-{PlatformInfo.ArtifactSuffix(_platform, _architecture)}";
        if (_options.Extensions.Count == 0) return name;

        var urls = string.Join('\n', _options.Extensions.Select(e => e.DownloadUrl));
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

            // read before the kill: the server's System V segment, which a kill would leave behind
            var segment = SharedMemory.Read(pidFile);

            using var postmaster = Process.GetProcessById(pid);
            postmaster.Kill(entireProcessTree: true);

            // poll rather than WaitForExit: a killed server that lingers as an unreaped zombie never
            // "exits" for a process that is not its parent
            for (var waited = 0; waited < 5000 && PgInstanceStatus.IsPostgresProcess(pid); waited += 50)
            {
                Thread.Sleep(50);
            }

            // only once the server is gone; a clean shutdown removes the segment itself
            if (segment is { } held && !PgInstanceStatus.IsPostgresProcess(pid)) SharedMemory.Remove(held, _logger);
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
