using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Net;
using System.Net.NetworkInformation;
using System.IO.Compression;
using System.Text;
using System.Diagnostics;

namespace MysticMind.PostgresEmbed
{
    internal class ProcessResult
    {
        public int ExitCode { get; set; }

        public string Output { get; set; }

        public string Error { get; set; }
    }

    internal static class Utils
    {
        public static async Task DownloadAsync(string url, string downloadFullPath, IProgress<double> progress, CancellationToken token)
        {
            HttpClient client = new HttpClient();

            using (HttpResponseMessage response = client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).Result)
            {
                response.EnsureSuccessStatusCode();

                using (Stream contentStream = await response.Content.ReadAsStreamAsync(), fileStream = new FileStream(downloadFullPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    var totalRead = 0L;
                    var totalReads = 0L;
                    var buffer = new byte[8192];
                    var isMoreToRead = true;

                    do
                    {
                        var read = await contentStream.ReadAsync(buffer, 0, buffer.Length);
                        if (read == 0)
                        {
                            isMoreToRead = false;
                        }
                        else
                        {
                            await fileStream.WriteAsync(buffer, 0, read);

                            totalRead += read;
                            totalReads += 1;

                            if (totalReads % 2000 == 0)
                            {
                                Console.WriteLine(string.Format("total bytes downloaded so far: {0:n0}", totalRead));
                            }
                        }
                    }
                    while (isMoreToRead);
                }
            }
        }

        public static void ExtractZip(string zipFile, string destDir, string extractPath="", bool ignoreRootDir=false)
        {
            ZipFile.ExtractToDirectory(zipFile, destDir);
        }

        public static void ExtractZipFolder(string zipFile, string destDir, string extractPath = "", bool ignoreRootDir = false)
        {
            using (var archive = ZipFile.OpenRead(zipFile))
            {
                var result = from entry in archive.Entries
                             where entry.FullName.StartsWith(extractPath)
                             select entry;

                foreach (ZipArchiveEntry entry in result)
                {
                    var fullName = entry.FullName;

                    if (ignoreRootDir)
                    {
                        var pathParts = entry.FullName.Split('/');
                        pathParts = pathParts.Skip(1).ToArray();

                        fullName = Path.Combine(pathParts);
                    }

                    string fullPath = Path.Combine(destDir, fullName);
                    if (String.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(fullPath);
                    }
                    else
                    {
                        entry.ExtractToFile(fullPath);
                    }
                }
            }
        }

        public static int GetAvailablePort(int startingPort=5500)
        {
            IPEndPoint[] endPoints;
            List<int> portArray = new List<int>();

            IPGlobalProperties properties = IPGlobalProperties.GetIPGlobalProperties();

            //getting active connections
            TcpConnectionInformation[] connections = properties.GetActiveTcpConnections();
            portArray.AddRange(from n in connections
                               where n.LocalEndPoint.Port >= startingPort
                               select n.LocalEndPoint.Port);

            //getting active tcp listners
            endPoints = properties.GetActiveTcpListeners();
            portArray.AddRange(from n in endPoints
                               where n.Port >= startingPort
                               select n.Port);

            //getting active udp listeners
            endPoints = properties.GetActiveUdpListeners();
            portArray.AddRange(from n in endPoints
                               where n.Port >= startingPort
                               select n.Port);

            portArray.Sort();

            for (int i = startingPort; i < UInt16.MaxValue; i++)
                if (!portArray.Contains(i))
                    return i;

            return 0;
        }

        public static ProcessResult RunProcess(string filename, List<string> args)
        {
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            using (var p = new Process())
            {
                p.StartInfo.RedirectStandardError = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.UseShellExecute = false;
                p.EnableRaisingEvents = true;
                p.OutputDataReceived += (sender, e) => outputBuilder.Append(e.Data);
                p.ErrorDataReceived += (sender, e) => errorBuilder.Append(e.Data);

                p.StartInfo.FileName = filename;
                p.StartInfo.Arguments = string.Join(" ", args);

                p.Start();

                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                p.WaitForExit();

                p.CancelOutputRead();
                p.CancelOutputRead();

                string output = outputBuilder.ToString();
                string error = errorBuilder.ToString();

                return new ProcessResult { ExitCode = p.ExitCode, Output = output, Error = error };
            }
        }
    }
}
