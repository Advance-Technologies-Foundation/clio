using System;

namespace Clio.Workspaces
{

	#region Interface: IWorkspacePathBuilder

	public interface IWorkspacePathBuilder
	{

		#region Properties: Public
		
		string RootPath { get; set; }
		bool IsWorkspace { get; }
		string ClioDirectoryPath { get; }
		string WorkspaceSettingsPath { get; }
		string WorkspaceEnvironmentSettingsPath { get; }
		string PackagesFolderPath { get; }
		string ProjectsFolderPath { get; }
		string ProjectsTestsFolderPath { get; }
		
		string SolutionFolderPath { get; }
		string SolutionPath { get; }
		string MainSolutionPath { get; }
		string MainSolutionFolderPath { get; }
		string NugetFolderPath { get; }
		string TasksFolderPath { get; }
		string ApplicationFolderPath { get; }
		string CoreBinFolderPath { get; } 
		string LibFolderPath { get; }
		string ConfigurationBinFolderPath { get; }

		/// <summary>
		/// Path to the external packages folder (sibling "packages" folder next to workspace root).
		/// </summary>
		string ExternalPackagesFolderPath { get; }

		#endregion

		#region Methods: Public

		string BuildPackagePath(string packageName);
		
		/// <summary>
		/// Path to csproj file of package
		/// </summary>
		/// <param name="packageName"></param>
		/// <returns></returns>
		string BuildPackageProjectPath(string packageName);

		/// <summary>
		/// Builds the path of the props file clio generates for a package and a target moniker.
		/// Both the props writer and the csproj import must derive the path here, because an
		/// import pointing at a path nobody wrote fails the whole project with MSB4019.
		/// </summary>
		/// <param name="packageName">Creatio package name.</param>
		/// <param name="moniker">Target moniker: net472 or netstandard.</param>
		string BuildPackagePropsPath(string packageName, string moniker);
		string BuildFrameworkCreatioSdkPath(Version nugetVersion);
		string BuildCoreCreatioSdkPath(Version nugetVersion);
		string BuildRelativePathRegardingPackageProjectPath(string destinationPath);

		#endregion

	}

	#endregion

}
