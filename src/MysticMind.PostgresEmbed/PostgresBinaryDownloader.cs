using System.IO.Compression;
using Microsoft.Extensions.Logging;

namespace MysticMind.PostgresEmbed;

/// <summary>
/// Downloads the Postgres binaries published by https://github.com/zonkyio/embedded-postgres-binaries.
/// Each Maven artifact is a jar holding a single <c>.txz</c> archive; the archive is cached in the
/// binaries folder and reused across instances.
/// </summary>
internal sealed class PostgresBinaryDownloader(
    string pgVersion,
    string binariesDir,
    Platform platform,
    Architecture architecture,
    string mavenRepo,
    HttpClient? httpClient,
    ILogger logger)
{
    public async Task<string> DownloadAsync(CancellationToken cancellationToken)
    {
        var artifact = $"embedded-postgres-binaries-{PlatformInfo.ArtifactSuffix(platform, architecture)}";
        var archivePath = Path.Combine(binariesDir, $"{artifact}-{pgVersion}.txz");

        if (File.Exists(archivePath)) return archivePath;

        var jarPath = Path.Combine(binariesDir, $"{artifact}-{pgVersion}.jar");

        if (!File.Exists(jarPath))
        {
            var url = $"{mavenRepo.TrimEnd('/')}/io/zonky/test/postgres/{artifact}/{pgVersion}/{artifact}-{pgVersion}.jar";
            await FileDownloader.DownloadAsync(httpClient, url, jarPath, logger, cancellationToken).ConfigureAwait(false);
            await VerifyPublishedChecksumAsync(url, jarPath, cancellationToken).ConfigureAwait(false);
        }

        ExtractArchiveFromJar(jarPath, archivePath);
        return archivePath;
    }

    // Maven publishes a .sha256 next to every artifact. A mirror that does not serve it only gets a
    // warning; a checksum that does not match always fails the download.
    private async Task VerifyPublishedChecksumAsync(string url, string jarPath, CancellationToken cancellationToken)
    {
        using var response = await (httpClient ?? FileDownloader.SharedClient)
            .GetAsync(url + ".sha256", cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            logger.LogWarning("No published SHA-256 at {Url}.sha256; {Jar} was not verified", url, Path.GetFileName(jarPath));
            return;
        }

        response.EnsureSuccessStatusCode();
        var expected = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        FileDownloader.VerifySha256(jarPath, expected, Path.GetFileName(jarPath));
        logger.LogDebug("Verified SHA-256 of {Jar}", Path.GetFileName(jarPath));
    }

    private static void ExtractArchiveFromJar(string jarPath, string archivePath)
    {
        using var jar = ZipFile.OpenRead(jarPath);

        var entry = jar.Entries.FirstOrDefault(e =>
            !string.IsNullOrEmpty(e.Name) &&
            e.FullName.EndsWith(".txz", StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            throw new PostgresEmbedException($"No .txz archive found inside '{jarPath}'.");
        }

        var tempPath = $"{archivePath}.{Guid.NewGuid():N}.tmp";
        entry.ExtractToFile(tempPath);
        File.Move(tempPath, archivePath, overwrite: true);
    }
}
