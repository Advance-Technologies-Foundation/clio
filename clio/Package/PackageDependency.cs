using Clio.Project.NuGet;

namespace Clio;

#region Struct: NugetPackageFullName

public struct PackageDependency
{
    #region Constructors: Public

    public PackageDependency(string name, string packageVersion, string uid = null)
    {
        Name = name;
        PackageVersion = packageVersion;
        UId = uid ?? string.Empty;
    }

    #endregion

    #region Properties: Public

    public string UId { get; set; }
    public string PackageVersion { get; set; }
    public string Name { get; set; }

    #endregion

    #region Methods: Public

    public static bool operator ==(PackageDependency packageDependency1, PackageDependency packageDependency2) =>
        packageDependency1.Equals(packageDependency2);

    public static bool operator !=(PackageDependency packageDependency1, PackageDependency packageDependency2) =>
        !packageDependency1.Equals(packageDependency2);

    public bool Equals(PackageDependency packageDependency) => Equals(packageDependency, this);

    public override bool Equals(object obj)
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

    public override string ToString() => $"{Name}:{PackageVersion}(UId='{UId}')";

    #endregion
}

#endregion
