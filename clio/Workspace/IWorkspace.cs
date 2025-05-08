using Command;

namespace Clio.Workspaces;

public interface IWorkspace
{
    WorkspaceSettings WorkspaceSettings { get; }

    bool IsWorkspace { get; }

    void SaveWorkspaceSettings();

    void Create(string environmentName, bool isAddedPackageNames = false);

    void Restore(WorkspaceOptions restoreWorkspaceOptions);

    void Install(string creatioPackagesZipName = null);

    void AddPackageIfNeeded(string packageName);

    void SaveWorkspaceEnvironment(string environmentName);

    void PublishZipToFolder(string zipFileName, string destionationFolderPath, bool overrideFile);

    string PublishToFolder(string workspacePath, string appStorePath, string appName, string appVersion,
        string branch = null);

    string GetWorkspaceApplicationCode();
}
