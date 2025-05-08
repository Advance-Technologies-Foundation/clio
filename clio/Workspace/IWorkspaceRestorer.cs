using System;

using Command;

namespace Clio.Workspaces;

public interface IWorkspaceRestorer
{
    void Restore(WorkspaceSettings workspaceSettings, EnvironmentSettings environmentSettings,
        WorkspaceOptions restoreWorkspaceOptions);
}
