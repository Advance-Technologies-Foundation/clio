namespace Clio.Command
{
	using System;
	using Clio.Common;
	using Clio.Workspaces;
	using CommandLine;

	#region Class: DownloadLibsCommandOptions

	[Verb("download-configuration", Aliases = new [] { "dconf" },
		HelpText = "Download libraries from web-application")]
	public class DownloadConfigurationCommandOptions : EnvironmentOptions
	{
	}

	#endregion

	#region Class: DownloadLibsCommand

	public class DownloadConfigurationCommand : Command<DownloadConfigurationCommandOptions>
	{
		
		#region Fields: Private

		private readonly IApplicationDownloader _applicationDownloader;
		private readonly IWorkspace _workspace;
		private readonly ILogger _logger;

		#endregion

		#region Constructors: Public

		public DownloadConfigurationCommand(IApplicationDownloader applicationDownloader, IWorkspace workspace, ILogger logger) {
			applicationDownloader.CheckArgumentNull(nameof(applicationDownloader));
			workspace.CheckArgumentNull(nameof(workspace));
			_applicationDownloader = applicationDownloader;
			_workspace = workspace;
			_logger = logger;
		}
		#endregion

		
		#region Methods: Public

		public override int Execute(DownloadConfigurationCommandOptions options) {
			_applicationDownloader.Download(_workspace.WorkspaceSettings.Packages);
			_logger.WriteLine("Done");
			return 0;
		}

		#endregion

	}

	#endregion

}