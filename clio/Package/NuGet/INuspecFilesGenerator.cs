using System.Collections.Generic;

namespace Clio.Project.NuGet
{
	public interface INuspecFilesGenerator
	{
		void Create(string packagesPath, IDictionary<string, PackageInfo> packagesInfo, string version);
	}
}