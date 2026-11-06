using Microsoft.Extensions.Logging;

namespace MysticMind.PostgresEmbed;

/// <summary>
/// An exclusive lock across processes, held by keeping a lock file open without sharing (an advisory
/// <c>flock</c> on Linux and macOS, a sharing lock on Windows). Lock files are left in place on release:
/// deleting them would let two processes each lock a different file of the same name.
/// </summary>
internal sealed class FileLock : IDisposable
{
    private readonly FileStream _stream;

    private FileLock(FileStream stream) => _stream = stream;

    public static async Task<FileLock> AcquireAsync(
        string path, TimeSpan timeout, string description, ILogger logger, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var deadline = DateTime.UtcNow + timeout;
        var waiting = false;

        while (true)
        {
            try
            {
                return new FileLock(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                if (!waiting)
                {
                    logger.LogInformation("Waiting for another process using {Description}", description);
                    waiting = true;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                throw new PostgresEmbedException(
                    $"Timed out after {timeout.TotalSeconds:N0}s waiting for another process using {description} ({path}).", ex);
            }
        }
    }

    public void Dispose() => _stream.Dispose();
}
