using System.Runtime.InteropServices;
using Clio.Common;
using CommandLine;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command
{
	[Verb("git-sync", Aliases = new string[] { "sync" }, HelpText = "Syncs environment with Git repository")]
	public class GitSyncOptions : EnvironmentNameOptions
	{

		[Option("Direction", Required = true, HelpText = "Sets sync direction")]
		public string Direction {
			get; set;
		}
		
	}

	public class GitSyncCommand : Command<GitSyncOptions>
	{

		private readonly EnvironmentSettings _settings;
		private readonly IProcessExecutor _processExecutor;
		private readonly ILogger _logger;
		private readonly IFileSystem _fileSystem;

		public GitSyncCommand(EnvironmentSettings settings, IProcessExecutor processExecutor, ILogger logger,
			IFileSystem fileSystem) {
			_settings = settings;
			_processExecutor = processExecutor;
			_logger = logger;
			_fileSystem = fileSystem;
		}

		public override int Execute(GitSyncOptions options) {

			var ext = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd" : "sh";

			var fileName = options.Direction.ToLower().IndexOf("git") < options.Direction.ToLower().IndexOf("env") ?"git-to-env" : "env-to-git";

            var batPath  = _fileSystem.Path.Join(_settings.WorkspacePathes,"tasks",$"{fileName}.{ext}");
			
			var result = _processExecutor.Execute(batPath, 
				options.Environment, true, _settings.WorkspacePathes, true);
			
			_logger.WriteInfo(result);
			
			return 0;
		}

	}
}
