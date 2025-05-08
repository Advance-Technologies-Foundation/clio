using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Clio.Package;
using Clio.Project.NuGet;
using Clio.Utilities;

namespace Clio.Workspaces;

public interface IWorkspaceCreator
{
    void Create(string environmentName, bool isAddingPackageNames = false);

    void SaveWorkspaceEnvironmentSettings(string environmentName);
}

public class WorkspaceCreator : IWorkspaceCreator
{
    private readonly IApplicationPackageListProvider _applicationPackageListProvider;
    private readonly ICreatioSdk _creatioSdk;
    private readonly IExecutablePermissionsActualizer _executablePermissionsActualizer;
    private readonly IFileSystem _fileSystem;
    private readonly IJsonConverter _jsonConverter;
    private readonly IOSPlatformChecker _osPlatformChecker;
    private readonly ITemplateProvider _templateProvider;
    private readonly IWorkspacePathBuilder _workspacePathBuilder;

    public WorkspaceCreator(IWorkspacePathBuilder workspacePathBuilder, ICreatioSdk creatioSdk,
        ITemplateProvider templateProvider, IJsonConverter jsonConverter, IFileSystem fileSystem,
        IApplicationPackageListProvider applicationPackageListProvider,
        IExecutablePermissionsActualizer executablePermissionsActualizer,
        IOSPlatformChecker osPlatformChecker)
    {
        workspacePathBuilder.CheckArgumentNull(nameof(workspacePathBuilder));
        creatioSdk.CheckArgumentNull(nameof(creatioSdk));
        templateProvider.CheckArgumentNull(nameof(templateProvider));
        jsonConverter.CheckArgumentNull(nameof(jsonConverter));
        fileSystem.CheckArgumentNull(nameof(fileSystem));
        applicationPackageListProvider.CheckArgumentNull(nameof(applicationPackageListProvider));
        executablePermissionsActualizer.CheckArgumentNull(nameof(executablePermissionsActualizer));
        osPlatformChecker.CheckArgumentNull(nameof(osPlatformChecker));
        _workspacePathBuilder = workspacePathBuilder;
        _creatioSdk = creatioSdk;
        _templateProvider = templateProvider;
        _jsonConverter = jsonConverter;
        _fileSystem = fileSystem;
        _applicationPackageListProvider = applicationPackageListProvider;
        _executablePermissionsActualizer = executablePermissionsActualizer;
        _osPlatformChecker = osPlatformChecker;
    }

    private string RootPath => _workspacePathBuilder.RootPath;

    private string WorkspaceSettingsPath => _workspacePathBuilder.WorkspaceSettingsPath;

    private string WorkspaceEnvironmentSettingsPath => _workspacePathBuilder.WorkspaceEnvironmentSettingsPath;

    private bool IsWorkspace => _workspacePathBuilder.IsWorkspace;

    private bool ExistsWorkspaceSettingsFile => _fileSystem.ExistsFile(WorkspaceSettingsPath);

    public void SaveWorkspaceEnvironmentSettings(string environmentName)
    {
        WorkspaceEnvironmentSettings defaultWorkspaceSettings = new() { Environment = environmentName ?? string.Empty };
        _jsonConverter.SerializeObjectToFile(defaultWorkspaceSettings, WorkspaceEnvironmentSettingsPath);
    }

    public void Create(string environmentName, bool isAddingPackageNames = false)
    {
        ValidateNotExistingWorkspace();
        ValidateDirectory();
        _templateProvider.CopyTemplateFolder("workspace", RootPath, string.Empty, string.Empty, false);
        if (!ExistsWorkspaceSettingsFile)
        {
            CreateWorkspaceSettingsFile(isAddingPackageNames);
            SaveWorkspaceEnvironmentSettings(environmentName);
        }

        if (_osPlatformChecker.IsWindowsEnvironment)
        {
            return;
        }

        ActualizeExecutablePermissions();
    }

    private WorkspaceSettings CreateDefaultWorkspaceSettings(string[] packages)
    {
        Version lv = _creatioSdk.LastVersion;
        WorkspaceSettings workspaceSettings = new()
        {
            ApplicationVersion = new Version(lv.Major, lv.Minor, lv.Build), Packages = packages
        };
        return workspaceSettings;
    }

    private void CreateWorkspaceSettingsFile(bool isAddingPackageNames = false)
    {
        string[] packages = [];
        if (isAddingPackageNames)
        {
            IEnumerable<PackageInfo> packagesInfo =
                _applicationPackageListProvider.GetPackages("{\"isCustomer\": \"true\"}");
            packages = packagesInfo.Select(s => s.Descriptor.Name).ToArray();
        }

        WorkspaceSettings defaultWorkspaceSettings = CreateDefaultWorkspaceSettings(packages);
        _jsonConverter.SerializeObjectToFile(defaultWorkspaceSettings, WorkspaceSettingsPath);
    }

    private void ActualizeExecutablePermissions()
    {
        _executablePermissionsActualizer.Actualize(_workspacePathBuilder.SolutionFolderPath);
        _executablePermissionsActualizer.Actualize(_workspacePathBuilder.TasksFolderPath);
    }

    private void ValidateNotExistingWorkspace()
    {
        if (IsWorkspace)
        {
            throw new InvalidOperationException("This operation can not execute inside existing workspace!");
        }
    }

    private void ValidateDirectory()
    {
        string[] existingDirectories = _fileSystem.GetDirectories();
        string[] templateDirectories = _templateProvider.GetTemplateDirectories("workspace");
        IEnumerable<string> commonDirectories = existingDirectories.Intersect(templateDirectories);
        if (commonDirectories.Any())
        {
            throw new InvalidOperationException("This operation requires empty folder!");
        }
    }
}
