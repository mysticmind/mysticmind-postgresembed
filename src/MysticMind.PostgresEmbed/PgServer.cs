using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using Polly;

namespace MysticMind.PostgresEmbed
{
    public class PgServer : IDisposable
    {
        private const string PG_SUPERUSER = "postgres";

        private const string PG_HOST = "localhost";

        private const string PG_DBNAME = "postgres";

        private const string PG_STOP_WAIT_S = "5";

        private const int PG_STARTUP_WAIT_MS = 10 * 1000;

        private const string PG_STOP_MODE = "fast";

        private const string PG_CTL_EXE = "pg_ctl.exe";

        private const string INITDB_EXE = "initdb.exe";

        private const string PSQL_EXE = "psql.exe";

        private Guid _instanceId = Guid.NewGuid();

        private string _pgBinaryFullPath;

        private bool _clearInstanceDir;

        private bool _clearWorkingDir;

        private Process _pgServerProcess;

        private List<string> _pgServerParams = new List<string>();

        private List<PgExtensionConfig> _pgExtensions = new List<PgExtensionConfig>();

        private Policy _retryPolicy;

        public PgServer(
            string pgVersion,
            string pgUser = PG_SUPERUSER,
            string dbDir = "",
            int port = 0,
            Dictionary<string, string> pgServerParams = null,
            List<PgExtensionConfig> pgExtensions = null,
            bool clearInstanceDir = true, 
            bool clearWorkingDir=false)
        {
            PgVersion = pgVersion;

            if (String.IsNullOrEmpty(pgUser))
            {
                PgUser = PG_SUPERUSER;
            }
            else
            {
                PgUser = pgUser;
            }

            if (string.IsNullOrEmpty(dbDir))
            {
                DbDir = Path.Combine(".", "pg_embed");
            }
            else
            {
                DbDir = Path.Combine(dbDir, "pg_embed");
            }

            if (port == 0)
            {
                Port = Utils.GetAvailablePort();
            }
            else
            {
                Port = port;
            }

            if (pgServerParams != null)
            {
                foreach (var item in pgServerParams)
                {
                    _pgServerParams.Add($"-c {item.Key}={item.Value}");
                }
            }

            if (pgExtensions != null)
            {
                _pgExtensions.AddRange(pgExtensions);
            }

            _clearInstanceDir = clearInstanceDir;
            _clearWorkingDir = clearWorkingDir;

            BinariesDir = Path.Combine(DbDir, "binaries");
            InstanceDir = Path.Combine(DbDir, _instanceId.ToString());
            PgDir = Path.Combine(InstanceDir, "pgsql");
            PgBinDir = Path.Combine(PgDir, "bin");
            DataDir = Path.Combine(InstanceDir, "data");
            

            // setup the policy for retry pertaining to downloading binary
            _retryPolicy =
                Polly.Policy.Handle<Exception>()
                    .WaitAndRetry(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) });
        }

        public string PgVersion { get; private set; }

        public string PgUser { get; private set; }

        public string DbDir { get; private set; }

        public string BinariesDir { get; private set; }

        public string InstanceDir { get; private set; }

        public string PgDir { get; private set; }

        public string PgBinDir { get; private set; }

        public string DataDir { get; private set; }

        public int Port { get; private set; }

        public string PgDbName
        {
            get
            {
                return PG_DBNAME; 
            }
        }

        private void DownloadPgBinary()
        {
            var downloader = new PgBinariesLiteBinaryDownloader(PgVersion, BinariesDir);

            try
            {
                _pgBinaryFullPath = _retryPolicy.Execute(() => downloader.Download());
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to download PgBinary", ex);
            }
        }

        private void DownloadPgExtensions()
        {
            foreach (var extnConfig in _pgExtensions)
            {
                var pgExtensionInstance = new PgExtension(PgVersion, PG_HOST, Port, PgUser, PgDbName, BinariesDir, PgDir, extnConfig);
                _retryPolicy.Execute(() => pgExtensionInstance.Download());
            }
        }

        private void CreateDirs()
        {
            Directory.CreateDirectory(DbDir);
            Directory.CreateDirectory(BinariesDir);
            Directory.CreateDirectory(PgDir);
            Directory.CreateDirectory(DataDir);
        }

        private void RemoveWorkingDir()
        {
            try
            {
                if (_clearWorkingDir)
                {
                    Directory.Delete(DbDir, true);
                }
            }
            catch
            {
            }
        }

        private void RemoveInstanceDir()
        {
            try
            {
                if (_clearInstanceDir)
                {
                    Directory.Delete(InstanceDir, true);
                }
            }
            catch
            {
            }   
        }

        private void ExtractPgBinary()
        {
            Utils.ExtractZip(_pgBinaryFullPath, InstanceDir);
        }

        private void ExtractPgExtensions()
        {
            foreach (var extnConfig in _pgExtensions)
            {
                var pgExtensionInstance = new PgExtension(PgVersion, PG_HOST, Port, PgUser, PgDbName, BinariesDir, PgDir, extnConfig);
                _retryPolicy.Execute(() => pgExtensionInstance.Extract());
            }
        }

        private void InitDb()
        {
            var filename = Path.Combine(PgBinDir, INITDB_EXE);
            var args = new List<string>();

            // add data dir
            args.Add($"-D {DataDir}");

            // add super user
            args.Add($"-U {PgUser}");

            // add encoding
            args.Add("-E UTF-8");

            try
            {
                var result = Utils.RunProcess(filename, args);

                if (result.ExitCode != 0)
                {
                    throw new Exception($"InitDb execution returned an error code {result.Output}");
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Error occurred while executing InitDb", ex);
            }
        }

        private void CreateExtensions()
        {
            foreach (var extnConfig in _pgExtensions)
            {
                var pgExtensionInstance = new PgExtension(PgVersion, PG_HOST, Port, PgUser, PgDbName, BinariesDir, PgDir, extnConfig);
                _retryPolicy.Execute(() => pgExtensionInstance.CreateExtension());
            }
        }

        private bool VerifyReady()
        {
            var filename = Path.Combine(PgBinDir, PSQL_EXE);

            List<string> args = new List<string>();

            // add host
            args.Add($"-h {PG_HOST}");

            //add port
            args.Add($"-p {Port}");

            //add  user
            args.Add($"-U {PgUser}");

            // add database name
            args.Add($"-d {PgDbName}");

            // add command
            args.Add($"-c \"SELECT 1 as test\"");

            var result = Utils.RunProcess(filename, args);

            return result.ExitCode == 0;
        }

        private void StartServer()
        {
            var filename = Path.Combine(PgBinDir, PG_CTL_EXE);

            List<string> args = new List<string>();

            // add the data dir arg
            args.Add($"-D {DataDir}");

            // add user
            args.Add($"-U {PgUser}");

            // create the init options arg
            var initOptions = new List<string>();

            // run without fsync
            initOptions.Add("-F");

            //set the port
            initOptions.Add($"-p {Port}");

            // add the additional parameters passed
            initOptions.AddRange(_pgServerParams);

            // add options arg
            args.Add($"-o \"{string.Join(" ", initOptions)}\"");

            // add start arg
            args.Add("start");

            try
            {
                _pgServerProcess = new Process();

                _pgServerProcess.StartInfo.RedirectStandardError = true;
                _pgServerProcess.StartInfo.RedirectStandardOutput = true;
                _pgServerProcess.StartInfo.UseShellExecute = false;
                _pgServerProcess.EnableRaisingEvents = true;

                _pgServerProcess.StartInfo.FileName = filename;
                _pgServerProcess.StartInfo.Arguments = string.Join(" ", args);

                _pgServerProcess.Start();

                // allow some time for postgres to start
                var watch = new Stopwatch();
                watch.Start();

                WaitForServerStartup(watch);
            }
            catch (Exception ex)
            {
                throw new Exception("Exception occurred while starting Pg server", ex);
            }

        }

        private void WaitForServerStartup(Stopwatch watch)
        {
            while (watch.ElapsedMilliseconds < PG_STARTUP_WAIT_MS)
            {
                // verify if server ready
                if (VerifyReady())
                {
                    return;
                }

                Thread.Sleep(100);
            }

            watch.Stop();

            throw new IOException($"Gave up waiting for server to start after {PG_STARTUP_WAIT_MS}ms");
        }

        private void StopServer()
        {
            var filename = Path.Combine(PgBinDir, PG_CTL_EXE);

            List<string> args = new List<string>();

            // add data dir
            args.Add($"-D {DataDir}");

            // add user
            args.Add($"-U {PgUser}");

            // add stop mode
            args.Add($"-m {PG_STOP_MODE}");

            // stop wait secs
            args.Add($"-t {PG_STOP_WAIT_S}");

            // add stop action
            args.Add("stop");

            try
            {
                Utils.RunProcess(filename, args);
            }
            catch
            {
            }
        }

        private void KillServerProcess()
        {
            try
            {
                _pgServerProcess.Kill();
            }
            catch
            {
            }
        }

        public void Start()
        {
            // clear working directory based on flag passed
            RemoveWorkingDir();

            CreateDirs();

            DownloadPgBinary();
            DownloadPgExtensions();

            ExtractPgBinary();
            ExtractPgExtensions();

            InitDb();
            StartServer();

            CreateExtensions();
        }

        public void Stop()
        {
            StopServer();
            KillServerProcess();

            // clear instance directory based on flag passed
            RemoveInstanceDir();
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
