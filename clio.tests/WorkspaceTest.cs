using Autofac;
using Clio.Package;
using Clio.Workspaces;
using FluentAssertions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text.Json;
using Clio.Common;
using Clio.Tests.Command;
using Clio.Tests.Extensions;
using NSubstitute;

namespace Clio.Tests;

[TestFixture]
internal class WorkspaceTest : BaseClioModuleTests
{
	// private BindingsModule _bindingModule;
	// private IContainer _diContainer;
	

	private EnvironmentSettings GetTestEnvironmentSettings() {
		const string envSettingsJson = @"{
				  ""Uri"": ""https://forrester-lcap-demo-dev.creatio.com/"",
				  ""Login"": ""Supervisor"",
				  ""Password"": ""Supervisor1"",
				  ""Maintainer"": """",
				  ""IsNetCore"": false,
				  ""ClientId"": """",
				  ""ClientSecret"": """",
				  ""WorkspacePathes"": """",
				  ""AuthAppUri"": ""https://forrester-lcap-demo-dev-is.creatio.com/connect/token/"",
				  ""SimpleloginUri"": ""https://forrester-lcap-demo-dev.creatio.com/0/Shell/?simplelogin=true"",
				  ""Safe"": false,
				  ""DeveloperModeEnabled"": false
			}";
		return JsonSerializer.Deserialize<EnvironmentSettings>(envSettingsJson);
	}

	protected override void AdditionalRegistrations(ContainerBuilder containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		
		
		var zip = Substitute.For<IZipFile>();
		zip.WhenForAnyArgs(x => x.CreateFromDirectory(Arg.Any<string>(), Arg.Any<string>())).Do(callInfo => {
			string sourceDir = callInfo.ArgAt<string>(0);
			string destZip = callInfo.ArgAt<string>(1);
			// Use System.IO.Compression to create a real zip file for testing
			FileSystem.AddFile(destZip, new MockFileData(new byte[90000]));
		});
		containerBuilder.RegisterInstance(zip);
		containerBuilder.RegisterInstance(GetTestEnvironmentSettings());
	}

	private static string GetPlatformPath(string disk, string folder) {
		if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)) {
			return Path.Combine($"{disk}:", folder);
		} else {
			return Path.Combine(Path.DirectorySeparatorChar + disk, folder);
		}
		
	}
	private IWorkspace GetTestWorkspace(EnvironmentSettings envSettings) {
		IWorkspace workspace = Container.Resolve<IWorkspace>();
		return workspace;
	}

	protected override MockFileSystem CreateFs() {
		MockFileSystem mockFs = base.CreateFs();
		mockFs.Directory.SetCurrentDirectory(GetPlatformPath("C", "iframe-sample"));
		mockFs.MockExamplesFolder("workspaces/iframe-sample", GetPlatformPath("C", "iframe-sample"));
		return mockFs;
	}

	private static void CopyDirectory(string sourceDirectory, string destinationDirectory) {
		var sourceInfo = new DirectoryInfo(sourceDirectory);
		if (!sourceInfo.Exists) {
			throw new DirectoryNotFoundException($"Source directory not found: {sourceDirectory}");
		}
		Directory.CreateDirectory(destinationDirectory);
		foreach (string filePath in Directory.GetFiles(sourceDirectory)) {
			string fileName = Path.GetFileName(filePath);
			string destFilePath = Path.Combine(destinationDirectory, fileName);
			File.Copy(filePath, destFilePath, true);
		}
		foreach (string directoryPath in Directory.GetDirectories(sourceDirectory)) {
			string directoryName = Path.GetFileName(directoryPath);
			string destSubDirectory = Path.Combine(destinationDirectory, directoryName);
			CopyDirectory(directoryPath, destSubDirectory);
		}
	}
	
	
	[Test]
	public void PublishWorkspaceTest() {
		// Arrange
		const string appStorePath = @"C:\clioAppStore";
		const string appName = "iframe-sample";
		const string appVersion = "1.0.0";
		const string fileName = $"{appName}_{appVersion}.zip";
		string dir = FileSystem.AllDirectories.FirstOrDefault(d => d.EndsWith("iframe-sample"));
		string expectedFileName = Path.Combine(appStorePath, appName, appVersion, fileName);
		
		// Act
		EnvironmentSettings envSettings = GetTestEnvironmentSettings();
		IWorkspace workspace = GetTestWorkspace(envSettings);
		string releaseFileName = workspace.PublishToFolder(dir, appStorePath, appName, appVersion);
		
		// Assert
		expectedFileName.Should().Be(releaseFileName);
		bool versionFileExist = FileSystem.File.Exists(expectedFileName);
		versionFileExist.Should().BeTrue();
		FileSystem.FileInfo.New(expectedFileName).Length.Should().BeGreaterThan(80000);
	}

	[TestCase("trunk")]
	[TestCase("master")]
	[TestCase("feature/rnd-2035")]
	[TestCase("bugf%i_:{}x/rnd-2035")]
	[TestCase("feature/rnd-2035")]
	public void PublishWorkspaceWithBranchTest(string branch) {
		// Arrange
		const string appStorePath = @"C:\clioAppStore";
		const string appName = "iframe-sample";
		const string appVersion = "1.0.0";
		string expectedFileName = Clio.Workspaces.Workspace.GetSanitizeFileNameFromString($"{appName}_{branch}_{appVersion}.zip");
		string originClioSourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory);
		string exampleWorkspacePath = Path.Combine(originClioSourcePath, "Examples", "workspaces", appName);
		string branchFolderName = Clio.Workspaces.Workspace.GetSanitizeFileNameFromString(branch);
		string dir = FileSystem.AllDirectories.FirstOrDefault(d => d.EndsWith("iframe-sample"));
		// app_store_path\app_name\bramch\file_name
		var expectedFilePath = Path.Combine(appStorePath, appName, branchFolderName, expectedFileName);
		
		
		// Act
		var envSettings = GetTestEnvironmentSettings();
		var workspace = GetTestWorkspace(envSettings);
		var releaseFileName = workspace.PublishToFolder(dir, appStorePath, appName, appVersion, branch);
		
		// Asserte
		expectedFilePath.Should().Be(releaseFileName);
		bool versionFileExist = FileSystem.File.Exists(expectedFilePath);
		versionFileExist.Should().BeTrue();
		FileSystem.FileInfo.New(expectedFilePath).Length.Should().BeGreaterThan(80000);
		
		
	}

	
	[Test]
	public void PublishWorkspaceToFileTest() {
		// Arrange
		const string appStorePath = @"C:\clioAppStore";
		const string appName = "iframe-sample";
		const string appVersion = "2.0.0";
		const string fileName = $"{appName}_{appVersion}.zip";
		string dir = FileSystem.AllDirectories.FirstOrDefault(d => d.EndsWith("iframe-sample"));
		var envSettings = GetTestEnvironmentSettings();
		var workspace = GetTestWorkspace(envSettings);
		string expectedFileName = Path.Combine(appStorePath, appName, appVersion, fileName);

		// Act
		string resultPath = workspace.PublishToFile(dir, expectedFileName, appVersion);

		// Assert
		resultPath.Should().Be(expectedFileName);
		bool versionFileExist = FileSystem.File.Exists(expectedFileName);
		versionFileExist.Should().BeTrue();
		FileSystem.FileInfo.New(expectedFileName).Length.Should().BeGreaterThan(80000);

		string[] descriptorPaths = FileSystem.Directory.GetFiles(dir, CreatioPackage.DescriptorName, SearchOption.AllDirectories);
		descriptorPaths.Should().NotBeEmpty("because at least one package descriptor should exist");
		// Check that at least one package has the updated version
		bool foundUpdatedVersion = false;
		foreach (string descriptorPath in descriptorPaths) {
			string descriptorContent = FileSystem.File.ReadAllText(descriptorPath);
			var descriptorDto = JsonSerializer.Deserialize<PackageDescriptorDto>(descriptorContent);
			if (descriptorDto?.Descriptor.PackageVersion == appVersion) {
				foundUpdatedVersion = true;
				break;
			}
		}
		foundUpdatedVersion.Should().BeTrue($"because at least one package should have version updated to {appVersion}");
	}

	[TestCase("")]
	[TestCase(null)]
	[Description("Should publish workspace to file without updating versions when app version is not provided")]
	public void PublishWorkspaceToFileWithoutVersionTest(string appVersionE) {
		// Arrange
		const string appStorePath = @"C:\clioAppStore";
		const string appName = "iframe-sample";
		const string appVersion = "7.8.0";
		const string fileName = $"{appName}_{appVersion}.zip";
		string dir = FileSystem.AllDirectories.FirstOrDefault(d => d.EndsWith("iframe-sample"));
		var envSettings = GetTestEnvironmentSettings();
		var workspace = GetTestWorkspace(envSettings);
		string expectedFileName = Path.Combine(appStorePath, appName, appVersion, fileName);

		// Act
		string resultPath = workspace.PublishToFile(dir, expectedFileName, appVersionE);
		
		// Assert
		resultPath.Should().Be(expectedFileName);
		bool versionFileExist = FileSystem.File.Exists(expectedFileName);
		versionFileExist.Should().BeTrue();
		FileSystem.FileInfo.New(expectedFileName).Length.Should().BeGreaterThan(80000);

		string[] descriptorPaths = FileSystem.Directory.GetFiles(dir, CreatioPackage.DescriptorName, SearchOption.AllDirectories);
		descriptorPaths.Should().NotBeEmpty("because at least one package descriptor should exist");
		// Check that at least one package has the updated version
		bool foundUpdatedVersion = false;
		foreach (string descriptorPath in descriptorPaths) {
			string descriptorContent = FileSystem.File.ReadAllText(descriptorPath);
			PackageDescriptorDto descriptorDto = JsonSerializer.Deserialize<PackageDescriptorDto>(descriptorContent);
			if (descriptorDto?.Descriptor.PackageVersion == appVersion) {
				foundUpdatedVersion = true;
				break;
			}
		}
		foundUpdatedVersion.Should().BeTrue($"because at least one package should have version updated to {appVersion}");
	}
	
}
