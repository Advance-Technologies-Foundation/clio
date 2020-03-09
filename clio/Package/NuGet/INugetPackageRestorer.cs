namespace Clio.Project.NuGet
{
	public interface INugetPackageRestorer
	{
		void RestoreToNugetFileStorage(string packageName, string version, string nugetSourceUrl, 
			string destinationNupkgDirectory);
		void RestoreToDirectory(string packageName, string version, string nugetSourceUrl,
			string destinationNupkgDirectory, bool overwrite);
		void RestoreToPackageStorage(string packageName, string version, string nugetSourceUrl,
			string destinationNupkgDirectory, bool overwrite);

	}
}