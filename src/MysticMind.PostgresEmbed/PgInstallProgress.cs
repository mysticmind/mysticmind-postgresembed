namespace MysticMind.PostgresEmbed;

/// <summary>A step of installing Postgres or an extension into the binaries folder.</summary>
public enum PgInstallStep
{
    /// <summary>Downloading an archive; <see cref="PgInstallProgress.BytesDone"/> grows as it arrives.</summary>
    Download,

    /// <summary>Checking a downloaded archive against its SHA-256.</summary>
    Verify,

    /// <summary>Extracting the binaries and extensions into the install folder.</summary>
    Extract
}

/// <summary>
/// Progress of an install, reported to <see cref="PgServerOptions.InstallProgress"/>. Only work that is done is
/// reported: nothing when the version is already cached.
/// </summary>
/// <param name="Step">What is happening.</param>
/// <param name="Item">What it happens to: the Postgres version and platform, or an extension's file name.</param>
/// <param name="BytesDone">For a download, the bytes received so far.</param>
/// <param name="BytesTotal">For a download, its size when the server sends one.</param>
public sealed record PgInstallProgress(PgInstallStep Step, string Item, long? BytesDone = null, long? BytesTotal = null);
