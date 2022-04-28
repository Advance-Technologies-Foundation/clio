using System.IO;
using Clio.Workspace;

namespace Clio.Command
{
	using System;
	using Clio.Common;
	using Clio.Project.NuGet;
	using CommandLine;

	#region Class: CreateOpenProjectFileOptions

	[Verb("create-workspace", Aliases = new string[] { "createw" }, HelpText = "Create open project cmd file")]
	public class CreateWorkspaceCommandOptions : EnvironmentOptions
	{

		
	}

	#endregion

	#region Class: CreateOpenProjectFileOptions

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
			try
			{
				_workspace.Create();
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