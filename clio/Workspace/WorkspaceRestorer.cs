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
		private readonly IWorkspaceSolutionCreator _workspaceSolutionCreator;
		private readonly IPackageDownloader _packageDownloader;
		private readonly ICreatioSdk _creatioSdk;


		#endregion

		#region Constructors: Public

		public WorkspaceRestorer(INuGetManager nugetManager, IWorkspacePathBuilder workspacePathBuilder,
				IEnvironmentScriptCreator environmentScriptCreator, IWorkspaceSolutionCreator workspaceSolutionCreator,
				IPackageDownloader packageDownloader, ICreatioSdk creatioSdk) {
			nugetManager.CheckArgumentNull(nameof(nugetManager));
			workspacePathBuilder.CheckArgumentNull(nameof(workspacePathBuilder));
			environmentScriptCreator.CheckArgumentNull(nameof(environmentScriptCreator));
			workspaceSolutionCreator.CheckArgumentNull(nameof(workspaceSolutionCreator));
			packageDownloader.CheckArgumentNull(nameof(packageDownloader));
			creatioSdk.CheckArgumentNull(nameof(creatioSdk));
			_nugetManager = nugetManager;
			_workspacePathBuilder = workspacePathBuilder;
			_environmentScriptCreator = environmentScriptCreator;
			_workspaceSolutionCreator = workspaceSolutionCreator;
			_packageDownloader = packageDownloader;
			_creatioSdk = creatioSdk;
		}

		#endregion

		#region Methods: Private

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
			_workspaceSolutionCreator.Create();
		}

		private void CreateEnvironmentScript(Version nugetCreatioSdkVersion) {
			_environmentScriptCreator.Create(nugetCreatioSdkVersion);
		}

		#endregion


		#region Methods: Public

		public void Restore(WorkspaceSettings workspaceSettings, EnvironmentSettings environmentSettings) {
			Version creatioSdkVersion = _creatioSdk.FindSdkVersion(workspaceSettings.ApplicationVersion);
			_packageDownloader.DownloadPackages(workspaceSettings.Packages, environmentSettings,
				_workspacePathBuilder.PackagesFolderPath);
			RestoreNugetCreatioSdk(creatioSdkVersion);
			CreateSolution();
			CreateEnvironmentScript(creatioSdkVersion);
		}

		#endregion

	}

	#endregion

}