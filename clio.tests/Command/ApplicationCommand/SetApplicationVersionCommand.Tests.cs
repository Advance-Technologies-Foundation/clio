using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Json;
using Clio.Command.ApplicationCommand;
using Clio.ComposableApplication;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.ApplicationCommand
{

	// TODO - Extract manager aseertion to ComposibleApp tests
	internal class SetApplicationVersionCommandTest: BaseCommandTests<SetApplicationVersionOption>
	{
		private static string mockPackageFolderPath = Path.Combine("C:", "MockPackageFolder");
		private static string mockPackageAppDescriptorPath = Path.Combine(mockPackageFolderPath, "Files", "app-descriptor.json");
		private static string mockWorspacePath = Path.Combine("C:", "MockWorkspaceFolder");
		private static string mockWorkspaceAppPackageFolderPath = Path.Combine(mockWorspacePath, "packages", "IFrameSample");
		private static string mockWorkspaceAppDescriptorPath = Path.Combine(mockWorkspaceAppPackageFolderPath, "Files", "app-descriptor.json");

		private static MockFileSystem CreateFs(string filePath, string packagePath) {
			string originClioSourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory);
			string appDescriptorExamplesDescriptorPath = Path.Combine(originClioSourcePath, "Examples", "AppDescriptors", filePath);
			string mockAppDescriptorFilePath = Path.Combine(packagePath, "Files", "app-descriptor.json");
			return new MockFileSystem(new Dictionary<string, MockFileData> {
				{
					mockAppDescriptorFilePath,
					new MockFileData(File.ReadAllText(appDescriptorExamplesDescriptorPath))
				}
			});
		}

		private static MockFileSystem CreateFs(Dictionary<string, string> appDescriptors) {
			string originClioSourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory);
			MockFileSystem mockFileSystem = new MockFileSystem();
			foreach (var appDescriptor in appDescriptors) {
				string appDescriptorExamplesDescriptorPath = Path.Combine(originClioSourcePath, "Examples", "AppDescriptors", appDescriptor.Value);
				string mockAppDescriptorJsonPath = Path.Combine(mockWorspacePath, "packages", appDescriptor.Key, "Files", "app-descriptor.json");
				mockFileSystem.AddFile(mockAppDescriptorJsonPath, new MockFileData(File.ReadAllText(appDescriptorExamplesDescriptorPath)));
			}
			return mockFileSystem;
		}

		private MockFileSystem _fileSystem;

		[TestCase("app-descriptor_v.json")]
		[TestCase("app-descriptor_wv.json")]
		[TestCase("app-descriptor_dv.json")]
		public void SetVersion_WhenWorkspaceContainsOneApplication(string descriptorPath) {
			_fileSystem = CreateFs(descriptorPath, mockWorkspaceAppPackageFolderPath);
			string expectedVersion = "8.1.1";
			var composableApplicationManager = new ComposableApplicationManager(_fileSystem, null, null, null);
			var command = new SetApplicationVersionCommand(composableApplicationManager);
			string worspaceFolderPath = mockWorspacePath;
			command.Execute(new SetApplicationVersionOption() {
				Version = expectedVersion, WorspaceFolderPath = worspaceFolderPath
			});
			var objectJson = JsonObject.Parse(_fileSystem.File.ReadAllText(mockWorkspaceAppDescriptorPath));
			string actualVersion = objectJson["Version"];
			_fileSystem.FileExists(mockWorkspaceAppDescriptorPath).Should().BeTrue();
			expectedVersion.Should().Be(actualVersion);
			_fileSystem.File.ReadAllLines(mockWorkspaceAppDescriptorPath).Length.Should().BeGreaterThan(20);
		}

		[Test]
		public void SetVersion_ThrowException_WhenWorkspaceContainsMoreThanOneApplication() {
			Dictionary<string, string> appDescriptions = new Dictionary<string, string>();
			appDescriptions.Add("Package1", "app1-app-descriptor.json");
			appDescriptions.Add("Package2", "app2-app-descriptor.json");
			_fileSystem = CreateFs(appDescriptions);
			var composableApplicationManager = new ComposableApplicationManager(_fileSystem, null, null, null);
			var command = new SetApplicationVersionCommand(composableApplicationManager);
			string expectedVersion = "8.1.1";
			string worspaceFolderPath = mockWorspacePath;
			var exception = Assert.Throws<Exception>( () => command.Execute(new SetApplicationVersionOption() { 
				Version = expectedVersion, WorspaceFolderPath = worspaceFolderPath }));
			exception.Message.Contains("Package1").Should().BeTrue();
			exception.Message.Contains("Package2").Should().BeTrue();
		}

		[Test]
		public void SetVersion_ThrowExceptionWhenAplicationExtendedAndPackageNotDefined() {
			Dictionary<string, string> appDescriptions = new Dictionary<string, string>();
			appDescriptions.Add("Package1", "app1-app-descriptor.json");
			appDescriptions.Add("Package2", "app1-ext-app-descriptor.json");
			_fileSystem = CreateFs(appDescriptions);
			string expectedVersion = "8.1.1";
			var composableApplicationManager = new ComposableApplicationManager(_fileSystem, null, null, null);
			var command = new SetApplicationVersionCommand(composableApplicationManager);
			string worspaceFolderPath = mockWorspacePath;
			var exception = Assert.Throws<Exception>(() => command.Execute(new SetApplicationVersionOption() { 
				Version = expectedVersion, WorspaceFolderPath = worspaceFolderPath }));
			exception.Message.Contains("Package1").Should().BeTrue();
			exception.Message.Contains("Package2").Should().BeTrue();
		}

		[Test]
		public void SetVersion_WhenAplicationExtendedAndPackageDefined() {
			Dictionary<string, string> appDescriptions = new Dictionary<string, string>();
			string extendPackageName = "Package2";
			appDescriptions.Add("Package1", "app1-app-descriptor.json");
			appDescriptions.Add(extendPackageName, "app1-ext-app-descriptor.json");
			_fileSystem = CreateFs(appDescriptions);
			string expectedVersion = "8.1.1"; 
			var composableApplicationManager = new ComposableApplicationManager(_fileSystem, null, null, null);
			var command = new SetApplicationVersionCommand(composableApplicationManager); 
			string worspaceFolderPath = mockWorspacePath;
			command.Execute(new SetApplicationVersionOption() {
				Version = expectedVersion,
				WorspaceFolderPath = worspaceFolderPath,
				PackageName = extendPackageName
			});
		}

		[TestCase("app-descriptor_v.json")]
		[TestCase("app-descriptor_wv.json")]
		[TestCase("app-descriptor_dv.json")]
		public void SetVersion_WhenSetAppFolderPathForOneApplication(string descriptorPath) {
			_fileSystem = CreateFs(descriptorPath, mockPackageFolderPath);
			string expectedVersion = "8.1.1";
			var composableApplicationManager = new ComposableApplicationManager(_fileSystem, null, null, null);
			var command = new SetApplicationVersionCommand(composableApplicationManager);
			command.Execute(new SetApplicationVersionOption() {
				Version = expectedVersion,
				PackageFolderPath = mockPackageFolderPath
			});
			var objectJson = JsonObject.Parse(_fileSystem.File.ReadAllText(mockPackageAppDescriptorPath));
			string actualVersion = objectJson["Version"];
			_fileSystem.FileExists(mockPackageAppDescriptorPath).Should().BeTrue();
			expectedVersion.Should().Be(actualVersion);
			_fileSystem.File.ReadAllLines(mockPackageAppDescriptorPath).Length.Should().BeGreaterThan(20);
		}
	}
}
