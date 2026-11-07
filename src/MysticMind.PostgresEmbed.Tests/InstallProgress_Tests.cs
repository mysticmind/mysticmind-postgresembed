using System.Net;
using Microsoft.Extensions.Logging.Abstractions;

namespace MysticMind.PostgresEmbed.Tests;

public class InstallProgressTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Records reports as they happen (Progress&lt;T&gt; would post them to a synchronization context).</summary>
    private sealed class Recorder : IProgress<PgInstallProgress>
    {
        public List<PgInstallProgress> Reports { get; } = [];
        public void Report(PgInstallProgress value) { lock (Reports) Reports.Add(value); }
    }

    [Fact]
    public async Task a_download_reports_its_bytes_from_zero_to_the_total()
    {
        var body = new byte[3 * 1024 * 1024];
        var handler = new FakeHttpHandler().On("/file.bin", () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        var target = Path.Combine(Path.GetTempPath(), $"progress-{Guid.NewGuid():N}.bin");
        var recorder = new Recorder();

        try
        {
            await FileDownloader.DownloadAsync(new HttpClient(handler), "https://example.test/file.bin", target, NullLogger.Instance, Ct, recorder, "file");

            Assert.All(recorder.Reports, r => Assert.Equal(PgInstallStep.Download, r.Step));
            Assert.Equal(0, recorder.Reports[0].BytesDone);
            Assert.Equal(body.Length, recorder.Reports[^1].BytesDone);
            Assert.Equal(body.Length, recorder.Reports[^1].BytesTotal);
            Assert.Equal(recorder.Reports.Select(r => r.BytesDone), recorder.Reports.Select(r => r.BytesDone).Order());
            Assert.Equal(body.Length, new FileInfo(target).Length);
        }
        finally
        {
            File.Delete(target);
        }
    }

    [Fact]
    public async Task an_install_reports_download_verify_and_extract_then_nothing_once_cached()
    {
        var binaries = Path.Combine(Directory.GetCurrentDirectory(), "pg_embed", $"progress-{Guid.NewGuid():N}");
        try
        {
            var first = new Recorder();
            await using (var server = new PgServer(new PgServerOptions { PgVersion = "18.6.0", BinariesDir = binaries, InstallProgress = first }))
            {
                await server.InstallAsync(Ct);
            }

            Assert.Equal([PgInstallStep.Download, PgInstallStep.Verify, PgInstallStep.Extract], first.Reports.Select(r => r.Step).Distinct());
            Assert.Contains("PostgreSQL 18.6.0", first.Reports[0].Item);
            var done = first.Reports.Last(r => r.Step == PgInstallStep.Download);
            Assert.True(done.BytesDone > 1_000_000 && done.BytesDone == done.BytesTotal);

            var again = new Recorder();
            await using (var server = new PgServer(new PgServerOptions { PgVersion = "18.6.0", BinariesDir = binaries, InstallProgress = again }))
            {
                await server.InstallAsync(Ct);
            }

            Assert.Empty(again.Reports);
        }
        finally
        {
            if (Directory.Exists(binaries)) Directory.Delete(binaries, recursive: true);
        }
    }
}

public class ChecksumTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_download_without_a_published_checksum_fails_when_one_is_required()
    {
        var jar = new byte[1024];
        var handler = new FakeHttpHandler()
            .On(".jar.sha256", () => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound))
            .On(".jar", () => new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(jar) });
        var binaries = Path.Combine(Path.GetTempPath(), $"checksum-{Guid.NewGuid():N}");
        try
        {
            await using var server = new PgServer(new PgServerOptions
            {
                PgVersion = "17.6.0", BinariesDir = binaries, MavenRepo = "https://mirror.test/maven2",
                HttpClient = new HttpClient(handler), RequireChecksum = true, DownloadRetryCount = 1
            });

            var ex = await Assert.ThrowsAsync<PostgresEmbedException>(() => server.InstallAsync(Ct));
            Assert.Contains("no published SHA-256", ex.Message);
            Assert.Empty(Directory.EnumerateFiles(binaries, "*.jar"));
        }
        finally
        {
            if (Directory.Exists(binaries)) Directory.Delete(binaries, recursive: true);
        }
    }
}
