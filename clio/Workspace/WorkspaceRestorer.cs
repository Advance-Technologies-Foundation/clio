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
		private readonly IEnvironmentScriptCreator _environmentScriptCreator;
		private readonly ISolutionCreator _solutionCreator;
		private readonly IStandalonePackageFileManager _standalonePackageFileManager;
		private readonly IPackageDownloader _packageDownloader;
		private readonly ICreatioSdk _creatioSdk;


		#endregion

		#region Constructors: Public

		public WorkspaceRestorer(INuGetManager nugetManager, IWorkspacePathBuilder workspacePathBuilder,
				IEnvironmentScriptCreator environmentScriptCreator, ISolutionCreator solutionCreator,
				IStandalonePackageFileManager standalonePackageFileManager, IPackageDownloader packageDownloader,
				ICreatioSdk creatioSdk) {
			nugetManager.CheckArgumentNull(nameof(nugetManager));
			workspacePathBuilder.CheckArgumentNull(nameof(workspacePathBuilder));
			environmentScriptCreator.CheckArgumentNull(nameof(environmentScriptCreator));
			solutionCreator.CheckArgumentNull(nameof(solutionCreator));
			standalonePackageFileManager.CheckArgumentNull(nameof(standalonePackageFileManager));
			packageDownloader.CheckArgumentNull(nameof(packageDownloader));
			creatioSdk.CheckArgumentNull(nameof(creatioSdk));
			_nugetManager = nugetManager;
			_workspacePathBuilder = workspacePathBuilder;
			_environmentScriptCreator = environmentScriptCreator;
			_solutionCreator = solutionCreator;
			_standalonePackageFileManager = standalonePackageFileManager;
			_packageDownloader = packageDownloader;
			_creatioSdk = creatioSdk;
		}

		#endregion

		#region Methods: Private

		private IEnumerable<SolutionProject> FindSolutionProjects() {
			IList<SolutionProject> solutionProjects = new List<SolutionProject>();
			IEnumerable<StandalonePackageProject> standalonePackageProjects = _standalonePackageFileManager
				.FindStandalonePackageProjects(_workspacePathBuilder.PackagesDirectoryPath);
			string solutionFolderPath = _workspacePathBuilder.SolutionFolderPath;
			foreach (StandalonePackageProject standalonePackageProject in standalonePackageProjects) {
				string relativeStandaloneProjectPath =
					Path.GetRelativePath(solutionFolderPath, standalonePackageProject.Path);
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

		private void CreateEnvironmentScript(Version nugetCreatioSdkVersion) {
			_environmentScriptCreator.Create(nugetCreatioSdkVersion);
		}

		#endregion


		#region Methods: Public

		public void Restore(WorkspaceSettings workspaceSettings, EnvironmentSettings environmentSettings) {
			Version creatioSdkVersion = _creatioSdk.FindSdkVersion(workspaceSettings.ApplicationVersion);
			_packageDownloader.DownloadPackages(workspaceSettings.Packages, environmentSettings,
				_workspacePathBuilder.PackagesDirectoryPath);
			RestoreNugetCreatioSdk(creatioSdkVersion);
			CreateSolution();
			CreateEnvironmentScript(creatioSdkVersion);
		}

		#endregion

	}

	#endregion

}