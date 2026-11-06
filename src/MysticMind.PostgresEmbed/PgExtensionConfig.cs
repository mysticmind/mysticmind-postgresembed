namespace MysticMind.PostgresEmbed;

/// <summary>
/// An extension archive (zip) to download and extract into the Postgres instance. After the server
/// starts, run <c>CREATE EXTENSION ...</c> yourself to install it.
/// </summary>
public sealed record PgExtensionConfig
{
    public PgExtensionConfig(string downloadUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadUrl);
        DownloadUrl = downloadUrl;
    }

    public string DownloadUrl { get; }
}
