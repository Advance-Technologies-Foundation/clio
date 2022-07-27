using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Clio.Common;

namespace Clio
{

	#region Class: PackageArchiver

	public class PackageArchiver : IPackageArchiver
	{

		#region Constants: Public

		public const string GzExtension = "gz";
		public const string ZipExtension = "zip";

		#endregion

		#region Fields: Private

		private readonly IFileSystem _fileSystem;
		private readonly IPackageUtilities _packageUtilities;
		private readonly ICompressionUtilities _compressionUtilities;
		private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
		private readonly ILogger _logger;

		#endregion

		#region Constructors: Public

		public PackageArchiver(IPackageUtilities packageUtilities, ICompressionUtilities compressionUtilities, 
				IWorkingDirectoriesProvider workingDirectoriesProvider, IFileSystem fileSystem, ILogger logger) {
			packageUtilities.CheckArgumentNull(nameof(packageUtilities));
			compressionUtilities.CheckArgumentNull(nameof(compressionUtilities));
			workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			logger.CheckArgumentNull(nameof(logger));
			_packageUtilities = packageUtilities;
			_compressionUtilities = compressionUtilities;
			_workingDirectoriesProvider = workingDirectoriesProvider;
			_fileSystem = fileSystem;
			_logger = logger;
		}

		#endregion

		#region Methods: Private

		private static void CheckPackArgument(string packagePath, string packedPackagePath) {
			packagePath.CheckArgumentNullOrWhiteSpace(nameof(packagePath));
			packedPackagePath.CheckArgumentNullOrWhiteSpace(nameof(packedPackagePath));
		}

		private static void CheckUnpackArgument(string packedPackagePath) {
			packedPackagePath.CheckArgumentNullOrWhiteSpace(nameof(packedPackagePath));
		}

		private static IEnumerable<string> GetAllFiles(string tempPath, bool skipPdb) {
			var files = Directory
				.GetFiles(tempPath, "*.*", SearchOption.AllDirectories)
				.Where(name => !name.EndsWith(".pdb") || !skipPdb);
			return files;
		}

		private static void CheckZipPackagesArgument(string sourceGzipFilesFolderPaths, 
				string destinationArchiveFileName) {
			sourceGzipFilesFolderPaths.CheckArgumentNullOrWhiteSpace(nameof(sourceGzipFilesFolderPaths));
			destinationArchiveFileName.CheckArgumentNullOrWhiteSpace(nameof(destinationArchiveFileName));
		}

		private static void CheckUnZipPackagesArgument(string zipFilePath) {
			zipFilePath.CheckArgumentNullOrWhiteSpace(nameof(zipFilePath));
		}

		private static void DeletePackedPackages(string[] packedPackagesPaths) {
			foreach (string packedPackagePath in packedPackagesPaths) {
				File.Delete(packedPackagePath);
			}
		}

		#endregion

		#region Methods: Public

		public string GetPackedPackageFileName(string packageName) => $"{packageName}.{GzExtension}";
		public string GetPackedGroupPackagesFileName(string groupPackagesName) => $"{groupPackagesName}.{ZipExtension}";

		public void CheckPackedPackageExistsAndNotEmpty(string packedPackagePath) {
			if (!File.Exists(packedPackagePath)) {
				throw new Exception($"Package archive {packedPackagePath} not found");
			}
			var fileInfo = new FileInfo(packedPackagePath);
			if (fileInfo.Length == 0) {
				throw new Exception($"Package archive {packedPackagePath} is empty");
			}
		}
		
		public IEnumerable<string> FindGzipPackedPackagesFiles(string searchDirectory) {
			return Directory.EnumerateFiles(searchDirectory, $"*.{GzExtension}", 
				SearchOption.AllDirectories);
		}

		public void Pack(string packagePath, string packedPackagePath, bool skipPdb, bool overwrite) {
			CheckPackArgument(packagePath, packedPackagePath);
			_fileSystem.CheckOrDeleteExistsFile(packedPackagePath, overwrite);
			_workingDirectoriesProvider.CreateTempDirectory(tempPath => {
				_packageUtilities.CopyPackageElements(packagePath, tempPath, overwrite);
				var files = GetAllFiles(tempPath, skipPdb);
				_compressionUtilities.PackToGZip(files, tempPath, packedPackagePath);
			}); 
		}

