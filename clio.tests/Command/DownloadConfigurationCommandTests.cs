using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Autofac;
using Clio.Command;
using Clio.Common;
using Clio.Workspace;
using Clio.Workspaces;
using CommandLine;
using FluentAssertions;
using FluentValidation.Results;
using NSubstitute;
using NUnit.Framework;
using SysIoAbstractions = System.IO.Abstractions;

namespace Clio.Tests.Command;

[TestFixture]
[NUnit.Framework.Description("Tests for DownloadConfigurationCommand and DownloadConfigurationCommandOptionsValidator")]
public class DownloadConfigurationCommandTests : BaseCommandTests<DownloadConfigurationCommandOptions>
{
	#region Fields: Private

	private IApplicationDownloader _applicationDownloaderMock;
	private IZipBasedApplicationDownloader _zipBasedApplicationDownloaderMock;
	private IWorkspace _workspaceMock;
	private ILogger _loggerMock;
	private MockFileSystem _mockFileSystem;
	private IFileSystem _fileSystemMock;

	#endregion

	#region Methods: Protected

	protected override void AdditionalRegistrations(ContainerBuilder containerBuilder)
	{
		_applicationDownloaderMock = Substitute.For<IApplicationDownloader>();
		_zipBasedApplicationDownloaderMock = Substitute.For<IZipBasedApplicationDownloader>();
		_workspaceMock = Substitute.For<IWorkspace>();
		_loggerMock = Substitute.For<ILogger>();
		_mockFileSystem = new MockFileSystem();
		_fileSystemMock = new FileSystem(_mockFileSystem);

		containerBuilder.RegisterInstance(_applicationDownloaderMock).As<IApplicationDownloader>();
		containerBuilder.RegisterInstance(_zipBasedApplicationDownloaderMock).As<IZipBasedApplicationDownloader>();
		containerBuilder.RegisterInstance(_workspaceMock).As<IWorkspace>();
		containerBuilder.RegisterInstance(_loggerMock).As<ILogger>();
		containerBuilder.RegisterInstance((SysIoAbstractions.IFileSystem)_mockFileSystem).As<SysIoAbstractions.IFileSystem>();
	}

	#endregion

	#region Tests: DownloadConfigurationCommandOptionsValidator - Basic Validation

	[Test]
	[Description("Should pass validation when BuildZipPath is null")]
	public void Validator_PassesValidation_WhenBuildZipPathIsNull()
	{
		// Arrange
		var options = new DownloadConfigurationCommandOptions { BuildZipPath = null };
		var validator = new DownloadConfigurationCommandOptionsValidator(_mockFileSystem);

		// Act
		ValidationResult result = validator.Validate(options);

		// Assert
		result.IsValid.Should().BeTrue(because: "Null BuildZipPath should be valid as it is optional");
	}

	[Test]
	[Description("Should pass validation when BuildZipPath is empty string")]
	public void Validator_PassesValidation_WhenBuildZipPathIsEmptyString()
	{
		// Arrange
		var options = new DownloadConfigurationCommandOptions { BuildZipPath = string.Empty };
		var validator = new DownloadConfigurationCommandOptionsValidator(_mockFileSystem);

		// Act
		ValidationResult result = validator.Validate(options);

		// Assert
		result.IsValid.Should().BeTrue(because: "Empty BuildZipPath should be valid as it is optional");
	}

	[Test]
	[Description("Should fail validation when ZIP file does not exist")]
	public void Validator_FailsValidation_WhenZipFileNotFound()
	{
		// Arrange
		var options = new DownloadConfigurationCommandOptions { BuildZipPath = "/nonexistent/file_12345.zip" };
		var validator = new DownloadConfigurationCommandOptionsValidator(_mockFileSystem);

		// Act
		ValidationResult result = validator.Validate(options);

		// Assert
		result.IsValid.Should().BeFalse(because: "Non-existent file should fail validation");
		result.Errors.Should().Contain(e => e.ErrorCode == "FILE001", because: "Should have FILE001 error for missing file");
	}

