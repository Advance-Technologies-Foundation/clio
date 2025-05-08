namespace Clio.Project.NuGet;

public class PackageForUpdate(LastVersionNugetPackages lastVersionNugetPackages, PackageInfo applicationPackage)
{
    public LastVersionNugetPackages LastVersionNugetPackages { get; } = lastVersionNugetPackages;

    public PackageInfo ApplicationPackage { get; } = applicationPackage;
}
