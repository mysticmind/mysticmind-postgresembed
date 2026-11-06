using System.Net;
using Microsoft.Extensions.Logging;

namespace MysticMind.PostgresEmbed;

internal static class FileDownloader
{
    internal static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>
    /// Checks <paramref name="path"/> against an expected SHA-256 (hex, case-insensitive; anything after the
    /// first whitespace, such as a file name, is ignored). On a mismatch the file is deleted, so a bad
    /// download is never reused, and a <see cref="PostgresEmbedException"/> is thrown.
    /// </summary>
    public static void VerifySha256(string path, string expected, string description)
    {
        var expectedHex = expected.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        string actualHex;
        using (var stream = File.OpenRead(path))
        {
            actualHex = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
        }

        if (!string.Equals(actualHex, expectedHex, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(path);
            throw new PostgresEmbedException(
                $"Checksum mismatch for {description}: expected SHA-256 {expectedHex.ToLowerInvariant()}, " +
                $"got {actualHex.ToLowerInvariant()}. The download was deleted; it was corrupted or altered in transit.");
        }
    }

    /// <summary>
    /// Downloads <paramref name="url"/> to <paramref name="destinationPath"/>. The file is written to a
    /// temporary name first and then moved into place, so parallel test runs never see a partial file.
    /// </summary>
    public static async Task DownloadAsync(
        HttpClient? client,
        string url,
        string destinationPath,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        client ??= SharedClient;
        var tempPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";

        logger.LogInformation("Downloading {Url}", url);

        try
        {
            using var response = await client
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // not transient, so this is excluded from download retries
                throw new PostgresEmbedException($"Nothing found at {url} (HTTP 404). Check the version and URL.");
            }

            response.EnsureSuccessStatusCode();

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var target = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, destinationPath, overwrite: true);
            logger.LogInformation("Downloaded {Url} ({Bytes:N0} bytes)", url, new FileInfo(destinationPath).Length);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // best effort cleanup of the temporary file
        }
        catch (UnauthorizedAccessException)
        {
            // best effort cleanup of the temporary file
        }
    }
}
