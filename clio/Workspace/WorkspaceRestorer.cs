namespace Clio.Workspace
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using Clio.Common;
	using Clio.Package;
	using Clio.Project.NuGet;

	#region Class: WorkspaceSettings

	public class WorkspaceRestorer : IWorkspaceRestorer
	{

		#region Fields: Private

		private readonly INuGetManager _nugetManager;
		private readonly IWorkspacePathBuilder _workspacePathBuilder;
		private readonly IOpenSolutionCreator _openSolutionCreator;
		private readonly ISolutionCreator _solutionCreator;
		private readonly IStandalonePackageFileManager _standalonePackageFileManager;
		private readonly IFileSystem _fileSystem;

		#endregion

		#region Constructors: Public

		public WorkspaceRestorer(INuGetManager nugetManager, IWorkspacePathBuilder workspacePathBuilder,
				IOpenSolutionCreator openSolutionCreator, ISolutionCreator solutionCreator, 
				IStandalonePackageFileManager standalonePackageFileManager, IFileSystem fileSystem) {
			nugetManager.CheckArgumentNull(nameof(nugetManager));
			workspacePathBuilder.CheckArgumentNull(nameof(workspacePathBuilder));
			openSolutionCreator.CheckArgumentNull(nameof(openSolutionCreator));
			solutionCreator.CheckArgumentNull(nameof(solutionCreator));
			standalonePackageFileManager.CheckArgumentNull(nameof(standalonePackageFileManager));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			_nugetManager = nugetManager;
			_workspacePathBuilder = workspacePathBuilder;
			_openSolutionCreator = openSolutionCreator;
			_solutionCreator = solutionCreator;
			_standalonePackageFileManager = standalonePackageFileManager;
			_fileSystem = fileSystem;
		}

		#endregion

		#region Methods: Private

		private IEnumerable<SolutionProject> FindSolutionProjects() {
			IList<SolutionProject> solutionProjects = new List<SolutionProject>();
			IEnumerable<StandalonePackageProject> standalonePackageProjects = _standalonePackageFileManager
				.FindStandalonePackageProjects(_workspacePathBuilder.PackagesDirectoryPath);
			foreach (StandalonePackageProject standalonePackageProject in standalonePackageProjects) {
				string relativeStandaloneProjectPath = Path.Combine("..", 
					_fileSystem.ConvertToRelativePath(standalonePackageProject.Path, 
						_workspacePathBuilder.RootPath));
				SolutionProject solutionProject = 
					new SolutionProject(standalonePackageProject.PackageName, relativeStandaloneProjectPath);
				solutionProjects.Add(solutionProject);
			}
			return solutionProjects;
		}

		private void RestoreNugetCreatioSdk(Version nugetCreatioSdkVersion) {
			const string nugetSourceUrl = "https://api.nuget.org/v3/index.json";
			const string packageName = "CreatioSDK";
			NugetPackageFullName nugetPackageFullName = new NugetPackageFullName() {
				Name = packageName,
				Version = nugetCreatioSdkVersion.ToString()
			};
			_nugetManager.RestoreToNugetFileStorage(nugetPackageFullName, nugetSourceUrl, 
				_workspacePathBuilder.NugetFolderPath);
		}

		private void CreateSolution() {
			IEnumerable<SolutionProject> solutionProjects = FindSolutionProjects();
			_solutionCreator.Create(_workspacePathBuilder.SolutionPath, solutionProjects);
		}

		private void CreateOpenSolutionCmd(Version nugetCreatioSdkVersion) {
			_openSolutionCreator.Create(nugetCreatioSdkVersion);
		}

		#endregion


		#region Methods: Public

		public void Restore(Version nugetCreatioSdkVersion) {
			RestoreNugetCreatioSdk(nugetCreatioSdkVersion);
			CreateSolution();
			CreateOpenSolutionCmd(nugetCreatioSdkVersion);
		}

		#endregion

	}

	#endregion

}