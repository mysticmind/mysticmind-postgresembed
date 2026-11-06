using System.Diagnostics;
using System.Globalization;

namespace MysticMind.PostgresEmbed;

/// <summary>Whether an instance's server is running.</summary>
public enum PgInstanceState
{
    Stopped,
    Starting,
    Running,
    Stopping
}

/// <summary>
/// The state of the server for an instance's data folder, whichever process started it. Read from
/// Postgres's own <c>postmaster.pid</c> plus a check that the process is alive.
/// </summary>
/// <param name="State">Stopped when there is no server, or <c>postmaster.pid</c> is left over from one that died.</param>
/// <param name="ProcessId">The server's process id while it runs.</param>
/// <param name="Port">The port the server listens on while it runs.</param>
/// <param name="StartedAt">When the server started.</param>
public sealed record PgInstanceStatus(PgInstanceState State, int? ProcessId, int? Port, DateTimeOffset? StartedAt)
{
    internal static readonly PgInstanceStatus Stopped = new(PgInstanceState.Stopped, null, null, null);

    /// <summary>
    /// Reads <c>postmaster.pid</c>: line 1 is the process id, 3 the start time (Unix seconds), 4 the
    /// port and 8 (Postgres 10+) the status: <c>starting</c>, <c>ready</c> or <c>stopping</c>.
    /// </summary>
    internal static PgInstanceStatus Read(string dataDir)
    {
        string[] lines;
        try
        {
            var path = Path.Combine(dataDir, "postmaster.pid");
            if (!File.Exists(path)) return Stopped;

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            lines = reader.ReadToEnd().Split('\n');
        }
        catch (IOException)
        {
            return Stopped; // removed while reading: the server just stopped
        }

        if (lines.Length < 1 || !int.TryParse(lines[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid)
            || !IsPostgresProcess(pid))
        {
            return Stopped;
        }

        int? port = lines.Length > 3 && int.TryParse(lines[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : null;
        DateTimeOffset? startedAt = lines.Length > 2 && long.TryParse(lines[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s)
            ? DateTimeOffset.FromUnixTimeSeconds(s)
            : null;
        var state = (lines.Length > 7 ? lines[7].Trim() : "ready") switch
        {
            "starting" => PgInstanceState.Starting,
            "stopping" => PgInstanceState.Stopping,
            _ => PgInstanceState.Running
        };

        return new PgInstanceStatus(state, pid, port, startedAt);
    }

    // A PID left in postmaster.pid can be reused by an unrelated process after a crash or reboot.
    internal static bool IsPostgresProcess(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited
                   && process.ProcessName.Contains("postgres", StringComparison.OrdinalIgnoreCase)
                   && !IsZombie(pid);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    // On Linux, a server that died but was never reaped stays in the process table as a zombie, which
    // still looks alive by PID and name. That happens in containers whose first process does not reap
    // orphans (the server's parent, pg_ctl, has exited). The state is the field after the ")" that
    // closes the command name in /proc/<pid>/stat: Z is a zombie, X dead.
    private static bool IsZombie(int pid)
    {
        if (!OperatingSystem.IsLinux()) return false;

        try
        {
            var stat = File.ReadAllText($"/proc/{pid}/stat");
            var close = stat.LastIndexOf(')');
            var state = close >= 0 && close + 2 < stat.Length ? stat[close + 2] : ' ';
            return state is 'Z' or 'X';
        }
        catch (IOException)
        {
            return true; // gone between the checks
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
