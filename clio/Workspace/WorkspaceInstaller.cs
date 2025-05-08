using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Clio.Utilities;
using Common;
using Package;
using Terrasoft.Core;

namespace Clio.Workspaces;

public interface IWorkspaceInstaller
{
    void Install(IEnumerable<string> packages, string creatioPackagesZipName = null);

    void Publish(IList<string> packages, string zipFileName, string destionationFolderPath, bool ovverideFile);

    string PublishToFolder(string zipFileName, string destinationFolderPath, string destinationFolderPath1, bool v);
}

public class WorkspaceInstaller : IWorkspaceInstaller
{
    private const string CreatioPackagesZipName = "CreatioPackages";
    private const string ResetSchemaChangeStateServicePath = @"/rest/CreatioApiGateway/ResetSchemaChangeState";
    private readonly EnvironmentSettings _environmentSettings;
    private readonly IWorkspacePathBuilder _workspacePathBuilder;
    private readonly IApplicationClientFactory _applicationClientFactory;
    private readonly IPackageInstaller _packageInstaller;
    private readonly IPackageArchiver _packageArchiver;
    private readonly IPackageBuilder _packageBuilder;
    private readonly IStandalonePackageFileManager _standalonePackageFileManager;
    private readonly IServiceUrlBuilder _serviceUrlBuilder;
    private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
    private readonly IFileSystem _fileSystem;
    private readonly IOSPlatformChecker _osPlatformChecker;
    private readonly Lazy<IApplicationClient> _applicationClientLazy;

    public WorkspaceInstaller(EnvironmentSettings environmentSettings, IWorkspacePathBuilder workspacePathBuilder,
        IApplicationClientFactory applicationClientFactory, IPackageInstaller packageInstaller,
        IPackageArchiver packageArchiver, IPackageBuilder packageBuilder,
        IStandalonePackageFileManager standalonePackageFileManager, IServiceUrlBuilder serviceUrlBuilder,
        IWorkingDirectoriesProvider workingDirectoriesProvider, IFileSystem fileSystem,
        IOSPlatformChecker osPlatformChecker)
    {
        environmentSettings.CheckArgumentNull(nameof(environmentSettings));
        workspacePathBuilder.CheckArgumentNull(nameof(workspacePathBuilder));
        applicationClientFactory.CheckArgumentNull(nameof(applicationClientFactory));
        packageInstaller.CheckArgumentNull(nameof(packageInstaller));
        packageArchiver.CheckArgumentNull(nameof(packageArchiver));
        packageBuilder.CheckArgumentNull(nameof(packageBuilder));
        standalonePackageFileManager.CheckArgumentNull(nameof(standalonePackageFileManager));
        serviceUrlBuilder.CheckArgumentNull(nameof(serviceUrlBuilder));
        workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
        fileSystem.CheckArgumentNull(nameof(fileSystem));
        osPlatformChecker.CheckArgumentNull(nameof(osPlatformChecker));
        _environmentSettings = environmentSettings;
        _workspacePathBuilder = workspacePathBuilder;
        _applicationClientFactory = applicationClientFactory;
        _packageInstaller = packageInstaller;
        _packageArchiver = packageArchiver;
        _packageBuilder = packageBuilder;
        _standalonePackageFileManager = standalonePackageFileManager;
        _serviceUrlBuilder = serviceUrlBuilder;
        _workingDirectoriesProvider = workingDirectoriesProvider;
        _fileSystem = fileSystem;
        _osPlatformChecker = osPlatformChecker;
        _applicationClientLazy = new Lazy<IApplicationClient>(CreateClient);
    }

    private IApplicationClient ApplicationClient => _applicationClientLazy.Value;

    private string ResetSchemaChangeStateServiceUrl => _serviceUrlBuilder.Build(ResetSchemaChangeStateServicePath);

    private IApplicationClient CreateClient() => _applicationClientFactory.CreateClient(_environmentSettings);

    private void ResetSchemaChangeStateServiceUrlByPackage(string packageName) =>
        ApplicationClient.ExecutePostRequest(
            ResetSchemaChangeStateServiceUrl,
            "{\"packageName\":\"" + packageName + "\"}");

