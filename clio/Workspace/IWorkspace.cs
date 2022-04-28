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
		void Install(string workspaceEnvironmentName);

		#endregion

	}

	#endregion

}