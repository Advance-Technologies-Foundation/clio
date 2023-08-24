using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

		public GitSyncCommand(EnvironmentSettings settings, IProcessExecutor processExecutor) {
			_settings = settings;
			_processExecutor = processExecutor;
		}

		public override int Execute(GitSyncOptions options) {
			
			var ext = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd" : "sh";
			
			var fileName = options.Direction.ToLower().IndexOf("git") < options.Direction.ToLower().IndexOf("env") ?"git-to-env" : "env-to-git";
			
            var batPath  = Path.Join(_settings.WorkspacePathes,"tasks",$"{fileName}.{ext}");
			
			
            var result = _processExecutor.Execute(batPath, 
				options.Environment, true, _settings.WorkspacePathes, true);

			Console.WriteLine(result);
			return 0;
		}

	}
}
