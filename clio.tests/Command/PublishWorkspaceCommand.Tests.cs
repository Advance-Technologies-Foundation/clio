using System;
using System.IO;
using Clio.Command;
using Clio.Common;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[Author("Vladimir Nikonov", "v.nikonov@creatio.com")]
[Category("UnitTests")]
[TestFixture]
public class PublishWorkspaceCommandTests : BaseCommandTests<PublishWorkspaceCommandOptions>
{
	private IWorkspace _mockWorkspace;
	private ILogger _logerMock;
	private PublishWorkspaceCommand _command;

	[SetUp]
	public void SetUp() {
		_mockWorkspace = Substitute.For<IWorkspace>();
		_logerMock = Substitute.For<ILogger>();
		_command = new PublishWorkspaceCommand(_mockWorkspace, _logerMock);
	}

	[Test]
	[Description("Should publish workspace to file with app version when version is provided")]
	public void Execute_WithFilePathAndAppVersion_ShouldCallPublishToFileWithVersion()
	{
		// Arrange
		var options = new PublishWorkspaceCommandOptions
		{
			PositionalWorkspacePath = "/workspace/path",
			FilePath = "/output/path/app.zip",
			AppVersion = "1.2.3"
		};
		_mockWorkspace.PublishToFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
			.Returns("/output/path/app.zip");

		// Act
		var result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because command should execute successfully");
		_mockWorkspace.Received(1).PublishToFile(
			Path.GetFullPath("/workspace/path"),
			Path.GetFullPath("/output/path/app.zip"),
			"1.2.3");
	}

	[Test]
	[Description("Should publish workspace to file without version when version is not provided")]
	public void Execute_WithFilePathWithoutAppVersion_ShouldCallPublishToFileWithNullVersion()
	{
		// Arrange
		var options = new PublishWorkspaceCommandOptions
		{
			PositionalWorkspacePath = "/workspace/path",
			FilePath = "/output/path/app.zip",
			AppVersion = null
		};
		_mockWorkspace.PublishToFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
			.Returns("/output/path/app.zip");

		// Act
		var result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because command should execute successfully");
		_mockWorkspace.Received(1).PublishToFile(
			Path.GetFullPath("/workspace/path"),
			Path.GetFullPath("/output/path/app.zip"),
			null);
	}

	[Test]
	[Description("Should publish workspace to file with empty version when version is empty string")]
	public void Execute_WithFilePathAndEmptyAppVersion_ShouldCallPublishToFileWithEmptyVersion()
	{
		// Arrange
		var options = new PublishWorkspaceCommandOptions
		{
			PositionalWorkspacePath = "/workspace/path",
			FilePath = "/output/path/app.zip",
			AppVersion = ""
		};
		_mockWorkspace.PublishToFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
			.Returns("/output/path/app.zip");

		// Act
		var result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because command should execute successfully");
		_mockWorkspace.Received(1).PublishToFile(
			Path.GetFullPath("/workspace/path"),
			Path.GetFullPath("/output/path/app.zip"),
			"");
	}

	[Test]
	[Description("Should use repo-path when both positional and repo-path are provided")]
	public void Execute_WithBothPositionalAndRepoPath_ShouldUseRepoPath()
	{
		// Arrange
		var options = new PublishWorkspaceCommandOptions
		{
			PositionalWorkspacePath = "/positional/path",
			WorkspaceFolderPath = "/repo/path",
			FilePath = "/output/path/app.zip",
			AppVersion = "1.0.0"
		};
		_mockWorkspace.PublishToFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
			.Returns("/output/path/app.zip");

		// Act
		var result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because command should execute successfully");
		_mockWorkspace.Received(1).PublishToFile(
			Path.GetFullPath("/repo/path"),
			Path.GetFullPath("/output/path/app.zip"),
			"1.0.0");
	}

	[Test]
	[Description("Should throw exception when workspace path is not provided")]
	public void Execute_WithoutWorkspacePath_ShouldThrowException()
	{
		// Arrange
		var options = new PublishWorkspaceCommandOptions
		{
			PositionalWorkspacePath = null,
			WorkspaceFolderPath = null,
			FilePath = "/output/path/app.zip"
		};

		// Act & Assert
		var result = _command.Execute(options);
		result.Should().Be(1, "because command should fail when workspace path is missing");
	}

	[Test]
	[Description("Should publish to hub when file path is not provided")]
	public void Execute_WithoutFilePath_ShouldCallPublishToFolder()
	{
		// Arrange
		var options = new PublishWorkspaceCommandOptions
		{
			PositionalWorkspacePath = "/workspace/path",
			AppHupPath = "/hub/path",
			AppName = "TestApp",
			AppVersion = "2.0.0",
			Branch = "main"
		};

		// Act
		var result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because command should execute successfully");
		_mockWorkspace.Received(1).PublishToFolder(
			Path.GetFullPath("/workspace/path"),
			"/hub/path",
			"TestApp",
			"2.0.0",
			"main");
	}

	[Test]
	[Description("Should throw exception when app hub path is missing in hub mode")]
	public void Execute_HubModeWithoutAppHubPath_ShouldThrowException()
	{
		// Arrange
		var options = new PublishWorkspaceCommandOptions
		{
			PositionalWorkspacePath = "/workspace/path",
			AppHupPath = null,
			AppName = "TestApp",
			AppVersion = "2.0.0"
		};

		// Act
		var result = _command.Execute(options);

		// Assert
		result.Should().Be(1, "because command should fail when app hub path is missing in hub mode");
	}

	[Test]
	[Description("Should throw exception when app name is missing in hub mode")]
	public void Execute_HubModeWithoutAppName_ShouldThrowException()
	{
		// Arrange
		var options = new PublishWorkspaceCommandOptions
		{
			PositionalWorkspacePath = "/workspace/path",
			AppHupPath = "/hub/path",
			AppName = null,
			AppVersion = "2.0.0"
		};

		// Act
		var result = _command.Execute(options);

		// Assert
		result.Should().Be(1, "because command should fail when app name is missing in hub mode");
	}

	[Test]
	[Description("Should handle exception from workspace and return error code")]
	public void Execute_WhenWorkspaceThrowsException_ShouldReturnErrorCode()
	{
		// Arrange
		var options = new PublishWorkspaceCommandOptions
		{
			PositionalWorkspacePath = "/workspace/path",
			FilePath = "/output/path/app.zip",
			AppVersion = "1.0.0"
		};
		_mockWorkspace.When(x => x.PublishToFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()))
			.Do(x => throw new InvalidOperationException("Test error"));

		// Act
		var result = _command.Execute(options);

		// Assert
		result.Should().Be(1, "because command should return error code when workspace operation fails");
	}
}
