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
            mockFileSystem.AddFile(fileInfo.Name, new MockFileData(File.ReadAllBytes(fileInfo.FullName)));
        }
	}
}