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
		void Restore(string workspaceEnvironmentName);

		#endregion

	}

	#endregion

}