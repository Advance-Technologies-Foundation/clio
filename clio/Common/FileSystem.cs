using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Ms = System.IO.Abstractions;

namespace Clio.Common;

#region Class: FileSystem

public class FileSystem(Ms.IFileSystem msFileSystem) : IFileSystem {
	#region Class: Nested

	public enum Algorithm{
		SHA1,
		SHA256,
		SHA384,
		SHA512,
		MD5
	}

	#endregion

	internal static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

	#region Methods: Public

	public void CreateLink(string link, string target) {
		msFileSystem.Directory.CreateSymbolicLink(link, target);
	}

	public void AppendTextToFile(string filePath, string contents, Encoding encoding = null) {
		msFileSystem.File.AppendAllText(filePath, contents, encoding ?? Utf8NoBom);
	}

	public void CheckOrDeleteExistsFile(string filePath, bool delete) {
		if (!msFileSystem.File.Exists(filePath)) {
			return;
		}

		if (delete) {
			DeleteFile(filePath);
		}
		else {
			throw new Exception($"The file {filePath} already exist");
		}
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

	public void ClearOrCreateDirectory(string directoryPath) {
		if (msFileSystem.Directory.Exists(directoryPath)) {
			ClearDirectory(directoryPath);
		}

		msFileSystem.Directory.CreateDirectory(directoryPath);
	}

	public bool CompareFiles(string first, string second) {
		return CompareFiles(Algorithm.MD5, first, second);
	}


	public bool CompareFiles(Algorithm algorithm, string first, string second) {
		if (!msFileSystem.File.Exists(first) || !msFileSystem.File.Exists(second)) {
			return false;
		}

		return GetFileHash(algorithm, first) == GetFileHash(algorithm, second);
	}

	public string ConvertToRelativePath(string path, string rootDirectoryPath) {
		rootDirectoryPath = rootDirectoryPath.TrimEnd(Path.DirectorySeparatorChar);
		int rootDirectoryPathLength = rootDirectoryPath.Length;
		string relativePath = path.Substring(rootDirectoryPathLength);
		return relativePath.TrimStart(Path.DirectorySeparatorChar);
	}

	public void CopyDirectory(string source, string destination, bool overwrite) {
		source.CheckArgumentNullOrWhiteSpace(nameof(source));
		destination.CheckArgumentNullOrWhiteSpace(nameof(destination));
		CreateOrOverwriteExistsDirectoryIfNeeded(destination, overwrite);
		foreach (string filePath in msFileSystem.Directory.GetFiles(source)) {
			msFileSystem.File.Copy(filePath, Path.Combine(destination, Path.GetFileName(filePath)), true);
		}

		foreach (string directoryPath in msFileSystem.Directory.GetDirectories(source)) {
			CopyDirectory(directoryPath, Path.Combine(destination, Path.GetFileName(directoryPath)), overwrite);
		}
	}

	public void CopyDirectoryWithFilter(string source, string destination, bool overwrite, Func<string, bool> filter) {
		source.CheckArgumentNullOrWhiteSpace(nameof(source));
		destination.CheckArgumentNullOrWhiteSpace(nameof(destination));
		CreateOrOverwriteExistsDirectoryIfNeeded(destination, overwrite);
		foreach (string filePath in msFileSystem.Directory.GetFiles(source)) {
			if (!filter(filePath)) {
				msFileSystem.File.Copy(filePath, Path.Combine(destination, Path.GetFileName(filePath)), true);
			}
		}

		foreach (string directoryPath in msFileSystem.Directory.GetDirectories(source)) {
			if (!filter(directoryPath)) {
				CopyDirectory(directoryPath, Path.Combine(destination, Path.GetFileName(directoryPath)), overwrite);
			}
		}
	}

	public void CopyFile(string from, string to, bool overwrite) {
		msFileSystem.File.Copy(from, to, overwrite);
	}

	public void CopyFiles(IEnumerable<string> filesPaths, string destinationDirectory, bool overwrite) {
		filesPaths.CheckArgumentNull(nameof(filesPaths));
		destinationDirectory.CheckArgumentNullOrWhiteSpace(nameof(destinationDirectory));
		foreach (string sourceFilePath in filesPaths) {
			Ms.IFileInfoFactory fileInfoFactory = msFileSystem.FileInfo;
			Ms.IFileInfo sourceFileInfo = fileInfoFactory.New(sourceFilePath);
			string destinationFilePath = Path.Combine(destinationDirectory, sourceFileInfo.Name);
			msFileSystem.File.Copy(sourceFilePath, destinationFilePath, overwrite);
		}
	}


	public Ms.IDirectoryInfo CreateDirectory(string directoryPath, bool throwWhenExists = false) {
		if (throwWhenExists && ExistsDirectory(directoryPath)) {
			throw new ArgumentException($"Directory {directoryPath} already exists");
		}

		return msFileSystem.Directory.CreateDirectory(directoryPath);
	}

	public void CreateDirectoryIfNotExists(string directoryPath) {
		if (msFileSystem.Directory.Exists(directoryPath)) {
			return;
		}

		msFileSystem.Directory.CreateDirectory(directoryPath);
	}

	public Ms.IFileSystemInfo CreateDirectorySymLink(string path, string pathToTarget) {
		return msFileSystem.Directory.CreateSymbolicLink(path, pathToTarget);
	}

	public Ms.FileSystemStream CreateFile(string filePath) {
		return msFileSystem.File.Create(filePath);
	}

	public Ms.IFileSystemInfo CreateFileSymLink(string path, string pathToTarget) {
		return msFileSystem.File.CreateSymbolicLink(path, pathToTarget);
	}

	public void CreateOrClearDirectory(string directoryPath) {
		if (msFileSystem.Directory.Exists(directoryPath)) {
			ClearDirectory(directoryPath);
		}
		else {
			msFileSystem.Directory.CreateDirectory(directoryPath);
		}
	}

	public void CreateOrOverwriteExistsDirectoryIfNeeded(string directoryPath, bool overwrite) {
		if (!msFileSystem.Directory.Exists(directoryPath)) {
			msFileSystem.Directory.CreateDirectory(directoryPath);
			return;
		}

		if (!overwrite) {
			return;
		}

		ClearDirectory(directoryPath);
	}


	public Ms.IFileSystemInfo CreateSymLink(string path, string pathToTarget) {
		path.CheckArgumentNullOrWhiteSpace(nameof(path));
		pathToTarget.CheckArgumentNullOrWhiteSpace(nameof(pathToTarget));
		if (msFileSystem.File.Exists(path)) {
			return CreateFileSymLink(path, pathToTarget);
		}

		if (msFileSystem.Directory.Exists(path)) {
			return CreateDirectorySymLink(path, pathToTarget);
		}

		throw new ArgumentOutOfRangeException(nameof(path), $"Path {path} does not exist");
	}

	public void DeleteDirectory(string directoryPath) {
		DeleteDirectory(directoryPath, false);
	}

	public void DeleteDirectory(string directoryPath, bool recursive) {
		directoryPath.CheckArgumentNullOrWhiteSpace(nameof(directoryPath));
		msFileSystem.Directory.Delete(directoryPath, recursive);
	}

	public void DeleteDirectoryIfExists(string directoryPath) {
		directoryPath.CheckArgumentNull(nameof(directoryPath));
		if (msFileSystem.Directory.Exists(directoryPath)) {
			msFileSystem.Directory.Delete(directoryPath, true);
		}
	}

	public bool DeleteFile(string filePath) {
		filePath.CheckArgumentNullOrWhiteSpace(nameof(filePath));
		if (IsReadOnlyFile(filePath)) {
			ResetFileReadOnlyAttribute(filePath);
		}

		msFileSystem.File.Delete(filePath);

		//TODO: Discuss with P.Makarchuk
		//why return type is bool when always true
		return true;
	}


	public bool DeleteFileIfExists(string filePath) {
		return msFileSystem.File.Exists(filePath) && DeleteFile(filePath);
	}

	/// <summary>
	///     Checks if directory exists
	/// </summary>
	/// <param name="directoryPath"></param>
	/// <returns></returns>
	public bool ExistsDirectory(string directoryPath) {
		return msFileSystem.Directory.Exists(directoryPath);
	}

	public bool ExistsFile(string filePath) {
		return msFileSystem.File.Exists(filePath);
	}

	public string ExtractFileExtensionFromPath(string filePath) {
		filePath.CheckArgumentNullOrWhiteSpace(nameof(filePath));

		//var fileInfo = new FileInfo(filePath);
		Ms.IFileInfoFactory fileInfoFactory = msFileSystem.FileInfo;
		Ms.IFileInfo fileInfo = fileInfoFactory.New(filePath);
		return fileInfo.Extension;
	}

	public string ExtractFileNameFromPath(string filePath) {
		filePath.CheckArgumentNullOrWhiteSpace(nameof(filePath));
		FileInfo packageFileInfo = new(filePath);
		return GetFileNameWithoutExtension(packageFileInfo);
	}

	public Ms.FileSystemStream FileOpenStream(string filePath, FileMode mode, FileAccess access, FileShare share) {
		return msFileSystem.File.Open(filePath, mode, access, share);
	}

	public string GetCurrentDirectoryIfEmpty(string directoryPath) {
		return string.IsNullOrWhiteSpace(directoryPath)
			? msFileSystem.Directory.GetCurrentDirectory()
			: directoryPath;
	}

	public string GetDestinationFileDirectory(string filePath, string destinationPath) {
		filePath.CheckArgumentNullOrWhiteSpace(nameof(filePath));
		destinationPath.CheckArgumentNullOrWhiteSpace(nameof(destinationPath));
		string fileName = ExtractFileNameFromPath(filePath);
		return Path.Combine(destinationPath, fileName);
	}

	public string[] GetDirectories(string directoryPath) {
		directoryPath.CheckArgumentNullOrWhiteSpace(nameof(directoryPath));
		return msFileSystem.Directory.GetDirectories(directoryPath);
	}

	public string[] GetDirectories(string directoryPath, string patternt, SearchOption searchOption) {
		directoryPath.CheckArgumentNullOrWhiteSpace(nameof(directoryPath));
		return msFileSystem.Directory.GetDirectories(directoryPath, patternt, searchOption);
	}

	public string[] GetDirectories() {
		return GetDirectories(msFileSystem.Directory.GetCurrentDirectory());
	}

	public string GetDirectoryHash(string directoryPath, Algorithm algorithm = Algorithm.SHA256) {
		directoryPath.CheckArgumentNullOrWhiteSpace(nameof(directoryPath));

		if (!msFileSystem.Directory.Exists(directoryPath)) {
			throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");
		}

		// Get all files in directory and subdirectories
		List<string> files = msFileSystem.Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories)
										 .OrderBy(f =>
											 f) // Sort for consistent hashing regardless of directory enumeration order
										 .ToList();

		if (files.Count == 0) {
			return string.Empty;
		}

		// Create the appropriate hash algorithm
		HashAlgorithm hashAlgorithm = algorithm switch {
										  Algorithm.SHA1 => SHA1.Create(),
										  Algorithm.SHA256 => SHA256.Create(),
										  Algorithm.SHA384 => SHA384.Create(),
										  Algorithm.SHA512 => SHA512.Create(),
										  Algorithm.MD5 => MD5.Create(),
										  var _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm,
											  null)
									  };

