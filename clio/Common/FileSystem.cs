using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Ms = System.IO.Abstractions;
namespace Clio.Common;

#region Class: FileSystem

public class FileSystem : IFileSystem
{

	private readonly Ms.IFileSystem _msFileSystem;
	public FileSystem(Ms.IFileSystem msFileSystem){
		_msFileSystem = msFileSystem;
	}

	public Ms.FileSystemStream CreateFile(string filePath){
		return _msFileSystem.File.Create(filePath);
	}
	
	
	
	#region Methods: Public

	public static void CreateLink(string link, string target) {
		Process mklinkProcess = Process.Start(
			new ProcessStartInfo("cmd", $"/c mklink /D \"{link}\" \"{target}\"") {
				CreateNoWindow = true
			});
		mklinkProcess.WaitForExit();
	}
	public long GetFileSize(string filePath) {
		filePath.CheckArgumentNullOrWhiteSpace(nameof(filePath));
		Ms.IFileInfoFactory fileInfoFactory = _msFileSystem.FileInfo;
		var ff = fileInfoFactory.New(filePath);
		return ff.Length;
	}
	public long GetFileSize(Ms.IFileInfo fileInfo) {
		return fileInfo.Length;
	}
	
		
	public Ms.IFileSystemInfo CreateSymLink(string path, string pathToTarget){
		path.CheckArgumentNullOrWhiteSpace(nameof(path));
		pathToTarget.CheckArgumentNullOrWhiteSpace(nameof(pathToTarget));
		if(_msFileSystem.File.Exists(path)) {
			return CreateFileSymLink(path, pathToTarget);
		}
		if(_msFileSystem.Directory.Exists(path)) {
			return CreateDirectorySymLink(path, pathToTarget);
		}
		throw new ArgumentOutOfRangeException(nameof(path), $"Path {path} does not exist");
	}
	public Ms.IFileSystemInfo CreateFileSymLink(string path, string pathToTarget) => 
		_msFileSystem.File.CreateSymbolicLink(path, pathToTarget);
	public Ms.IFileSystemInfo CreateDirectorySymLink(string path, string pathToTarget) => 
		_msFileSystem.Directory.CreateSymbolicLink(path, pathToTarget);

	public void CheckOrDeleteExistsFile(string filePath, bool delete) {
		if (!_msFileSystem.File.Exists(filePath)) {
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
			Ms.IFileInfoFactory fileInfoFactory = _msFileSystem.FileInfo;
			Ms.IFileInfo sourceFileInfo = fileInfoFactory.New(sourceFilePath);
			string destinationFilePath = Path.Combine(destinationDirectory, sourceFileInfo.Name);
			_msFileSystem.File.Copy(sourceFilePath, destinationFilePath, overwrite);
		}
	}
		
	public void CopyFile(string from, string to, bool overwrite){
		_msFileSystem.File.Copy(from, to, overwrite);
	}

	public bool DeleteFile(string filePath) {
		filePath.CheckArgumentNullOrWhiteSpace(nameof(filePath));
		if (IsReadOnlyFile(filePath)) {
			ResetFileReadOnlyAttribute(filePath);
		}
		_msFileSystem.File.Delete(filePath);
		//TODO: Discuss with P.Makarchuk
		//why return type is bool when always true
		return true;
	}
		
		
	public bool DeleteFileIfExists(string filePath) => 
		_msFileSystem.File.Exists(filePath) && DeleteFile(filePath);

	public bool ExistsFile(string filePath) => _msFileSystem.File.Exists(filePath);

	public string ExtractFileNameFromPath(string filePath) {
		filePath.CheckArgumentNullOrWhiteSpace(nameof(filePath));
		var packageFileInfo = new FileInfo(filePath);
		return GetFileNameWithoutExtension(packageFileInfo);
	}

