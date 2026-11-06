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
        }

        ExtractArchiveFromJar(jarPath, archivePath);
        return archivePath;
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
