using System.Collections.Generic;
using System.IO.Compression;

namespace Clio.Common
{
	public interface ICompressionUtilities
	{
		void PackToGZip(IEnumerable<string> files, string rootDirectoryPath, string destinationPackagePath);
		void UnpackFromGZip(string packedPackagePath, string destinationPackageDirectoryPath);

	}
}