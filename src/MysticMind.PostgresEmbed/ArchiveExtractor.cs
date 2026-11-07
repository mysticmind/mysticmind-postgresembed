using System.IO.Compression;
using SharpCompress.Readers;

namespace MysticMind.PostgresEmbed;

internal static class ArchiveExtractor
{
    /// <summary>
    /// Extracts a tar based archive (the zonky <c>.txz</c>) into <paramref name="destinationDir"/>.
    /// Symbolic links are recreated as links on Linux and macOS, and as file copies on Windows.
    /// </summary>
    public static void ExtractTarArchive(string archivePath, string destinationDir)
    {
        var root = Path.GetFullPath(destinationDir);
        var links = new List<(string LinkPath, string Target)>();

        using (var stream = File.OpenRead(archivePath))
        using (var reader = ReaderFactory.OpenReader(stream, new ReaderOptions()))
        {
            while (reader.MoveToNextEntry())
            {
                var entry = reader.Entry;
                if (string.IsNullOrEmpty(entry.Key)) continue;

                var path = ResolveInside(root, entry.Key);

                if (entry.IsDirectory)
                {
                    Directory.CreateDirectory(path);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                if (!string.IsNullOrEmpty(entry.LinkTarget))
                {
                    // create links once every regular file exists
                    links.Add((path, entry.LinkTarget));
                    continue;
                }

                using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                reader.WriteEntryTo(output);
            }
        }

        foreach (var (linkPath, target) in links)
        {
            CreateLink(root, linkPath, target);
        }
    }

    /// <summary>
    /// Extracts a zip archive into <paramref name="destinationDir"/>. When <paramref name="rootFolderToStrip"/>
    /// is set, only entries under that folder are extracted, without the folder itself.
    /// </summary>
    public static void ExtractZip(string zipPath, string destinationDir, string? rootFolderToStrip = null)
    {
        var root = Path.GetFullPath(destinationDir);
        var prefix = string.IsNullOrEmpty(rootFolderToStrip) ? "" : rootFolderToStrip.TrimEnd('/') + "/";

        using var archive = ZipFile.OpenRead(zipPath);

        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith(prefix, StringComparison.Ordinal)) continue;

            var relativePath = entry.FullName[prefix.Length..];
            if (relativePath.Length == 0) continue;

            var path = ResolveInside(root, relativePath);

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(path);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            entry.ExtractToFile(path, overwrite: true);
        }
    }

    /// <summary>
    /// Returns the single folder that wraps the archive content (for example <c>postgis-bundle-pg17-3.6.2x64</c>
    /// containing <c>bin</c>, <c>lib</c> and <c>share</c>), or <c>null</c> when the content is at the root.
    /// </summary>
    public static string? FindWrappingFolder(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);

        foreach (var entry in archive.Entries)
        {
            var parts = entry.FullName.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;

            if (parts[1] is "bin" or "lib" or "share") return parts[0];
        }

        return null;
    }

    // protects against archive entries such as "../../etc/passwd" (zip slip)
    private static string ResolveInside(string root, string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        var rootWithSeparator = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;

        if (!path.StartsWith(rootWithSeparator, StringComparison.Ordinal) && path != root)
        {
            throw new PostgresEmbedException($"Archive entry '{relativePath}' resolves outside of '{root}'.");
        }

        return path;
    }

    private static void CreateLink(string root, string linkPath, string target)
    {
        // links that point outside of the extracted tree are not needed to run Postgres; skip them
        // compared with a trailing separator: a sibling folder whose name starts like root is outside it
        var resolvedTarget = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(linkPath)!, target));
        var rootWithSeparator = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        if (!resolvedTarget.StartsWith(rootWithSeparator, StringComparison.Ordinal) && resolvedTarget != root) return;

        if (File.Exists(linkPath)) File.Delete(linkPath);

        if (OperatingSystem.IsWindows())
        {
            if (File.Exists(resolvedTarget)) File.Copy(resolvedTarget, linkPath);
            return;
        }

        File.CreateSymbolicLink(linkPath, target);
    }
}
