using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace Clio.Common
{

	#region Class: FileSystem

	public class FileSystem : IFileSystem
	{

		#region Methods: Public

		public static void CreateLink(string link, string target) {
			Process mklinkProcess = Process.Start(
				new ProcessStartInfo("cmd", $"/c mklink /D \"{link}\" \"{target}\"") {
					CreateNoWindow = true
				});
			mklinkProcess.WaitForExit();
		}

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

		public string ExtractFileNameFromPath(string filePath) {
			filePath.CheckArgumentNullOrWhiteSpace(nameof(filePath));
			var packageFileInfo = new FileInfo(filePath);
			return GetFileNameWithoutExtension(packageFileInfo);
		}

		public string ExtractFileExtensionFromPath(string filePath) {
			filePath.CheckArgumentNullOrWhiteSpace(nameof(filePath));
			var fileInfo = new FileInfo(filePath);
			return fileInfo.Extension;
		}

		public string GetFileNameWithoutExtension(FileInfo fileInfo) {
			fileInfo.CheckArgumentNull(nameof(fileInfo));
			return fileInfo.Name
				.Substring(0, fileInfo.Name.Length - fileInfo.Extension.Length);
		}

		public string[] GetFiles(string directoryPath) {
			directoryPath.CheckArgumentNullOrWhiteSpace(nameof(directoryPath));
			return Directory.GetFiles(directoryPath);
		}

		public string[] GetFiles(string directoryPath, string searchPattern, SearchOption searchOption) {
			directoryPath.CheckArgumentNullOrWhiteSpace(nameof(directoryPath));
			return Directory.GetFiles(directoryPath, searchPattern, searchOption);
		}

		public FileInfo[] GetFilesInfos(string directoryPath, string searchPattern, SearchOption searchOption) {
			directoryPath.CheckArgumentNullOrWhiteSpace(nameof(directoryPath));
			var directoryInfo = new DirectoryInfo(directoryPath);

			//TODO: Discuss with P.Makarchuk
			//directoryInfo.GetFiles causes System.IO.DirectoryNotFoundException when Schemas does not exist 
			if (directoryInfo.Exists){
				return directoryInfo.GetFiles(searchPattern, searchOption);	
			}
			return new FileInfo[0] ;
		}

		public bool IsReadOnlyFile(string filePath) {
			if (!File.Exists(filePath)) {
				return false;
			}
			return (File.GetAttributes(filePath) & FileAttributes.ReadOnly) == FileAttributes.ReadOnly;
		}

		public void MoveFile(string oldFilePath, string newFilePath) =>
			File.Move(oldFilePath, newFilePath);

		public void ResetFileReadOnlyAttribute(string filePath) {
			if (!File.Exists(filePath)) {
				return;
			}
			if (IsReadOnlyFile(filePath)) {
				File.SetAttributes(filePath, File.GetAttributes(filePath) & ~FileAttributes.ReadOnly);
			}
		}

		public string ReadAllText(string filePath) => File.ReadAllText(filePath);

		public void WriteAllTextToFile(string filePath, string contents) {
			File.WriteAllText(filePath, contents);
		}

		public void ClearOrCreateDirectory(string directoryPath) {
			if (Directory.Exists(directoryPath)) {
				ClearDirectory(directoryPath);
			}
			Directory.CreateDirectory(directoryPath);
		}

		public void CreateOrOverwriteExistsDirectoryIfNeeded(string directoryPath, bool overwrite) {
			if (!Directory.Exists(directoryPath)) {
				Directory.CreateDirectory(directoryPath);
				return;
			}
			if (!overwrite) {
				return;
			}
			ClearDirectory(directoryPath);
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
			CreateOrOverwriteExistsDirectoryIfNeeded(destination, overwrite);
			foreach (string filePath in Directory.GetFiles(source)) {
				File.Copy(filePath, Path.Combine(destination, Path.GetFileName(filePath)), true);
			}
			foreach (string directoryPath in Directory.GetDirectories(source)) {
				CopyDirectory(directoryPath, Path.Combine(destination, Path.GetFileName(directoryPath)), overwrite);
			}
		}

		public DirectoryInfo CreateDirectory(string directoryPath) => Directory.CreateDirectory(directoryPath);

		public void CreateDirectoryIfNotExists(string directoryPath) {
			if (Directory.Exists(directoryPath)) {
				return;
			}
			Directory.CreateDirectory(directoryPath);
		}

		public void CreateOrClearDirectory(string directoryPath) {
			if (Directory.Exists(directoryPath)) {
				ClearDirectory(directoryPath);
			} else {
				Directory.CreateDirectory(directoryPath);
			}
		}

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

		public bool IsEmptyDirectory() =>
			!Directory.GetFileSystemEntries(Directory.GetCurrentDirectory()).Any();

		public string GetDestinationFileDirectory(string filePath, string destinationPath) {
			filePath.CheckArgumentNullOrWhiteSpace(nameof(filePath));
			destinationPath.CheckArgumentNullOrWhiteSpace(nameof(destinationPath));
			string fileName = ExtractFileNameFromPath(filePath);
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