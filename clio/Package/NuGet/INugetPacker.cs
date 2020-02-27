using System.Collections.Generic;

namespace Clio.Project.NuGet
{
	public interface INugetPacker
	{

		string GetNupkgFileName(PackageInfo packageInfo); 

		string Pack(string nuspecFilePath, string destinationNupkgFilePath);
	}
}