		using HashAlgorithm algorithm1 = hashAlgorithm;
		using MemoryStream ms = new();
		foreach (string file in files) {
			// Calculate file hash
			string fileHash = GetFileHash(algorithm, file);

			// Get relative path to include directory structure in the hash
			string relativePath = ConvertToRelativePath(file, directoryPath);

			// Combine file path and hash in a deterministic way
			byte[] fileData = Encoding.UTF8.GetBytes($"{relativePath}:{fileHash}");
			ms.Write(fileData, 0, fileData.Length);
		}

		// Reset stream position and calculate final hash
		ms.Position = 0;
		byte[] directoryHash = hashAlgorithm.ComputeHash(ms);
		return BitConverter.ToString(directoryHash).Replace("-", string.Empty);
	}

	public Ms.IDirectoryInfo GetDirectoryInfo(string path) {
		Ms.IDirectoryInfoFactory dirInfoFactory = msFileSystem.DirectoryInfo;
		return dirInfoFactory.New(path);
	}

	public string GetFileHash(Algorithm algorithm, string fileName) {
		HashAlgorithm hashAlgorithm = algorithm switch {
										  Algorithm.SHA1 => SHA1.Create(),
										  Algorithm.SHA256 => SHA256.Create(),
										  Algorithm.SHA384 => SHA384.Create(),
										  Algorithm.SHA512 => SHA512.Create(),
										  Algorithm.MD5 => MD5.Create(),
										  var _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm,
											  null)
									  };

		using Ms.FileSystemStream stream = msFileSystem.File.OpenRead(fileName);
		byte[] hash = hashAlgorithm.ComputeHash(stream);
		return BitConverter.ToString(hash).Replace("-", string.Empty);
	}

	public string GetFileNameWithoutExtension(FileInfo fileInfo) {
		fileInfo.CheckArgumentNull(nameof(fileInfo));
		return fileInfo.Name
					   .Substring(0, fileInfo.Name.Length - fileInfo.Extension.Length);
	}

	public string[] GetFiles(string directoryPath) {
		//TODO: Should probably be IEnumerable<string> instead of string[]
		directoryPath.CheckArgumentNullOrWhiteSpace(nameof(directoryPath));
		return msFileSystem.Directory.GetFiles(directoryPath);
	}

	public string[] GetFiles(string directoryPath, string searchPattern, SearchOption searchOption) {
		directoryPath.CheckArgumentNullOrWhiteSpace(nameof(directoryPath));
		return msFileSystem.Directory.GetFiles(directoryPath, searchPattern, searchOption);
	}

	public FileInfo[] GetFilesInfos(string directoryPath, string searchPattern, SearchOption searchOption) {
		directoryPath.CheckArgumentNullOrWhiteSpace(nameof(directoryPath));
		DirectoryInfo directoryInfo = new(directoryPath);

		//TODO: Discuss with P.Makarchuk
		//directoryInfo.GetFiles causes System.IO.DirectoryNotFoundException when Schemas does not exist 
		if (directoryInfo.Exists) {
			return directoryInfo.GetFiles(searchPattern, searchOption);
		}

		return new FileInfo[0];
	}

	public Ms.IFileInfo GetFilesInfos(string filePath) {
		Ms.IFileInfoFactory fileInfoFactory = msFileSystem.FileInfo;
		Ms.IFileInfo fileInfo = fileInfoFactory.New(filePath);
		return fileInfo;
	}

	public long GetFileSize(string filePath) {
		filePath.CheckArgumentNullOrWhiteSpace(nameof(filePath));
		Ms.IFileInfoFactory fileInfoFactory = msFileSystem.FileInfo;
		Ms.IFileInfo ff = fileInfoFactory.New(filePath);
		return ff.Length;
	}

	public long GetFileSize(Ms.IFileInfo fileInfo) {
		return fileInfo.Length;
	}

	public bool IsEmptyDirectory() {
		return !msFileSystem.Directory.GetFileSystemEntries(msFileSystem.Directory.GetCurrentDirectory()).Any();
	}

	public bool IsEmptyDirectory(string path) {
		return !msFileSystem.Directory.GetFileSystemEntries(path).Any();
	}

	public bool IsReadOnlyFile(string filePath) {
		if (!msFileSystem.File.Exists(filePath)) {
			return false;
		}

		return (msFileSystem.File.GetAttributes(filePath) & FileAttributes.ReadOnly) != 0;
	}

	public void MoveFile(string oldFilePath, string newFilePath) {
		msFileSystem.File.Move(oldFilePath, newFilePath);
	}

	public string NormalizeFilePathByPlatform(string filePath) {
		if (string.IsNullOrWhiteSpace(filePath)) {
			return filePath;
		}

		string[] filePathItem = filePath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
		string result = Path.Combine(filePathItem);

		// Path.Combine loses the root separator on Unix; restore it for absolute paths
		if (Path.IsPathRooted(filePath) && !Path.IsPathRooted(result)) {
			result = Path.DirectorySeparatorChar + result;
		}

		return result;
	}

	public void OverwriteExistsDirectory(string directoryPath) {
		directoryPath.CheckArgumentNullOrWhiteSpace(nameof(directoryPath));
		if (!msFileSystem.Directory.Exists(directoryPath)) {
			return;
		}

		msFileSystem.Directory.Delete(directoryPath, true);
		msFileSystem.Directory.CreateDirectory(directoryPath);
	}

	public byte[] ReadAllBytes(string filePath) {
		return msFileSystem.File.ReadAllBytes(filePath);
	}

	public string ReadAllText(string filePath) {
		return msFileSystem.File.ReadAllText(filePath, Utf8NoBom);
	}

	public void ResetFileReadOnlyAttribute(string filePath) {
		if (!msFileSystem.File.Exists(filePath)) {
			return;
		}

		if (IsReadOnlyFile(filePath)) {
			msFileSystem.File.SetAttributes(filePath,
				msFileSystem.File.GetAttributes(filePath) & ~FileAttributes.ReadOnly);
		}
	}

	public void SafeDeleteDirectory(string directoryPath) {
		directoryPath.CheckArgumentNullOrWhiteSpace(nameof(directoryPath));
		ClearDirectory(directoryPath);
		DeleteDirectory(directoryPath, false);
		while (ExistsDirectory(directoryPath)) {
			Thread.Sleep(0);
		}
	}


	public void WriteAllTextToFile(string filePath, string contents) {
		WriteAllTextToFile(filePath, contents, Utf8NoBom);
	}

	public void WriteAllTextToFile(string filePath, string contents, Encoding encoding) {
		msFileSystem.File.WriteAllText(filePath, contents, encoding);
	}

	#endregion
}

#endregion
