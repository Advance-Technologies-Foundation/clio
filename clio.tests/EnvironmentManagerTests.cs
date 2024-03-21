using Clio.Command;
using NUnit.Framework;
using System;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

namespace Clio.Tests
{
	[TestFixture]
	internal class EnvironmentManagerTest
	{
		IFileSystem _fileSystem;

		[SetUp]
		public void SetupFileSystem() {
			_fileSystem = new FileSystem();
			string originClioSourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory);
			string examplesFilePath = Path.Combine(originClioSourcePath, "Examples", "deployments-manifest");
			string preprodConfigExapleFilePath = Path.Combine(examplesFilePath, "preprod-creatio-config.yaml");
			var exampleFiles = Directory.GetFiles(examplesFilePath);
			var mockFileSystem = new MockFileSystem();
			foreach (var exampleFile in exampleFiles) {
				FileInfo fileInfo = new FileInfo(exampleFile);
				mockFileSystem.AddFile(fileInfo.Name, new MockFileData(File.ReadAllBytes(fileInfo.FullName));
			}
			_fileSystem = mockFileSystem;
		}

		[Test]
		public void AddApplication_WhenApplicationDoesNotExist() {
			var environmentManager = new EnvironmentManager();
			var manifestFilePath = "preprod-creatio-config.yaml";
			environmentManager.ApplyManifest(manifestFilePath);
		}
	}
}
