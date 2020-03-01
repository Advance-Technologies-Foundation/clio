using System;
using System.Collections.Generic;

namespace Clio
{
	public interface IPackageArchiver
	{
		string GetPackedPackageFileName(string packageName);
		string GetPackedGroupPackagesFileName(string groupPackagesName);
		void Pack(string packagePath, string packedPackagePath, bool skipPdb, bool overwrite);
		void Pack(string sourcePath, string destinationPath, IEnumerable<string> names, bool skipPdb, bool overwrite);
		void Unpack(string packedPackagePath, bool overwrite, string destinationPath = null);
		void Unpack(IEnumerable<string> packedPackagesPaths, string destinationPath = null, 
			Action<string, string> onStart = null, Action<string, string> onComplete = null);
		void UnZipPackages(string zipFilePath, bool overwrite, bool deleteGzFiles = true, 
			string destinationPath = null, Action<string, string> onStart = null, 
			Action<string> onComplete = null);
	}
}