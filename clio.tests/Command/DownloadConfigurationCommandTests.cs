using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Reflection;
using Autofac;
using Clio.Command;
using Clio.Common;
using Clio.UserEnvironment;
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
[Description("Tests for DownloadConfigurationCommand and DownloadConfigurationCommandOptionsValidator")]
public class DownloadConfigurationCommandTests : BaseCommandTests<DownloadConfigurationCommandOptions>{
	#region Fields: Private

	private IApplicationDownloader _applicationDownloaderMock;
	private IFileSystem _fileSystemMock;
	private ILogger _loggerMock;
	private MockFileSystem _mockFileSystem;
	private IWorkspace _workspaceMock;
	private IZipBasedApplicationDownloader _zipBasedApplicationDownloaderMock;
	private ISettingsRepository _settingsRepositoryMock;

	#endregion

	#region Methods: Protected

	protected override void AdditionalRegistrations(ContainerBuilder containerBuilder) {
		_applicationDownloaderMock = Substitute.For<IApplicationDownloader>();
		_zipBasedApplicationDownloaderMock = Substitute.For<IZipBasedApplicationDownloader>();
		_workspaceMock = Substitute.For<IWorkspace>();
		_loggerMock = Substitute.For<ILogger>();
		_mockFileSystem = new MockFileSystem();
		_fileSystemMock = new FileSystem(_mockFileSystem);
		_settingsRepositoryMock = Substitute.For<ISettingsRepository>();

		containerBuilder.RegisterInstance(_applicationDownloaderMock).As<IApplicationDownloader>();
		containerBuilder.RegisterInstance(_zipBasedApplicationDownloaderMock).As<IZipBasedApplicationDownloader>();
		containerBuilder.RegisterInstance(_workspaceMock).As<IWorkspace>();
		containerBuilder.RegisterInstance(_loggerMock).As<ILogger>();
		containerBuilder.RegisterInstance((SysIoAbstractions.IFileSystem)_mockFileSystem)
						.As<SysIoAbstractions.IFileSystem>();
		containerBuilder.RegisterInstance(_settingsRepositoryMock).As<ISettingsRepository>();

	}

	[TearDown]
	public void Teardown() {
		_applicationDownloaderMock.ClearReceivedCalls();
		_zipBasedApplicationDownloaderMock.ClearReceivedCalls();
	}
	
	
	#endregion

	#region Methods: Public

	[Test]
	[Description("Should have correct verb name and alias for command")]
	public void CommandOptions_HasCorrectVerbAndAlias() {
		// Act
		VerbAttribute verbAttribute = typeof(DownloadConfigurationCommandOptions)
									  .GetCustomAttributes(typeof(VerbAttribute), false)
									  .FirstOrDefault() as VerbAttribute;

		// Assert
		verbAttribute.Should().NotBeNull("Command should have Verb attribute");
		verbAttribute.Name.Should().Be("download-configuration", "Main verb should be 'download-configuration'");
		verbAttribute.Aliases.Should().Contain("dconf", "Should have 'dconf' alias for short command name");
	}

	[Test]
	[Description("Should have BuildZipPath property as optional")]
	public void CommandOptions_HasOptionalBuildZipPath() {
		// Act
		PropertyInfo property
			= typeof(DownloadConfigurationCommandOptions).GetProperty(nameof(DownloadConfigurationCommandOptions
				.BuildZipPath));
		OptionAttribute optionAttribute = property
										  .GetCustomAttributes(typeof(OptionAttribute), false)
										  .FirstOrDefault() as OptionAttribute;

		// Assert
		property.Should().NotBeNull("BuildZipPath property should exist");
		optionAttribute.Should().NotBeNull("BuildZipPath should have Option attribute");
		optionAttribute!.Required.Should().BeFalse("BuildZipPath should be optional parameter");
		optionAttribute.ShortName.Should().Be("b", "Short option should be 'b'");
		optionAttribute.LongName.Should().Be("build", "Long option should be 'build'");
	}