		public void Pack(string sourcePath, string destinationPath, IEnumerable<string> names, bool skipPdb, 
				bool overwrite) {
			_workingDirectoriesProvider.CreateTempDirectory(tempPath => {
				sourcePath ??= Environment.CurrentDirectory;
				foreach (var name in names)
				{
					var currentSourcePath = Path.Combine(sourcePath, name);
					var currentDestinationPath = Path.Combine(tempPath, name + ".gz");
					Pack(currentSourcePath, currentDestinationPath, skipPdb, overwrite);
				}
				ZipFile.CreateFromDirectory(tempPath, destinationPath);
			});
		}

		public void Unpack(string packedPackagePath, bool overwrite, string destinationPath = null) {
			CheckUnpackArgument(packedPackagePath);
			CheckPackedPackageExistsAndNotEmpty(packedPackagePath);
			destinationPath = _fileSystem.GetCurrentDirectoryIfEmpty(destinationPath);
			string destinationPackageDirectory = 
				_fileSystem.GetDestinationFileDirectory(packedPackagePath, destinationPath);
			_fileSystem.CheckOrOverwriteExistsDirectory(destinationPackageDirectory, overwrite);
			_compressionUtilities.UnpackFromGZip(packedPackagePath, destinationPackageDirectory);
		}

		public void Unpack(IEnumerable<string> packedPackagesPaths, bool overwrite, string destinationPath = null) {
			packedPackagesPaths.CheckArgumentNull(nameof(packedPackagesPaths));
			destinationPath = _fileSystem.GetCurrentDirectoryIfEmpty(destinationPath);
			foreach (var packedPackagePath in packedPackagesPaths) {
				string packageName = _fileSystem.ExtractNameFromPath(packedPackagePath);
				_logger.WriteLine($"Start unzip package ({packageName}).");
				Unpack(packedPackagePath, overwrite, destinationPath);
				_logger.WriteLine($"Unzip package ({packageName}) completed.");
			}
		}
		
		public void ZipPackages(string sourceGzipFilesFolderPaths, string destinationArchiveFileName, bool overwrite) {
			CheckZipPackagesArgument(sourceGzipFilesFolderPaths, destinationArchiveFileName);
			_fileSystem.CheckOrDeleteExistsFile(destinationArchiveFileName, overwrite);
			ZipFile.CreateFromDirectory(sourceGzipFilesFolderPaths, destinationArchiveFileName);
		}

		public void UnZipPackages(string zipFilePath, bool overwrite, bool deleteGzFiles = true, 
				bool unpackIsSameFolder = false, string destinationPath = null) {
			CheckUnZipPackagesArgument(zipFilePath);
			destinationPath = _fileSystem.GetCurrentDirectoryIfEmpty(destinationPath);
			CheckPackedPackageExistsAndNotEmpty(zipFilePath);
			string targetDirectoryPath = unpackIsSameFolder
				? destinationPath
				: _fileSystem.GetDestinationFileDirectory(zipFilePath, destinationPath);
			if (overwrite) {
				_fileSystem.OverwriteExistsDirectory(targetDirectoryPath);
			}
			ZipFile.ExtractToDirectory(zipFilePath, targetDirectoryPath);
			string[] packedPackagesPaths = Directory.GetFiles(targetDirectoryPath);
			 Unpack(packedPackagesPaths, true, targetDirectoryPath);
			if (deleteGzFiles) {
				DeletePackedPackages(packedPackagesPaths);
			}
		}

		public void ExtractPackages(string zipFilePath, bool overwrite, bool deleteGzFiles = true,
				bool unpackIsSameFolder = false, string destinationPath = null) {
			CheckUnZipPackagesArgument(zipFilePath);
			destinationPath = _fileSystem.GetCurrentDirectoryIfEmpty(destinationPath);
			CheckPackedPackageExistsAndNotEmpty(zipFilePath);
			string targetDirectoryPath = unpackIsSameFolder
				? destinationPath
				: Environment.CurrentDirectory;
			ZipFile.ExtractToDirectory(zipFilePath, targetDirectoryPath);
			string[] packedPackagesPaths = Directory.GetFiles(targetDirectoryPath, "*.gz");
			Unpack(packedPackagesPaths, true, targetDirectoryPath);
			if (deleteGzFiles) {
				DeletePackedPackages(packedPackagesPaths);
			}
		}

		public void UnZip(string zipFilePath, bool overwrite, string destinationPath = null) {
			CheckUnZipPackagesArgument(zipFilePath);
			CheckPackedPackageExistsAndNotEmpty(zipFilePath);
			ZipFile.ExtractToDirectory(zipFilePath, Environment.CurrentDirectory);
		}



		#endregion

	}

	#endregion

}