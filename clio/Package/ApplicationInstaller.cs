using System.IO;
using CreatioModel;

namespace Clio.Package;

public class ApplicationInstaller(
    IApplicationLogProvider applicationLogProvider,
    EnvironmentSettings environmentSettings,
    IApplicationClientFactory applicationClientFactory,
    IApplication application,
    IPackageArchiver packageArchiver,
    ISqlScriptExecutor scriptExecutor,
    IServiceUrlBuilder serviceUrlBuilder,
    IFileSystem fileSystem,
    ILogger logger,
    IPackageLockManager packageLockManager) : BasePackageInstaller(applicationLogProvider, environmentSettings,
    applicationClientFactory, application,
    packageArchiver, scriptExecutor, serviceUrlBuilder, fileSystem, logger, packageLockManager), IApplicationInstaller
{
    protected override string BackupUrl => @"/ServiceModel/PackageInstallerService.svc/CreatePackageBackup";

    protected override string InstallUrl => @"/ServiceModel/AppInstallerService.svc/InstallAppFromFile";

    protected string UnInstallUrl => @"/ServiceModel/AppInstallerService.svc/UninstallApp";

    public bool Install(string packagePath, EnvironmentSettings environmentSettings = null,
        string reportPath = null) =>
        InternalInstall(packagePath, environmentSettings, null, reportPath);

    public bool UnInstall(SysInstalledApp appInfo, EnvironmentSettings environmentSettings = null,
        string reportPath = null) =>
        InternalUnInstall(appInfo, environmentSettings, null, reportPath);

    private bool InternalUnInstall(SysInstalledApp appInfo, EnvironmentSettings environmentSettings, object o,
        string reportPath)
    {
        IApplicationClient client = _applicationClientFactory.CreateClient(environmentSettings);
        string completeUrl = GetCompleteUrl(UnInstallUrl, environmentSettings);
        _logger.WriteInfo($"Uninstalling {appInfo.Code}");
        _ = client.ExecutePostRequest(completeUrl, "\"" + appInfo.Id + "\"");
        _logger.WriteInfo($"Application {appInfo.Code} uninstalled");
        return true;
    }

    protected override string GetRequestData(string fileName, PackageInstallOptions packageInstallOptions)
    {
        string code = _fileSystem.GetFileNameWithoutExtension(new FileInfo(fileName));
        return
            $"{{\"Name\":\"{code}\",\"Code\":\"{code}\",\"ZipPackageName\":\"{fileName}\",\"LastUpdateString\":0}}";
    }
}
