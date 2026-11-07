using Microsoft.Extensions.Logging;

namespace MysticMind.PostgresEmbed;

/// <summary>
/// Configuration for an embedded <see cref="PgServer"/>.
/// </summary>
public sealed record PgServerOptions
{
    /// <summary>The default Maven repository used to download the zonky Postgres binaries.</summary>
    public const string DefaultMavenRepo = "https://repo1.maven.org/maven2";

    /// <summary>
    /// Postgres version to run: an exact version (<c>"17.6.0"</c>), or a spec resolved to the newest
    /// matching version published for this platform when the server starts or installs: a major
    /// (<c>"17"</c>, or <c>"9.6"</c> before Postgres 10), a minor (<c>"17.6"</c>) or <c>"latest"</c>.
    /// Without network access, a spec falls back to the newest matching version already in
    /// <see cref="BinariesDir"/>. See <see cref="PgVersions"/> to list the published versions.
    /// </summary>
    public required string PgVersion { get; init; }

    /// <summary>Superuser created by initdb. Defaults to <c>postgres</c>.</summary>
    public string PgUser { get; init; } = "postgres";

    /// <summary>
    /// Root directory under which a <c>pg_embed</c> folder is created. Defaults to the current directory.
    /// Relative paths are resolved against the current directory. A named instance (<see cref="InstanceName"/>)
    /// lives in the shared cache (<see cref="PgCache.DefaultDirectory"/>) unless this is set.
    /// </summary>
    public string? DbDir { get; init; }

    /// <summary>
    /// Folder caching downloaded archives and extracted installs. Point several servers, test projects or
    /// <see cref="DbDir"/>s at one folder to download and extract each Postgres version only once.
    /// Defaults to <c>binaries</c> in the shared cache when <c>PGEMBED_CACHE_DIR</c> is set or for a named
    /// instance kept in the shared cache, and to <c>pg_embed/binaries</c> under <see cref="DbDir"/> otherwise.
    /// </summary>
    public string? BinariesDir { get; init; }

    /// <summary>
    /// Instance id used as the instance folder name. Pass a fixed value to reuse an instance (data
    /// included) across runs. When <c>null</c>, a new instance is created for each server. Prefer
    /// <see cref="InstanceName"/>; set one or the other, not both.
    /// </summary>
    public Guid? InstanceId { get; init; }

    /// <summary>
    /// A readable, stable name for an instance kept across runs, for example <c>"app"</c>: letters, digits,
    /// <c>.</c>, <c>_</c> and <c>-</c>, starting with a letter or digit, up to 63 characters. It lives in
    /// <c>instances/&lt;name&gt;</c> in the shared cache (<see cref="PgCache.DefaultDirectory"/>), unless
    /// <see cref="DbDir"/> is set, so tests, apps and the pgembed CLI on one machine meet at the same
    /// instance. Its data is kept when the server stops.
    /// </summary>
    public string? InstanceName { get; init; }

    /// <summary>
    /// When the instance's server is already running, for example started by the pgembed CLI or another
    /// test run, attach to it instead of starting another one. On by default. A server is only stopped on
    /// dispose by the process that started it; <see cref="PgServer.StopAsync"/> stops it either way.
    /// </summary>
    public bool AttachIfRunning { get; init; } = true;

    /// <summary>TCP port to listen on. <c>0</c> (the default) picks a free port.</summary>
    public int Port { get; init; }

    /// <summary>Additional Postgres configuration parameters, for example <c>{ ["max_connections"] = "300" }</c>.</summary>
    public IDictionary<string, string> ServerParams { get; init; } = new Dictionary<string, string>();

    /// <summary>Extension archives to download and extract into the instance.</summary>
    public IList<PgExtensionConfig> Extensions { get; init; } = new List<PgExtensionConfig>();

    /// <summary>
    /// Windows only: grant the current user full control on the instance folder before running initdb.
    /// Needed in some CI environments. Ignored on other platforms.
    /// </summary>
    public bool AddLocalUserAccessPermission { get; init; }

    /// <summary>
    /// Delete the instance folder when the server stops. When <c>null</c> (the default), the folder is
    /// deleted for ephemeral instances (no <see cref="InstanceId"/> or <see cref="InstanceName"/>) and kept
    /// for the others. A folder is never deleted while a server still runs from it.
    /// </summary>
    public bool? ClearInstanceDirOnStop { get; init; }

    /// <summary>
    /// Delete the whole <c>pg_embed</c> folder before starting, including cached binaries unless
    /// <see cref="BinariesDir"/> points elsewhere.
    /// </summary>
    public bool ClearWorkingDirOnStart { get; init; }

    /// <summary>
    /// Keep the server running when this process exits without stopping it. By default the server is
    /// killed on process exit, so test runs never leave Postgres behind; set this for a server meant to
    /// outlive its process, such as a local development database. <see cref="PgServer.StopAsync"/> and
    /// disposing still stop it.
    /// </summary>
    public bool KeepRunningAfterExit { get; init; }

    /// <summary>Locale passed to initdb. Defaults to <c>en_US.UTF-8</c> on Linux and macOS, and the system locale on Windows.</summary>
    public string? Locale { get; init; }

    /// <summary>Override the detected operating system.</summary>
    public Platform? Platform { get; init; }

    /// <summary>Override the detected CPU architecture.</summary>
    public Architecture? Architecture { get; init; }

    /// <summary>How long to wait for the server to accept connections. Defaults to 30 seconds.</summary>
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How long to wait for a fast shutdown before forcing an immediate one. Defaults to 10 seconds.</summary>
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Maven repository (or mirror) hosting the zonky binaries.</summary>
    public string MavenRepo { get; init; } = DefaultMavenRepo;

    /// <summary>Number of retries for each download. Defaults to 3.</summary>
    public int DownloadRetryCount { get; init; } = 3;

    /// <summary>Number of retries when deleting folders that are briefly locked. Defaults to 5.</summary>
    public int DeleteFolderRetryCount { get; init; } = 5;

    /// <summary>First delay between folder delete retries; it doubles on each attempt. Defaults to 16 ms.</summary>
    public TimeSpan DeleteFolderInitialDelay { get; init; } = TimeSpan.FromMilliseconds(16);

    /// <summary>
    /// <see cref="HttpClient"/> used for downloads, for example to configure a proxy. A shared client is used when <c>null</c>.
    /// </summary>
    public HttpClient? HttpClient { get; init; }

    /// <summary>Logger for setup progress and server lifecycle messages.</summary>
    public ILogger? Logger { get; init; }

    /// <summary>
    /// Receives install progress: download bytes as they arrive, checksum verification and extraction. Reported
    /// only when a download or install actually happens, so a test runner or CLI can show why a first run is slow.
    /// </summary>
    public IProgress<PgInstallProgress>? InstallProgress { get; init; }

    /// <summary>
    /// Fail a download whose repository publishes no SHA-256 for it, instead of installing it unverified with a
    /// warning. Maven Central publishes one for every archive; a mirror might not.
    /// </summary>
    public bool RequireChecksum { get; init; }
}
