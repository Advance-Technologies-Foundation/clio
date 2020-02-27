using System.Collections.Generic;

namespace Clio.Project.NuGet
{
	public interface INuGetManager
	{

		string GetNuspecFileName(PackageInfo packageInfo);

		string GetNupkgFileName(PackageInfo packageInfo);

		void CreateNuspecFile(PackageInfo packageInfo, IEnumerable<DependencyInfo> dependencies, 
			string nuspecFilePath);

		void Pack(string nuspecFilePath, string nupkgFilePath);

		void Push(string nupkgFilePath, string apiKey, string nugetSourceUrl);

	}
}