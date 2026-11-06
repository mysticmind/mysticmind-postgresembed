using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace MysticMind.PostgresEmbed;

/// <summary>
/// The System V shared memory segment a Postgres server holds on Linux and macOS. A clean shutdown
/// removes it; a killed server leaves it behind until reboot. On macOS only 32 segments may exist on
/// the whole machine (kern.sysv.shmmni), so after enough kills no Postgres can start at all
/// ("could not create shared memory segment: No space left on device"). Removing the segment after a
/// kill prevents that. Windows uses no System V memory and needs nothing.
/// </summary>
internal static class SharedMemory
{
    internal readonly record struct Segment(int Key, int Id);

    private const int IpcRmid = 0; // IPC_RMID on Linux and macOS alike

    /// <summary>
    /// The segment recorded on line 7 of postmaster.pid (<c>key id</c>), or null when there is none,
    /// as on Windows or before the server has created it.
    /// </summary>
    public static Segment? Read(string pidFile)
    {
        if (OperatingSystem.IsWindows()) return null;
        try
        {
            var line = File.ReadLines(pidFile).Skip(6).FirstOrDefault();
            var parts = line?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts is not { Length: 2 } ||
                !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var key) ||
                !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                return null;
            }

            // Postgres prints key_t (a 32-bit int) as an unsigned long
            return new Segment(unchecked((int)key), id);
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Whether a segment with this key exists.</summary>
    public static bool Exists(int key) => !OperatingSystem.IsWindows() && shmget(key, 0, 0) >= 0;

    /// <summary>
    /// Removes the segment, only if its key still names the same segment: an id reused by an unrelated
    /// segment is never touched. Call only once the server that held it is gone.
    /// </summary>
    public static void Remove(Segment segment, ILogger logger)
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            var id = shmget(segment.Key, 0, 0);
            if (id != segment.Id) return;

            if (shmctl(id, IpcRmid, IntPtr.Zero) == 0) logger.LogDebug("Removed shared memory segment {Id} of the killed server", id);
            else logger.LogDebug("Could not remove shared memory segment {Id} (errno {Errno})", id, Marshal.GetLastPInvokeError());
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            logger.LogDebug(ex, "Could not remove the shared memory segment of the killed server");
        }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int shmget(int key, nuint size, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int shmctl(int id, int command, IntPtr buffer);
}
