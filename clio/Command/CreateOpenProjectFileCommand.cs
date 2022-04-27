using System.Collections.Generic;
using System.Linq;

namespace Clio.Command
{
	using System;
	using System.IO;
	using Clio.Common;
	using Clio.Project;
	using Clio.Project.NuGet;
	using CommandLine;

	#region Class: CreateOpenProjectFileOptions

	[Verb("create-open-project-file", Aliases = new string[] { "open" }, HelpText = "Create open project cmd file")]
	public class CreateOpenProjectFileOptions : EnvironmentOptions
	{

		#region Properties: Public

		[Value(0, MetaName = "PackagePath", Required = true, HelpText = "Path of package folder")]
		public string PackagePath { get; set; }

		[Option('v', "Version", Required = false, HelpText = "Version application", 
			Default = PackageVersion.LastVersion)]
		public string Version { get; set; }

		#endregion

	}

	#endregion

	#region Class: CreateOpenProjectFileOptions

	public class CreateOpenProjectFileCommand : Command<CreateOpenProjectFileOptions>
	{
		#region Constants: Private

		private const string PackagesFolderName = "packages";

		#endregion

		#region Fields: Private

		private readonly INuGetManager _nugetManager;
		private readonly IFileSystem _fileSystem;
		private readonly IOpenSolutionCreator _openSolutionCreator;
		private readonly ISolutionCreator _solutionCreator;

		#endregion

		#region Constructors: Public

		public CreateOpenProjectFileCommand(INuGetManager nugetManager, IOpenSolutionCreator openSolutionCreator,
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

		private string BuildStandaloneProjectPath(string packagesPath, string packageName) =>
			Path.Combine(packagesPath, PackagesFolderName, packageName, "Files",
				$"{packageName}.csproj");

		private bool ExistsStandaloneProject(string packagesPath, string packageName) =>
			File.Exists(BuildStandaloneProjectPath(packagesPath, packageName));

		private IEnumerable<SolutionProject> FindSolutionProjects(string rootPath) {
			string packagesPath = Path.Combine(rootPath, PackagesFolderName);
			DirectoryInfo packagesDirectoryInfo = new DirectoryInfo(packagesPath);
			var packagesNames = packagesDirectoryInfo
				.GetDirectories("*.*", SearchOption.TopDirectoryOnly)
				.Select(packageDirectoryInfo => packageDirectoryInfo.Name);
			IList<SolutionProject> solutionProjects = new List<SolutionProject>();
			foreach (string packageName in packagesNames) {
				string standaloneProjectPath = BuildStandaloneProjectPath(packagesPath, packageName);
				if (!File.Exists(standaloneProjectPath)) {
					continue;
				}
				string relativeStandaloneProjectPath =
					_fileSystem.ConvertToRelativePath(standaloneProjectPath, rootPath);
				SolutionProject solutionProject = new SolutionProject(packageName, relativeStandaloneProjectPath);
				solutionProjects.Add(solutionProject);
			}
			return solutionProjects;
		}

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