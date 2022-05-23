using System.Collections.Generic;
using System.IO;

namespace Clio.Common
{

	#region Interface: IFileSystem

	public interface IFileSystem
	{

		#region Methods: Public

		void CheckOrDeleteExistsFile(string filePath, bool delete);
		void CheckOrDeleteExistsDirectory(string directoryPath, bool delete);
		void OverwriteExistsDirectory(string directoryPath);
		void CheckOrOverwriteExistsDirectory(string directoryPath, bool overwrite);
		string GetCurrentDirectoryIfEmpty(string directory);
		string ExtractNameFromPath(string path);
		string GetDestinationFileDirectory(string filePath, string destinationPath);
		void CopyDirectory(string source, string destination, bool overwrite);
		void Copy(IEnumerable<string> filesPaths, string destinationDirectory, bool overwrite);
		void DeleteFileIfExists(string path);
		void DeleteDirectoryIfExists(string path);
		string ConvertToRelativePath(string path, string rootDirectoryPath);
		void WriteAllTextToFile(string path, string contents);
		DirectoryInfo CreateDirectory(string path) => Directory.CreateDirectory(path);
		bool DirectoryExists(string path);
		bool FileExists(string path);


		#endregion

	}

	#endregion

}