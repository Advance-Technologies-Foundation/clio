using System;
using System.Collections.Generic;
using Clio.Command;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Description("Tests for EnvManageUiCommand - Interactive environment management")]
public class EnvManageUiCommandTests : BaseCommandTests<EnvManageUiOptions>
{
	#region Fields: Private

	private ISettingsRepository _settingsRepository;
	private ILogger _logger;
	private IEnvManageUiService _service;
	private EnvManageUiCommand _command;

	#endregion

	#region Methods: Setup

	[SetUp]
	public void Setup()
	{
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_logger = Substitute.For<ILogger>();
		_service = new EnvManageUiService();
		
		_command = new EnvManageUiCommand(_settingsRepository, _logger, _service);
		
		// Default setup
		_settingsRepository.AppSettingsFilePath.Returns("/test/path/appsettings.json");
		_settingsRepository.GetAllEnvironments().Returns(new Dictionary<string, EnvironmentSettings>());
		_settingsRepository.GetDefaultEnvironmentName().Returns("dev");
	}

	#endregion

	#region Tests: Constructor

	[Test]
	[Description("Command should be properly instantiated with all dependencies")]
	public void Constructor_WithValidDependencies_ShouldCreateInstance()
	{
		// Arrange & Act
		var command = new EnvManageUiCommand(_settingsRepository, _logger, _service);
		
		// Assert
		command.Should().NotBeNull(because: "command should be created with valid dependencies");
	}

	[Test]
	[Description("Command should throw when required dependencies are null")]
	public void Constructor_WithNullDependencies_ShouldThrow()
	{
		// Arrange & Act & Assert
		Action actNullRepo = () => new EnvManageUiCommand(null, _logger, _service);
		Action actNullLogger = () => new EnvManageUiCommand(_settingsRepository, null, _service);
		Action actNullService = () => new EnvManageUiCommand(_settingsRepository, _logger, null);
		
		actNullRepo.Should().Throw<ArgumentNullException>(because: "settings repository is required");
		actNullLogger.Should().Throw<ArgumentNullException>(because: "logger is required");
		actNullService.Should().Throw<ArgumentNullException>(because: "service is required");
	}

	#endregion
}

[TestFixture]
[Category("Unit")]
[Description("Tests for EnvManageUiService - Business logic and validation")]
public class EnvManageUiServiceTests
{
	#region Fields: Private

	private ISettingsRepository _settingsRepository;
	private EnvManageUiService _service;

	#endregion

	#region Methods: Setup

	[SetUp]
	public void Setup()
	{
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_service = new EnvManageUiService();
	}

	#endregion

	#region Tests: ValidateEnvironmentName

	[Test]
	[Description("Valid environment name should pass validation")]
	[TestCase("dev")]
	[TestCase("prod")]
	[TestCase("test-env")]
	[TestCase("my_environment")]
	[TestCase("env123")]
	public void ValidateEnvironmentName_ValidName_ShouldReturnSuccess(string name)
	{
		// Arrange
		_settingsRepository.IsEnvironmentExists(name).Returns(false);
		
		// Act
		var result = _service.ValidateEnvironmentName(name, _settingsRepository);
		
		// Assert
		result.Successful.Should().BeTrue(because: $"'{name}' is a valid environment name");
	}

	[Test]
	[Description("Empty or whitespace name should fail validation")]
	[TestCase("")]
	[TestCase(" ")]
	[TestCase(null)]
	public void ValidateEnvironmentName_EmptyName_ShouldReturnError(string name)
	{
		// Arrange & Act
		var result = _service.ValidateEnvironmentName(name, _settingsRepository);
		
		// Assert
		result.Successful.Should().BeFalse(because: "environment name cannot be empty");
		result.Message.Should().Contain("cannot be empty", because: "error message should be descriptive");
	}

	[Test]
	[Description("Name exceeding 50 characters should fail validation")]
	public void ValidateEnvironmentName_TooLongName_ShouldReturnError()
	{
		// Arrange
		string longName = new string('a', 51);
		
		// Act
		var result = _service.ValidateEnvironmentName(longName, _settingsRepository);
		
		// Assert
		result.Successful.Should().BeFalse(because: "name length should be limited to 50 characters");
		result.Message.Should().Contain("50 characters", because: "error should specify the limit");
	}

	[Test]
	[Description("Name with invalid characters should fail validation")]
	[TestCase("test env")]
	[TestCase("test@env")]
	[TestCase("test.env")]
	[TestCase("test/env")]
	[TestCase("test\\env")]
	public void ValidateEnvironmentName_InvalidCharacters_ShouldReturnError(string name)
	{
		// Arrange & Act
		var result = _service.ValidateEnvironmentName(name, _settingsRepository);
		
		// Assert
		result.Successful.Should().BeFalse(because: $"'{name}' contains invalid characters");
		result.Message.Should().Contain("letters, numbers, underscores, and hyphens", 
			because: "error should specify allowed characters");
	}

