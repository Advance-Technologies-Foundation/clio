using Clio.Tests.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
	}
}
