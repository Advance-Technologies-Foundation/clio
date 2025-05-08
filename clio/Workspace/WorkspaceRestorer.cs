using System;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using Clio.Project.NuGet;

namespace Clio.Workspaces;

public class WorkspaceRestorer : IWorkspaceRestorer
{
    private readonly ICreatioSdk _creatioSdk;
    private readonly IEnvironmentScriptCreator _environmentScriptCreator;
    private readonly INuGetManager _nugetManager;
    private readonly IPackageDownloader _packageDownloader;
    private readonly IWorkspacePathBuilder _workspacePathBuilder;
    private readonly IWorkspaceSolutionCreator _workspaceSolutionCreator;

    public WorkspaceRestorer(INuGetManager nugetManager, IWorkspacePathBuilder workspacePathBuilder,
        IEnvironmentScriptCreator environmentScriptCreator, IWorkspaceSolutionCreator workspaceSolutionCreator,
        IPackageDownloader packageDownloader, ICreatioSdk creatioSdk)
    {
        nugetManager.CheckArgumentNull(nameof(nugetManager));
        workspacePathBuilder.CheckArgumentNull(nameof(workspacePathBuilder));
        environmentScriptCreator.CheckArgumentNull(nameof(environmentScriptCreator));
        workspaceSolutionCreator.CheckArgumentNull(nameof(workspaceSolutionCreator));
        packageDownloader.CheckArgumentNull(nameof(packageDownloader));
        creatioSdk.CheckArgumentNull(nameof(creatioSdk));
        _nugetManager = nugetManager;
        _workspacePathBuilder = workspacePathBuilder;
        _environmentScriptCreator = environmentScriptCreator;
        _workspaceSolutionCreator = workspaceSolutionCreator;
        _packageDownloader = packageDownloader;
        _creatioSdk = creatioSdk;
    }

    public void Restore(WorkspaceSettings workspaceSettings, EnvironmentSettings environmentSettings,
        WorkspaceOptions restoreWorkspaceOptions)
    {
        Version creatioSdkVersion = _creatioSdk.FindLatestSdkVersion(workspaceSettings.ApplicationVersion);
        _packageDownloader.DownloadPackages(workspaceSettings.Packages, environmentSettings,
            _workspacePathBuilder.PackagesFolderPath);
        if (restoreWorkspaceOptions.IsNugetRestore == true)
        {
            RestoreNugetCreatioSdk(creatioSdkVersion);
            CreateEnvironmentScript(creatioSdkVersion);
        }

        if (restoreWorkspaceOptions.IsCreateSolution == true)
        {
            CreateSolution();
        }
    }

    private void RestoreNugetCreatioSdk(Version nugetCreatioSdkVersion)
    {
        const string nugetSourceUrl = "https://api.nuget.org/v3/index.json";
        const string packageName = "CreatioSDK";
        NugetPackageFullName nugetPackageFullName = new()
        {
            Name = packageName, Version = nugetCreatioSdkVersion.ToString()
        };
        _nugetManager.RestoreToNugetFileStorage(nugetPackageFullName, nugetSourceUrl,
            _workspacePathBuilder.NugetFolderPath);
    }

    private void CreateSolution() => _workspaceSolutionCreator.Create();

    private void CreateEnvironmentScript(Version nugetCreatioSdkVersion) =>
        _environmentScriptCreator.Create(nugetCreatioSdkVersion);
}
