using System.Collections.Generic;

namespace Clio.Project.NuGet
{
	public interface INuspecFilesGenerator
	{

		string GetNuspecFileName(PackageInfo packageInfo);

		void Create(PackageInfo packageInfo, IEnumerable<DependencyInfo> dependencies, string nuspecFilePath);
	}
}