using System;

namespace Clio.Workspaces;

#region Interface: IWorkspacePathBuilder

public interface IWorkspacePathBuilder
{

    #region Properties: Public

    string ApplicationFolderPath { get; }

    string ClioDirectoryPath { get; }

    string ConfigurationBinFolderPath { get; }

    string CoreBinFolderPath { get; }

    bool IsWorkspace { get; }

    string LibFolderPath { get; }

    string NugetFolderPath { get; }

    string PackagesFolderPath { get; }

    string ProjectsFolderPath { get; }

    string ProjectsTestsFolderPath { get; }

    string RootPath { get; set; }

    string SolutionFolderPath { get; }

    string SolutionPath { get; }

    string TasksFolderPath { get; }

    string WorkspaceEnvironmentSettingsPath { get; }

    string WorkspaceSettingsPath { get; }

    #endregion

    #region Methods: Public

    string BuildCoreCreatioSdkPath(Version nugetVersion);

    string BuildFrameworkCreatioSdkPath(Version nugetVersion);

    string BuildPackagePath(string packageName);

    /// <summary>
    ///     Path to csproj file of package
    /// </summary>
    /// <param name="packageName"></param>
    /// <returns></returns>
    string BuildPackageProjectPath(string packageName);

    string BuildRelativePathRegardingPackageProjectPath(string destinationPath);

    #endregion

}

#endregion
