namespace Clio.Workspace
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

		#endregion

	}

	#endregion

}