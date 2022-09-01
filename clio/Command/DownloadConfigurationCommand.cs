namespace Clio.Command
{
	using System;
	using Clio.Common;
	using Clio.Workspace;
	using CommandLine;

	#region Class: DownloadLibsCommandOptions

	[Verb("download-configuration", Aliases = new string[] { "dconf" },
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

		#endregion

		#region Constructors: Public

		public DownloadConfigurationCommand(IApplicationDownloader applicationDownloader, IWorkspace workspace) {
			applicationDownloader.CheckArgumentNull(nameof(applicationDownloader));
			workspace.CheckArgumentNull(nameof(workspace));
			_applicationDownloader = applicationDownloader;
			_workspace = workspace;
		}

		#endregion

		#region Methods: Private

		#endregion

		#region Methods: Public

		public override int Execute(DownloadConfigurationCommandOptions options) {
			try {
				_applicationDownloader.Download(_workspace.WorkspaceSettings.Packages);
				Console.WriteLine("Done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e.Message);
				return 1;
			}
		}

		#endregion


	}

	#endregion

}