namespace Clio.Project.NuGet
{
	public interface INugetPackageRestorer
	{
		void RestoreToNugetFileStorage(NugetPackageFullName nugetPackageFullName, string nugetSourceUrl, 
			string destinationDirectory);
		void RestoreToDirectory(NugetPackageFullName nugetPackageFullName, string nugetSourceUrl,
			string destinationDirectory, bool overwrite);
		void RestoreToPackageStorage(NugetPackageFullName nugetPackageFullName, string nugetSourceUrl,
			string destinationDirectory, bool overwrite);

	}
}