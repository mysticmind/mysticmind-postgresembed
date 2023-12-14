using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Net.NetworkInformation;
using System.IO.Compression;
using System.Text;
using System.Diagnostics;
using SharpCompress.Common;
using SharpCompress.Readers;
using System.Runtime.InteropServices;

namespace MysticMind.PostgresEmbed;

internal class ProcessResult
{
    public int ExitCode { get; init; }

    public string Output { get; init; }

    public string Error { get; init; }
}

internal static class Utils
{
    public static async Task DownloadAsync(string url, string downloadFullPath, IProgress<double> progress, CancellationToken token)
    {
        var client = new HttpClient();

        using HttpResponseMessage response = client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).Result;
        response.EnsureSuccessStatusCode();

        await using Stream contentStream = await response.Content.ReadAsStreamAsync(token), fileStream = new FileStream(downloadFullPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
        var totalRead = 0L;
        var totalReads = 0L;
        var buffer = new byte[8192];
        var isMoreToRead = true;

        do
        {
            var read = await contentStream.ReadAsync(buffer, token);
            if (read == 0)
            {
                isMoreToRead = false;
            }
            else
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), token);

                totalRead += read;
                totalReads += 1;

                if (totalReads % 2000 == 0)
                {
                    Console.WriteLine($"total bytes downloaded so far: {totalRead:n0}");
                }
            }
        }
        while (isMoreToRead);
    }
    
    public static void Download(string url, string downloadFullPath, IProgress<double> progress)
    {
        var client = new HttpClient();

        using var response = client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None).Result;
        response.EnsureSuccessStatusCode();

        using Stream contentStream = response.Content.ReadAsStream(), fileStream = new FileStream(downloadFullPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
        var totalRead = 0L;
        var totalReads = 0L;
        var buffer = new byte[8192];
        var isMoreToRead = true;

        do
        {
            var read = contentStream.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                isMoreToRead = false;
            }
            else
            {
                fileStream.Write(buffer.AsSpan(0, read));

                totalRead += read;
                totalReads += 1;

                if (totalReads % 2000 == 0)
                {
                    Console.WriteLine($"total bytes downloaded so far: {totalRead:n0}");
                }
            }
        }
        while (isMoreToRead);
    }

    // public static void ExtractZip(string zipFile, string destDir, string extractPath="", bool ignoreRootDir=false)
    // {
    //     ZipFile.ExtractToDirectory(zipFile, destDir, overwriteFiles: true);
    // }
        
    public static void ExtractZip(string zipFile, string destDir, string extractPath="", bool ignoreRootDir=false)
    {
        using Stream stream = File.OpenRead(zipFile);
        using var reader = ReaderFactory.Open(stream);
        var isWindows = Utils.IsWindows();
        var symbolicLinks = new Dictionary<string, string>();

        var opts = new ExtractionOptions()
        {
            ExtractFullPath = true,
            Overwrite = true,
            WriteSymbolicLink = (symbolicLinkPath, symbolicLinkSourceFile) =>
            {
                if (isWindows) return;
                var fileDir = Path.GetDirectoryName(symbolicLinkPath);
                symbolicLinks[symbolicLinkPath] = Path.Combine(fileDir, symbolicLinkSourceFile);
            }
        };
            
        while (reader.MoveToNextEntry())
        {
            if (reader.Entry.IsDirectory) continue;
            // Specify the extraction path for the entry
            var extractionPath = Path.Combine(destDir, reader.Entry.Key);

            // Ensure that the target directory exists
            var targetDirectory = Path.GetDirectoryName(extractionPath);
            if (!Directory.Exists(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory!);    
            }

            reader.WriteEntryToFile(extractionPath, opts);
        }

        foreach (var item in symbolicLinks.Where(item => File.Exists(item.Value)))
        {
            File.Copy(item.Value, item.Key);
        }
    }

    public static void ExtractZipFolder(string zipFile, string destDir, string extractPath = "", bool ignoreRootDir = false)
    {
        using var archive = ZipFile.OpenRead(zipFile);
        var result = from entry in archive.Entries
            where entry.FullName.StartsWith(extractPath)
            select entry;

        foreach (var entry in result)
        {
            var fullName = entry.FullName;

            if (ignoreRootDir)
            {
                var pathParts = entry.FullName.Split('/');
                pathParts = pathParts.Skip(1).ToArray();

                fullName = Path.Combine(pathParts);
            }

            var fullPath = Path.Combine(destDir, fullName);
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(fullPath);
            }
            else
            {
                entry.ExtractToFile(fullPath, overwrite: true);
            }
        }
    }

    public static int GetAvailablePort(int startingPort=5500)
    {
        List<int> portArray = new List<int>();

        IPGlobalProperties properties = IPGlobalProperties.GetIPGlobalProperties();

        //getting active connections
        TcpConnectionInformation[] connections = properties.GetActiveTcpConnections();
        portArray.AddRange(from n in connections
            where n.LocalEndPoint.Port >= startingPort
            select n.LocalEndPoint.Port);

        //getting active tcp listeners
        var endPoints = properties.GetActiveTcpListeners();
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

        using var p = new Process();
        p.StartInfo.RedirectStandardError = true;
        p.StartInfo.RedirectStandardOutput = true;
        p.StartInfo.UseShellExecute = false;
        p.EnableRaisingEvents = true;
        p.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                outputBuilder.AppendLine(e.Data);
        };

        p.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                errorBuilder.AppendLine(e.Data);
        };

        p.StartInfo.FileName = filename;
        p.StartInfo.Arguments = string.Join(" ", args);
        p.StartInfo.CreateNoWindow = true;

        p.Start();

        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        p.WaitForExit();

        p.CancelOutputRead();
        p.CancelErrorRead();

        var output = outputBuilder.ToString();
        var error = errorBuilder.ToString();

        return new ProcessResult { ExitCode = p.ExitCode, Output = output, Error = error };
    }

    public static bool IsWindows()
    {
        return RuntimeInformation.IsOSPlatform(
            OSPlatform.Windows
        );
    }

    public static Platform? GetPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Platform.Windows;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return Platform.Linux;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return Platform.Darwin;
        }

        return null;
    }

    public static Architecture GetArchitecture(Platform platform)
    {
        if (platform is not Platform.Darwin) return Architecture.Amd64;
            
        var processResult = Utils.RunProcess("sysctl", new List<string>
        {
            "machdep.cpu.brand_string"
        });

        return processResult.Output.Contains("Apple M") ? Architecture.Arm64V8 : Architecture.Amd64;
    }
}