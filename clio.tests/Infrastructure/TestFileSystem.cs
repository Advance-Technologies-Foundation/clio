using Clio.Tests.Extensions;
using System;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

namespace Clio.Tests.Infrastructure
{
	internal class TestFileSystem
	{


		internal static IFileSystem MockExamplesFolder(string exampleFolderName) {
			var mockFileSystem = new MockFileSystem();
			mockFileSystem.MockExamplesFolder(exampleFolderName);
			return mockFileSystem;
		}

		
		
		internal static MockFileSystem MockFileSystem() {
			var mockFileSystem = new MockFileSystem();
			return mockFileSystem;
		}


		public static string ReadExamplesFile(string folderName, string fileName) {
			var filePath = Path.Combine(FileSystemExtension.ExamplesFolderPath, folderName, fileName);
			return File.ReadAllText(filePath);
		}
	}
}
