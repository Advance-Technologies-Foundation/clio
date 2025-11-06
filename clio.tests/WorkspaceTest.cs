using Autofac;
using Clio.Package;
using Clio.Workspaces;
using FluentAssertions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Clio.Tests;

[TestFixture]
internal class WorkspaceTest
{
	private BindingsModule _bindingModule;
	private IContainer _diContainer;

	private EnvironmentSettings GetTestEnvironmentSettings() {
		var envSettingsJson = @"{
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

	private IWorkspace GetTestWorkspace(EnvironmentSettings envSettings) {
		_bindingModule = new BindingsModule();
		_diContainer = _bindingModule.Register(envSettings);
		var workspace = _diContainer.Resolve<IWorkspace>();
		return workspace;
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
		string appStorePath = @"C:\Temp\clioAppStore";
		string appName = "iframe-sample";
		string appVersion = "1.0.0";
		string fileName = $"{appName}_{appVersion}.zip";
		string originClioSourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory);
		var expectedFileName = Path.Combine(appStorePath, appName, appVersion, fileName);
		string exampleWorkspacePath = Path.Combine(originClioSourcePath, "Examples", "workspaces", appName);
		try {
			// Act
			var envSettings = GetTestEnvironmentSettings();
			var workspace = GetTestWorkspace(envSettings);
			var releaseFileName = workspace.PublishToFolder(exampleWorkspacePath, appStorePath, appName, appVersion);
			// Assert
			expectedFileName.Should().Be(releaseFileName);
			var versionFileExist = File.Exists(expectedFileName);
			versionFileExist.Should().BeTrue();
			new FileInfo(expectedFileName).Length.Should().BeGreaterThan(80000);
		} finally {
			if (File.Exists(expectedFileName)) {
				File.Delete(expectedFileName);
			}
		}
	}

