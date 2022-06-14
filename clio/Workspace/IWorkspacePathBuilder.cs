using System;

namespace Clio.Workspace
{

	#region Interface: IWorkspacePathBuilder

	public interface IWorkspacePathBuilder
	{

		#region Properties: Public

		string RootPath { get; }
		string ClioDirectoryPath { get; }
		string WorkspaceSettingsPath { get; }
		string PackagesFolderPath { get; }
		string SolutionFolderPath { get; }
		string SolutionPath { get; }
		string NugetFolderPath { get; }
		string TasksFolderPath { get; }

		#endregion

		#region Methods: Public

		string BuildFrameworkCreatioSdkPath(Version nugetVersion);
		string BuildCoreCreatioSdkPath(Version nugetVersion);
		string BuildRelativePathRegardingPackageProjectPath(string destinationPath);

		#endregion

	}

	#endregion

}