	public string ExtractFileExtensionFromPath(string filePath) {
		filePath.CheckArgumentNullOrWhiteSpace(nameof(filePath));
		//var fileInfo = new FileInfo(filePath);
		Ms.IFileInfoFactory fileInfoFactory = _msFileSystem.FileInfo;
		Ms.IFileInfo fileInfo = fileInfoFactory.New(filePath);
		return fileInfo.Extension;
	}

	public string GetFileNameWithoutExtension(FileInfo fileInfo) {
		fileInfo.CheckArgumentNull(nameof(fileInfo));
		return fileInfo.Name
			.Substring(0, fileInfo.Name.Length - fileInfo.Extension.Length);
	}

	public string[] GetFiles(string directoryPath) {
		//TODO: Should probably be IEnumerable<string> instead of string[]
		directoryPath.CheckArgumentNullOrWhiteSpace(nameof(directoryPath));
		return _msFileSystem.Directory.GetFiles(directoryPath);
	}

	public string[] GetFiles(string directoryPath, string searchPattern, SearchOption searchOption) {
		directoryPath.CheckArgumentNullOrWhiteSpace(nameof(directoryPath));
		return _msFileSystem.Directory.GetFiles(directoryPath, searchPattern, searchOption);
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

	public Ms.IDirectoryInfo GetDirectoryInfo(string path){
		
		Ms.IDirectoryInfoFactory dirInfoFactory = _msFileSystem.DirectoryInfo;
		return dirInfoFactory.New(path);
	}
	
	public bool IsReadOnlyFile(string filePath) {
		if (!_msFileSystem.File.Exists(filePath)) {
			return false;
		}
		return (_msFileSystem.File.GetAttributes(filePath) & FileAttributes.ReadOnly) != 0;
	}

	public void MoveFile(string oldFilePath, string newFilePath) =>
		_msFileSystem.File.Move(oldFilePath, newFilePath);

	public void ResetFileReadOnlyAttribute(string filePath) {
		if (!_msFileSystem.File.Exists(filePath)) {
			return;
		}
		if (IsReadOnlyFile(filePath)) {
			_msFileSystem.File.SetAttributes(filePath, _msFileSystem.File.GetAttributes(filePath) & ~FileAttributes.ReadOnly);
		}
	}

	public string ReadAllText(string filePath) => 
		_msFileSystem.File.ReadAllText(filePath, Encoding.UTF8);

	public void WriteAllTextToFile(string filePath, string contents) =>
		WriteAllTextToFile(filePath, contents, Encoding.UTF8);

	public void ClearOrCreateDirectory(string directoryPath) {
		if (_msFileSystem.Directory.Exists(directoryPath)) {
			ClearDirectory(directoryPath);
		}
		_msFileSystem.Directory.CreateDirectory(directoryPath);
	}

	public void CreateOrOverwriteExistsDirectoryIfNeeded(string directoryPath, bool overwrite) {
		if (!_msFileSystem.Directory.Exists(directoryPath)) {
			_msFileSystem.Directory.CreateDirectory(directoryPath);
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
		foreach (string filePath in _msFileSystem.Directory.GetFiles(source)) {
			_msFileSystem.File.Copy(filePath, Path.Combine(destination, Path.GetFileName(filePath)), true);
		}
		foreach (string directoryPath in _msFileSystem.Directory.GetDirectories(source)) {
			CopyDirectory(directoryPath, Path.Combine(destination, Path.GetFileName(directoryPath)), overwrite);
		}
	}

	public Ms.IDirectoryInfo CreateDirectory(string directoryPath, bool throwWhenExists = false) {
		if(throwWhenExists && ExistsDirectory(directoryPath)) {
			throw new ArgumentException($"Directory {directoryPath} already exists");
		}
		return _msFileSystem.Directory.CreateDirectory(directoryPath);
	}

	public void CreateDirectoryIfNotExists(string directoryPath) {
		if (_msFileSystem.Directory.Exists(directoryPath)) {
			return;
		}
		_msFileSystem.Directory.CreateDirectory(directoryPath);
	}

	public void CreateOrClearDirectory(string directoryPath) {
		if (_msFileSystem.Directory.Exists(directoryPath)) {
			ClearDirectory(directoryPath);
		} else {
			_msFileSystem.Directory.CreateDirectory(directoryPath);
		}
	}

	public void DeleteDirectory(string directoryPath) {
		DeleteDirectory(directoryPath, false);
	}

	public void DeleteDirectory(string directoryPath, bool recursive) {
		directoryPath.CheckArgumentNullOrWhiteSpace(nameof(directoryPath));
		_msFileSystem.Directory.Delete(directoryPath, recursive);
	}

	public void DeleteDirectoryIfExists(string directoryPath) {
		directoryPath.CheckArgumentNull(nameof(directoryPath));
		if (_msFileSystem.Directory.Exists(directoryPath)) {
			_msFileSystem.Directory.Delete(directoryPath, true);
		}
	}

	/// <summary>
	/// Checks if directory exists
	/// </summary>
	/// <param name="directoryPath"></param>
	/// <returns></returns>
	public bool ExistsDirectory(string directoryPath) => _msFileSystem.Directory.Exists(directoryPath);

	public string GetCurrentDirectoryIfEmpty(string directoryPath) {
		return string.IsNullOrWhiteSpace(directoryPath)
			? _msFileSystem.Directory.GetCurrentDirectory()
			: directoryPath;
	}

	public bool IsEmptyDirectory() =>
		!_msFileSystem.Directory.GetFileSystemEntries(_msFileSystem.Directory.GetCurrentDirectory()).Any();

	public string GetDestinationFileDirectory(string filePath, string destinationPath) {
		filePath.CheckArgumentNullOrWhiteSpace(nameof(filePath));
		destinationPath.CheckArgumentNullOrWhiteSpace(nameof(destinationPath));
		string fileName = ExtractFileNameFromPath(filePath);
		return Path.Combine(destinationPath, fileName);
	}

	public string[] GetDirectories(string directoryPath) {
		directoryPath.CheckArgumentNullOrWhiteSpace(nameof(directoryPath));
		return _msFileSystem.Directory.GetDirectories(directoryPath);
	}

	public void OverwriteExistsDirectory(string directoryPath) {
		directoryPath.CheckArgumentNullOrWhiteSpace(nameof(directoryPath));
		if (!_msFileSystem.Directory.Exists(directoryPath)) {
			return;
		}
		_msFileSystem.Directory.Delete(directoryPath, true);
		_msFileSystem.Directory.CreateDirectory(directoryPath);
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

	public enum Algorithm
	{
		SHA1,
		SHA256,
		SHA384,
		SHA512,
		MD5
	}
		
	public string GetFileHash(Algorithm algorithm, string fileName){
		HashAlgorithm hashAlgorithm = algorithm switch {
			Algorithm.SHA1 => SHA1.Create(),
			Algorithm.SHA256 => SHA256.Create(),
			Algorithm.SHA384 => SHA384.Create(),
			Algorithm.SHA512 => SHA512.Create(),
			Algorithm.MD5 => MD5.Create(),
			var _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, null)
		};
			
		using Ms.FileSystemStream  stream = _msFileSystem.File.OpenRead(fileName);
		byte[] hash = hashAlgorithm.ComputeHash(stream);
		return BitConverter.ToString(hash).Replace("-", string.Empty);
	}
		
	public bool CompareFiles(string first, string second) => CompareFiles(Algorithm.MD5, first, second);
	public string[] GetDirectories() {
		return GetDirectories(Directory.GetCurrentDirectory());
	}


	public bool CompareFiles(Algorithm algorithm, string first, string second){
		if (!_msFileSystem.File.Exists(first) || !_msFileSystem.File.Exists(second)){
			return false;
		}
		return GetFileHash(algorithm, first) == GetFileHash(algorithm, second);
	}

	public void WriteAllTextToFile(string filePath, string contents, Encoding encoding) {
		_msFileSystem.File.WriteAllText(filePath, contents, encoding);
	}

	#endregion

}

#endregion