    private void PackPackage(string packageName, string rootPackedPackagePath)
    {
        string packagePath = Path.Combine(_workspacePathBuilder.PackagesFolderPath, packageName);
        string packedPackagePath = Path.Combine(rootPackedPackagePath, $"{packageName}.gz");
        _packageArchiver.Pack(packagePath, packedPackagePath, true, true);
    }

    private string CreateRootPackedPackageDirectory(string creatioPackagesZipName, string tempDirectory)
    {
        string rootPackedPackagePath = Path.Combine(tempDirectory, creatioPackagesZipName);
        _fileSystem.CreateDirectory(rootPackedPackagePath);
        return rootPackedPackagePath;
    }

    private string ZipPackages(string creatioPackagesZipName, string tempDirectory, string rootPackedPackagePath)
    {
        string applicationZip = Path.Combine(tempDirectory, $"{creatioPackagesZipName}.zip");
        _packageArchiver.ZipPackages(
            rootPackedPackagePath,
            applicationZip, true);
        return applicationZip;
    }

    private void InstallApplication(string applicationZip) =>
        _packageInstaller.Install(applicationZip, _environmentSettings);

    private void BuildStandalonePackagesIfNeeded()
    {
        if (_osPlatformChecker.IsWindowsEnvironment || _environmentSettings.IsNetCore)
        {
            return;
        }

        IEnumerable<string> standalonePackagesNames = _standalonePackageFileManager
            .FindStandalonePackagesNames(_workspacePathBuilder.PackagesFolderPath);
        _packageBuilder.Build(standalonePackagesNames);
    }

    public void Install(IEnumerable<string> packages, string creatioPackagesZipName = null)
    {
        creatioPackagesZipName ??= CreatioPackagesZipName;
        _workingDirectoriesProvider.CreateTempDirectory(tempDirectory =>
        {
            string rootPackedPackagePath =
                CreateRootPackedPackageDirectory(creatioPackagesZipName, tempDirectory);
            foreach (string packageName in packages)
            {
                PackPackage(packageName, rootPackedPackagePath);
                ResetSchemaChangeStateServiceUrlByPackage(packageName);
            }

            string applicationZip = ZipPackages(creatioPackagesZipName, tempDirectory, rootPackedPackagePath);
            InstallApplication(applicationZip);
            BuildStandalonePackagesIfNeeded();
        });
    }

    public void Publish(IList<string> packages, string zipFileName, string destionationFolderPath,
        bool overrideFile = false) =>
        _workingDirectoriesProvider.CreateTempDirectory(tempDirectory =>
        {
            string rootPackedPackagePath =
                CreateRootPackedPackageDirectory(zipFileName, tempDirectory);
            foreach (string packageName in packages)
            {
                PackPackage(packageName, rootPackedPackagePath);
                ResetSchemaChangeStateServiceUrlByPackage(packageName);
            }

            string applicationZip = ZipPackages(zipFileName, tempDirectory, rootPackedPackagePath);
            _fileSystem.CopyFile(applicationZip, Path.Combine(destionationFolderPath, zipFileName), overrideFile);
        });

    public string PublishToFolder(string workspaceFolderPath, string zipFileName, string destinationFolderPath,
        bool overwrite)
    {
        _workspacePathBuilder.RootPath = workspaceFolderPath;
        string resultApplicationFilePath = string.Empty;
        IEnumerable<string> packages = Directory.GetDirectories(_workspacePathBuilder.PackagesFolderPath)
            .Select(p => new DirectoryInfo(p).Name);
        _workingDirectoriesProvider.CreateTempDirectory(tempDirectory =>
        {
            string rootPackedPackagePath =
                CreateRootPackedPackageDirectory(zipFileName, tempDirectory);
            foreach (string packageName in packages)
            {
                PackPackage(packageName, rootPackedPackagePath);

                // ResetSchemaChangeStateServiceUrl(packageName);
            }

            string applicationZip = ZipPackages(zipFileName, tempDirectory, rootPackedPackagePath);
            string filename = Path.GetFileName(applicationZip);
            resultApplicationFilePath = Path.Combine(destinationFolderPath, filename);
            _fileSystem.CreateDirectoryIfNotExists(destinationFolderPath);
            _fileSystem.CopyFile(applicationZip, resultApplicationFilePath, overwrite);
        });
        return resultApplicationFilePath;
    }
}
