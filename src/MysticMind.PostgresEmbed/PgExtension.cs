using System;
using System.Linq;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace MysticMind.PostgresEmbed;

internal class PgExtension
{
    private readonly string _binariesDir;
    private readonly string _pgDir;
    private readonly PgExtensionConfig _config;
    private readonly string _filename;

    public PgExtension(
        string binariesDir,
        string pgDir,
        PgExtensionConfig config)
    {
        _binariesDir = binariesDir;
        _pgDir = pgDir;
        _config = config;

        _filename = Path.GetFileName(_config.DownloadUrl);
    }

    public string Download()
    {
        var zipFile = Path.Combine(_binariesDir, _filename);

        // check if zip file exists in the destination folder
        // return the file path and don't require to download again
        if (File.Exists(zipFile))
        {
            return zipFile;
        }

        var progress = new Progress<double>();
        progress.ProgressChanged += (_, value) => Console.WriteLine("\r %{0:N0}", value);

        try
        {
            // download the file
            Utils.Download(_config.DownloadUrl, zipFile, progress);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to download {_config.DownloadUrl}", ex);
        }

        return zipFile;
    }
    
    public async Task<string> DownloadAsync()
    {
        var zipFile = Path.Combine(_binariesDir, _filename);

        // check if zip file exists in the destination folder
        // return the file path and don't require to download again
        if (File.Exists(zipFile))
        {
            return zipFile;
        }

        var progress = new Progress<double>();
        progress.ProgressChanged += (_, value) => Console.WriteLine("\r %{0:N0}", value);

        try
        {
            // download the file
            var cs = new CancellationTokenSource();
            await Utils.DownloadAsync(_config.DownloadUrl, zipFile, progress, cs.Token);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to download {_config.DownloadUrl}", ex);
        }

        return zipFile;
    }

    public void Extract()
    {
        var zipFile = Path.Combine(_binariesDir, _filename);

        // some extensions such as plv8 hs a container folder
        // when we extract the binary archive, it get extracted with the container folder
        // we want the contents without the container folder for the extensions to install properly
        var containerFolderInBinary = GetContainerFolderInBinary(zipFile);

        var ignoreRootFolder = !string.IsNullOrEmpty(containerFolderInBinary);

        Utils.ExtractZipFolder(zipFile, _pgDir, containerFolderInBinary, ignoreRootFolder);
    }

    private static string GetContainerFolderInBinary(string zipFile)
    {
        //some of the extension binaries may have a root folder which need to be ignored while extracting content
        var containerFolder = "";

        using var archive = ZipFile.OpenRead(zipFile);
        var result = 
            from entry in archive.Entries
            where 
                entry.FullName.EndsWith("/bin/") ||
                entry.FullName.EndsWith("/lib/") || 
                entry.FullName.EndsWith("/share/")
            select entry;

        var item = result.FirstOrDefault();

        if (item == null) return containerFolder;
        var parts = item.FullName.Split('/');
        if (parts.Length > 1)
        {
            containerFolder = parts[0];
        }

        return containerFolder;
    }
}