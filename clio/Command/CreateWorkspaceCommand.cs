namespace Clio.Command
{
	using System;
	using System.IO;
	using Clio.Common;
	using Clio.Project;
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

		private readonly INuGetManager _nugetManager;
		private readonly IFileSystem _fileSystem;
		private readonly IOpenSolutionCreator _openSolutionCreator;
		private readonly ISolutionCreator _solutionCreator;

		#endregion

		#region Constructors: Public

		public CreateWorkspaceCommand(INuGetManager nugetManager, IOpenSolutionCreator openSolutionCreator,
				ISolutionCreator solutionCreator, IFileSystem fileSystem) {
			nugetManager.CheckArgumentNull(nameof(nugetManager));
			openSolutionCreator.CheckArgumentNull(nameof(openSolutionCreator));
			solutionCreator.CheckArgumentNull(nameof(solutionCreator));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			_nugetManager = nugetManager;
			_openSolutionCreator = openSolutionCreator;
			_solutionCreator = solutionCreator;
			_fileSystem = fileSystem;
		}

		#endregion

		#region Methods: Private

		#endregion

		#region Methods: Public

		public override int Execute(CreateWorkspaceCommandOptions options) {
			try
			{

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