	[Test]
	[Description("Should throw ArgumentNullException when IApplicationDownloader is null")]
	public void Constructor_ThrowsArgumentNullException_WhenApplicationDownloaderIsNull() {
		// Arrange & Act & Assert
		Action act = () => {
			DownloadConfigurationCommand downloadConfigurationCommand = new(
				null,
				_zipBasedApplicationDownloaderMock,
				_workspaceMock,
				_loggerMock, _fileSystemMock, _settingsRepositoryMock);
		};
		act.Should().Throw<ArgumentNullException>("Null applicationDownloader should throw ArgumentNullException");
	}

	[Test]
	[Description("Should throw ArgumentNullException when IWorkspace is null")]
	public void Constructor_ThrowsArgumentNullException_WhenWorkspaceIsNull() {
		// Arrange & Act & Assert
		Action act = () => {
			DownloadConfigurationCommand downloadConfigurationCommand = new DownloadConfigurationCommand(
				_applicationDownloaderMock,
				_zipBasedApplicationDownloaderMock,
				null,
				_loggerMock, _fileSystemMock, _settingsRepositoryMock);
		};

		act.Should().Throw<ArgumentNullException>("Null workspace should throw ArgumentNullException");
	}

	[Test]
	[Description("Should throw ArgumentNullException when IZipBasedApplicationDownloader is null")]
	public void Constructor_ThrowsArgumentNullException_WhenZipBasedApplicationDownloaderIsNull() {
		// Arrange & Act & Assert
		Action act = () => new DownloadConfigurationCommand(
			_applicationDownloaderMock,
			null,
			_workspaceMock,
			_loggerMock, _fileSystemMock, _settingsRepositoryMock);

		act.Should()
		   .Throw<ArgumentNullException>("Null zipBasedApplicationDownloader should throw ArgumentNullException");
	}

