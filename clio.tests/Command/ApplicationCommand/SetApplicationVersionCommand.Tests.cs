using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Json;
using Clio.Command.ApplicationCommand;
using NUnit.Framework;

namespace Clio.Tests.Command.ApplicationCommand
{

	internal class SetApplicationVersionCommandTest: BaseCommandTests<SetApplicationVersionOption>
	{

		private static string mockWorspacePath = Path.Combine("C:", "iframe-sample");
		private static string appDescriptorJsonPath = Path.Combine(mockWorspacePath, "packages", "IFrameSample", "Files", "app-descriptor.json");

		private static MockFileSystem CreateFs(string filePath) {
			string originClioSourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory);
			string appDescriptorExamplesDescriptorPath = Path.Combine(originClioSourcePath, "Examples", "AppDescriptors", filePath);

			return new MockFileSystem(new Dictionary<string, MockFileData> {
				{
					appDescriptorJsonPath,
					new MockFileData(File.ReadAllText(appDescriptorExamplesDescriptorPath))
				}
			});
		}

		private MockFileSystem _fileSystem;

		[TestCase("app-descriptor.json")]
		[TestCase("app-descriptor_wv.json")]
		[TestCase("app-descriptor_dv.json")]
		public void SetVersion_WhenSetCorrectVersion(string descriptorPath) {
			_fileSystem = CreateFs(descriptorPath);
			string expectedVersion = "8.1.1";
			var command = new SetApplicationVersionCommand(_fileSystem);
			string worspaceFolderPath = mockWorspacePath;
			command.Execute(new SetApplicationVersionOption() { Version = expectedVersion, WorspaceFolderPath = worspaceFolderPath });
			var objectJson = JsonObject.Parse(_fileSystem.File.ReadAllText(appDescriptorJsonPath));
			string actualVersion = objectJson["Version"];
			Assert.True(_fileSystem.FileExists(appDescriptorJsonPath));
			Assert.AreEqual(expectedVersion, actualVersion);
			Assert.Greater(20, _fileSystem.File.ReadAllLines(appDescriptorJsonPath).Length);
		}

	}
}
