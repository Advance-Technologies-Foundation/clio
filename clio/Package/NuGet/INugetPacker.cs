using System.Collections.Generic;

namespace Clio.Project.NuGet
{
	public interface INugetPacker
	{

		string GetNupkgFileName(PackageInfo packageInfo); 

		void Pack(string nuspecFilePath, string destinationNupkgDirectory);
	}
}