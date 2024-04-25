using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

namespace Clio.Tests.Extensions;

public static class FileSystemExtension
{
	public static void MockFolder(this MockFileSystem mockFileSystem, string folderPath){
		var exampleFiles = Directory.GetFiles(folderPath);
        foreach (var exampleFile in exampleFiles) {
            FileInfo fileInfo = new FileInfo(exampleFile);
            mockFileSystem.AddFile(fileInfo.FullName, new MockFileData(File.ReadAllBytes(fileInfo.FullName)));
        }
				
		var subDirs = Directory.GetDirectories(folderPath);
		
		foreach(string subFolder in subDirs) {
			mockFileSystem.AddDirectory(subFolder);
			MockFolder(mockFileSystem, subFolder);
			
		}
	}
}