	[Test]
	[Description("Duplicate environment name should fail validation")]
	public void ValidateEnvironmentName_DuplicateName_ShouldReturnError()
	{
		// Arrange
		const string existingName = "dev";
		_settingsRepository.IsEnvironmentExists(existingName).Returns(true);
		
		// Act
		var result = _service.ValidateEnvironmentName(existingName, _settingsRepository);
		
		// Assert
		result.Successful.Should().BeFalse(because: "duplicate names should not be allowed");
		result.Message.Should().Contain("already exists", because: "error should indicate duplication");
	}

	#endregion

	#region Tests: ValidateUrl

	[Test]
	[Description("Valid HTTP and HTTPS URLs should pass validation")]
	[TestCase("https://dev.creatio.com")]
	[TestCase("http://localhost:8080")]
	[TestCase("https://app.example.com:443")]
	[TestCase("http://192.168.1.1")]
	public void ValidateUrl_ValidUrl_ShouldReturnSuccess(string url)
	{
		// Arrange & Act
		var result = _service.ValidateUrl(url);
		
		// Assert
		result.Successful.Should().BeTrue(because: $"'{url}' is a valid URL");
	}

	[Test]
	[Description("Empty or whitespace URL should fail validation")]
	[TestCase("")]
	[TestCase(" ")]
	[TestCase(null)]
	public void ValidateUrl_EmptyUrl_ShouldReturnError(string url)
	{
		// Arrange & Act
		var result = _service.ValidateUrl(url);
		
		// Assert
		result.Successful.Should().BeFalse(because: "URL cannot be empty");
		result.Message.Should().Contain("cannot be empty", because: "error message should be descriptive");
	}

	[Test]
	[Description("Invalid URL format should fail validation")]
	[TestCase("not-a-url")]
	[TestCase("just-text")]
	[TestCase("www.example.com")]
	public void ValidateUrl_InvalidFormat_ShouldReturnError(string url)
	{
		// Arrange & Act
		var result = _service.ValidateUrl(url);
		
		// Assert
		result.Successful.Should().BeFalse(because: $"'{url}' is not a valid URL format");
		result.Message.Should().Contain("Invalid URL", because: "error should indicate format problem");
	}

	[Test]
	[Description("URL with invalid protocol should fail validation")]
	[TestCase("ftp://example.com")]
	[TestCase("file:///path/to/file")]
	[TestCase("mailto:test@example.com")]
	public void ValidateUrl_InvalidProtocol_ShouldReturnError(string url)
	{
		// Arrange & Act
		var result = _service.ValidateUrl(url);
		
		// Assert
		result.Successful.Should().BeFalse(because: $"'{url}' uses invalid protocol");
		result.Message.Should().Contain("http", because: "error should mention required protocols");
	}

	#endregion

	#region Tests: MaskSensitiveData

	[Test]
	[Description("Sensitive fields should be masked with asterisks")]
	[TestCase("Password", "secret123")]
	[TestCase("ClientSecret", "abc-def-ghi")]
	[TestCase("DBPassword", "dbpass")]
	public void MaskSensitiveData_SensitiveField_ShouldReturnMasked(string fieldName, string value)
	{
		// Arrange & Act
		var result = _service.MaskSensitiveData(fieldName, value);
		
		// Assert
		result.Should().Contain("****", because: $"{fieldName} should be masked for security");
		result.Should().NotContain(value, because: "actual value should not be visible");
	}

	[Test]
	[Description("Non-sensitive fields should not be masked")]
	[TestCase("Username", "admin")]
	[TestCase("URL", "https://example.com")]
	[TestCase("Name", "MyEnvironment")]
	public void MaskSensitiveData_NonSensitiveField_ShouldReturnValue(string fieldName, string value)
	{
		// Arrange & Act
		var result = _service.MaskSensitiveData(fieldName, value);
		
		// Assert
		result.Should().Contain(value, because: $"{fieldName} is not sensitive and should be visible");
	}

	[Test]
	[Description("Empty or null values should be indicated as not set")]
	[TestCase("Password", "")]
	[TestCase("ClientSecret", null)]
	public void MaskSensitiveData_EmptyValue_ShouldReturnNotSet(string fieldName, string value)
	{
		// Arrange & Act
		var result = _service.MaskSensitiveData(fieldName, value);
		
		// Assert
		result.Should().Contain("not set", because: "empty values should be clearly indicated");
	}

	#endregion

	#region Tests: CreateDetailsTable

	[Test]
	[Description("CreateDetailsTable should return properly configured table")]
	public void CreateDetailsTable_WithTitle_ShouldReturnConfiguredTable()
	{
		// Arrange
		const string title = "Test Configuration";
		
		// Act
		var table = _service.CreateDetailsTable(title);
		
		// Assert
		table.Should().NotBeNull(because: "table should be created");
		table.Title.Should().NotBeNull(because: "table should have a title");
		table.Columns.Count.Should().Be(2, because: "table should have Property and Value columns");
	}

	[Test]
	[Description("CreateDetailsTable should handle empty title")]
	public void CreateDetailsTable_WithEmptyTitle_ShouldStillCreateTable()
	{
		// Arrange
		const string title = "";
		
		// Act
		var table = _service.CreateDetailsTable(title);
		
		// Assert
		table.Should().NotBeNull(because: "table should be created even with empty title");
		table.Columns.Count.Should().Be(2, because: "table structure should remain consistent");
	}

	#endregion
}