	[Test]
	[Description("Should call DownloadFromPath when BuildZipPath is provided with directory")]
	public void Execute_CallsDownloadFromPath_WhenBuildZipPathProvidedWithDirectory() {
		// Arrange
		const string directoryPath = "/tmp/extracted/creatio";
		_mockFileSystem.AddDirectory(directoryPath);
		_mockFileSystem.AddFile($"{directoryPath}/file.txt", new MockFileData("content"));
		DownloadConfigurationCommandOptions options = new() {
			BuildZipPath = directoryPath
		};

		DownloadConfigurationCommand command = Container.Resolve<DownloadConfigurationCommand>();

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, "Command should return success code when using directory path");
		_zipBasedApplicationDownloaderMock.Received(1).DownloadFromPath(directoryPath);
		_applicationDownloaderMock.DidNotReceive().Download(Arg.Any<string[]>());
	}

	[Test]
	[Description("Should call DownloadFromPath when BuildZipPath is provided with ZIP file")]
	public void Execute_CallsDownloadFromPath_WhenBuildZipPathProvidedWithZipFile() {
		// Arrange
		const string zipPath = "/tmp/creatio.zip";
		_mockFileSystem.AddFile(zipPath, new MockFileData("PK\x03\x04"));
		DownloadConfigurationCommandOptions options = new() {
			BuildZipPath = zipPath
		};

		DownloadConfigurationCommand command = Container.Resolve<DownloadConfigurationCommand>();

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, "Command should return success code when using ZIP path");
		_zipBasedApplicationDownloaderMock.Received(1).DownloadFromPath(zipPath);
		_applicationDownloaderMock.DidNotReceive().Download(Arg.Any<string[]>());
	}

	[Test]
	[Description("Should download from environment when BuildZipPath is not provided")]
	public void Execute_DownloadsFromEnvironment_WhenBuildZipPathNotProvided() {
		// Arrange
		DownloadConfigurationCommandOptions options = new() {
			BuildZipPath = null,
			Environment = "demo"
		};
		_workspaceMock.WorkspaceSettings.Returns(new WorkspaceSettings { Packages = ["Package1"] });
		DownloadConfigurationCommand command = Container.Resolve<DownloadConfigurationCommand>();
		
		EnvironmentSettings envSettings = new() { EnvironmentPath = "/path/to/env" };
		_mockFileSystem.Directory.CreateDirectory(envSettings.EnvironmentPath);
		_settingsRepositoryMock.FindEnvironment(options.Environment)
							   .Returns(envSettings);
		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, "Command should return success code when downloading from environment");
		_applicationDownloaderMock.DidNotReceive().Download(Arg.Any<string[]>());
		_zipBasedApplicationDownloaderMock.Received(1).DownloadFromPath(envSettings.EnvironmentPath);
	}

	[Test]
	[Description("Should handle mixed path separators in ZIP path")]
	public void Execute_HandlesMixedPathSeparators_InZipPath() {
		// Arrange
		const string zipPath = "/tmp/extracted/creatio.zip";
		_mockFileSystem.AddFile(zipPath, new MockFileData("PK\x03\x04"));
		DownloadConfigurationCommandOptions options = new() {
			BuildZipPath = zipPath
		};
		DownloadConfigurationCommand command = Container.Resolve<DownloadConfigurationCommand>();

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, "Command should handle path separators correctly");
		_applicationDownloaderMock.DidNotReceive().Download(Arg.Any<string[]>());
		_zipBasedApplicationDownloaderMock.Received(1).DownloadFromPath(zipPath);
	}

	[Test]
	[Description("Should log debug info when using build mode")]
	public void Execute_LogsDebugInfo_WhenUsingBuildMode() {
		// Arrange
		Program.IsDebugMode = true;
		string zipPath = "/tmp/creatio.zip";
		_mockFileSystem.AddFile(zipPath, new MockFileData("PK\x03\x04"));
		DownloadConfigurationCommandOptions options = new() {
			BuildZipPath = zipPath
		};

		DownloadConfigurationCommand command = Container.Resolve<DownloadConfigurationCommand>();

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, "Command should succeed with debug mode enabled");
		_loggerMock.Received(1).WriteDebug(Arg.Is<string>(msg =>
			msg.Contains("build mode") && msg.Contains(zipPath)));

		// Cleanup
		Program.IsDebugMode = false;
	}

	[Test]
	[Description("Should log debug info when using environment mode")]
	public void Execute_LogsDebugInfo_WhenUsingEnvironmentMode() {
		// Arrange
		Program.IsDebugMode = true;
		DownloadConfigurationCommandOptions options = new() {
			BuildZipPath = null,
			Environment = "demo"
		};
		_workspaceMock.WorkspaceSettings.Returns(new WorkspaceSettings { Packages = new string[] { } });

		DownloadConfigurationCommand command = Container.Resolve<DownloadConfigurationCommand>();

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, "Command should succeed with debug mode enabled");
		_loggerMock.Received(1).WriteDebug(Arg.Is<string>(msg =>
			msg.Contains("named env mode")));

		// Cleanup
		Program.IsDebugMode = false;
	}

	[Test]
	[Description("Should log error with stack trace in debug mode when exception occurs")]
	public void Execute_LogsStackTrace_WhenExceptionOccursInDebugMode() {
		// Arrange
		Program.IsDebugMode = true;
		DownloadConfigurationCommandOptions options = new() {
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
		result.Should().Be(1, "Command should return error code when exception occurs");
		_loggerMock
			.Received(1)
			.WriteError(Arg.Is<string>(msg => msg.Contains("Stack trace")));

		// Cleanup
		Program.IsDebugMode = false;
	}

	[Test]
	[Description("Should return error code when exception occurs during execution")]
	public void Execute_ReturnsErrorCode_WhenExceptionOccurs() {
		// Arrange
		DownloadConfigurationCommandOptions options = new() {
			BuildZipPath = null
		};
		_workspaceMock.WorkspaceSettings.Returns(new WorkspaceSettings { Packages = new string[] { } });
		_applicationDownloaderMock
			.When(x => x.Download(Arg.Any<string[]>()))
			.Do(_ => throw new Exception("Test exception"));

		DownloadConfigurationCommand command = Container.Resolve<DownloadConfigurationCommand>();

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, "Command should return error code (1) when exception occurs");
		_loggerMock.Received(1).WriteError(Arg.Is<string>(msg => msg.Contains("Test exception")));
	}

	[Test]
	[Description("Should treat whitespace-only BuildZipPath as empty")]
	public void Execute_TreatsWhitespacePathAsEmpty_WhenOnlyWhitespace() {
		// Arrange
		DownloadConfigurationCommandOptions options = new() {
			BuildZipPath = "   \t\n   "
		};
		_workspaceMock.WorkspaceSettings.Returns(new WorkspaceSettings { Packages = new string[] { } });

		DownloadConfigurationCommand command = Container.Resolve<DownloadConfigurationCommand>();

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, "Command should treat whitespace as empty and use environment mode");
		_applicationDownloaderMock.Received(1).Download(Arg.Any<string[]>());
	}

	[Test]
	[Description("Should accept validation when file has .ZIP extension in uppercase")]
	public void Validator_AcceptsValidation_WhenFileHasUppercaseZipExtension() {
		// Arrange
		string testFilePath = "/tmp/testfile.ZIP";
		_mockFileSystem.AddFile(testFilePath, new MockFileData("test content"));

		DownloadConfigurationCommandOptions options = new() { BuildZipPath = testFilePath };
		DownloadConfigurationCommandOptionsValidator validator = new(_mockFileSystem);

		// Act
		ValidationResult result = validator.Validate(options);

		// Assert
		result.IsValid.Should().BeTrue(".ZIP extension should be accepted (case-insensitive comparison)");
	}

	[Test]
	[Description("Should fail validation when directory is provided but is empty")]
	public void Validator_FailsValidation_WhenDirectoryIsEmpty() {
		// Arrange
		string directoryPath = "/tmp/extracted/empty";
		_mockFileSystem.AddDirectory(directoryPath);

		DownloadConfigurationCommandOptions options = new() { BuildZipPath = directoryPath };
		DownloadConfigurationCommandOptionsValidator validator = new(_mockFileSystem);

		// Act
		ValidationResult result = validator.Validate(options);

		// Assert
		result.IsValid.Should().BeFalse("Empty directory should fail validation");
		result.Errors.Should().Contain(e => e.ErrorCode == "FILE004",
			"Should have FILE004 error for empty directory");
	}

	[Test]
	[Description("Should fail validation when file exists but does not have .zip extension")]
	public void Validator_FailsValidation_WhenFileExistsButNotZipExtension() {
		// Arrange
		string testFilePath = "/tmp/testfile.txt";
		_mockFileSystem.AddFile(testFilePath, new MockFileData("test content"));

		DownloadConfigurationCommandOptions options = new() { BuildZipPath = testFilePath };
		DownloadConfigurationCommandOptionsValidator validator = new(_mockFileSystem);

		// Act
		ValidationResult result = validator.Validate(options);

		// Assert
		result.IsValid.Should().BeFalse("File without .zip extension should fail validation");
		result.Errors.Should().Contain(e => e.ErrorCode == "FILE002",
			"Should have FILE002 error for non-zip file extension");
		result.Errors.Should().Contain(e => e.ErrorMessage.Contains(".txt"),
			"Error message should mention the actual extension");
	}

	[Test]
	[Description("Should fail validation when file exists with no extension")]
	public void Validator_FailsValidation_WhenFileExistsWithNoExtension() {
		// Arrange
		string testFilePath = "/tmp/testfile";
		_mockFileSystem.AddFile(testFilePath, new MockFileData("test content"));

		DownloadConfigurationCommandOptions options = new() { BuildZipPath = testFilePath };
		DownloadConfigurationCommandOptionsValidator validator = new(_mockFileSystem);

		// Act
		ValidationResult result = validator.Validate(options);

		// Assert
		result.IsValid.Should().BeFalse("File without any extension should fail validation");
		result.Errors.Should().Contain(e => e.ErrorCode == "FILE002",
			"Should have FILE002 error for missing extension");
	}

	[Test]
	[Description("Should fail validation when .zip file exists but is empty")]
	public void Validator_FailsValidation_WhenZipFileIsEmpty() {
		// Arrange
		string testFilePath = "/tmp/testfile.zip";
		_mockFileSystem.AddFile(testFilePath, new MockFileData(string.Empty));

		DownloadConfigurationCommandOptions options = new() { BuildZipPath = testFilePath };
		DownloadConfigurationCommandOptionsValidator validator = new(_mockFileSystem);

		// Act
		ValidationResult result = validator.Validate(options);

		// Assert
		result.IsValid.Should().BeFalse("Empty .zip file should fail validation");
		result.Errors.Should().Contain(e => e.ErrorCode == "FILE003",
			"Should have FILE003 error for empty zip file");
	}

	[Test]
	[Description("Should fail validation when ZIP file does not exist")]
	public void Validator_FailsValidation_WhenZipFileNotFound() {
		// Arrange
		DownloadConfigurationCommandOptions options = new() { BuildZipPath = "/nonexistent/file_12345.zip" };
		DownloadConfigurationCommandOptionsValidator validator = new(_mockFileSystem);

		// Act
		ValidationResult result = validator.Validate(options);

		// Assert
		result.IsValid.Should().BeFalse("Non-existent file should fail validation");
		result.Errors.Should().Contain(e => e.ErrorCode == "FILE001", "Should have FILE001 error for missing file");
	}

	[Test]
	[Description("Should pass validation when BuildZipPath is empty string")]
	public void Validator_PassesValidation_WhenBuildZipPathIsEmptyString() {
		// Arrange
		DownloadConfigurationCommandOptions options = new() { BuildZipPath = string.Empty };
		DownloadConfigurationCommandOptionsValidator validator = new(_mockFileSystem);

		// Act
		ValidationResult result = validator.Validate(options);

		// Assert
		result.IsValid.Should().BeTrue("Empty BuildZipPath should be valid as it is optional");
	}

	[Test]
	[Description("Should pass validation when BuildZipPath is null")]
	public void Validator_PassesValidation_WhenBuildZipPathIsNull() {
		// Arrange
		DownloadConfigurationCommandOptions options = new() { BuildZipPath = null };
		DownloadConfigurationCommandOptionsValidator validator = new(_mockFileSystem);

		// Act
		ValidationResult result = validator.Validate(options);

		// Assert
		result.IsValid.Should().BeTrue("Null BuildZipPath should be valid as it is optional");
	}

	[Test]
	[Description("Should pass validation when directory is provided and is not empty")]
	public void Validator_PassesValidation_WhenValidDirectoryProvided() {
		// Arrange
		string directoryPath = "/tmp/extracted/creatio";
		_mockFileSystem.AddDirectory(directoryPath);
		_mockFileSystem.AddFile($"{directoryPath}/file.txt", new MockFileData("content"));

		DownloadConfigurationCommandOptions options = new() { BuildZipPath = directoryPath };
		DownloadConfigurationCommandOptionsValidator validator = new(_mockFileSystem);

		// Act
		ValidationResult result = validator.Validate(options);

		// Assert
		result.IsValid.Should().BeTrue("Non-empty directory should pass validation");
	}

	[Test]
	[Description("Should pass validation when file has .zip extension and is not empty")]
	public void Validator_PassesValidation_WhenValidZipFileProvided() {
		// Arrange
		string testFilePath = "/tmp/testfile.zip";
		_mockFileSystem.AddFile(testFilePath, new MockFileData("test zip content"));

		DownloadConfigurationCommandOptions options = new() { BuildZipPath = testFilePath };
		DownloadConfigurationCommandOptionsValidator validator = new(_mockFileSystem);

		// Act
		ValidationResult result = validator.Validate(options);

		// Assert
		result.IsValid.Should().BeTrue("Valid .zip file with content should pass validation");
	}

	#endregion
}
