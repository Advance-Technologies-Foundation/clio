using System.Collections.Generic;

namespace Clio.Project.NuGet
{
	public interface INuGetManager
	{
		IEnumerable<string> GetNuspecFilesPaths(string nuspecFilesDirectory);

		IEnumerable<string> GetNupkgFilesPaths(string nupkgFilesDirectory);

		void CreateNuspecFiles(string packagesPath, IDictionary<string, PackageInfo> packagesInfo, 
			string version);

		void Pack(IEnumerable<string> nuspecFilesPaths, string destinationNupkgDirectory);

		void Push(IEnumerable<string> nupkgFilesPaths, string apiKey, string nugetSourceUrl);

	}
}