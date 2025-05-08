using System;

namespace Clio.Project.NuGet;

#region Struct: NugetPackageFullName

public struct NugetPackageFullName
{

    #region Constructors: Public

    public NugetPackageFullName(string fullNamesDescription)
    {
        string[] fullNameItems = fullNamesDescription
            .Split(new[]
            {
                ':'
            }, StringSplitOptions.RemoveEmptyEntries);
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

    #endregion

    #region Properties: Public

    public string Name { get; set; }

    public string Version { get; set; }

    #endregion

    #region Methods: Public

    public static bool operator ==(NugetPackageFullName packageFullName1, NugetPackageFullName packageFullName2)
    {
        return packageFullName1.Equals(packageFullName2);
    }

    public static implicit operator string(NugetPackageFullName packageFullName)
    {
        return packageFullName.ToString();
    }

    public static bool operator !=(NugetPackageFullName packageFullName1, NugetPackageFullName packageFullName2)
    {
        return !packageFullName1.Equals(packageFullName2);
    }

    public override bool Equals(object obj)
    {
        if (obj == null || GetType() != obj.GetType())
        {
            return false;
        }
        NugetPackageFullName packageFullName = (NugetPackageFullName)obj;
        return packageFullName.Name == Name &&
            packageFullName.Version == Version;
    }

    public override int GetHashCode()
    {
        return ToString().GetHashCode();
    }

    public override string ToString()
    {
        return $"{Name}:{Version}";
    }

    public bool Equals(NugetPackageFullName packageFullName)
    {
        return Equals(packageFullName, this);
    }

    #endregion

}

#endregion
