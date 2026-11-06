namespace MysticMind.PostgresEmbed;

/// <summary>
/// An extension available to <c>CREATE EXTENSION</c>, as described by its control file.
/// </summary>
/// <param name="Name">The name to use in <c>CREATE EXTENSION</c>, for example <c>pg_trgm</c>.</param>
/// <param name="DefaultVersion">The version <c>CREATE EXTENSION</c> installs by default.</param>
/// <param name="Comment">A one-line description.</param>
/// <param name="Requires">Extensions that must be created first (<c>CREATE EXTENSION ... CASCADE</c> does it).</param>
public sealed record PgAvailableExtension(
    string Name,
    string? DefaultVersion,
    string? Comment,
    IReadOnlyList<string> Requires)
{
    /// <summary>
    /// Reads every <c>*.control</c> file of an install, sorted by name. The extension folder is
    /// <c>share/extension</c>, or <c>share/postgresql/extension</c> in some builds (macOS).
    /// </summary>
    internal static IReadOnlyList<PgAvailableExtension> ReadFrom(string installDir)
    {
        string[] folders =
        [
            Path.Combine(installDir, "share", "extension"),
            Path.Combine(installDir, "share", "postgresql", "extension")
        ];

        return folders
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.control"))
            .Select(Parse)
            .GroupBy(e => e.Name, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(e => e.Name, StringComparer.Ordinal)
            .ToList();
    }

    // Control files are "key = value" lines, values optionally single-quoted with '' as an escaped
    // quote, and # starting a comment (outside quotes).
    internal static PgAvailableExtension Parse(string controlFile)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in File.ReadLines(controlFile))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            values[line[..eq].Trim()] = ParseValue(line[(eq + 1)..].Trim());
        }

        var requires = values.TryGetValue("requires", out var r)
            ? r.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];

        return new PgAvailableExtension(
            Path.GetFileNameWithoutExtension(controlFile),
            values.GetValueOrDefault("default_version"),
            values.GetValueOrDefault("comment"),
            requires);
    }

    private static string ParseValue(string value)
    {
        if (!value.StartsWith('\''))
        {
            var hash = value.IndexOf('#');
            return (hash >= 0 ? value[..hash] : value).Trim();
        }

        var result = new System.Text.StringBuilder();
        for (var i = 1; i < value.Length; i++)
        {
            if (value[i] != '\'') { result.Append(value[i]); continue; }
            if (i + 1 < value.Length && value[i + 1] == '\'') { result.Append('\''); i++; continue; }
            break; // closing quote
        }

        return result.ToString();
    }
}