	[TestCase("trunk")]
	[TestCase("master")]
	[TestCase("feature/rnd-2035")]
	[TestCase("bugf%i_:{}x/rnd-2035")]
	[TestCase("feature/rnd-2035")]
	public void PublishWorkspaceWithBranchTest(string branch) {
		// Arrange
		string appStorePath = @"C:\Temp\clioAppStore";
		string appName = "iframe-sample";
		string appVersion = "1.0.0";
		string expectedFileName = Clio.Workspaces.Workspace.GetSanitizeFileNameFromString($"{appName}_{branch}_{appVersion}.zip");
		string originClioSourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory);
		string exampleWorkspacePath = Path.Combine(originClioSourcePath, "Examples", "workspaces", appName);
		string branchFolderName = Clio.Workspaces.Workspace.GetSanitizeFileNameFromString(branch);
		// app_store_path\app_name\bramch\file_name
		var expectedFilePath = Path.Combine(appStorePath, appName, branchFolderName, expectedFileName);
		try {
			// Act
			var envSettings = GetTestEnvironmentSettings();
			var workspace = GetTestWorkspace(envSettings);
			var releaseFileName = workspace.PublishToFolder(exampleWorkspacePath, appStorePath, appName, appVersion, branch);
			// Assert
			expectedFilePath.Should().Be(releaseFileName);
			var versionFileExist = File.Exists(expectedFilePath);
			versionFileExist.Should().BeTrue();
			new FileInfo(expectedFilePath).Length.Should().BeGreaterThan(80000);
		} finally {
			if (File.Exists(expectedFilePath)) {
				File.Delete(expectedFilePath);
			}
		}
	}

	[Test]
	public void PublishWorkspaceToFileTest() {
		// Arrange
		string appName = "iframe-sample";
		string appVersion = "2.3.4";
		string originClioSourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory);
		string originalWorkspacePath = Path.Combine(originClioSourcePath, "Examples", "workspaces", appName);
		string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		string workspaceCopyPath = Path.Combine(tempRoot, "workspace");
		string outputDirectory = Path.Combine(tempRoot, "output");
		string outputFilePath = Path.Combine(outputDirectory, "custom-output.zip");
		Directory.CreateDirectory(outputDirectory);
		CopyDirectory(originalWorkspacePath, workspaceCopyPath);
		try {
			var envSettings = GetTestEnvironmentSettings();
			var workspace = GetTestWorkspace(envSettings);

			// Act
			var resultPath = workspace.PublishToFile(workspaceCopyPath, outputFilePath, appVersion);

			// Assert
			resultPath.Should().Be(Path.GetFullPath(outputFilePath));
			File.Exists(outputFilePath).Should().BeTrue();
			new FileInfo(outputFilePath).Length.Should().BeGreaterThan(80000);
			string[] descriptorPaths = Directory.GetFiles(workspaceCopyPath, CreatioPackage.DescriptorName, SearchOption.AllDirectories);
			descriptorPaths.Should().NotBeEmpty("because at least one package descriptor should exist");
			// Check that at least one package has the updated version
			bool foundUpdatedVersion = false;
			foreach (string descriptorPath in descriptorPaths) {
				string descriptorContent = File.ReadAllText(descriptorPath);
				var descriptorDto = JsonSerializer.Deserialize<PackageDescriptorDto>(descriptorContent);
				if (descriptorDto?.Descriptor.PackageVersion == appVersion) {
					foundUpdatedVersion = true;
					break;
				}
			}
			foundUpdatedVersion.Should().BeTrue($"because at least one package should have version updated to {appVersion}");
		} finally {
			try {
				if (Directory.Exists(tempRoot)) {
					Directory.Delete(tempRoot, true);
				}
			} catch {
				// ignore cleanup errors
			}
		}
	}

	[Test]
	[Description("Should publish workspace to file without updating versions when app version is not provided")]
	public void PublishWorkspaceToFileWithoutVersionTest() {
		// Arrange
		string appName = "iframe-sample";
		string originClioSourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory);
		string originalWorkspacePath = Path.Combine(originClioSourcePath, "Examples", "workspaces", appName);
		string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		string workspaceCopyPath = Path.Combine(tempRoot, "workspace");
		string outputDirectory = Path.Combine(tempRoot, "output");
		string outputFilePath = Path.Combine(outputDirectory, "custom-output-no-version.zip");
		Directory.CreateDirectory(outputDirectory);
		CopyDirectory(originalWorkspacePath, workspaceCopyPath);
		
		// Store original versions before workspace operation
		string[] descriptorPaths = Directory.GetFiles(workspaceCopyPath, CreatioPackage.DescriptorName, SearchOption.AllDirectories);
		var originalVersions = new Dictionary<string, string>();
		foreach (string descriptorPath in descriptorPaths) {
			string descriptorContent = File.ReadAllText(descriptorPath);
			var descriptorDto = JsonSerializer.Deserialize<PackageDescriptorDto>(descriptorContent);
			originalVersions[descriptorPath] = descriptorDto?.Descriptor.PackageVersion ?? "";
		}
		
		try {
			var envSettings = GetTestEnvironmentSettings();
			var workspace = GetTestWorkspace(envSettings);

			// Act - publish without specifying version
			var resultPath = workspace.PublishToFile(workspaceCopyPath, outputFilePath, null);

			// Assert
			resultPath.Should().Be(Path.GetFullPath(outputFilePath));
			File.Exists(outputFilePath).Should().BeTrue("because the zip file should be created");
			new FileInfo(outputFilePath).Length.Should().BeGreaterThan(80000, "because the zip file should contain workspace data");
			
			// Verify that versions were not changed
			foreach (string descriptorPath in descriptorPaths) {
				string descriptorContent = File.ReadAllText(descriptorPath);
				var descriptorDto = JsonSerializer.Deserialize<PackageDescriptorDto>(descriptorContent);
				string currentVersion = descriptorDto?.Descriptor.PackageVersion ?? "";
				string originalVersion = originalVersions[descriptorPath];
				currentVersion.Should().Be(originalVersion, 
					$"because version in {descriptorPath} should remain unchanged when no version is provided");
			}
		} finally {
			try {
				if (Directory.Exists(tempRoot)) {
					Directory.Delete(tempRoot, true);
				}
			} catch {
				// ignore cleanup errors
			}
		}
	}

	[Test]
	[Description("Should publish workspace to file without updating versions when app version is empty string")]
	public void PublishWorkspaceToFileWithEmptyVersionTest() {
		// Arrange
		string appName = "iframe-sample";
		string originClioSourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory);
		string originalWorkspacePath = Path.Combine(originClioSourcePath, "Examples", "workspaces", appName);
		string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		string workspaceCopyPath = Path.Combine(tempRoot, "workspace");
		string outputDirectory = Path.Combine(tempRoot, "output");
		string outputFilePath = Path.Combine(outputDirectory, "custom-output-empty-version.zip");
		Directory.CreateDirectory(outputDirectory);
		CopyDirectory(originalWorkspacePath, workspaceCopyPath);
		
		// Store original versions before workspace operation
		string[] descriptorPaths = Directory.GetFiles(workspaceCopyPath, CreatioPackage.DescriptorName, SearchOption.AllDirectories);
		var originalVersions = new Dictionary<string, string>();
		foreach (string descriptorPath in descriptorPaths) {
			string descriptorContent = File.ReadAllText(descriptorPath);
			var descriptorDto = JsonSerializer.Deserialize<PackageDescriptorDto>(descriptorContent);
			originalVersions[descriptorPath] = descriptorDto?.Descriptor.PackageVersion ?? "";
		}
		
		try {
			var envSettings = GetTestEnvironmentSettings();
			var workspace = GetTestWorkspace(envSettings);

			// Act - publish with empty version string
			var resultPath = workspace.PublishToFile(workspaceCopyPath, outputFilePath, "");

			// Assert
			resultPath.Should().Be(Path.GetFullPath(outputFilePath));
			File.Exists(outputFilePath).Should().BeTrue("because the zip file should be created");
			new FileInfo(outputFilePath).Length.Should().BeGreaterThan(80000, "because the zip file should contain workspace data");
			
			// Verify that versions were not changed
			foreach (string descriptorPath in descriptorPaths) {
				string descriptorContent = File.ReadAllText(descriptorPath);
				var descriptorDto = JsonSerializer.Deserialize<PackageDescriptorDto>(descriptorContent);
				string currentVersion = descriptorDto?.Descriptor.PackageVersion ?? "";
				string originalVersion = originalVersions[descriptorPath];
				currentVersion.Should().Be(originalVersion, 
					$"because version in {descriptorPath} should remain unchanged when empty version is provided");
			}
		} finally {
			try {
				if (Directory.Exists(tempRoot)) {
					Directory.Delete(tempRoot, true);
				}
			} catch {
				// ignore cleanup errors
			}
		}
	}
}
