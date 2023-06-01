using System;
using System.Collections.Generic;

namespace Clio
{
	public interface IPackageArchiver
	{
		public bool IsZipArchive(string filePath);
		public bool IsGzArchive(string filePath);
		string GetPackedPackageFileName(string packageName);
		string GetPackedGroupPackagesFileName(string groupPackagesName);
		void CheckPackedPackageExistsAndNotEmpty(string packedPackagePath);
		IEnumerable<string> FindGzipPackedPackagesFiles(string searchDirectory);
		void Pack(string packagePath, string packedPackagePath, bool skipPdb, bool overwrite = true);
		void Pack(string sourcePath, string destinationPath, IEnumerable<string> names, bool skipPdb, bool overwrite = true);
		void Unpack(string packedPackagePath, bool overwrite, bool isShowDialogOverwrite = false,
			string destinationPath = null);
		void Unpack(IEnumerable<string> packedPackagesPaths, bool overwrite, bool isShowDialogOverwrite = false,
			string destinationPath = null);
		void ZipPackages(string sourceGzipFilesFolderPaths, string destinationArchiveFileName, bool overwrite);
		void UnZipPackages(string zipFilePath, bool overwrite, bool deleteGzFiles = true, 
			bool unpackIsSameFolder = false, bool isShowDialogOverwrite = false, string destinationPath = null);
		void UnZip(string zipFilePath, bool overwrite, string destinationPath = null);
		void ExtractPackages(string zipFilePath, bool overwrite, bool deleteGzFiles = true,
			bool unpackIsSameFolder = false, bool isShowDialogOverwrite = false, string destinationPath = null);

	}
}