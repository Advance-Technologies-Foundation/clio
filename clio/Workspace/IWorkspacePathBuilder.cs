using System;

namespace Clio.Workspace
{

	#region Interface: IWorkspacePathBuilder

	public interface IWorkspacePathBuilder
	{

		#region Properties: Public

		string RootPath { get; }
		bool IsWorkspace { get; }
		string ClioDirectoryPath { get; }
		string WorkspaceSettingsPath { get; }
		string WorkspaceEnvironmentSettingsPath { get; }
		string PackagesFolderPath { get; }
		string ProjectsFolderPath { get; }
		string SolutionFolderPath { get; }
		string SolutionPath { get; }
		string NugetFolderPath { get; }
		string TasksFolderPath { get; }
		string ApplicationFolderPath { get; }
		string CoreBinFolderPath { get; } 
		string LibFolderPath { get; }
		string ConfigurationBinFolderPath { get; }

		#endregion

		#region Methods: Public

		string BuildPackagePath(string packageName);
		string BuildFrameworkCreatioSdkPath(Version nugetVersion);
		string BuildCoreCreatioSdkPath(Version nugetVersion);
		string BuildRelativePathRegardingPackageProjectPath(string destinationPath);

		#endregion

	}

	#endregion

}