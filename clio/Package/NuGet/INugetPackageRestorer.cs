namespace Clio.Project.NuGet
{
	public interface INugetPackageRestorer
	{
		string Restore(string name, string version, string nugetSourceUrl, string destinationNupkgDirectory);
	}
}