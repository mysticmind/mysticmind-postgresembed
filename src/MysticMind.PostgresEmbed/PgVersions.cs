using System.Globalization;
using System.Xml.Linq;

namespace MysticMind.PostgresEmbed;

/// <summary>
/// Discovers the Postgres versions published as zonky binaries, and resolves version specs such as
/// <c>"17"</c> or <c>"latest"</c> to an exact version.
/// </summary>
public static class PgVersions
{
    /// <summary>Version spec for the newest version published for the platform.</summary>
    public const string Latest = "latest";

    /// <summary>
    /// The versions published for a platform, oldest first, for example <c>17.6.0</c> or <c>13.1.0-1</c>
    /// (a repackaged build of 13.1.0). Reads one small metadata file from the Maven repository.
    /// </summary>
    public static async Task<IReadOnlyList<string>> GetAvailableAsync(
        Platform? platform = null,
        Architecture? architecture = null,
        string mavenRepo = PgServerOptions.DefaultMavenRepo,
        HttpClient? httpClient = null,
        CancellationToken cancellationToken = default)
    {
        var p = platform ?? PlatformInfo.DetectPlatform();
        var a = architecture ?? PlatformInfo.DetectArchitecture(p);
        var artifact = $"embedded-postgres-binaries-{PlatformInfo.ArtifactSuffix(p, a)}";
        var url = $"{mavenRepo.TrimEnd('/')}/io/zonky/test/postgres/{artifact}/maven-metadata.xml";

        using var response = await (httpClient ?? FileDownloader.SharedClient)
            .GetAsync(url, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new PostgresEmbedException($"No Postgres binaries are published for {artifact} ({url} returned 404).");
        }

        response.EnsureSuccessStatusCode();
        var xml = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return XDocument.Parse(xml)
            .Descendants("version")
            .Select(e => e.Value.Trim())
            .Where(v => PgVersionNumber.TryParse(v, out _))
            .Distinct()
            .OrderBy(v => PgVersionNumber.Parse(v))
            .ToList();
    }

    /// <summary>
    /// True for a spec naming one exact version, such as <c>17.6.0</c> or <c>13.1.0-1</c>, which needs no lookup.
    /// </summary>
    public static bool IsExact(string spec) =>
        PgVersionNumber.TryParse(spec, out var v) && v.Parts.Length >= 3;

    /// <summary>
    /// The newest version in <paramref name="available"/> matching <paramref name="spec"/>: <c>"latest"</c>,
    /// a major (<c>"17"</c>, or <c>"9.6"</c> before Postgres 10), a minor (<c>"17.6"</c>) or an exact
    /// version. Returns null when nothing matches.
    /// </summary>
    public static string? Match(string spec, IEnumerable<string> available)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spec);
        var candidates = available.Where(v => PgVersionNumber.TryParse(v, out _));

        if (!string.Equals(spec.Trim(), Latest, StringComparison.OrdinalIgnoreCase))
        {
            if (!PgVersionNumber.TryParse(spec.Trim(), out var wanted)) return null;
            candidates = candidates.Where(v => PgVersionNumber.Parse(v).StartsWith(wanted));
        }

        return candidates.OrderBy(v => PgVersionNumber.Parse(v)).LastOrDefault();
    }

    /// <summary>The distinct majors in <paramref name="available"/>, for error messages: 9.6, 10, 11, ...</summary>
    internal static string DescribeMajors(IEnumerable<string> available) =>
        string.Join(", ", available
            .Select(PgVersionNumber.Parse)
            .Select(v => v.Major)
            .Distinct()
            .OrderBy(m => PgVersionNumber.Parse(m)));
}

/// <summary>
/// A zonky version such as <c>9.6.24</c>, <c>17.6.0</c> or <c>13.1.0-1</c>, where <c>-1</c> marks a
/// repackaged build that sorts after the plain version.
/// </summary>
internal sealed record PgVersionNumber(int[] Parts, int Revision) : IComparable<PgVersionNumber>
{
    public static bool TryParse(string? value, out PgVersionNumber version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var revision = 0;
        var main = value;
        var dash = value.IndexOf('-');
        if (dash >= 0)
        {
            if (!int.TryParse(value[(dash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out revision)) return false;
            main = value[..dash];
        }

        var pieces = main.Split('.');
        var parts = new int[pieces.Length];
        for (var i = 0; i < pieces.Length; i++)
        {
            if (!int.TryParse(pieces[i], NumberStyles.None, CultureInfo.InvariantCulture, out parts[i])) return false;
        }

        version = new PgVersionNumber(parts, revision);
        return true;
    }

    public static PgVersionNumber Parse(string value) =>
        TryParse(value, out var v) ? v : throw new FormatException($"'{value}' is not a Postgres version.");

    /// <summary>The major: the first number from Postgres 10 on, the first two before (9.6).</summary>
    public string Major => Parts[0] >= 10 || Parts.Length < 2
        ? Parts[0].ToString(CultureInfo.InvariantCulture)
        : $"{Parts[0]}.{Parts[1]}";

    /// <summary>True when every part of <paramref name="prefix"/> (and its revision, if any) matches.</summary>
    public bool StartsWith(PgVersionNumber prefix) =>
        prefix.Parts.Length <= Parts.Length
        && prefix.Parts.Select((p, i) => p == Parts[i]).All(x => x)
        && (prefix.Revision == 0 || prefix.Revision == Revision);

    public int CompareTo(PgVersionNumber? other)
    {
        if (other is null) return 1;
        for (var i = 0; i < Math.Max(Parts.Length, other.Parts.Length); i++)
        {
            var c = (i < Parts.Length ? Parts[i] : 0).CompareTo(i < other.Parts.Length ? other.Parts[i] : 0);
            if (c != 0) return c;
        }

        return Revision.CompareTo(other.Revision);
    }
}
