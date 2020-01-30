using System;
using System.IO;
using System.IO.Compression;
using Clio.Common;
using CommandLine;

namespace Сlio.Command.PackageCommand
{
	[Verb("extract-pkg-zip", Aliases = new string[] { "extract", "unzip" }, HelpText = "Prepare an archive of creatio package")]
	public class UnzipPkgOptions
	{
		[Value(0, MetaName = "Name", Required = false, HelpText = "Name of the compressed package")]
		public string Name { get; set; }

		[Option('d', "DestinationPath", Required = false, HelpText = "Destination path for package folder")]
		public string DestinationPath { get; set; }
	}


	public class ExtractPackageCommand
	{
		public static int ExtractPackage(UnzipPkgOptions options) {
			var packageFile = options.Name;
			if (!packageFile.EndsWith(".gz")) {
				packageFile += ".gz";
			}

			if (!File.Exists(packageFile)) { 
				throw new Exception($"Package archive {packageFile} not found");
			}
			var fileInfo = new FileInfo(packageFile);
			string destinationFolder = String.IsNullOrEmpty(options.DestinationPath) ? Directory.GetCurrentDirectory() : options.DestinationPath;
			string packageName = fileInfo.Name.Substring(0, fileInfo.Name.Length - 3);
			var folderPath = Path.Combine(destinationFolder, packageName);
			try {
				if (Directory.Exists(folderPath)) {
					Console.Write($"Folder {folderPath} already exist. Do you want replace it (y/n)? ");
					var key = Console.ReadKey();
					Console.WriteLine();
					if (key.KeyChar == 'y') {
						Directory.Delete(folderPath, true);
					} else {
						throw new Exception($"Folder {folderPath} already exist");
					}
				} else {
					Directory.CreateDirectory(folderPath);
				}
				Console.WriteLine($"Start unzip package ({packageName}).");
				using (var fileStream = new FileStream(packageFile, FileMode.Open, FileAccess.Read, FileShare.None)) {
					using (var zipStream = new GZipStream(fileStream, CompressionMode.Decompress, true)) {
						while (CompressionUtilities.UnzipFile(folderPath, zipStream)) {
						}
					}
				}
				Console.WriteLine($"Unzip package ({packageName}) completed.");
				return 0;
			} catch (Exception e) {
				Console.WriteLine($"{e.Message}");
				return 1;
			}
		}
	}
}
