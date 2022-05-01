namespace Clio.Workspace
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using Clio.Common;
	using Clio.Project.NuGet;

	#region Class: WorkspaceSettings

	public class WorkspaceRestorer : IWorkspaceRestorer
	{

		#region Fields: Private

		private readonly INuGetManager _nugetManager;
		private readonly IWorkspacePathBuilder _workspacePathBuilder;
		private readonly IFileSystem _fileSystem;
		private readonly IOpenSolutionCreator _openSolutionCreator;
		private readonly ISolutionCreator _solutionCreator;

		#endregion

		#region Constructors: Public

		public WorkspaceRestorer(INuGetManager nugetManager, IWorkspacePathBuilder workspacePathBuilder,
				IOpenSolutionCreator openSolutionCreator, ISolutionCreator solutionCreator, IFileSystem fileSystem) {
			nugetManager.CheckArgumentNull(nameof(nugetManager));
			workspacePathBuilder.CheckArgumentNull(nameof(workspacePathBuilder));
			openSolutionCreator.CheckArgumentNull(nameof(openSolutionCreator));
			solutionCreator.CheckArgumentNull(nameof(solutionCreator));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			_nugetManager = nugetManager;
			_workspacePathBuilder = workspacePathBuilder;
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

		private IEnumerable<SolutionProject> FindSolutionProjects(string rootPath) {
			string packagesPath = _workspacePathBuilder.BuildPackagesDirectoryPath(rootPath);
			var packagesNames = GetPackagesNames(packagesPath);
			IList<SolutionProject> solutionProjects = new List<SolutionProject>();
			foreach (string packageName in packagesNames) {
				string standaloneProjectPath = BuildStandaloneProjectPath(packagesPath, packageName);
				if (!File.Exists(standaloneProjectPath)) {
					continue;
				}
				string relativeStandaloneProjectPath = Path.Combine("..", 
					_fileSystem.ConvertToRelativePath(standaloneProjectPath, rootPath));
				SolutionProject solutionProject = new SolutionProject(packageName, relativeStandaloneProjectPath);
				solutionProjects.Add(solutionProject);
			}
			return solutionProjects;
		}

		private void RestoreNugetCreatioSdk(string rootPath, Version nugetCreatioSdkVersion) {
			const string nugetSourceUrl = "https://api.nuget.org/v3/index.json";
			const string packageName = "CreatioSDK";
			NugetPackageFullName nugetPackageFullName = new NugetPackageFullName() {
				Name = packageName,
				Version = nugetCreatioSdkVersion.ToString()
			};
			string baseNugetLibPath = _workspacePathBuilder.BuildNugetFolderPath(rootPath);;
			_nugetManager.RestoreToNugetFileStorage(nugetPackageFullName, nugetSourceUrl, baseNugetLibPath);
		}

		private void CreateSolution(string rootPath) {
			string clioDirectoryPath = _workspacePathBuilder.BuildClioDirectoryPath(rootPath);
			IEnumerable<SolutionProject> solutionProjects = FindSolutionProjects(rootPath);
			string solutionPath = _workspacePathBuilder.BuildSolutionPath(clioDirectoryPath);
			_solutionCreator.Create(solutionPath, solutionProjects);
		}

		private void CreateOpenSolutionCmd(string rootPath, Version nugetCreatioSdkVersion) {
			_openSolutionCreator.Create(rootPath, nugetCreatioSdkVersion);
		}

		#endregion


		#region Methods: Public

		public void Restore(string rootPath, Version nugetCreatioSdkVersion) {
			RestoreNugetCreatioSdk(rootPath, nugetCreatioSdkVersion);
			CreateSolution(rootPath);
			CreateOpenSolutionCmd(rootPath, nugetCreatioSdkVersion);
		}

		#endregion

	}

	#endregion

}