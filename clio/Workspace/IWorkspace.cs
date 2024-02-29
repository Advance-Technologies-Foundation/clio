namespace Clio.Workspaces
{

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
		void Restore();
		void Install(string creatioPackagesZipName = null);
		void AddPackageIfNeeded(string packageName);
		void SaveWorkspaceEnvironment(string environmentName);
		void PublishZipToFolder(string zipFileName, string destionationFolderPath, bool overrideFile);
		string PublishToFolder(string exampleWorkspacePath, string appStorePath, string appName, string appVersion);
		#endregion

	}

	#endregion

}