namespace Clio.Workspace
{

	#region Interface: IWorkspace

	public interface IWorkspace
	{
		#region Properties: Public

		WorkspaceSettings WorkspaceSettings { get; }

		#endregion

		#region Methods: Public

		void Create();
		void Restore();
		void Install(string creatioPackagesZipName = null);

		#endregion

	}

	#endregion

}