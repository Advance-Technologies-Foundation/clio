namespace Clio.Workspaces
{
	using System.Collections.Generic;
	using Clio.Command;

	#region Interface: IWorkspace

	public interface IWorkspace
	{
		#region Properties: Public

		WorkspaceSettings WorkspaceSettings { get; }
		bool IsWorkspace { get; }

		#endregion

		#region Methods: Public

		void SaveWorkspaceSettings();
		void Create(string environmentName, bool isAddedPackageNames = false);
		void Restore(WorkspaceOptions restoreWorkspaceOptions);
		void Install(string creatioPackagesZipName = null, bool useApplicationInstaller = false);
		void InstallUsingApplicationInstaller(string creatioPackagesZipName = null);
		void AddPackageIfNeeded(string packageName);
		void SaveWorkspaceEnvironment(string environmentName);
		void PublishZipToFolder(string zipFileName, string destionationFolderPath, bool overrideFile);
		string PublishToFolder(string workspacePath, string appStorePath, string appName, string appVersion, string branch = null);
		string PublishToFile(string workspacePath, string filePath, string appVersion);
		string GetWorkspaceApplicationCode();
		IEnumerable<string> GetFilteredPackages();
		#endregion

	}

	#endregion

}
