namespace Clio.Package;

public class PackageInstaller(
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
    packageArchiver, scriptExecutor, serviceUrlBuilder, fileSystem, logger, packageLockManager), IPackageInstaller
{
    protected override string InstallUrl => @"/ServiceModel/PackageInstallerService.svc/InstallPackage";

    protected override string BackupUrl => @"/ServiceModel/PackageInstallerService.svc/CreateBackup";


    public bool Install(string packagePath, EnvironmentSettings environmentSettings = null,
        PackageInstallOptions packageInstallOptions = null, string reportPath = null) =>
        InternalInstall(packagePath, environmentSettings, packageInstallOptions, reportPath);

    protected override string GetRequestData(string fileName, PackageInstallOptions packageInstallOptions) =>
        packageInstallOptions == null
            ? $"\"{fileName}\""
            : $" {{ \"zipPackageName\": \"{fileName}\", " +
              "\"packageInstallOptions\": { " +
              $"\"installSqlScript\": \"{packageInstallOptions.InstallSqlScript.ToString().ToLower()}\", " +
              $"\"installPackageData\": \"{packageInstallOptions.InstallPackageData.ToString().ToLower()}\", " +
              $"\"continueIfError\": \"{packageInstallOptions.ContinueIfError.ToString().ToLower()}\", " +
              $"\"skipConstraints\": \"{packageInstallOptions.SkipConstraints.ToString().ToLower()}\", " +
              $"\"skipValidateActions\": \"{packageInstallOptions.SkipValidateActions.ToString().ToLower()}\", " +
              $"\"executeValidateActions\": \"{packageInstallOptions.ExecuteValidateActions.ToString().ToLower()}\", " +
              $"\"isForceUpdateAllColumns\": \"{packageInstallOptions.IsForceUpdateAllColumns.ToString().ToLower()}\"  " +
              " } }";
}
