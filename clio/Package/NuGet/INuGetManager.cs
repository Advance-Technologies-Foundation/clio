using System.Collections.Generic;

namespace Clio.Project.NuGet
{
	public interface INuGetManager
	{

		string GetNuspecFileName(PackageInfo packageInfo);

		string GetNupkgFileName(PackageInfo packageInfo);

		void CreateNuspecFile(PackageInfo packageInfo, IEnumerable<DependencyInfo> dependencies, 
			string nuspecFilePath);

		string Pack(string nuspecFilePath, string nupkgFilePath);

		string Push(string nupkgFilePath, string apiKey, string nugetSourceUrl);

		string Restore(string name, string version, string nugetSourceUrl, string destinationNupkgDirectory);
	}
}