	[Test]
	[Description("Should fail validation when file exists but does not have .zip extension")]
	public void Validator_FailsValidation_WhenFileExistsButNotZipExtension()
	{
		// Arrange
		string testFilePath = "/tmp/testfile.txt";
		_mockFileSystem.AddFile(testFilePath, new MockFileData("test content"));
		
		var options = new DownloadConfigurationCommandOptions { BuildZipPath = testFilePath };
		var validator = new DownloadConfigurationCommandOptionsValidator(_mockFileSystem);

		// Act
		ValidationResult result = validator.Validate(options);

		// Assert
		result.IsValid.Should().BeFalse(because: "File without .zip extension should fail validation");
		result.Errors.Should().Contain(e => e.ErrorCode == "FILE002", 
			because: "Should have FILE002 error for non-zip file extension");
		result.Errors.Should().Contain(e => e.ErrorMessage.Contains(".txt"), 
			because: "Error message should mention the actual extension");
	}

	[Test]
	[Description("Should accept validation when file has .ZIP extension in uppercase")]
	public void Validator_AcceptsValidation_WhenFileHasUppercaseZipExtension()
	{
		// Arrange
		string testFilePath = "/tmp/testfile.ZIP";
		_mockFileSystem.AddFile(testFilePath, new MockFileData("test content"));
		
		var options = new DownloadConfigurationCommandOptions { BuildZipPath = testFilePath };
		var validator = new DownloadConfigurationCommandOptionsValidator(_mockFileSystem);

		// Act
		ValidationResult result = validator.Validate(options);

		// Assert
		result.IsValid.Should().BeTrue(because: ".ZIP extension should be accepted (case-insensitive comparison)");
	}

	[Test]
	[Description("Should fail validation when file exists with no extension")]
	public void Validator_FailsValidation_WhenFileExistsWithNoExtension()
	{
		// Arrange
		string testFilePath = "/tmp/testfile";
		_mockFileSystem.AddFile(testFilePath, new MockFileData("test content"));
		
		var options = new DownloadConfigurationCommandOptions { BuildZipPath = testFilePath };
		var validator = new DownloadConfigurationCommandOptionsValidator(_mockFileSystem);

		// Act
		ValidationResult result = validator.Validate(options);

		// Assert
		result.IsValid.Should().BeFalse(because: "File without any extension should fail validation");
		result.Errors.Should().Contain(e => e.ErrorCode == "FILE002", 
			because: "Should have FILE002 error for missing extension");
	}

	[Test]
	[Description("Should pass validation when file has .zip extension and is not empty")]
	public void Validator_PassesValidation_WhenValidZipFileProvided()
	{
		// Arrange
		string testFilePath = "/tmp/testfile.zip";
		_mockFileSystem.AddFile(testFilePath, new MockFileData("test zip content"));
		
		var options = new DownloadConfigurationCommandOptions { BuildZipPath = testFilePath };
		var validator = new DownloadConfigurationCommandOptionsValidator(_mockFileSystem);

		// Act
		ValidationResult result = validator.Validate(options);

		// Assert
		result.IsValid.Should().BeTrue(because: "Valid .zip file with content should pass validation");
	}

	[Test]
	[Description("Should fail validation when .zip file exists but is empty")]
	public void Validator_FailsValidation_WhenZipFileIsEmpty()
	{
		// Arrange
		string testFilePath = "/tmp/testfile.zip";
		_mockFileSystem.AddFile(testFilePath, new MockFileData(string.Empty));
		
		var options = new DownloadConfigurationCommandOptions { BuildZipPath = testFilePath };
		var validator = new DownloadConfigurationCommandOptionsValidator(_mockFileSystem);

		// Act
		ValidationResult result = validator.Validate(options);

		// Assert
		result.IsValid.Should().BeFalse(because: "Empty .zip file should fail validation");
		result.Errors.Should().Contain(e => e.ErrorCode == "FILE003", 
			because: "Should have FILE003 error for empty zip file");
	}

