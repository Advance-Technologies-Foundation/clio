using Clio.Command;

namespace Clio.Workspaces;

#region Interface: IWorkspaceRestorer

public interface IWorkspaceRestorer
{

    #region Methods: Public

    void Restore(WorkspaceSettings workspaceSettings, EnvironmentSettings environmentSettings,
        WorkspaceOptions restoreWorkspaceOptions);

    #endregion

}

#endregion
