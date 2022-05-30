namespace Clio.Workspace
{
	using System;

	#region Interface: IWorkspaceRestorer

	public interface IWorkspaceRestorer
	{

		#region Methods: Public

		void Restore(WorkspaceSettings workspaceSettings, EnvironmentSettings environmentSettings);

		#endregion

	}

	#endregion

}