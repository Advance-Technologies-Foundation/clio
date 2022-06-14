using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Clio.Common
{

	#region Class: FileSystem

	public class FileSystem : IFileSystem
	{

		#region Methods: Public

		public void CheckOrDeleteExistsFile(string filePath, bool delete) {
			if (!File.Exists(filePath)) {
				return;
			}
			if (delete) {
				DeleteFile(filePath);
			} else {
				throw new Exception($"The file {filePath} already exist");
			}
		}

		public void CopyFiles(IEnumerable<string> filesPaths, string destinationDirectory, bool overwrite) {
			filesPaths.CheckArgumentNull(nameof(filesPaths));
			destinationDirectory.CheckArgumentNullOrWhiteSpace(nameof(destinationDirectory));
			foreach (string sourceFilePath in filesPaths) {
				var sourceFileInfo = new FileInfo(sourceFilePath);
				string destinationFilePath = Path.Combine(destinationDirectory, sourceFileInfo.Name);
				File.Copy(sourceFilePath, destinationFilePath, overwrite);
			}
		}

		public bool DeleteFile(string filePath) {
			filePath.CheckArgumentNullOrWhiteSpace(nameof(filePath));
			if (IsReadOnlyFile(filePath)) {
				ResetFileReadOnlyAttribute(filePath);
			}
			File.Delete(filePath);
			return true;
		}

		public bool DeleteFileIfExists(string filePath) {
			if (!File.Exists(filePath)) {
				return false;
			}
			return DeleteFile(filePath);
		}

		public bool ExistsFile(string filePath) => File.Exists(filePath);

		public string ExtractNameFromPath(string filePath) {
			filePath.CheckArgumentNullOrWhiteSpace(nameof(filePath));
			var packageFileInfo = new FileInfo(filePath);
			return packageFileInfo.Name
				.Substring(0, packageFileInfo.Name.Length - packageFileInfo.Extension.Length);
		}

		public string[] GetFiles(string filePath) {
			filePath.CheckArgumentNullOrWhiteSpace(nameof(filePath));
			return Directory.GetFiles(filePath);
		}

		public string[] GetFiles(string filePath, string searchPattern, SearchOption searchOption) {
			filePath.CheckArgumentNullOrWhiteSpace(nameof(filePath));
			return Directory.GetFiles(filePath, searchPattern, searchOption);
		}

		public bool IsReadOnlyFile(string filePath) {
			if (!File.Exists(filePath)) {
				return false;
			}
			return (File.GetAttributes(filePath) & FileAttributes.ReadOnly) == FileAttributes.ReadOnly;
		}

		public void ResetFileReadOnlyAttribute(string filePath) {
			if (!File.Exists(filePath)) {
				return;
			}
			if (IsReadOnlyFile(filePath)) {
				File.SetAttributes(filePath, File.GetAttributes(filePath) & ~FileAttributes.ReadOnly);
			}
		}

		public void WriteAllTextToFile(string filePath, string contents) {
			File.WriteAllText(filePath, contents);
		}

		public void CheckOrClearExistsDirectory(string directoryPath, bool overwrite) {
			if (!Directory.Exists(directoryPath)) {
				return;
			}
			if (overwrite) {
				ClearDirectory(directoryPath);
			} else {
				throw new Exception($"The directory {directoryPath} already exist");
			}
		}

		public void CheckOrOverwriteExistsDirectory(string directoryPath, bool overwrite) {
			CheckOrClearExistsDirectory(directoryPath, overwrite);
			Directory.CreateDirectory(directoryPath);
		}

		public void ClearDirectory(string directoryPath) {
			directoryPath.CheckArgumentNullOrWhiteSpace(nameof(directoryPath));
			string[] files = GetFiles(directoryPath);
			foreach (string filePath in files) {
				DeleteFileIfExists(filePath);
			}
			string[] directories = GetDirectories(directoryPath);
			foreach (string childDirectoryPath in directories) {
				SafeDeleteDirectory(childDirectoryPath);
			}
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

		public DirectoryInfo CreateDirectory(string directoryPath) => Directory.CreateDirectory(directoryPath);

		public void DeleteDirectory(string directoryPath) {
			DeleteDirectory(directoryPath, false);
		}

		public void DeleteDirectory(string directoryPath, bool recursive) {
			directoryPath.CheckArgumentNullOrWhiteSpace(nameof(directoryPath));
			Directory.Delete(directoryPath, recursive);
		}

		public void DeleteDirectoryIfExists(string directoryPath) {
			directoryPath.CheckArgumentNull(nameof(directoryPath));
			if (Directory.Exists(directoryPath)) {
				Directory.Delete(directoryPath, true);
			}
		}

		public bool ExistsDirectory(string directoryPath) => Directory.Exists(directoryPath);

		public string GetCurrentDirectoryIfEmpty(string directoryPath) {
			return string.IsNullOrWhiteSpace(directoryPath)
				? Directory.GetCurrentDirectory()
				: directoryPath;
		}

		public string GetDestinationFileDirectory(string filePath, string destinationPath) {
			filePath.CheckArgumentNullOrWhiteSpace(nameof(filePath));
			destinationPath.CheckArgumentNullOrWhiteSpace(nameof(destinationPath));
			string fileName = ExtractNameFromPath(filePath);
			return Path.Combine(destinationPath, fileName);
		}

		public string[] GetDirectories(string directoryPath) {
			directoryPath.CheckArgumentNullOrWhiteSpace(nameof(directoryPath));
			return Directory.GetDirectories(directoryPath);
		}

		public void OverwriteExistsDirectory(string directoryPath) {
			directoryPath.CheckArgumentNullOrWhiteSpace(nameof(directoryPath));
			if (!Directory.Exists(directoryPath)) {
				return;
			}
			Directory.Delete(directoryPath, true);
			Directory.CreateDirectory(directoryPath);
		}

		public void SafeDeleteDirectory(string directoryPath) {
			directoryPath.CheckArgumentNullOrWhiteSpace(nameof(directoryPath));
			ClearDirectory(directoryPath);
			DeleteDirectory(directoryPath, recursive: false);
			while (ExistsDirectory(directoryPath)) {
				Thread.Sleep(0);
			}
		}

		public string ConvertToRelativePath(string path, string rootDirectoryPath) {
			rootDirectoryPath = rootDirectoryPath.TrimEnd(Path.DirectorySeparatorChar);
			int rootDirectoryPathLength = rootDirectoryPath.Length;
			string relativePath = path.Substring(rootDirectoryPathLength);
			return relativePath.TrimStart(Path.DirectorySeparatorChar);
		}

		public string NormalizeFilePathByPlatform(string filePath) {
			if (string.IsNullOrWhiteSpace(filePath)) {
				return filePath;
			}
			string[] filePathItem = filePath.Split(new char[] { '\\', '/' }, StringSplitOptions.None);
			return Path.Combine(filePathItem);
		}


		#endregion

	}

	#endregion

}