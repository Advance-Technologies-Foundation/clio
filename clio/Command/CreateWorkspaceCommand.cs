using System.Collections.Generic;

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

		public override int Execute(CreateOpenProjectFileOptions options) {
			try
			{
				string rootPath = _fileSystem.GetCurrentDirectory();
				IEnumerable<SolutionProject> solutionProject = FindSolutionProjects(rootPath);
				// const string nugetSourceUrl = "https://api.nuget.org/v3/index.json";
				// const string packageName = "CreatioSDK";
				// const string nugetFolderName = ".nuget";
				// const string solutionName = "CreatioPackages.sln";
				// const string packagesFolderName = "packages";
				// string nugetCreatioSdkVersion = options.Version;
				// NugetPackageFullName nugetPackageFullName = new NugetPackageFullName() {
				// 	Name = packageName,
				// 	Version = nugetCreatioSdkVersion
				// };
				// SolutionProject[] solutionProjects = new[] {
				// 	new SolutionProject("A1", @"packages\A1\Files\A1.csproj"),
				// 	new SolutionProject("A2", @"packages\A2\Files\A2.csproj"),
				// 	new SolutionProject("A3", @"packages\A3\Files\A3.csproj"),
				// };
				//string solutionPath = Path.Combine(_fileSystem.GetCurrentDirectory(), solutionName);
				// _solutionCreator.Create(solutionPath, solutionProjects);
				// string baseNugetLibPath = Path.Combine(_fileSystem.GetCurrentDirectory(), nugetFolderName);
				// _nugetManager.RestoreToNugetFileStorage(nugetPackageFullName, nugetSourceUrl, baseNugetLibPath);
				// _openSolutionCreator.Create(solutionName, nugetFolderName, nugetCreatioSdkVersion);
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