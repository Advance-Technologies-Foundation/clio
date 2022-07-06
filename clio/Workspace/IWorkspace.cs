namespace Clio.Workspace
{

	#region Interface: IWorkspace

	public interface IWorkspace
	{
		#region Properties: Public

		WorkspaceSettings WorkspaceSettings { get; }

		#endregion

		#region Methods: Public

		void SaveWorkspaceSettings();
		void Create(bool isAddedPackageNames = false);
		void Restore();
		void Install(string creatioPackagesZipName = null);

		#endregion

	}

	#endregion

}