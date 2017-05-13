using LibGit2Sharp;
using Microsoft.Extensions.CommandLineUtils;
using Serilog;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GitBackup
{
    class Program
    {
        static void Main(string[] args)
        {
            var cmd = ProgramCommand();

            try
            {
                cmd.Execute(args);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "GitBackup:: problem during backup");
                Environment.ExitCode = ex.HResult;
            }
        }

        #region Command

        static CommandLineApplication ProgramCommand()
        {
            var cmd = new CommandLineApplication(true);
            cmd.FullName = "GitBackup";

            AddHelpOption(cmd, true);

            AddVersionOption(cmd);

            var backupCommand = cmd.Command("backup", cfg => BackupCommand(cfg), true);

            var batchCommand = cmd.Command("batch", cfg => BatchBackupCommand(cfg), true);

            var findCommand = cmd.Command("find", cfg => FindRepositoryCommand(cfg), true);

            cmd.OnExecute(() =>
            {
                return 0;
            });

            return cmd;
        }

        static CommandOption AddHelpOption(CommandLineApplication configuration, bool showInHelpText = false)
        {
            var option = configuration.HelpOption("-h|--help");
            option.ShowInHelpText = showInHelpText;
            return option;
        }

        static CommandOption AddVersionOption(CommandLineApplication configuration)
        {
            return configuration.VersionOption(
                "-v|--version",
                () => typeof(Program).Assembly.GetName().Version.ToString());
        }

        static CommandOption AddLogOption(CommandLineApplication configuration)
        {
            return configuration.Option(
                "-l|--log",
                "Path to log file",
                CommandOptionType.SingleValue);
        }

        static void ExecuteLogOption(CommandOption option)
        {
            var path = string.Empty;

            if (option.HasValue())
            {
                path = option.Value();
            }

            var config = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.ColoredConsole(restrictedToMinimumLevel: LogEventLevel.Information);

            if (!string.IsNullOrWhiteSpace(path))
            {
                path = Path.GetFullPath(path);
                config.WriteTo.File(path, restrictedToMinimumLevel: LogEventLevel.Verbose, fileSizeLimitBytes: 2097152);
            }

            Log.Logger = config.CreateLogger();
        }

        static CommandOption AddOutputOption(CommandLineApplication configuration, string description)
        {
            return configuration.Option(
                "-o|--out",
                description,
                CommandOptionType.SingleValue);
        }

        static string ExecuteOutputOption(CommandOption option)
        {
            if (option.HasValue())
            {
                return option.Value();
            }
            return Environment.CurrentDirectory;
        }

        static CommandOption AddExcludeOption(CommandLineApplication configuration)
        {
            return configuration.Option(
                "-x|--exclude",
                "Exclude directory from search",
                CommandOptionType.MultipleValue);
        }

        static IEnumerable<string> ExecuteExcludeOption(CommandOption option)
        {
            if (option.HasValue())
            {
                return option.Values;
            }
            return new List<string>();
        }

        static CommandArgument AddPathArgument(CommandLineApplication configuration, string description)
        {
            return configuration.Argument("path", description, false);
        }
        
        static CommandLineApplication BackupCommand(CommandLineApplication configuration)
        {
            configuration.Description = "Backup a Git repository";

            AddHelpOption(configuration);
            var pathArgument = AddPathArgument(configuration, "Path to git repository");
            var outputOption = AddOutputOption(configuration, "Path to backup directory");
            var logOption = AddLogOption(configuration);

            configuration.OnExecute(() =>
            {
                ExecuteLogOption(logOption);

                var destination = ExecuteOutputOption(outputOption);

                if (pathArgument.Values.Count > 0)
                {
                    var p = new Program();

                    p.Backup(pathArgument.Value, destination);

                    return 0;
                }
                else
                {
                    configuration.ShowHelp();
                }

                return 0;
            });

            return configuration;
        }

        static CommandLineApplication BatchBackupCommand(CommandLineApplication configuration)
        {
            configuration.Description = "Batch bacup Git repositories";

            AddHelpOption(configuration);
            var pathArgument = AddPathArgument(configuration, "Path to text file contain list of directories");
            var outputOption = AddOutputOption(configuration, "Path to backup directory");
            var logOption = AddLogOption(configuration);

            configuration.OnExecute(() =>
            {
                ExecuteLogOption(logOption);

                var destination = ExecuteOutputOption(outputOption);

                if (pathArgument.Values.Count > 0)
                {
                    var p = new Program();

                    p.BatchBackup(pathArgument.Value, destination);

                    return 0;
                }
                else
                {
                    configuration.ShowHelp();
                }

                return 0;
            });

            return configuration;
        }
        
        static CommandLineApplication FindRepositoryCommand(CommandLineApplication configuration)
        {
            configuration.Description = "Find repositories";

            AddHelpOption(configuration);
            var pathArgument = AddPathArgument(configuration, "Directory to search for Git repository");
            var outputOption = AddOutputOption(configuration, "Path to text file");
            var excludeOption = AddExcludeOption(configuration);
            var logOption = AddLogOption(configuration);

            configuration.OnExecute(() =>
            {
                ExecuteLogOption(logOption);

                var outputFile = ExecuteOutputOption(outputOption);

                if (Directory.Exists(outputFile))
                {
                    outputFile = Path.Combine(outputFile, "find.txt");
                }

                var excludePatterns = ExecuteExcludeOption(excludeOption);

                if (pathArgument.Values.Count > 0)
                {
                    var p = new Program();

                    p.FindRepository(pathArgument.Value, outputFile, excludePatterns);

                    return 0;
                }
                else
                {
                    configuration.ShowHelp();
                }

                return 0;
            });

            return configuration;
        }

        #endregion

        #region Program

        public void FindRepository(string source, string outputFile, IEnumerable<string> excludePatterns)
        {
            var dirs = Directory.EnumerateDirectories(source, ".git", SearchOption.AllDirectories);

            var paths = new List<string>();

            foreach (var d in dirs)
            {
                var exclude = false;
                foreach(var pattern in excludePatterns)
                {
                    if (d.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Log.Verbose("Exclude repository {0}", d);
                        exclude = true;
                        continue;
                    }
                }

                if (exclude) { continue; }

                Log.Information("Found repository in {0}", d);
                paths.Add(d.Substring(0, d.Length - 5));
            }

            using (var writer = new StreamWriter(outputFile, false))
            {
                foreach(var p in paths)
                {
                    writer.WriteLine(p);
                }
            }
            Log.Verbose("Write output to {0}", outputFile);
        }

        public void Backup(string source, string destination)
        {
            var di = new DirectoryInfo(source);
            source = di.FullName;
            var filename = di.Name;

            Backup(source, destination, filename);
        }

        public void Backup(string source, string destination, string filename)
        {
            Log.Information("Backup {0}", source);

            if (!Directory.Exists(Path.Combine(source, ".git")))
            {
                Log.Warning("Cannot find Git repository in directory {0}", source);
                return;
            }

            var di = new DirectoryInfo(destination);
            destination = di.FullName;

            var tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

            var sourceLastCommitHash = GetRepositoryLastCommitHash(source);
            var archiveLastCommitHash = GetArchiveLastCommitHash(destination, filename + ".zip");

            if (string.Equals(sourceLastCommitHash, archiveLastCommitHash))
            {
                Log.Information("Backup not required");
                return;
            }

            CloneRepository(source, tempPath);

            ArchiveRepository(tempPath, destination, filename + ".zip");

            SaveArchiveLastCommit(destination, filename + ".zip", sourceLastCommitHash);

            DeleteTempDirectory(tempPath);
        }

        public void BatchBackup(string source, string destinatino)
        {
            var lines = new List<string>();
            using (var reader = new StreamReader(source))
            {
                while(!reader.EndOfStream)
                {
                    var line = reader.ReadLine();

                    if (string.IsNullOrEmpty(line))
                    {
                        Log.Warning("Not a directory {0}", line);
                        continue;
                    }

                    if (!Directory.Exists(line))
                    {
                        Log.Warning("Cannot find directory {0}", line);
                        continue;
                    }
                    
                    lines.Add(line);
                }
            }

            var nameMap = new Dictionary<string, List<string>>();
            var fullPathMap = new Dictionary<string, string>();

            foreach(var line in lines)
            {
                var di = new DirectoryInfo(line);
                if (fullPathMap.ContainsKey(di.FullName))
                {
                    continue;
                }
                fullPathMap.Add(di.FullName, di.Name);

                var name = di.Name.ToLower();
                if (!nameMap.ContainsKey(name))
                {
                    nameMap.Add(name, new List<string>());
                }

                nameMap[name].Add(di.FullName);
            }

            // remove directories with same name
            foreach(var kvp in nameMap)
            {
                var commonPath = string.Empty;
                if (kvp.Value.Count > 1)
                {
                    Log.Warning("Muliple directory contain name {0}", kvp.Key);
                    foreach(var v in kvp.Value)
                    {
                        fullPathMap.Remove(v);
                        Log.Warning("- Skip backup directory {0}", kvp.Value);
                    }
                }
            }

            foreach(var kvp in fullPathMap)
            {
                Backup(kvp.Key, destinatino, kvp.Value);
            }
        }
        
        private void CloneRepository(string source, string destination)
        {
            Log.Information("Clone repository {0}", source);
            Log.Verbose("Temporary repository is {0}", destination);

            Repository.Clone(source, destination,
                new CloneOptions()
                {
                    Checkout = false,
                    IsBare = true
                });

            Log.Verbose("Update remote branch origin's fetch refspec to +refs/*:refs/*");
            using (var repo = new Repository(destination))
            {
                if (repo.Network.Remotes["origin"] != null)
                {
                    repo.Network.Remotes.Update("origin", x => x.FetchRefSpecs = new string[] { "+refs/*:refs/*" });
                }
            }
        }

        private void ArchiveRepository(string source, string destination, string filename)
        {
            Log.Information("Archive repository to {0}", Path.Combine(destination, filename));

            var backupFilename = filename + ".bak";
            if (File.Exists(Path.Combine(destination, filename)))
            {
                File.Move(Path.Combine(destination, filename), Path.Combine(destination, backupFilename));
            }

            ZipFile.CreateFromDirectory(
                source,
                Path.Combine(destination, filename),
                CompressionLevel.Fastest,
                false);

            if (File.Exists(Path.Combine(destination, backupFilename)))
            {
                File.Delete(Path.Combine(destination, backupFilename));
            }
        }

        private string GetRepositoryLastCommitHash(string source)
        {
            using (var repo = new Repository(source))
            {
                return repo.Commits
                           .OrderByDescending(c => Math.Max(c.Author.When.Ticks, c.Committer.When.Ticks))
                           .FirstOrDefault()?
                           .Id
                           .Sha;
            }
        }

        private string GetArchiveLastCommitHash(string destination, string filename)
        {
            var hash = string.Empty;

            var path = Path.Combine(destination, filename);
            var jsonPath = path + ".json";
            if (File.Exists(path) && File.Exists(jsonPath))
            {
                try
                {
                    Log.Verbose("Checking last commit hash in archive json file {0}", jsonPath);
                    using (var textReader = new StreamReader(jsonPath))
                    using (var jsonReader = new Newtonsoft.Json.JsonTextReader(textReader))
                    {
                        var serializer = new Newtonsoft.Json.JsonSerializer();
                        var info = serializer.Deserialize<ArchiveInfo>(jsonReader);
                        hash = info.LastCommit;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "GetArchiveLastCommit:: unable to read last commit hash from json file {0}", jsonPath);
                }
            }
            else
            {
                Log.Verbose("Archive file {0} or corresponding json file is missing", path);
            }

            return hash;
        }

        private void SaveArchiveLastCommit(string destination, string filename, string hash)
        {
            var path = Path.Combine(destination, filename);
            var jsonPath = path + ".json";
            if (File.Exists(path))
            {
                try
                {
                    Log.Verbose("Checking last commit hash in archive json file {0}", jsonPath);
                    using (var textReader = new StreamWriter(jsonPath, false))
                    using (var jsonReader = new Newtonsoft.Json.JsonTextWriter(textReader))
                    {
                        var serializer = new Newtonsoft.Json.JsonSerializer();
                        serializer.Serialize(jsonReader, new ArchiveInfo()
                        {
                            LastCommit = hash
                        });
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "GetArchiveLastCommit:: unable to write last commit hash to json file");
                }
            }
            else
            {
                Log.Warning("Archive file {0} is missing", path);
            }
        }

        private void DeleteTempDirectory(string path)
        {

            Log.Verbose("Delete temporary repository {0}", path);

            try
            {
                Directory.Delete(path, true);
            }
            catch (Exception ex)
            {
                Log.Warning("DeleteTempDirectory:: Unable to delete temp directory: {0}", ex.Message);
            }
        }

        #endregion

        internal class ArchiveInfo
        {
            public string LastCommit { get; set; }
        }
    }
}
