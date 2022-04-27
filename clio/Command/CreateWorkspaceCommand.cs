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
		#region Constants: Private

		

		#endregion

		#region Fields: Private

		private readonly IFileSystem _fileSystem;

		#endregion

		#region Constructors: Public

		public CreateWorkspaceCommand(IFileSystem fileSystem) {
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			_fileSystem = fileSystem;
		}

		#endregion

		#region Methods: Private

		#endregion

		#region Methods: Public

		public override int Execute(CreateWorkspaceCommandOptions options) {
			try
			{
				string rootPath = _fileSystem.GetCurrentDirectory();
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