using System;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Autofac;
using Clio.Command;
using Clio.Common;
using Clio.Workspaces;
using CommandLine;
using FluentAssertions;
using FluentValidation.Results;
using NSubstitute;
using NUnit.Framework;
using SysIoAbstractions = System.IO.Abstractions;

namespace Clio.Tests.Command;

[TestFixture(Category = "Unit")]
[NUnit.Framework.Description("Tests for DownloadConfigurationCommand and DownloadConfigurationCommandOptionsValidator")]
public class DownloadConfigurationCommandTests : BaseCommandTests<DownloadConfigurationCommandOptions>
{
	#region Fields: Private

	private IApplicationDownloader _applicationDownloaderMock;
	private IZipBasedApplicationDownloader _zipBasedApplicationDownloaderMock;
	private IWorkspace _workspaceMock;
	private ILogger _loggerMock;
	private MockFileSystem _mockFileSystem;

	#endregion

	#region Methods: Protected

	protected override void AdditionalRegistrations(ContainerBuilder containerBuilder)
	{
		_applicationDownloaderMock = Substitute.For<IApplicationDownloader>();
		_zipBasedApplicationDownloaderMock = Substitute.For<IZipBasedApplicationDownloader>();
		_workspaceMock = Substitute.For<IWorkspace>();
		_loggerMock = Substitute.For<ILogger>();
		_mockFileSystem = new MockFileSystem();

		containerBuilder.RegisterInstance(_applicationDownloaderMock).As<IApplicationDownloader>();
		containerBuilder.RegisterInstance(_zipBasedApplicationDownloaderMock).As<IZipBasedApplicationDownloader>();
		containerBuilder.RegisterInstance(_workspaceMock).As<IWorkspace>();
		containerBuilder.RegisterInstance(_loggerMock).As<ILogger>();
		containerBuilder.RegisterInstance((SysIoAbstractions.IFileSystem)_mockFileSystem).As<SysIoAbstractions.IFileSystem>();
	}

	#endregion

	#region Tests: DownloadConfigurationCommandOptionsValidator - Basic Validation

	[Test]
	[NUnit.Framework.Description("Should pass validation when BuildZipPath is null")]
	public void Validator_PassesValidation_WhenBuildZipPathIsNull()
	{
		// Arrange
		var options = new DownloadConfigurationCommandOptions { BuildZipPath = null };
		var validator = new DownloadConfigurationCommandOptionsValidator();

		// Act
		ValidationResult result = validator.Validate(options);

		// Assert
		result.IsValid.Should().BeTrue(because: "Null BuildZipPath should be valid as it is optional");
	}

	[Test]
	[NUnit.Framework.Description("Should pass validation when BuildZipPath is empty string")]
	public void Validator_PassesValidation_WhenBuildZipPathIsEmptyString()
	{
		// Arrange
		var options = new DownloadConfigurationCommandOptions { BuildZipPath = string.Empty };
		var validator = new DownloadConfigurationCommandOptionsValidator();

		// Act
		ValidationResult result = validator.Validate(options);

		// Assert
		result.IsValid.Should().BeTrue(because: "Empty BuildZipPath should be valid as it is optional");
	}

	[Test]
	[NUnit.Framework.Description("Should fail validation when ZIP file does not exist")]
	public void Validator_FailsValidation_WhenZipFileNotFound()
	{
		// Arrange
		var options = new DownloadConfigurationCommandOptions { BuildZipPath = @"C:\nonexistent\file_12345.zip" };
		var validator = new DownloadConfigurationCommandOptionsValidator();

		// Act
		ValidationResult result = validator.Validate(options);

		// Assert
		result.IsValid.Should().BeFalse(because: "Non-existent file should fail validation");
		result.Errors.Should().Contain(e => e.ErrorCode == "FILE001", because: "Should have FILE001 error for missing file");
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
		var zipPath = @"C:\creatio.zip";
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
		var directoryPath = @"C:\extracted\creatio";
		_mockFileSystem.AddDirectory(directoryPath);
		_mockFileSystem.AddFile($@"{directoryPath}\file.txt", new MockFileData("content"));
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
		var options = new DownloadConfigurationCommandOptions 
		{ 
			BuildZipPath = null
		};
		_workspaceMock.WorkspaceSettings.Returns(new WorkspaceSettings { Packages = new string[] { } });
		_applicationDownloaderMock
			.When(x => x.Download(Arg.Any<string[]>()))
			.Do(_ => throw new InvalidOperationException("Debug test exception"));
		
		var command = Container.Resolve<DownloadConfigurationCommand>();

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, because: "Command should return error code when exception occurs");
		_loggerMock
			.Received(1)
			.WriteError(Arg.Is<string>(msg => msg.Contains("[DEBUG]") && msg.Contains("Stack trace")));
		
