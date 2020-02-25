using System.Collections.Generic;

namespace Clio.Project.NuGet
{
	public interface INugetPacker
	{
		void Pack(IEnumerable<string> nuspecFilesPaths, string destinationNupkgDirectory);
	}
}