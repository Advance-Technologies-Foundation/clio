namespace Clio.Workspace
{
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using Clio.Common;
	using Clio.Project;
	using Clio.Project.NuGet;

	#region Class: WorkspaceSettings

	public class WorkspaceRestorer : IWorkspaceRestorer
	{

		#region Constants: Private

		private const string PackagesFolderName = "packages";
		private const string NugetFolderName = ".nuget";
		private const string SolutionName = "CreatioPackages.sln";

		#endregion

		#region Fields: Private

		private readonly INuGetManager _nugetManager;
		private readonly IFileSystem _fileSystem;
		private readonly IOpenSolutionCreator _openSolutionCreator;
		private readonly ISolutionCreator _solutionCreator;

		#endregion

		#region Constructors: Public

		public WorkspaceRestorer(INuGetManager nugetManager, IOpenSolutionCreator openSolutionCreator,
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

		private static string BuildStandaloneProjectPath(string packagesPath, string packageName) =>
			Path.Combine(packagesPath, packageName, "Files", $"{packageName}.csproj");

		private static IEnumerable<string> GetPackagesNames(string packagesPath) {
			DirectoryInfo packagesDirectoryInfo = new DirectoryInfo(packagesPath);
			return packagesDirectoryInfo
				.GetDirectories("*.*", SearchOption.TopDirectoryOnly)
				.Select(packageDirectoryInfo => packageDirectoryInfo.Name);
		}

		private string ConvertToRelativePath(string path, string rootDirectoryPath) =>
			_fileSystem.ConvertToRelativePath(path, rootDirectoryPath)
				.TrimStart(Path.DirectorySeparatorChar);

		private IEnumerable<SolutionProject> FindSolutionProjects(string rootPath) {
			string packagesPath = Path.Combine(rootPath, PackagesFolderName);
			var packagesNames = GetPackagesNames(packagesPath);
			IList<SolutionProject> solutionProjects = new List<SolutionProject>();
			foreach (string packageName in packagesNames) {
				string standaloneProjectPath = BuildStandaloneProjectPath(packagesPath, packageName);
				if (!File.Exists(standaloneProjectPath)) {
					continue;
				}
				string relativeStandaloneProjectPath = ConvertToRelativePath(standaloneProjectPath, rootPath);
				SolutionProject solutionProject = new SolutionProject(packageName, relativeStandaloneProjectPath);
				solutionProjects.Add(solutionProject);
			}
			return solutionProjects;
		}

		private void RestoreNugetCreatioSdk(string rootPath, string nugetCreatioSdkVersion) {
			const string nugetSourceUrl = "https://api.nuget.org/v3/index.json";
			const string packageName = "CreatioSDK";
			NugetPackageFullName nugetPackageFullName = new NugetPackageFullName() {
				Name = packageName,
				Version = nugetCreatioSdkVersion
			};
			string baseNugetLibPath = Path.Combine(rootPath, NugetFolderName);
			_nugetManager.RestoreToNugetFileStorage(nugetPackageFullName, nugetSourceUrl, baseNugetLibPath);
		}

		private void CreateSolution(string rootPath) {
			IEnumerable<SolutionProject> solutionProjects = FindSolutionProjects(rootPath);
			string solutionPath = Path.Combine(rootPath, SolutionName);
			_solutionCreator.Create(solutionPath, solutionProjects);
		}

		private void CreateOpenSolutionCmd(string rootPath, string nugetCreatioSdkVersion) {
			_openSolutionCreator.Create(rootPath, SolutionName, NugetFolderName, nugetCreatioSdkVersion);
		}

		#endregion


		#region Methods: Public

		public void Restore(string nugetCreatioSdkVersion) {
			string rootPath = _fileSystem.GetCurrentDirectory();
			RestoreNugetCreatioSdk(rootPath, nugetCreatioSdkVersion);
			CreateSolution(rootPath);
			CreateOpenSolutionCmd(rootPath, nugetCreatioSdkVersion);
		}

		#endregion

	}

	#endregion

}