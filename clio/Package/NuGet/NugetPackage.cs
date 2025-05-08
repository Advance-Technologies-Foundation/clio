using System;

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

    public override bool Equals(object nugetPackage) => Equals(nugetPackage as NugetPackage);

    public bool Equals(NugetPackage nugetPackage) =>
        ReferenceEquals(nugetPackage, this) ||
        (!ReferenceEquals(nugetPackage, null) &&
         Name == nugetPackage.Name &&
         Version == nugetPackage.Version);

    public override int GetHashCode()
    {
        string calculation = $"{Name}{Version}";
        return calculation.GetHashCode();
    }

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

    #endregion
}

#endregion
