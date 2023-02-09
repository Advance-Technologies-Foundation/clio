namespace Clio.Command
{
	using System;
	using Clio.Common;
	using Clio.Workspace;
	using CommandLine;

	#region Class: CreateWorkspaceCommandOptions

	[Verb("create-workspace", Aliases = new string[] { "createw" }, HelpText = "Create open project cmd file")]
	public class CreateWorkspaceCommandOptions : EnvironmentOptions
	{
	}

	#endregion

	#region Class: CreateWorkspaceCommand

	public class CreateWorkspaceCommand : Command<CreateWorkspaceCommandOptions>
	{

		#region Fields: Private

		private readonly IWorkspace _workspace;

		#endregion

		#region Constructors: Public

		public CreateWorkspaceCommand(IWorkspace workspace) {
			workspace.CheckArgumentNull(nameof(workspace));
			_workspace = workspace;
		}

		#endregion

		#region Methods: Private

		#endregion

		#region Methods: Public

		public override int Execute(CreateWorkspaceCommandOptions options) {
			try {
				if (options.Environment == null) {
					_workspace.Create(options.Environment);
				} else {
					_workspace.Create(options.Environment, true);
					_workspace.Restore();
				}
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