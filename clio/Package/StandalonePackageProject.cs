namespace Clio.Package;

public class StandalonePackageProject(string packageName, string path)
{
    public string PackageName { get; } = packageName;

    public string Path { get; } = path;
}