	[Test]
	[Description("Should pass validation when directory is provided and is not empty")]
	public void Validator_PassesValidation_WhenValidDirectoryProvided()
	{
		// Arrange
		string directoryPath = "/tmp/extracted/creatio";
		_mockFileSystem.AddDirectory(directoryPath);
		_mockFileSystem.AddFile($"{directoryPath}/file.txt", new MockFileData("content"));
		
		var options = new DownloadConfigurationCommandOptions { BuildZipPath = directoryPath };
		var validator = new DownloadConfigurationCommandOptionsValidator(_mockFileSystem);

		// Act
		ValidationResult result = validator.Validate(options);

		// Assert
		result.IsValid.Should().BeTrue(because: "Non-empty directory should pass validation");
	}

	[Test]
	[Description("Should fail validation when directory is provided but is empty")]
	public void Validator_FailsValidation_WhenDirectoryIsEmpty()
	{
		// Arrange
		string directoryPath = "/tmp/extracted/empty";
		_mockFileSystem.AddDirectory(directoryPath);
		
		var options = new DownloadConfigurationCommandOptions { BuildZipPath = directoryPath };
		var validator = new DownloadConfigurationCommandOptionsValidator(_mockFileSystem);

		// Act
		ValidationResult result = validator.Validate(options);

		// Assert
		result.IsValid.Should().BeFalse(because: "Empty directory should fail validation");
		result.Errors.Should().Contain(e => e.ErrorCode == "FILE004", 
			because: "Should have FILE004 error for empty directory");
	}

	#endregion

	#region Tests: DownloadConfigurationCommand - Execute Method

