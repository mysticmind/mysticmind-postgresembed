using System.Net;

namespace MysticMind.PostgresEmbed.Tests;

/// <summary>Answers requests from a table of URL suffixes; anything else goes to the real network.</summary>
internal sealed class FakeHttpHandler : DelegatingHandler
{
    private readonly List<(string Suffix, Func<HttpResponseMessage> Respond)> _routes = [];

    public FakeHttpHandler() : base(new HttpClientHandler())
    {
    }

    public List<string> Requests { get; } = [];

    public FakeHttpHandler On(string urlSuffix, HttpStatusCode status, string body) =>
        On(urlSuffix, () => new HttpResponseMessage(status) { Content = new StringContent(body) });

    public FakeHttpHandler On(string urlSuffix, Func<HttpResponseMessage> respond)
    {
        _routes.Add((urlSuffix, respond));
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        Requests.Add(url);
        foreach (var (suffix, respond) in _routes)
        {
            if (url.EndsWith(suffix, StringComparison.Ordinal)) return Task.FromResult(respond());
        }

        return base.SendAsync(request, cancellationToken);
    }
}

public class PgVersionsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] Available =
        ["9.6.20-1", "9.6.24", "10.23.0", "13.1.0", "13.1.0-1", "17.5.0", "17.6.0", "17.11.0", "18.6.0"];

    [Theory]
    [InlineData("17", "17.11.0")]          // newest of the major, numerically (11 > 6)
    [InlineData("17.6", "17.6.0")]
    [InlineData("latest", "18.6.0")]
    [InlineData("LATEST", "18.6.0")]
    [InlineData("13.1", "13.1.0-1")]       // a repackaged build sorts after the plain one
    [InlineData("9.6", "9.6.24")]          // two-part major before Postgres 10
    [InlineData("9", "9.6.24")]
    [InlineData("17.6.0", "17.6.0")]
    public void match_picks_the_newest_matching_version(string spec, string expected) =>
        Assert.Equal(expected, PgVersions.Match(spec, Available));

    [Theory]
    [InlineData("19")]
    [InlineData("17.7")]
    [InlineData("not-a-version")]
    public void match_returns_null_when_nothing_matches(string spec) =>
        Assert.Null(PgVersions.Match(spec, Available));

    [Theory]
    [InlineData("17.6.0", true)]
    [InlineData("13.1.0-1", true)]
    [InlineData("17.6", false)]
    [InlineData("17", false)]
    [InlineData("latest", false)]
    public void is_exact_only_for_full_versions(string spec, bool expected) =>
        Assert.Equal(expected, PgVersions.IsExact(spec));

    [Fact]
    public void majors_are_described_in_version_order() =>
        Assert.Equal("9.6, 10, 13, 17, 18", PgVersions.DescribeMajors(Available));

    [Fact]
    public async Task available_versions_come_from_the_platform_metadata_oldest_first()
    {
        const string metadata = """
            <metadata><versioning><versions>
              <version>17.6.0</version><version>13.1.0-1</version><version>13.1.0</version><version>9.6.24</version>
              <version>garbage</version>
            </versions></versioning></metadata>
            """;
        var handler = new FakeHttpHandler().On("/embedded-postgres-binaries-linux-amd64/maven-metadata.xml", HttpStatusCode.OK, metadata);

        var versions = await PgVersions.GetAvailableAsync(Platform.Linux, Architecture.Amd64, "https://repo.example/maven2",
            new HttpClient(handler), Ct);

        Assert.Equal(["9.6.24", "13.1.0", "13.1.0-1", "17.6.0"], versions);
        Assert.Equal(
            "https://repo.example/maven2/io/zonky/test/postgres/embedded-postgres-binaries-linux-amd64/maven-metadata.xml",
            Assert.Single(handler.Requests));
    }

    [Fact]
    public async Task an_unpublished_platform_is_reported_clearly()
    {
        var handler = new FakeHttpHandler().On("maven-metadata.xml", HttpStatusCode.NotFound, "");

        var ex = await Assert.ThrowsAsync<PostgresEmbedException>(() => PgVersions.GetAvailableAsync(
            Platform.AlpineLinux, Architecture.Arm64V8, "https://repo.example/maven2", new HttpClient(handler), Ct));

        Assert.Contains("linux-arm64v8-alpine", ex.Message);
    }

    [Fact]
    public async Task the_real_repository_lists_versions_for_this_platform()
    {
        var versions = await PgVersions.GetAvailableAsync(cancellationToken: Ct);

        Assert.Contains("18.6.0", versions);
        Assert.Equal(versions.OrderBy(PgVersionNumber.Parse), versions);
    }

    [Fact]
    public void control_files_are_parsed_like_postgres_does()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var file = Path.Combine(dir, "demo.control");
        File.WriteAllText(file, """
            # demo extension
            comment = 'it''s a demo # not a comment'
            default_version = '1.2'
            relocatable = true   # trailing comment
            requires = 'cube, plpgsql'
            """);

        var ext = PgAvailableExtension.Parse(file);

        Assert.Equal("demo", ext.Name);
        Assert.Equal("1.2", ext.DefaultVersion);
        Assert.Equal("it's a demo # not a comment", ext.Comment);
        Assert.Equal(["cube", "plpgsql"], ext.Requires);
    }
}
