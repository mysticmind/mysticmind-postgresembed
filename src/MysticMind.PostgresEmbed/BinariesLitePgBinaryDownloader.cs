using System;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using System.IO.Compression;
using System.Threading;
using NuGet.Configuration;
using NuGet.Protocol.Core.Types;
using NuGet.Protocol;
using NuGet.Versioning;
using NuGet.Packaging.Core;
using NuGet.Frameworks;
using NuGet.Common;

namespace MysticMind.PostgresEmbed
{
    internal class PgBinariesLiteBinaryDownloader
    {
        private const string FILE_NAME = "postgresql-{0}-windows-x64-binaries-lite.zip";

        private string _pgVersion;

        private string _destDir; 

        public PgBinariesLiteBinaryDownloader(string pgVersion, string destDir)
        {
            _pgVersion = pgVersion;
            _destDir = destDir;
        }

        private async Task<Uri> GetNugetUri(CancellationToken cancellationToken)
        {
            var settings = Settings.LoadDefaultSettings(null);
            var packageSourceProvider = new PackageSourceProvider(settings);
            var sourceRepositoryProvider = new SourceRepositoryProvider(packageSourceProvider, FactoryExtensionsV3.GetCoreV3(Repository.Provider));
            var package = new PackageIdentity("PostgreSql.Binaries.Lite", NuGetVersion.Parse(_pgVersion));
            using (var cacheContext = new SourceCacheContext())
            {
                var repositories = sourceRepositoryProvider.GetRepositories();
                foreach (var sourceRepository in repositories)
                {
                    var dependencyInfoResource = await sourceRepository.GetResourceAsync<DependencyInfoResource>();
                    var dependencyInfo = await dependencyInfoResource.ResolvePackage(
                        package, NuGetFramework.AnyFramework, cacheContext, NullLogger.Instance, cancellationToken);
                    if (dependencyInfo != null)
                    {
                        return dependencyInfo.DownloadUri;
                    }
                }
            }
            throw new Exception("Could not find PostgreSql.Binaries.Lite package");
        }
        
        public string Download()
        {
            var versionParts = _pgVersion.Split('.');

            string zipFilename = "";

            if (versionParts.Length > 3)
            {
                zipFilename = string.Format(FILE_NAME, $"{versionParts[0]}.{versionParts[1]}.{versionParts[2]}-{versionParts[3]}");
            }
            else
            {
                zipFilename = string.Format(FILE_NAME, $"{versionParts[0]}.{versionParts[1]}-{versionParts[2]}");
            }

            var zipFile = Path.Combine(_destDir, zipFilename);

            // check if zip file exists in the destination folder
            // return the file path and don't require to download again
            if (File.Exists(zipFile))
            {
                return zipFile;
            }

            // first step is to download the nupkg file
            var cs = new CancellationTokenSource();
            var url = GetNugetUri(cs.Token).Result;
            var nupkgFile = Path.Combine(_destDir, $@"PostgreSql.Binaries.Lite.{_pgVersion}.nupkg");

            var progress = new System.Progress<double>();
            progress.ProgressChanged += (sender, value) => Console.WriteLine("\r %{0:N0}", value);

            // download the file
            Utils.DownloadAsync(url.AbsoluteUri, nupkgFile, progress, cs.Token).Wait();


            // extract the PG binary zip file
            using (var archive = ZipFile.OpenRead(nupkgFile))
            {
                var result = from entry in archive.Entries
                             where Path.GetDirectoryName(entry.FullName) == "content"
                             where !string.IsNullOrEmpty(entry.Name)
                             select entry;

                var pgBinaryZipFile = result.FirstOrDefault();

                if (pgBinaryZipFile != null)
                {
                    pgBinaryZipFile.ExtractToFile(zipFile, true);

                    return zipFile;
                }

                return string.Empty;
            }
        }
    }
}
