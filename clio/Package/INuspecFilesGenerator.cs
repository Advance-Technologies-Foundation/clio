using System.Collections.Generic;

namespace Clio
{
	public interface INuspecFilesGenerator
	{
		void Create(string nuspecFilesDirectory, IDictionary<string, PackageInfo> packagesInfo, string version);
	}
}