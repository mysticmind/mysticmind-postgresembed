using System.Runtime.InteropServices;

namespace MysticMind.PostgresEmbed;

internal static class PathUtils
{
    /// <summary>
    /// Postgres tools on Windows resolve their own location and data directory through ANSI APIs, so a
    /// path with characters outside the active code page (for example Chinese folder names) breaks
    /// initdb and pg_ctl (issue #29). On Windows this maps such a path to its 8.3 short form, which is
    /// ASCII only. The path must exist. Paths are returned unchanged on other platforms.
    /// </summary>
    /// <param name="path">The folder Postgres will use.</param>
    /// <param name="optionName">The <see cref="PgServerOptions"/> property the folder comes from, named in the error.</param>
    public static string ToProcessSafePath(string path, string optionName)
    {
        if (!OperatingSystem.IsWindows() || IsAscii(path)) return path;

        var buffer = new char[32767];
        var length = GetShortPathNameW(path, buffer, (uint)buffer.Length);

        if (length == 0 || length > buffer.Length)
        {
            throw new PostgresEmbedException(NonAsciiPathMessage(path, optionName, Marshal.GetLastPInvokeError()));
        }

        var shortPath = new string(buffer, 0, (int)length);

        if (!IsAscii(shortPath))
        {
            throw new PostgresEmbedException(NonAsciiPathMessage(path, optionName));
        }

        return shortPath;
    }

    /// <summary>
    /// The error for a non-ASCII path Postgres cannot use on Windows: which characters, why, which option to
    /// change and how to fix it. <paramref name="win32Error"/> is set when Windows could not resolve a short
    /// name at all, rather than the drive having short names turned off.
    /// </summary>
    internal static string NonAsciiPathMessage(string path, string optionName, int? win32Error = null)
    {
        var characters = string.Join(" ", path.Where(c => !char.IsAscii(c)).Distinct());
        // a local drive letter; network paths (\\server\share) have none, and fsutil cannot change them
        string? drive = path.Length >= 2 && path[1] == ':' && char.IsAsciiLetter(path[0]) ? path[..2].ToUpperInvariant() : null;
        var reason = win32Error is not null
            ? $"Windows could not provide an ASCII-only (8.3) form of the path (Win32 error {win32Error})"
            : drive is not null
                ? $"drive {drive} has 8.3 short names turned off, so there is no ASCII-only form of the path to use instead"
                : "its volume has 8.3 short names turned off, so there is no ASCII-only form of the path to use instead";
        var nl = Environment.NewLine;

        var message =
            $"Postgres on Windows cannot use the folder '{path}'.{nl}" +
            $"It contains non-ASCII characters ({characters}), which Postgres on Windows cannot read, and {reason}.{nl}";

        message += drive is null
            ? $"To fix it, set PgServerOptions.{optionName} to a folder whose full path is ASCII only, for example C:\\pg-embed.{nl}"
            : $"To fix it, do one of these:{nl}" +
              $"  - Set PgServerOptions.{optionName} to a folder whose full path is ASCII only, for example C:\\pg-embed.{nl}" +
              $"  - Turn on 8.3 short names for {drive} (as administrator: fsutil 8dot3name set {drive} 0), then delete and " +
              $"recreate the folder. Folders created while short names were off never get one.{nl}";

        return message + "Details: https://github.com/mysticmind/mysticmind-postgresembed/issues/29";
    }

    private static bool IsAscii(string value) => value.All(char.IsAscii);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathNameW(string longPath, [Out] char[] shortPath, uint bufferLength);
}
