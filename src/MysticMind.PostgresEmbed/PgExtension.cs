using Microsoft.Extensions.Logging;

namespace MysticMind.PostgresEmbed;

internal sealed class PgExtension(string binariesDir, PgExtensionConfig config)
{
    private readonly string _archivePath = Path.Combine(binariesDir, Path.GetFileName(new Uri(config.DownloadUrl).AbsolutePath));

    public async Task DownloadAsync(HttpClient? httpClient, ILogger logger, CancellationToken cancellationToken,
        IProgress<PgInstallProgress>? progress = null)
    {
        var item = Path.GetFileName(_archivePath);
        if (!File.Exists(_archivePath))
        {
            await FileDownloader
                .DownloadAsync(httpClient, config.DownloadUrl, _archivePath, logger, cancellationToken, progress, item)
                .ConfigureAwait(false);
        }

        // checked for cached archives too: they are only extracted when a new install is built
        if (!string.IsNullOrWhiteSpace(config.Sha256))
        {
            progress?.Report(new PgInstallProgress(PgInstallStep.Verify, item));
            FileDownloader.VerifySha256(_archivePath, config.Sha256, Path.GetFileName(_archivePath));
        }
    }

    public void Extract(string installDir)
    {
        // some bundles (for example PostGIS) wrap bin, lib and share in a top level folder;
        // the content has to land directly in the Postgres install folder for Postgres to find it
        var wrappingFolder = ArchiveExtractor.FindWrappingFolder(_archivePath);
        ArchiveExtractor.ExtractZip(_archivePath, installDir, wrappingFolder);
    }
}
