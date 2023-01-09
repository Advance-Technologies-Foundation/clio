using System;
using System.IO;
using Clio;
using Clio.Command;
using Clio.Common;
using CommandLine;

namespace Сlio.Command.PackageCommand
{

	#region Class: UnzipPkgOptions

	[Verb("extract-pkg-zip", Aliases = new string[] { "extract", "unzip" }, HelpText = "Prepare an archive of creatio package")]
	public class UnzipPkgOptions
	{
		[Value(0, MetaName = "Name", Required = false, HelpText = "Name of the compressed package")]
		public string Name { get; set; }

		[Option('d', "DestinationPath", Required = false, HelpText = "Destination path for package folder")]
		public string DestinationPath { get; set; }
	}

	#endregion

	#region Class: ExtractPackageCommand

	public class ExtractPackageCommand  : Command<UnzipPkgOptions>
	{

		#region Fields: Private

		private readonly IPackageArchiver _packageArchiver; 
		private readonly IPackageUtilities _packageUtilities;
		private readonly IFileSystem _fileSystem;
		
		#endregion
		
		#region Constructors: Public

		public ExtractPackageCommand(IPackageArchiver packageArchiver, IPackageUtilities packageUtilities,
			IFileSystem fileSystem) {
			packageArchiver.CheckArgumentNull(nameof(packageArchiver));
			packageUtilities.CheckArgumentNull(nameof(packageUtilities));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			_packageArchiver = packageArchiver;
			_packageUtilities = packageUtilities;
			_fileSystem = fileSystem;
		}

		#endregion

		#region Methods: Private

		private static bool CheckDirectory(string dir, out FileInfo[] files) {
			bool result = false;
			files = null;
			if (Directory.Exists(dir)) {
				DirectoryInfo directoryInfo = new DirectoryInfo(dir);
				files = directoryInfo.GetFiles("*.gz");
				result = files.Length > 0;
			}
			return result;
		}

		private string GetCorrectedPackagePath(UnzipPkgOptions options) {
			var packagePath = options.Name;
			if (!_packageArchiver.IsGzArchive(packagePath) && !_packageArchiver.IsZipArchive(packagePath)) {
				packagePath += ".gz";
			}
			return packagePath;
		}

		private void Unpack(string destinationPath, string packagePath, bool isShowDialogOverwrite = false) {
			var destinationDirectory = _fileSystem.GetCurrentDirectoryIfEmpty(destinationPath);
			string packageName = _fileSystem.ExtractFileNameFromPath(packagePath);
			var destinationPackagePath = Path.Combine(destinationDirectory, packageName);
			Console.WriteLine($"Start unzip package ({packageName}).");
			if (_packageArchiver.IsZipArchive(packagePath)) {
				_packageArchiver.UnZipPackages(packagePath, true, true, true,
					isShowDialogOverwrite, destinationDirectory);
			} else {
				_packageArchiver.Unpack(packagePath, true, isShowDialogOverwrite, destinationDirectory);
			}
			Console.WriteLine($"Unzip package ({packageName}) completed.");
		}

		#endregion

		#region Methods: Public

		public override int Execute(UnzipPkgOptions options) {
			try {
				FileInfo[] pkgFiles;
				if (CheckDirectory(options.Name, out pkgFiles)) {
                    foreach (var item in pkgFiles) {
						Unpack(options.DestinationPath, item.FullName, true);
					}
				} else {
					var packagePath = GetCorrectedPackagePath(options);
					Unpack(options.DestinationPath, packagePath, true);
				}
				return 0;
			} catch (Exception e) {
				Console.WriteLine($"{e.Message}");
				return 1;
			}
		}

		#endregion

	}

	#endregion

}
