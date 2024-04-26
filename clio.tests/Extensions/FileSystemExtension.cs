using System.IO;
using System.IO.Abstractions.TestingHelpers;

namespace Clio.Tests.Extensions;

public static class FileSystemExtension
{

	#region Methods: Public

	public static void MockFolder(this MockFileSystem mockFileSystem, string folderPath){
		string[] exampleFiles = Directory.GetFiles(folderPath);
		foreach (string exampleFile in exampleFiles) {
			FileInfo fileInfo = new(exampleFile);
			mockFileSystem.AddFile(fileInfo.Name, new MockFileData(File.ReadAllBytes(fileInfo.FullName)));
		}
	}

	public static void MockFolderWithDir(this MockFileSystem mockFileSystem, string folderPath){
		string[] exampleFiles = Directory.GetFiles(folderPath);
		foreach (string exampleFile in exampleFiles) {
			FileInfo fileInfo = new(exampleFile);
			mockFileSystem.AddFile(fileInfo.FullName, new MockFileData(File.ReadAllBytes(fileInfo.FullName)));
		}
		string[] subDirs = Directory.GetDirectories(folderPath);
		foreach (string subFolder in subDirs) {
			mockFileSystem.AddDirectory(subFolder);
			MockFolderWithDir(mockFileSystem, subFolder);
		}
	}

	#endregion

}