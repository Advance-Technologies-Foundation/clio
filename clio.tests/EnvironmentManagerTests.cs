using Clio.Command;
using NUnit.Framework;
using System;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Clio.Tests.Extensions;

namespace Clio.Tests
{
	[TestFixture]
	internal class EnvironmentManagerTest
	{
		const string FolderName = "Examples";
		IFileSystem _fileSystem;

		[SetUp]
		public void SetupFileSystem() {
			string originClioSourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory);
			string examplesFilePath = Path.Combine(originClioSourcePath, FolderName, "deployments-manifest");
			var mockFileSystem = new MockFileSystem();
			mockFileSystem.MockFolder(examplesFilePath);
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
