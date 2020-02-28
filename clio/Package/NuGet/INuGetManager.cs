using System.Collections.Generic;

namespace Clio.Project.NuGet
{
	public interface INuGetManager
	{

		string Pack(string packagePath, IEnumerable<PackageDependency> dependencies, bool skipPdb,
			string destinationNupkgDirectory);

		string Push(string nupkgFilePath, string apiKey, string nugetSourceUrl);

		string Restore(string name, string version, string nugetSourceUrl, string destinationNupkgDirectory);
	}
}