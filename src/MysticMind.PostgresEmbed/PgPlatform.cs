namespace MysticMind.PostgresEmbed;

/// <summary>A platform Postgres binaries are published for.</summary>
/// <param name="Platform">The operating system family.</param>
/// <param name="Architecture">The CPU architecture of the binaries.</param>
/// <param name="ArtifactName">The binaries' artifact name, for example <c>darwin-arm64v8</c> or <c>linux-amd64-alpine</c>.</param>
/// <param name="RuntimeIdentifier">The .NET runtime identifier, for example <c>osx-arm64</c> or <c>linux-musl-x64</c>.</param>
public sealed record PgPlatformInfo(Platform Platform, Architecture Architecture, string ArtifactName, string RuntimeIdentifier);

/// <summary>Detects the current platform and maps between platforms and runtime identifiers.</summary>
public static class PgPlatform
{
    private static readonly (string Rid, Platform Platform, Architecture Architecture)[] Known =
    [
        ("win-x64", Platform.Windows, Architecture.Amd64),
        ("linux-x64", Platform.Linux, Architecture.Amd64),
        ("linux-arm64", Platform.Linux, Architecture.Arm64V8),
        ("linux-musl-x64", Platform.AlpineLinux, Architecture.Amd64),
        ("linux-musl-arm64", Platform.AlpineLinux, Architecture.Arm64V8),
        ("osx-x64", Platform.Darwin, Architecture.Amd64),
        ("osx-arm64", Platform.Darwin, Architecture.Arm64V8),
    ];

    /// <summary>The runtime identifiers that have Postgres binaries, for example <c>linux-arm64</c>.</summary>
    public static IReadOnlyList<string> SupportedRuntimeIdentifiers { get; } = Known.Select(k => k.Rid).ToArray();

    /// <summary>
    /// The platform this process runs on, as <see cref="PgServer"/> sees it. Windows on Arm reports
    /// <c>win-x64</c>, since it runs the x64 binaries under emulation.
    /// </summary>
    public static PgPlatformInfo Detect()
    {
        var platform = PlatformInfo.DetectPlatform();
        return From(platform, PlatformInfo.DetectArchitecture(platform));
    }

    /// <summary>Describes a platform and architecture combination.</summary>
    /// <exception cref="UnsupportedPlatformException">No binaries are published for the combination.</exception>
    public static PgPlatformInfo From(Platform platform, Architecture architecture)
    {
        var known = Known.FirstOrDefault(k => k.Platform == platform && k.Architecture == architecture);
        if (known.Rid is null)
        {
            throw new UnsupportedPlatformException($"No Postgres binaries are published for {platform} on {architecture}.");
        }

        return new PgPlatformInfo(platform, architecture, PlatformInfo.ArtifactSuffix(platform, architecture), known.Rid);
    }

    /// <summary>Parses a runtime identifier such as <c>linux-arm64</c> (case-insensitive).</summary>
    public static bool TryParse(string? runtimeIdentifier, out PgPlatformInfo info)
    {
        var known = Known.FirstOrDefault(k => string.Equals(k.Rid, runtimeIdentifier?.Trim(), StringComparison.OrdinalIgnoreCase));
        info = known.Rid is null ? null! : From(known.Platform, known.Architecture);
        return known.Rid is not null;
    }
}
