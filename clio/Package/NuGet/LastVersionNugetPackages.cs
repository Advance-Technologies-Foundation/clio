namespace Clio.Project.NuGet;

public class LastVersionNugetPackages(string name, NugetPackage last, NugetPackage stable)
{
    public string Name { get; } = name;

    public NugetPackage Last { get; } = last;

    public NugetPackage Stable { get; } = stable;

    public bool LastIsStable => Last == Stable;

    public bool StableIsNotExists => Stable == null;
}
