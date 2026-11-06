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
    public static string ToProcessSafePath(string path)
    {
        if (!OperatingSystem.IsWindows() || IsAscii(path)) return path;

        var buffer = new char[32767];
        var length = GetShortPathNameW(path, buffer, (uint)buffer.Length);

        if (length == 0 || length > buffer.Length)
        {
            throw new PostgresEmbedException(
                $"Path '{path}' contains non-ASCII characters and its short (8.3) form could not be resolved " +
                $"(Win32 error {Marshal.GetLastPInvokeError()}). Use an ASCII only DbDir.");
        }

        var shortPath = new string(buffer, 0, (int)length);

        if (!IsAscii(shortPath))
        {
            throw new PostgresEmbedException(
                $"Path '{path}' contains non-ASCII characters and 8.3 short names are disabled on this volume. " +
                "Postgres on Windows cannot use it; use an ASCII only DbDir.");
        }

        return shortPath;
    }

    private static bool IsAscii(string value) => value.All(char.IsAscii);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathNameW(string longPath, [Out] char[] shortPath, uint bufferLength);
}
