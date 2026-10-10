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
		/// <summary>
		/// Packs one package folder into a <c>.gz</c> package archive.
		/// </summary>
		/// <param name="packagePath">Package folder; a <c>branches/&lt;version&gt;</c> layout is resolved to its single version.</param>
		/// <param name="packedPackagePath">Archive to write.</param>
		/// <param name="skipPdb">Whether <c>.pdb</c> files are left out.</param>
		/// <param name="overwrite">Whether an existing archive at <paramref name="packedPackagePath"/> is replaced.</param>
		/// <exception cref="Clio.Package.PackageItemDescriptorMissingException">
		/// A folder under <c>Schemas/</c> or <c>Data/</c> would be packed without <c>descriptor.json</c>, which Creatio
		/// rejects with "Invalid descriptor". No archive is written and an existing one is left in place.
		/// </exception>
		void Pack(string packagePath, string packedPackagePath, bool skipPdb, bool overwrite = true);

		/// <summary>
		/// Packs several package folders, each into its own <c>.gz</c>, and zips them into one archive.
		/// </summary>
		/// <param name="sourcePath">Folder that holds the packages; the current directory when <c>null</c>.</param>
		/// <param name="destinationPath">Zip archive to write.</param>
		/// <param name="names">Package folder names under <paramref name="sourcePath"/>.</param>
		/// <param name="skipPdb">Whether <c>.pdb</c> files are left out.</param>
		/// <param name="overwrite">Whether existing package archives are replaced.</param>
		/// <exception cref="Clio.Package.PackageItemDescriptorMissingException">
		/// One or more packages hold a <c>Schemas/</c> or <c>Data/</c> folder without <c>descriptor.json</c>. Every
		/// package is checked first, so the exception names the folders of all of them, and no zip is written.
		/// </exception>
		void Pack(string sourcePath, string destinationPath, IEnumerable<string> names, bool skipPdb, bool overwrite = true);
		void Unpack(string packedPackagePath, bool overwrite, bool isShowDialogOverwrite = false,
			string destinationPath = null);
		void Unpack(IEnumerable<string> packedPackagesPaths, bool overwrite, bool isShowDialogOverwrite = false,
			string destinationPath = null);
		void ZipPackages(string sourceGzipFilesFolderPaths, string destinationArchiveFileName, bool overwrite);
		void UnZipPackages(string zipFilePath, bool overwrite, bool deleteGzFiles = true, 
			bool unpackIsSameFolder = false, bool isShowDialogOverwrite = false, string destinationPath = null);
		
		/// <inheritdoc cref="System.IO.Compression.ZipFile.ExtractToDirectory(string, string)"/>
		void UnZip(string zipFilePath, bool overwrite, string destinationPath = null);
		void ExtractPackages(string zipFilePath, bool overwrite, bool deleteGzFiles = true,
			bool unpackIsSameFolder = false, bool isShowDialogOverwrite = false, string destinationPath = null);

	}
}