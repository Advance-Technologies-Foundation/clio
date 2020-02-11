using System.Collections.Generic;

namespace Clio.Common
{
	public interface IProjectUtilities
	{
		void CompressProject(string sourcePath, string destinationPath, bool skipPdb);
		void CompressProjects(string sourcePath, string destinationPath, IEnumerable<string> names, bool skipPdb);
	}
}
