namespace Clio.Package;

public interface IPackageInstaller
{
    bool Install(string packagePath, EnvironmentSettings environmentSettings = null,
        PackageInstallOptions packageInstallOptions = null, string reportPath = null);
}
