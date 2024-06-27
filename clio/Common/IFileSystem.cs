using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Text;
using System.Text.Unicode;

namespace Clio.Common
{
	#region Interface: IFileSystem

	/// <summary>
	/// The IFileSystem interface provides methods for interacting with the file system.
	/// </summary>
	public interface IFileSystem
	{
		public FileSystemStream CreateFile(string filePath);

		#region Methods: Public

		public IDirectoryInfo GetDirectoryInfo(string path);
		
		
		/// <summary>
		/// Creates a symbolic link at the specified path that points to the target path.
		/// </summary>
		/// <param name="path">The path where the symbolic link should be created.</param>
		/// <param name="pathToTarget">The path that the symbolic link should point to.</param>
		/// <returns>An object that represents the symbolic link.</returns>
		/// <exception cref="System.ArgumentNullException">Thrown when the path or pathToTarget is null</exception>
		/// <exception cref="System.ArgumentException">Thrown when – path or pathToTarget is empty. -or- path or pathToTarget contains invalid path characters.</exception>
		/// <exception cref="System.IO.IOException"> A file or directory already exists in the location of path. -or- An I/O error occurred</exception>
		IFileSystemInfo CreateSymLink(string path, string pathToTarget);
		
		/// <inheritdoc cref="System.IO.Abstractions.IDirectory.CreateSymbolicLink(string,string)"/>
		IFileSystemInfo CreateDirectorySymLink(string path, string pathToTarget);
		
		/// <inheritdoc cref="System.IO.Abstractions.IFile.CreateSymbolicLink(string,string)"/>
		IFileSystemInfo CreateFileSymLink(string path, string pathToTarget);

		/// <summary>
		/// Checks if a file exists at the given file path. If the file exists and the delete flag is set to true, the file is deleted.
		/// If the file exists and the delete flag is set to false, an exception is thrown.
		/// </summary>
		/// <param name="filePath">The path of the file to check or delete.</param>
		/// <param name="delete">A flag indicating whether to delete the file if it exists.</param>
		/// <exception cref="System.Exception">Thrown when the file exists and the delete flag is set to false.</exception>
		void CheckOrDeleteExistsFile(string filePath, bool delete);

		void CopyFiles(IEnumerable<string> filesPaths, string destinationDirectory, bool overwrite);
		
		/// <inheritdoc cref="System.IO.Abstractions.IFile.Copy(string,string,bool)"/>
		void CopyFile(string from, string to, bool overwrite);

		/// <summary>
		/// Deletes the file at the specified path.
		/// </summary>
		/// <param name="filePath">The path of the file to delete.</param>
		/// <returns>True if the file was deleted, false otherwise.</returns>
		/// <inheritdoc cref="System.IO.Abstractions.IFile.Delete(string)"/>
		bool DeleteFile(string filePath);

		/// <summary>
		/// Deletes the file at the specified path if it exists.
		/// </summary>
		/// <param name="filePath">The path of the file to delete.</param>
		/// <returns>True if the file was deleted, false otherwise.</returns>
		/// <inheritdoc cref="DeleteFile"/>
		bool DeleteFileIfExists(string filePath);

		/// <inheritdoc cref="System.IO.Abstractions.IFile.Exists(string)"/>
		bool ExistsFile(string filePath);

		string ExtractFileNameFromPath(string filePath);

		string ExtractFileExtensionFromPath(string filePath);

		string GetFileNameWithoutExtension(FileInfo fileInfo);

		string[] GetFiles(string directoryPath);

		string[] GetFiles(string directoryPath, string searchPattern, SearchOption searchOption);

		FileInfo[] GetFilesInfos(string directoryPath, string searchPattern, SearchOption searchOption);

		/// <summary>
		/// Checks if the file at the given path is read-only.
		/// </summary>
		/// <param name="filePath">The path of the file to check.</param>
		/// <returns>True if the file is read-only, false otherwise.</returns>
		bool IsReadOnlyFile(string filePath);

		/// <inheritdoc cref="System.IO.Abstractions.IFile.Move(string, string)"/>
		void MoveFile(string oldFilePath, string newFilePath);

