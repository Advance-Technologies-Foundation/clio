using Clio.Tests.Extensions;
using System;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

namespace Clio.Tests.Infrastructure
{
	internal class TestFileSystem
	{
		public static string OriginFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory);
		public static string ExamplesFolderPath = Path.Combine(OriginFolderPath, "Examples");

		internal static IFileSystem MockExamplesFolder(string exampleFolderName) {
			var mockFileSystem = new MockFileSystem();
			var examplesTestFolder = Path.Combine(ExamplesFolderPath, exampleFolderName);
			mockFileSystem.MockFolder(examplesTestFolder);
			return mockFileSystem;
		}

		internal static IFileSystem MockFileSystem() {
			var mockFileSystem = new MockFileSystem();
			return mockFileSystem;
		}


		public static string ReadExamplesFile(string folderName, string fileName) {
			var filePath = Path.Combine(ExamplesFolderPath, folderName, fileName);
			return File.ReadAllText(filePath);
		}
	}
}
