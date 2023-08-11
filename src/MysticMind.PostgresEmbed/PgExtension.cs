using System;
using System.Linq;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace MysticMind.PostgresEmbed
{
    using System.Collections.Generic;

    internal class PgExtension
    {
        private const string PsqlExe = "psql.exe";

        private readonly string _pgVersion;

        private readonly string _pgHost;

        private readonly int _pgPort;

        private readonly string _pgUser;

        private readonly string _pgDbName;

        private readonly string _binariesDir;

        private readonly string _pgDir;

        private readonly PgExtensionConfig _config;

        private readonly string _filename;

        public PgExtension(
            string pgVersion,
            string pgHost,
            int pgPort,
            string pgUser,
            string pgDbName,
            string binariesDir,
            string pgDir,
            PgExtensionConfig config)
        {
            _pgVersion = pgVersion;
            _pgHost = pgHost;
            _pgPort = pgPort;
            _pgUser = pgUser;
            _pgDbName = pgDbName;

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
                var cs = new CancellationTokenSource();
                Utils.DownloadAsync(_config.DownloadUrl, zipFile, progress, cs.Token).Wait(cs.Token);
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

        public void CreateExtension()
        {
            // create a single sql command with semicolon separators
            var sql = string.Join(";", _config.CreateExtensionSqlList);

            var args = new List<string>
            {
                // add host
                $"-h {_pgHost}",
                // add port
                $"-p {_pgPort}",
                // add user
                $"-U {_pgUser}",
                // add database name
                $"-d {this._pgDbName}",
                // add command
                $"-c \"{sql}\""
            };

            var filename = Path.Combine(_pgDir, "bin", PsqlExe);

            try
            {
                var result = Utils.RunProcess(filename, args);

                if (result.ExitCode != 0)
                {
                    throw new Exception($"'{sql}' execution returned an error code {result.ExitCode} {result.Output} {result.Error}");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Exception occurred while executing '{sql}'", ex);
            }
        }

        private string GetContainerFolderInBinary(string zipFile)
        {
            //some of the extension binaries may have a root folder which need to be ignored while extracting content
            var containerFolder = "";

            using var archive = ZipFile.OpenRead(zipFile);
            var result = from entry in archive.Entries
                where entry.FullName.EndsWith("/bin/") ||
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
}
