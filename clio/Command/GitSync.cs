using System.IO;
using System.Runtime.InteropServices;
using Clio.Common;
using CommandLine;

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

		public GitSyncCommand(EnvironmentSettings settings, IProcessExecutor processExecutor, ILogger logger) {
			_settings = settings;
			_processExecutor = processExecutor;
			_logger = logger;
		}

		public override int Execute(GitSyncOptions options) {
			
			var ext = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd" : "sh";
			
			var fileName = options.Direction.ToLower().IndexOf("git") < options.Direction.ToLower().IndexOf("env") ?"git-to-env" : "env-to-git";
			
            var batPath  = Path.Join(_settings.WorkspacePathes,"tasks",$"{fileName}.{ext}");
			
			var result = _processExecutor.Execute(batPath, 
				options.Environment, true, _settings.WorkspacePathes, true);
			
			_logger.WriteInfo(result);
			
			return 0;
		}

	}
}