		// Cleanup
		Program.IsDebugMode = false;
	}

	[Test]
	[NUnit.Framework.Description("Should log debug info when using build mode")]
	public void Execute_LogsDebugInfo_WhenUsingBuildMode()
	{
		// Arrange
		Program.IsDebugMode = true;
		var zipPath = @"C:\creatio.zip";
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
		_loggerMock.Received(1).WriteInfo(Arg.Is<string>(msg => 
			msg.Contains("[DEBUG]") && msg.Contains("build mode") && msg.Contains(zipPath)));
		
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
		_loggerMock.Received(1).WriteInfo(Arg.Is<string>(msg => 
			msg.Contains("[DEBUG]") && msg.Contains("environment mode")));
		
		// Cleanup
		Program.IsDebugMode = false;
	}

	#endregion

	#region Tests: DownloadConfigurationCommand - Constructor Validation

	[Test]
	[NUnit.Framework.Description("Should throw ArgumentNullException when IApplicationDownloader is null")]
	public void Constructor_ThrowsArgumentNullException_WhenApplicationDownloaderIsNull()
	{
		// Arrange & Act & Assert
		Action act = () => new DownloadConfigurationCommand(
			null,
			_zipBasedApplicationDownloaderMock,
			_workspaceMock,
			_loggerMock);
		
		act.Should().Throw<ArgumentNullException>(because: "Null applicationDownloader should throw ArgumentNullException");
	}

	[Test]
	[NUnit.Framework.Description("Should throw ArgumentNullException when IZipBasedApplicationDownloader is null")]
	public void Constructor_ThrowsArgumentNullException_WhenZipBasedApplicationDownloaderIsNull()
	{
		// Arrange & Act & Assert
		Action act = () => new DownloadConfigurationCommand(
			_applicationDownloaderMock,
			null,
			_workspaceMock,
			_loggerMock);
		
		act.Should().Throw<ArgumentNullException>(because: "Null zipBasedApplicationDownloader should throw ArgumentNullException");
	}

	[Test]
	[NUnit.Framework.Description("Should throw ArgumentNullException when IWorkspace is null")]
	public void Constructor_ThrowsArgumentNullException_WhenWorkspaceIsNull()
	{
		// Arrange & Act & Assert
		Action act = () => new DownloadConfigurationCommand(
			_applicationDownloaderMock,
			_zipBasedApplicationDownloaderMock,
			null,
			_loggerMock);
		
		act.Should().Throw<ArgumentNullException>(because: "Null workspace should throw ArgumentNullException");
	}

	#endregion

	#region Tests: DownloadConfigurationCommandOptions Metadata

	[Test]
	[NUnit.Framework.Description("Should have correct verb name and alias for command")]
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
	[NUnit.Framework.Description("Should have BuildZipPath property as optional")]
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
		var zipPath = @"C:/extracted\creatio.zip";
		_mockFileSystem.AddFile(zipPath, new MockFileData("PK\x03\x04"));
		var options = new DownloadConfigurationCommandOptions 
		{ 
			BuildZipPath = zipPath
		};
		
		var command = Container.Resolve<DownloadConfigurationCommand>();

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, because: "Command should handle mixed path separators");
		_zipBasedApplicationDownloaderMock.Received(1).DownloadFromPath(zipPath);
	}

	#endregion
}

