namespace Clio.Project.NuGet
{
	public interface IInstallNugetPackage
	{
		void Install(string packageName, string version, string nugetSourceUrl);
	}
}