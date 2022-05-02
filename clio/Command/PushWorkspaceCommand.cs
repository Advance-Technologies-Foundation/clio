namespace Clio.Command
{
	using System;
	using Clio.Common;
	using Clio.Workspace;
	using CommandLine;

	#region Class: PushWorkspaceCommandOptions

	[Verb("push-workspace", Aliases = new string[] { "pushw" }, HelpText = "Push workspace to selected environment")]
	public class PushWorkspaceCommandOptions : EnvironmentOptions
	{
	}

	#endregion

	#region Class: PushWorkspaceCommand

	public class PushWorkspaceCommand : Command<PushWorkspaceCommandOptions>
	{

		#region Fields: Private

		private readonly IWorkspace _workspace;

		#endregion

		#region Constructors: Public

		public PushWorkspaceCommand(IWorkspace workspace) {
			workspace.CheckArgumentNull(nameof(workspace));
			_workspace = workspace;
		}

		#endregion

		#region Methods: Public

		public override int Execute(PushWorkspaceCommandOptions options) {
			try
			{
				_workspace.Install();
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