using System;
using System.Text.Json;
using System.Net.Http;
using Clio.Common;
using CommandLine;

namespace Clio.Command
{
	#region Class: TideInstallGitCommandOptions

	[Verb("tide-install-git", HelpText = "Install Git to the T.I.D.E. environment")]
	public class TideInstallGitCommandOptions : RemoteCommandOptions
	{

	}

	#endregion

	#region Class: TideInstallGitCommand

	public class TideInstallGitCommand : RemoteCommand<TideInstallGitCommandOptions>
	{

		#region Constructors: Public

		public TideInstallGitCommand(IApplicationClient applicationClient, EnvironmentSettings environmentSettings)
			: base(applicationClient, environmentSettings) {
			ServicePath = "/rest/Tide/InstallConsoleGit";
		}

		public TideInstallGitCommand(EnvironmentSettings environmentSettings)
			: base(environmentSettings) {
			ServicePath = "/rest/Tide/InstallConsoleGit";
		}

		public TideInstallGitCommand() {
			ServicePath = "/rest/Tide/InstallConsoleGit";
		}

		#endregion

		#region Properties: Public

		public override HttpMethod HttpMethod => HttpMethod.Get;

		#endregion

		#region Methods: Protected

		protected override void ProceedResponse(string response, TideInstallGitCommandOptions options) {
			if (string.IsNullOrEmpty(response)) {
				Logger.WriteError("Empty response received from server");
				return;
			}
			Logger.WriteInfo("Git installation process completed successfully");
		}

		#endregion

	}

	#endregion
}