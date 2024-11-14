namespace Clio.Command
{
	using System;
	using Clio.Common;
	using Clio.Workspaces;
	using CommandLine;

	#region Class: RestoreWorkspaceOptions

	public class WorkspaceOptions : EnvironmentOptions
	{

		public WorkspaceOptions() {
			IsNugetRestore = true;
			IsCreateSolution = true;
		}

		[Option( "IsNugetRestore", Required = false, HelpText = "True if you need to restore nugget package SDK", Default = true)]
		public bool? IsNugetRestore { get; set; }


		[Option("IsCreateSolution", Required = false, HelpText = "True if you need to create the Solution", Default = true)]
		public bool? IsCreateSolution { get; set; }

	}

	[Verb("restore-workspace", Aliases = new string[] { "restorew", "pullw", "pull-workspace" },
		HelpText = "Restore clio workspace")]
	public class RestoreWorkspaceOptions : WorkspaceOptions
	{

	}

	#endregion

	#region Class: RestoreWorkspaceCommand

	public class RestoreWorkspaceCommand : Command<RestoreWorkspaceOptions>
	{

		#region Fields: Private

		private readonly IWorkspace _workspace;
		private readonly ILogger _logger;

		#endregion

		#region Constructors: Public

		public RestoreWorkspaceCommand(IWorkspace workspace, ILogger logger) {
			workspace.CheckArgumentNull(nameof(workspace));
			_workspace = workspace;
			_logger = logger;
		}

		#endregion

		#region Methods: Public

		public override int Execute(RestoreWorkspaceOptions options) {
			try {
				_workspace.Restore(options);
				_logger.WriteInfo("Done");
				return 0;
			} catch (Exception e) {
				_logger.WriteError(e.Message);
				return 1;
			}
		}

		#endregion

	}

	#endregion

}