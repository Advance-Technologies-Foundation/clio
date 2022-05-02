using System;
using System.IO;
using Clio;
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

	public class ExtractPackageCommand
	{

		#region Methods: Private

		private static string GetCorrectedPackagePath(UnzipPkgOptions options) {
			var packagePath = options.Name;
			if (!packagePath.EndsWith(".gz")) {
				packagePath += ".gz";
			}
			return packagePath;
		}

		private static bool ShowDialogOverwriteDestinationPackageDir(string destinationPackagePath) {
			bool overwrite = true;
			if (Directory.Exists(destinationPackagePath)) {
				Console.Write($"Directory {destinationPackagePath} already exist. Do you want replace it (y/n)? ");
				var key = Console.ReadKey();
				Console.WriteLine();
				overwrite = key.KeyChar == 'y';
			} else {
				Directory.CreateDirectory(destinationPackagePath);
			}
			return overwrite;
		}

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

		#endregion

		#region Methods: Public

		public static int ExtractPackage(UnzipPkgOptions options, IPackageArchiver packageArchiver, 
				IPackageUtilities packageUtilities, IFileSystem fileSystem) {
			try
			{
				FileInfo[] pkgFiles;
				if (CheckDirectory(options.Name, out pkgFiles))
				{
                    foreach (var item in pkgFiles)
                    {
						var destinationDirectory = fileSystem.GetCurrentDirectoryIfEmpty(options.DestinationPath);
						string packageName = fileSystem.ExtractNameFromPath(item.FullName);
						var destinationPackagePath = Path.Combine(destinationDirectory, packageName);
						Console.WriteLine($"Start unzip package ({packageName}).");
						var overwrite = ShowDialogOverwriteDestinationPackageDir(destinationPackagePath);
						packageArchiver.Unpack(item.FullName, overwrite, destinationDirectory);
						Console.WriteLine($"Unzip package ({packageName}) completed.");
					}
				}
				else
				{
					var packagePath = GetCorrectedPackagePath(options);
					var destinationDirectory = fileSystem.GetCurrentDirectoryIfEmpty(options.DestinationPath);
					string packageName = fileSystem.ExtractNameFromPath(packagePath);
					var destinationPackagePath = Path.Combine(destinationDirectory, packageName);
					Console.WriteLine($"Start unzip package ({packageName}).");
					var overwrite = ShowDialogOverwriteDestinationPackageDir(destinationPackagePath);
					packageArchiver.Unpack(packagePath, overwrite, destinationDirectory);
					Console.WriteLine($"Unzip package ({packageName}) completed.");
				}
				return 0;
			}
			catch (Exception e)
			{
				Console.WriteLine($"{e.Message}");
				return 1;
			}
		}

		#endregion

	}

	#endregion

}
