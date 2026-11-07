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
        CancellationToken cancellationToken,
        IProgress<PgInstallProgress>? progress = null,
        string? item = null)
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
                if (progress is null)
                {
                    await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await CopyWithProgressAsync(source, target, response.Content.Headers.ContentLength, progress,
                        item ?? Path.GetFileName(destinationPath), cancellationToken).ConfigureAwait(false);
                }
            }

            File.Move(tempPath, destinationPath, overwrite: true);
            logger.LogInformation("Downloaded {Url} ({Bytes:N0} bytes)", url, new FileInfo(destinationPath).Length);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    // reports at most ten times a second, plus the start and the end
    private static async Task CopyWithProgressAsync(
        Stream source, Stream target, long? total, IProgress<PgInstallProgress> progress, string item, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long done = 0;
        var lastReport = System.Diagnostics.Stopwatch.StartNew();
        progress.Report(new PgInstallProgress(PgInstallStep.Download, item, 0, total));

        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            done += read;
            if (lastReport.ElapsedMilliseconds >= 100)
            {
                progress.Report(new PgInstallProgress(PgInstallStep.Download, item, done, total));
                lastReport.Restart();
            }
        }

        progress.Report(new PgInstallProgress(PgInstallStep.Download, item, done, total ?? done));
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
