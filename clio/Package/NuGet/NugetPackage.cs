namespace Clio.Project.NuGet;

#region Class: NugetPackage

public class NugetPackage
{

    #region Constructors: Public

    public NugetPackage(string name, PackageVersion version)
    {
        Name = name;
        Version = version;
    }

    #endregion

    #region Properties: Public

    public string Name { get; }

    public PackageVersion Version { get; }

    #endregion

    #region Methods: Public

    public static bool operator ==(NugetPackage nugetPackage1, NugetPackage nugetPackage2)
    {
        if (ReferenceEquals(nugetPackage1, null))
        {
            return ReferenceEquals(nugetPackage2, null);
        }
        return nugetPackage1.Equals(nugetPackage2);
    }

    public static bool operator !=(NugetPackage nugetPackage1, NugetPackage nugetPackage2)
    {
        if (ReferenceEquals(nugetPackage1, null))
        {
            return ReferenceEquals(nugetPackage2, null);
        }
        return !nugetPackage1.Equals(nugetPackage2);
    }

    public override bool Equals(object nugetPackage)
    {
        return Equals(nugetPackage as NugetPackage);
    }

    public override int GetHashCode()
    {
        string calculation = $"{Name}{Version}";
        return calculation.GetHashCode();
    }

    public bool Equals(NugetPackage nugetPackage)
    {
        return ReferenceEquals(nugetPackage, this) ||
            (!ReferenceEquals(nugetPackage, null) &&
                Name == nugetPackage.Name &&
                Version == nugetPackage.Version);
    }

    #endregion

}

#endregion
