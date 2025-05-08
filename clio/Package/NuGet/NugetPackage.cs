namespace Clio.Project.NuGet;

public class NugetPackage(string name, PackageVersion version)
{
    public string Name { get; } = name;

    public PackageVersion Version { get; } = version;

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
}
