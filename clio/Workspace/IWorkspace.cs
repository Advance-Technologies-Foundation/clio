using Clio.Command;

namespace Clio.Workspaces;

#region Interface: IWorkspace

public interface IWorkspace
{

    #region Properties: Public

    bool IsWorkspace { get; }

    WorkspaceSettings WorkspaceSettings { get; }

    #endregion

    #region Methods: Public

    void AddPackageIfNeeded(string packageName);

    void Create(string environmentName, bool isAddedPackageNames = false);

    string GetWorkspaceApplicationCode();

    void Install(string creatioPackagesZipName = null);

    string PublishToFolder(string workspacePath, string appStorePath, string appName, string appVersion,
        string branch = null);

    void PublishZipToFolder(string zipFileName, string destionationFolderPath, bool overrideFile);

    void Restore(WorkspaceOptions restoreWorkspaceOptions);

    void SaveWorkspaceEnvironment(string environmentName);

    void SaveWorkspaceSettings();

    #endregion

}

#endregion
