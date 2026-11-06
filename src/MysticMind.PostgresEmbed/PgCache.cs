namespace MysticMind.PostgresEmbed;

/// <summary>
/// The shared cache: one folder per user holding downloaded binaries and named instances, so that
/// tests, apps and tools (such as the pgembed CLI) on one machine reuse the same downloads and can
/// meet at the same named instance.
/// </summary>
public static class PgCache
{
    /// <summary>The environment variable that overrides <see cref="DefaultDirectory"/>.</summary>
    public const string DirectoryVariable = "PGEMBED_CACHE_DIR";

    /// <summary>
    /// <c>PGEMBED_CACHE_DIR</c> when set, otherwise the per-user cache folder:
    /// <c>$XDG_CACHE_HOME/pgembed</c> or <c>~/.cache/pgembed</c> on Linux,
    /// <c>~/Library/Caches/pgembed</c> on macOS, and <c>%LOCALAPPDATA%\pgembed</c> on Windows.
    /// </summary>
    public static string DefaultDirectory => Resolve(Environment.GetEnvironmentVariable, OperatingSystem.IsWindows(), OperatingSystem.IsMacOS());

    /// <summary>True when <c>PGEMBED_CACHE_DIR</c> is set, which also makes it the default binaries folder for every server.</summary>
    public static bool IsConfigured => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DirectoryVariable));

    internal static string Resolve(Func<string, string?> getEnvironmentVariable, bool isWindows, bool isMacOS)
    {
        var configured = getEnvironmentVariable(DirectoryVariable);
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);

        if (isWindows)
        {
            var localAppData = getEnvironmentVariable("LOCALAPPDATA");
            return Path.Combine(string.IsNullOrWhiteSpace(localAppData)
                ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                : localAppData, "pgembed");
        }

        var home = getEnvironmentVariable("HOME");
        if (string.IsNullOrWhiteSpace(home)) home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (isMacOS) return Path.Combine(home, "Library", "Caches", "pgembed");

        var xdg = getEnvironmentVariable("XDG_CACHE_HOME");
        return Path.Combine(string.IsNullOrWhiteSpace(xdg) ? Path.Combine(home, ".cache") : xdg, "pgembed");
    }
}
