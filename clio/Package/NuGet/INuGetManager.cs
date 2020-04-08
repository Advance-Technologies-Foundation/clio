using System.Collections.Generic;

namespace Clio.Project.NuGet
{
	public interface INuGetManager
	{

		void Pack(string packagePath, IEnumerable<PackageDependency> dependencies, bool skipPdb,
			string destinationNupkgDirectory);
		void Push(string nupkgFilePath, string apiKey, string nugetSourceUrl);
		void RestoreToNugetFileStorage(string packageName, string version, string nugetSourceUrl, 
			string destinationNupkgDirectory);
		void RestoreToDirectory(string packageName, string version, string nugetSourceUrl,
			string destinationNupkgDirectory, bool overwrite);
		void RestoreToPackageStorage(string packageName, string version, string nugetSourceUrl,
			string destinationNupkgDirectory, bool overwrite);
		IEnumerable<PackageForUpdate> GetPackagesForUpdate(string nugetSourceUrl);

	}
}