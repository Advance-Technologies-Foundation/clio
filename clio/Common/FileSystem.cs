using System;
using System.IO;

namespace Clio.Common
{

	#region Class: FileSystem

	public class FileSystem : IFileSystem
	{

		#region Methods: Public
		
		public void CheckOrDeleteExistsFile(string filePath, bool delete) {
			filePath.CheckArgumentNullOrWhiteSpace(nameof(filePath));
			if (File.Exists(filePath)) {
				if (delete) {
					File.Delete(filePath);
				} else {
					throw new Exception($"The file {filePath} already exist");
				}
			}
		}

		public void CheckOrDeleteExistsDirectory(string directoryPath, bool delete) {
			directoryPath.CheckArgumentNullOrWhiteSpace(nameof(directoryPath));
			if (Directory.Exists(directoryPath)) {
				if (delete) {
					Directory.Delete(directoryPath, true);
				} else {
					throw new Exception($"The directory {directoryPath} already exist");
				}
			}
		}

		public void CheckOrOverwriteExistsDirectory(string directoryPath, bool overwrite) {
			directoryPath.CheckArgumentNullOrWhiteSpace(nameof(directoryPath));
			CheckOrDeleteExistsDirectory(directoryPath, overwrite);
			Directory.CreateDirectory(directoryPath);
		}

		public string GetCurrentDirectoryIfEmpty(string directory) {
			return string.IsNullOrWhiteSpace(directory)
				? Directory.GetCurrentDirectory()
				: directory;
		}

		public string ExtractNameFromPath(string path) {
			path.CheckArgumentNullOrWhiteSpace(nameof(path));
			var packageFileInfo = new FileInfo(path);
			return packageFileInfo.Name
				.Substring(0, packageFileInfo.Name.Length - packageFileInfo.Extension.Length);
		}

		public string GetDestinationFileDirectory(string filePath, string destinationPath) {
			filePath.CheckArgumentNullOrWhiteSpace(nameof(filePath));
			destinationPath.CheckArgumentNullOrWhiteSpace(nameof(destinationPath));
			string fileName = ExtractNameFromPath(filePath);
			return Path.Combine(destinationPath, fileName);
		}

		public void CopyDirectory(string source, string destination, bool overwrite) {
			source.CheckArgumentNullOrWhiteSpace(nameof(source));
			destination.CheckArgumentNullOrWhiteSpace(nameof(destination));
			CheckOrOverwriteExistsDirectory(destination, overwrite);
			foreach (string filePath in Directory.GetFiles(source)) {
				File.Copy(filePath, Path.Combine(destination, Path.GetFileName(filePath)), true);
			}
			foreach (string directoryPath in Directory.GetDirectories(source)) {
				CopyDirectory(directoryPath, Path.Combine(destination, Path.GetFileName(directoryPath)), overwrite);
			}
		}

		public void DeleteDirectoryIfExists(string path) {
			path.CheckArgumentNull(nameof(path));
			if (Directory.Exists(path)) {
				Directory.Delete(path, true);
			}
		}

		public string ConvertToRelativePath(string path, string rootDirectoryPath) {
			rootDirectoryPath = rootDirectoryPath.TrimEnd(Path.DirectorySeparatorChar);
			int rootDirectoryPathLength = rootDirectoryPath.Length;
			string relativePath = path.Substring(rootDirectoryPathLength);
			return relativePath;
		}

		#endregion

	}

	#endregion

}