using System.Runtime.CompilerServices;

namespace MysticMind.PostgresEmbed.Samples;

internal static class SampleCache
{
    // The samples run against a cache of their own, so a sample named "app" never touches a real instance of
    // that name. CI sets PGEMBED_CACHE_DIR to a cached folder instead.
    [ModuleInitializer]
    internal static void Use()
    {
        if (!PgCache.IsConfigured)
            Environment.SetEnvironmentVariable(PgCache.DirectoryVariable, Path.Combine(Path.GetTempPath(), "postgresembed-samples"));
    }
}
