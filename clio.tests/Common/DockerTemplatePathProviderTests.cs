using System;
using System.IO.Abstractions.TestingHelpers;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
[Description("Unit tests for DockerTemplatePathProvider")]
public class DockerTemplatePathProviderTests {
	private MockFileSystem _mockFileSystem = null!;
	private Clio.Common.IFileSystem _fileSystem = null!;

	[SetUp]
	public void Setup() {
		_mockFileSystem = new MockFileSystem();
		_fileSystem = new Clio.Common.FileSystem(_mockFileSystem);
	}

	[Test]
	[Description("ResolveTemplate should copy bundled templates into the settings docker-templates folder and resolve the requested bundled template.")]
	public void ResolveTemplate_Should_CopyBundledTemplateIntoSettingsFolder() {
		// Arrange
		string bundledRoot = "/bundle/docker-templates";
		string settingsRoot = "/settings";
		_mockFileSystem.AddFile("/bundle/docker-templates/dev/Dockerfile", new MockFileData("FROM scratch"));
		string expectedTemplatePath = _fileSystem.GetFullPath(_fileSystem.Combine(settingsRoot, "docker-templates", "dev"));
		string copiedDockerfilePath = _fileSystem.Combine(expectedTemplatePath, "Dockerfile");

		DockerTemplatePathProvider provider =
			new(_fileSystem, _mockFileSystem, settingsRootPath: settingsRoot, bundledTemplatesRootPath: bundledRoot);

		// Act
		DockerTemplateResolution result = provider.ResolveTemplate("dev");

		// Assert
		result.Name.Should().Be("dev", "because the bundled template name should be preserved");
		result.IsBundled.Should().BeTrue("because the template came from bundled assets");
		result.TemplatePath.Should().Be(expectedTemplatePath,
			"because bundled templates should be copied next to the infrastructure folder");
		_mockFileSystem.File.Exists(copiedDockerfilePath).Should().BeTrue(
			"because the bundled template should be copied into the settings folder");
	}

	[Test]
	[Description("ResolveTemplate should accept a custom template directory path without copying bundled assets.")]
	public void ResolveTemplate_Should_UseCustomTemplateDirectoryWhenPathProvided() {
		// Arrange
		string customTemplatePath = "/workspace/templates/custom-prod";
		_mockFileSystem.AddFile("/workspace/templates/custom-prod/Dockerfile", new MockFileData("FROM scratch"));
		string expectedTemplatePath = _fileSystem.GetFullPath(customTemplatePath);
		DockerTemplatePathProvider provider =
			new(_fileSystem, _mockFileSystem, settingsRootPath: "/settings", bundledTemplatesRootPath: "/bundle/docker-templates");

		// Act
		DockerTemplateResolution result = provider.ResolveTemplate(customTemplatePath);

		// Assert
		result.Name.Should().Be("custom-prod", "because custom templates should use the folder name in image naming");
		result.IsBundled.Should().BeFalse("because this template came from a custom filesystem path");
		result.TemplatePath.Should().Be(expectedTemplatePath,
			"because the custom path should be normalized to an absolute path");
	}
}
