using System;

namespace Clio.Workspace
{

	#region Interface: IWorkspacePathBuilder

	public interface IWorkspacePathBuilder
	{

		#region Methods: Public

		string BuildClioDirectoryPath(string rootPath);

		string BuildWorkspaceSettingsPath(string rootPath);

		string BuildPackagesDirectoryPath(string rootPath);

		string BuildSolutionPath(string rootPath);

		string BuildNugetFolderPath(string rootPath);

		public string BuildFrameworkCreatioSdkPath(string rootPath, Version nugetVersion);

		public string BuildCoreCreatioSdkPath(string rootPath, Version nugetVersion);


		#endregion

	}

	#endregion

}