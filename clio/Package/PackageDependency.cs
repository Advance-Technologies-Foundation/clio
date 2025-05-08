using Clio.Project.NuGet;

namespace Clio;

public struct PackageDependency(string name, string packageVersion, string uid = null)
{
    public string UId { get; set; } = uid ?? string.Empty;

    public string PackageVersion { get; set; } = packageVersion;

    public string Name { get; set; } = name;

    public static bool operator ==(PackageDependency packageDependency1, PackageDependency packageDependency2) =>
        packageDependency1.Equals(packageDependency2);

    public static bool operator !=(PackageDependency packageDependency1, PackageDependency packageDependency2) =>
        !packageDependency1.Equals(packageDependency2);

    public readonly bool Equals(PackageDependency packageDependency) => Equals(packageDependency, this);

    public override readonly bool Equals(object obj)
    {
        if (obj == null || GetType() != obj.GetType())
        {
            return false;
        }

        PackageDependency dependencyInfo = (PackageDependency)obj;
        return dependencyInfo.Name == Name &&
               dependencyInfo.PackageVersion == PackageVersion &&
               dependencyInfo.UId == UId;
    }

    public override int GetHashCode() => ToString().GetHashCode();

    public override readonly string ToString() => $"{Name}:{PackageVersion}(UId='{UId}')";
}
