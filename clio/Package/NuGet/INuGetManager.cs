using System.Collections.Generic;

namespace Clio.Project.NuGet
{
	public interface INuGetManager
	{

		void Pack(string packagePath, IEnumerable<PackageDependency> dependencies, bool skipPdb,
			string destinationNupkgDirectory);
		void Push(string nupkgFilePath, string apiKey, string nugetSourceUrl);
		void RestoreToNugetFileStorage(NugetPackageFullName nugetPackageFullName, string nugetSourceUrl, 
			string destinationDirectory);
		void RestoreToDirectory(NugetPackageFullName nugetPackageFullName, string nugetSourceUrl,
			string destinationDirectory, bool overwrite);
		void RestoreToPackageStorage(NugetPackageFullName nugetPackageFullName, string nugetSourceUrl,
			string destinationDirectory, bool overwrite);
		IEnumerable<PackageForUpdate> GetPackagesForUpdate(string nugetSourceUrl);

	}
}