		/// <summary>
		/// Resets the read-only attribute of the file at the given path.
		/// </summary>
		/// <param name="filePath">The path of the file to reset the read-only attribute for.</param>
		/// <exception cref="T:System.ArgumentException">.NET Framework and .NET Core versions older than 2.1: <paramref name="filePath" />
		/// is empty, contains only white spaces, contains invalid characters, or the file attribute is invalid.</exception>
		/// <exception cref="T:System.IO.PathTooLongException">The specified path, file name, or both exceed the system-defined maximum length.</exception>
		/// <exception cref="T:System.NotSupportedException"><paramref name="filePath" /> is in an invalid format.</exception>
		/// <exception cref="T:System.IO.DirectoryNotFoundException">The specified path is invalid, (for example, it is on an unmapped drive).</exception>
		/// <exception cref="T:System.IO.FileNotFoundException">The file cannot be found.</exception>
		/// <exception cref="T:System.UnauthorizedAccessException"><paramref name="filePath" /> specified a file that is read-only.
		/// 
		/// -or-
		/// 
		/// This operation is not supported on the current platform.
		/// 
		/// -or-
		/// 
		/// <paramref name="filePath" /> specified a directory.
		/// 
		/// -or-
		/// 
		/// The caller does not have the required permission.</exception>
		void ResetFileReadOnlyAttribute(string filePath);

		/// <inheritdoc cref="System.IO.Abstractions.IFile.ReadAllText(string)"/>
		string ReadAllText(string filePath);

		/// <inheritdoc cref="System.IO.Abstractions.IFile.WriteAllText(string, string)"/>
		void WriteAllTextToFile(string filePath, string contents);

		/// <inheritdoc cref="System.IO.Abstractions.IFile.WriteAllText(string, string, Encoding)"/>
		void WriteAllTextToFile(string filePath, string contents, Encoding encoding);

		void ClearOrCreateDirectory(string directoryPath);

		void CreateOrOverwriteExistsDirectoryIfNeeded(string directoryPath, bool overwrite);

		/// <summary>
		/// Clears the directory at the specified path.
		/// </summary>
		/// <param name="directoryPath">The path of the directory to clear.</param>
		/// <remarks>
		/// This method deletes all files and subdirectories within the specified directory.
		/// It uses the `GetFiles` method to retrieve all files in the directory and the `DeleteFileIfExists` method to delete each file.
		/// It also uses the `GetDirectories` method to retrieve all subdirectories in the directory and the `SafeDeleteDirectory` method to delete each subdirectory.
		/// </remarks>
		void ClearDirectory(string directoryPath);

		void CopyDirectory(string source, string destination, bool overwrite);

		/// <inheritdoc cref="System.IO.Abstractions.IDirectory.CreateDirectory(string)"/>
		IDirectoryInfo CreateDirectory(string directoryPath);

		void CreateDirectoryIfNotExists(string directoryPath);

		void CreateOrClearDirectory(string directoryPath);

		void DeleteDirectory(string directoryPath);

		void DeleteDirectory(string directoryPath, bool recursive);

		void DeleteDirectoryIfExists(string directoryPath);

		bool ExistsDirectory(string directoryPath);

		string GetCurrentDirectoryIfEmpty(string directoryPath);

		bool IsEmptyDirectory();

		string GetDestinationFileDirectory(string filePath, string destinationPath);

		string[] GetDirectories(string directoryPath);

		void OverwriteExistsDirectory(string directoryPath);

		void SafeDeleteDirectory(string directoryPath);

		string ConvertToRelativePath(string path, string rootDirectoryPath);

		string NormalizeFilePathByPlatform(string filePath);
		string[] GetDirectories();

		/// <summary>
		/// Computes hash string of a files
		/// </summary>
		/// <param name="algorithm">Algorithm to use</param>
		/// <param name="fileName">Full file path</param>
		/// <returns></returns>
		string GetFileHash(FileSystem.Algorithm algorithm, string fileName);

		/// <summary>
		/// Compares two files by their hashes, with a specific algorithm
		/// </summary>
		/// <param name="algorithm"></param>
		/// <param name="first"></param>
		/// <param name="second"></param>
		/// <returns></returns>
		bool CompareFiles(FileSystem.Algorithm algorithm, string first, string second);

		/// <summary>
		/// Compares two files by their hashes
		/// </summary>
		/// <param name="fileName1">Full path to the first file</param>
		/// <param name="fileName2">Full path to the second file</param>
		/// <returns>Result of the comparison</returns>
		/// <remarks>Uses <see cref="FileSystem.Algorithm.MD5"/> by default</remarks>
		/// <exception cref="FileNotFoundException">when either of the files is not found</exception>
		bool CompareFiles(string fileName1, string fileName2);

		#endregion

	}

	#endregion
}