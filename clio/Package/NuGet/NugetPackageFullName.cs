using System;

namespace Clio.Project.NuGet;

public struct NugetPackageFullName
{
    public NugetPackageFullName(string fullNamesDescription)
    {
        string[] fullNameItems = fullNamesDescription
            .Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
        if (fullNameItems.Length > 2)
        {
            throw new ArgumentException($"Wrong format the Nuget package full name: '{fullNamesDescription}'. "
                                        + "The format the Nuget package full name mast be: "
                                        + "'<PackageName>' or '<PackageName>:<PackageVersion>'");
        }

        Name = fullNameItems[0];
        Version = fullNameItems.Length == 2
            ? fullNameItems[1]
            : PackageVersion.LastVersion;
    }

    public NugetPackageFullName(string name, string version)
    {
        Name = name;
        Version = string.IsNullOrWhiteSpace(version)
            ? PackageVersion.LastVersion
            : version;
    }

    public string Version { get; set; }

    public string Name { get; set; }

    public static bool operator ==(NugetPackageFullName packageFullName1, NugetPackageFullName packageFullName2) =>
        packageFullName1.Equals(packageFullName2);

    public static bool operator !=(NugetPackageFullName packageFullName1, NugetPackageFullName packageFullName2) =>
        !packageFullName1.Equals(packageFullName2);

    public static implicit operator string(NugetPackageFullName packageFullName) => packageFullName.ToString();

    public readonly bool Equals(NugetPackageFullName packageFullName) => Equals(packageFullName, this);

    public override readonly bool Equals(object obj)
    {
        if (obj == null || GetType() != obj.GetType())
        {
            return false;
        }

        NugetPackageFullName packageFullName = (NugetPackageFullName)obj;
        return packageFullName.Name == Name &&
               packageFullName.Version == Version;
    }

    public override int GetHashCode() => ToString().GetHashCode();

    public override readonly string ToString() => $"{Name}:{Version}";
}
