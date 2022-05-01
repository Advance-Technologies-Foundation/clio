using System;
using System.IO;

namespace Clio.Workspace
{

	#region Class: WorkspacePathBuilder

	public class WorkspacePathBuilder : IWorkspacePathBuilder
	{

		#region Constants: Private

		private const string PackagesFolderName = "packages";
		private const string ClioDirectoryName = ".clio";
		private const string WorkspaceSettingsJson = "workspaceSettings.json";
		private const string SolutionName = "CreatioPackages.sln";
		private const string NugetFolderName = ".nuget";

		#endregion

		#region Methods: Public

		public string BuildClioDirectoryPath(string rootPath) => 
			Path.Combine(rootPath, ClioDirectoryName);

		public string BuildWorkspaceSettingsPath(string rootPath) => 
			Path.Combine(BuildClioDirectoryPath(rootPath), WorkspaceSettingsJson);

		public string BuildPackagesDirectoryPath(string rootPath) => 
			Path.Combine(rootPath, PackagesFolderName);

		public string BuildSolutionPath(string rootPath) =>
			Path.Combine(rootPath, SolutionName);

		public string BuildNugetFolderPath(string rootPath) => 
			Path.Combine(rootPath, NugetFolderName);

		public string BuildFrameworkCreatioSdkPath(string rootPath, Version nugetVersion) => 
			Path.Combine(BuildNugetFolderPath(rootPath), "creatiosdk", nugetVersion.ToString(), "lib", 
				"net40");

		public string BuildCoreCreatioSdkPath(string rootPath, Version nugetVersion) => 
			Path.Combine(BuildNugetFolderPath(rootPath), "creatiosdk", nugetVersion.ToString(), "lib", 
				"netstandard2.0");


		#endregion

	}

	#endregion

}