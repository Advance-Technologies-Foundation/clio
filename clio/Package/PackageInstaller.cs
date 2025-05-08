using Clio.Common;
using Clio.WebApplication;

namespace Clio.Package;

#region Class: PackageInstaller

public class PackageInstaller : BasePackageInstaller, IPackageInstaller
{

    #region Fields: Private

    private readonly IApplicationLogProvider applicationLogProvider;

    #endregion

    #region Constructors: Public

    public PackageInstaller(IApplicationLogProvider applicationLogProvider, EnvironmentSettings environmentSettings,
        IApplicationClientFactory applicationClientFactory, IApplication application,
        IPackageArchiver packageArchiver, ISqlScriptExecutor scriptExecutor,
        IServiceUrlBuilder serviceUrlBuilder, IFileSystem fileSystem, ILogger logger,
        IPackageLockManager packageLockManager)
        : base(applicationLogProvider, environmentSettings, applicationClientFactory, application,
            packageArchiver, scriptExecutor, serviceUrlBuilder, fileSystem, logger, packageLockManager)
    { }

    #endregion

    #region Properties: Protected

    protected override string BackupUrl => @"/ServiceModel/PackageInstallerService.svc/CreateBackup";

    protected override string InstallUrl => @"/ServiceModel/PackageInstallerService.svc/InstallPackage";

    #endregion

    #region Methods: Protected

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

    #endregion

    #region Methods: Public

    public bool Install(string packagePath, EnvironmentSettings environmentSettings = null,
        PackageInstallOptions packageInstallOptions = null, string reportPath = null)
    {
        return InternalInstall(packagePath, environmentSettings, packageInstallOptions, reportPath);
    }

    #endregion

}

#endregion
