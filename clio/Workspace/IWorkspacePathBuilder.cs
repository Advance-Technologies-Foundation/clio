using System;

namespace Clio.Workspace
{

	#region Interface: IWorkspacePathBuilder

	public interface IWorkspacePathBuilder
	{

		#region Properties: Public

		string RootPath { get; }

		public string ClioDirectoryPath { get; }

		public string WorkspaceSettingsPath { get; }

		public string PackagesDirectoryPath { get; }

		public string SolutionPath { get; }

		public string NugetFolderPath { get; }

		#endregion

		#region Methods: Public

		public string BuildFrameworkCreatioSdkPath(Version nugetVersion);

		public string BuildCoreCreatioSdkPath(Version nugetVersion);


		#endregion

	}

	#endregion

}