namespace Clio.Command
{
	using System;
	using Clio.Common;
	using Clio.Workspace;
	using CommandLine;

	#region Class: RestoreWorkspaceOptions

	[Verb("restore-workspace", Aliases = new string[] { "restorew" }, HelpText = "Restore clio workspace")]
	public class RestoreWorkspaceOptions : EnvironmentOptions
	{

		#region Properties: Public

		public string WorkspaceEnvironmentName { get; set; }

		#endregion

	}

	#endregion

	#region Class: RestoreWorkspaceCommand

	public class RestoreWorkspaceCommand : Command<RestoreWorkspaceOptions>
	{

		#region Fields: Private

		private readonly IWorkspace _workspace;

		#endregion

		#region Constructors: Public

		public RestoreWorkspaceCommand(IWorkspace workspace) {
			workspace.CheckArgumentNull(nameof(workspace));
			_workspace = workspace;
		}

		#endregion

		#region Methods: Public

		public override int Execute(RestoreWorkspaceOptions options) {
			try {
				_workspace.Restore(options.WorkspaceEnvironmentName);
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