namespace Clio.Common
{

	#region Interface: IFileSystem

	public interface IFileSystem
	{

		#region Methods: Public

		void CheckOrDeleteExistsFile(string filePath, bool delete);
		void CheckOrDeleteExistsDirectory(string directoryPath, bool delete);
		void CheckOrOverwriteExistsDirectory(string directoryPath, bool overwrite);
		string GetCurrentDirectoryIfEmpty(string directory);
		string ExtractNameFromPath(string path);
		string GetDestinationFileDirectory(string filePath, string destinationPath);
		void CopyDirectory(string source, string destination, bool overwrite);
		void DeleteDirectoryIfExists(string path);
		string ConvertToRelativePath(string path, string rootDirectoryPath);

		#endregion

	}

	#endregion

}