	[Test]
	[NUnit.Framework.Description("Should download from environment when BuildZipPath is not provided")]
	public void Execute_DownloadsFromEnvironment_WhenBuildZipPathNotProvided()
	{
		// Arrange
		var options = new DownloadConfigurationCommandOptions 
		{ 
			BuildZipPath = null,
			Environment = "demo"
		};
		_workspaceMock.WorkspaceSettings.Returns(new WorkspaceSettings { Packages = new[] { "Package1" } });
		
		var command = Container.Resolve<DownloadConfigurationCommand>();

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, because: "Command should return success code when downloading from environment");
		_applicationDownloaderMock.Received(1).Download(Arg.Any<string[]>());
		_zipBasedApplicationDownloaderMock.DidNotReceive().DownloadFromPath(Arg.Any<string>());
		_loggerMock.Received(1).WriteLine("Done");
	}

	[Test]
	[NUnit.Framework.Description("Should call DownloadFromPath when BuildZipPath is provided with ZIP file")]
	public void Execute_CallsDownloadFromPath_WhenBuildZipPathProvidedWithZipFile()
	{
		// Arrange
		var zipPath = "/tmp/creatio.zip";
		_mockFileSystem.AddFile(zipPath, new MockFileData("PK\x03\x04"));
		var options = new DownloadConfigurationCommandOptions 
		{ 
			BuildZipPath = zipPath
		};
		
		var command = Container.Resolve<DownloadConfigurationCommand>();

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, because: "Command should return success code when using ZIP path");
		_zipBasedApplicationDownloaderMock.Received(1).DownloadFromPath(zipPath);
		_applicationDownloaderMock.DidNotReceive().Download(Arg.Any<string[]>());
		_loggerMock.Received(1).WriteLine("Done");
	}

	[Test]
	[NUnit.Framework.Description("Should call DownloadFromPath when BuildZipPath is provided with directory")]
	public void Execute_CallsDownloadFromPath_WhenBuildZipPathProvidedWithDirectory()
	{
		// Arrange
		var directoryPath = "/tmp/extracted/creatio";
		_mockFileSystem.AddDirectory(directoryPath);
		_mockFileSystem.AddFile($"{directoryPath}/file.txt", new MockFileData("content"));
		var options = new DownloadConfigurationCommandOptions 
		{ 
			BuildZipPath = directoryPath
		};
		
		var command = Container.Resolve<DownloadConfigurationCommand>();

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, because: "Command should return success code when using directory path");
		_zipBasedApplicationDownloaderMock.Received(1).DownloadFromPath(directoryPath);
		_loggerMock.Received(1).WriteLine("Done");
	}

	[Test]
	[NUnit.Framework.Description("Should return error code when exception occurs during execution")]
	public void Execute_ReturnsErrorCode_WhenExceptionOccurs()
	{
		// Arrange
		var options = new DownloadConfigurationCommandOptions 
		{ 
			BuildZipPath = null
		};
		_workspaceMock.WorkspaceSettings.Returns(new WorkspaceSettings { Packages = new string[] { } });
		_applicationDownloaderMock
			.When(x => x.Download(Arg.Any<string[]>()))
			.Do(_ => throw new Exception("Test exception"));
		
		var command = Container.Resolve<DownloadConfigurationCommand>();

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, because: "Command should return error code (1) when exception occurs");
		_loggerMock.Received(1).WriteError(Arg.Is<string>(msg => msg.Contains("Test exception")));
	}

	[Test]
	[NUnit.Framework.Description("Should log error with stack trace in debug mode when exception occurs")]
	public void Execute_LogsStackTrace_WhenExceptionOccursInDebugMode()
	{
		// Arrange
		Program.IsDebugMode = true;
		DownloadConfigurationCommandOptions options = new () { 
			BuildZipPath = null
		};
		_workspaceMock.WorkspaceSettings.Returns(new WorkspaceSettings { Packages = [] });
		_applicationDownloaderMock
			.When(x => x.Download(Arg.Any<IEnumerable<string>>()))
			.Do(_ => throw new InvalidOperationException("Debug test exception"));
		
		DownloadConfigurationCommand command = Container.Resolve<DownloadConfigurationCommand>();

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, because: "Command should return error code when exception occurs");
		_loggerMock
			.Received(1)
			.WriteError(Arg.Is<string>(msg => msg.Contains("Stack trace")));
		
		// Cleanup
		Program.IsDebugMode = false;
	}

	[Test]
	[NUnit.Framework.Description("Should log debug info when using build mode")]
	public void Execute_LogsDebugInfo_WhenUsingBuildMode()
	{
		// Arrange
		Program.IsDebugMode = true;
		var zipPath = "/tmp/creatio.zip";
		_mockFileSystem.AddFile(zipPath, new MockFileData("PK\x03\x04"));
		var options = new DownloadConfigurationCommandOptions 
		{ 
			BuildZipPath = zipPath
		};
		
		var command = Container.Resolve<DownloadConfigurationCommand>();

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, because: "Command should succeed with debug mode enabled");
		_loggerMock.Received(1).WriteDebug(Arg.Is<string>(msg => 
			msg.Contains("build mode") && msg.Contains(zipPath)));
		
		// Cleanup
		Program.IsDebugMode = false;
	}

	[Test]
	[NUnit.Framework.Description("Should log debug info when using environment mode")]
	public void Execute_LogsDebugInfo_WhenUsingEnvironmentMode()
	{
		// Arrange
		Program.IsDebugMode = true;
		var options = new DownloadConfigurationCommandOptions 
		{ 
			BuildZipPath = null,
			Environment = "demo"
		};
		_workspaceMock.WorkspaceSettings.Returns(new WorkspaceSettings { Packages = new string[] { } });
		
		var command = Container.Resolve<DownloadConfigurationCommand>();

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, because: "Command should succeed with debug mode enabled");
		_loggerMock.Received(1).WriteDebug(Arg.Is<string>(msg => 
			msg.Contains("environment mode")));
		
		// Cleanup
		Program.IsDebugMode = false;
	}

	#endregion

	#region Tests: DownloadConfigurationCommand - Constructor Validation

	[Test]
	[Description("Should throw ArgumentNullException when IApplicationDownloader is null")]
	public void Constructor_ThrowsArgumentNullException_WhenApplicationDownloaderIsNull()
	{
		// Arrange & Act & Assert
		Action act = () => new DownloadConfigurationCommand(
			null,
			_zipBasedApplicationDownloaderMock,
			_workspaceMock,
			_loggerMock, _fileSystemMock);
		
		act.Should().Throw<ArgumentNullException>(because: "Null applicationDownloader should throw ArgumentNullException");
	}

	[Test]
	[Description("Should throw ArgumentNullException when IZipBasedApplicationDownloader is null")]
	public void Constructor_ThrowsArgumentNullException_WhenZipBasedApplicationDownloaderIsNull()
	{
		// Arrange & Act & Assert
		Action act = () => new DownloadConfigurationCommand(
			_applicationDownloaderMock,
			null,
			_workspaceMock,
			_loggerMock, _fileSystemMock);
		
		act.Should().Throw<ArgumentNullException>(because: "Null zipBasedApplicationDownloader should throw ArgumentNullException");
	}

	[Test]
	[Description("Should throw ArgumentNullException when IWorkspace is null")]
	public void Constructor_ThrowsArgumentNullException_WhenWorkspaceIsNull()
	{
		// Arrange & Act & Assert
		Action act = () => new DownloadConfigurationCommand(
			_applicationDownloaderMock,
			_zipBasedApplicationDownloaderMock,
			null,
			_loggerMock, _fileSystemMock);
		
		act.Should().Throw<ArgumentNullException>(because: "Null workspace should throw ArgumentNullException");
	}

	#endregion

	#region Tests: DownloadConfigurationCommandOptions Metadata

	[Test]
	[Description("Should have correct verb name and alias for command")]
	public void CommandOptions_HasCorrectVerbAndAlias()
	{
		// Act
		var verbAttribute = typeof(DownloadConfigurationCommandOptions)
			.GetCustomAttributes(typeof(VerbAttribute), false)
			.FirstOrDefault() as VerbAttribute;

		// Assert
		verbAttribute.Should().NotBeNull(because: "Command should have Verb attribute");
		verbAttribute.Name.Should().Be("download-configuration", because: "Main verb should be 'download-configuration'");
		verbAttribute.Aliases.Should().Contain("dconf", because: "Should have 'dconf' alias for short command name");
	}

	[Test]
	[Description("Should have BuildZipPath property as optional")]
	public void CommandOptions_HasOptionalBuildZipPath()
	{
		// Act
		var property = typeof(DownloadConfigurationCommandOptions).GetProperty(nameof(DownloadConfigurationCommandOptions.BuildZipPath));
		var optionAttribute = property
			.GetCustomAttributes(typeof(OptionAttribute), false)
			.FirstOrDefault() as OptionAttribute;

		// Assert
		property.Should().NotBeNull(because: "BuildZipPath property should exist");
		optionAttribute.Should().NotBeNull(because: "BuildZipPath should have Option attribute");
		optionAttribute.Required.Should().BeFalse(because: "BuildZipPath should be optional parameter");
		optionAttribute.ShortName.Should().Be("b", because: "Short option should be 'b'");
		optionAttribute.LongName.Should().Be("build", because: "Long option should be 'build'");
	}

	#endregion

	#region Tests: Edge Cases

	[Test]
	[NUnit.Framework.Description("Should treat whitespace-only BuildZipPath as empty")]
	public void Execute_TreatsWhitespacePathAsEmpty_WhenOnlyWhitespace()
	{
		// Arrange
		var options = new DownloadConfigurationCommandOptions 
		{ 
			BuildZipPath = "   \t\n   "
		};
		_workspaceMock.WorkspaceSettings.Returns(new WorkspaceSettings { Packages = new string[] { } });
		
		var command = Container.Resolve<DownloadConfigurationCommand>();

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, because: "Command should treat whitespace as empty and use environment mode");
		_applicationDownloaderMock.Received(1).Download(Arg.Any<string[]>());
	}

	[Test]
	[NUnit.Framework.Description("Should handle mixed path separators in ZIP path")]
	public void Execute_HandlesMixedPathSeparators_InZipPath()
	{
		// Arrange
		var zipPath = "/tmp/extracted/creatio.zip";
		_mockFileSystem.AddFile(zipPath, new MockFileData("PK\x03\x04"));
		var options = new DownloadConfigurationCommandOptions 
		{ 
			BuildZipPath = zipPath
		};
		
		var command = Container.Resolve<DownloadConfigurationCommand>();

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, because: "Command should handle path separators correctly");
		_zipBasedApplicationDownloaderMock.Received(1).DownloadFromPath(zipPath);
	}

	#endregion
}

