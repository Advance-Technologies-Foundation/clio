using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using Clio.Command;
using Clio.UserEnvironment;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
///  Unit tests for <see cref="SetFsmConfigCommand" />.
/// </summary>
[TestFixture]
public class SetFsmConfigCommandTests : BaseCommandTests<SetFsmConfigOptions> {

	#region Setup/Teardown

	/// <summary>
	///  Initializes the test setup.
	/// </summary>
	[SetUp]
	public override void Setup() {
		base.Setup();
		_validator = Substitute.For<IValidator<SetFsmConfigOptions>>();
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_command = new SetFsmConfigCommand(_validator, _settingsRepository);
	}

	#endregion

	#region Fields: Private

	private IValidator<SetFsmConfigOptions> _validator;
	private ISettingsRepository _settingsRepository;
	private SetFsmConfigCommand _command;

	#endregion

	#region Methods: Private

	/// <summary>
	///  Returns a sample web.config XML string.
	/// </summary>
	/// <param name="fileDesignModeEnabled">The value for the fileDesignMode enabled attribute.</param>
	/// <param name="useStaticFileContent">The value for the UseStaticFileContent key.</param>
	/// <returns>A sample web.config XML string.</returns>
	private static string GetSampleWebConfig(string fileDesignModeEnabled, string useStaticFileContent) {
		return $"""
				<?xml version="1.0" encoding="utf-8"?>
				<configuration>
				  <terrasoft>
					<fileDesignMode enabled="{fileDesignModeEnabled}" />
				  </terrasoft>
				  <appSettings>
					<add key="UseStaticFileContent" value="{useStaticFileContent}" />
				  </appSettings>
				</configuration>
				""";
	}

	#endregion

	/// <summary>
	///  Verifies that the command returns an error if the config file does not exist.
	/// </summary>
	[Test]
	[Category("Unit")]
	[Description("Verifies that the command returns an error when the config file does not exist.")]
	public void Execute_ReturnsError_WhenConfigDoesNotExist() {
		// Arrange
		string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		Directory.CreateDirectory(tempDir);

		SetFsmConfigOptions options = new() {IsFsm = "on", PhysicalPath = tempDir};
		_validator.Validate(options).Returns(new ValidationResult());

		EnvironmentSettings env = new() {Uri = "http://test.com", IsNetCore = false};
		_settingsRepository.GetEnvironment(options).Returns(env);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, "because config file does not exist");

		Directory.Delete(tempDir, true);
	}

	/// <summary>
	///  Verifies that the command returns an error when validation fails.
	/// </summary>
	[Test]
	[Category("Unit")]
	[Description("Verifies that the command returns an error when validation fails.")]
	public void Execute_ReturnsError_WhenValidationFails() {
		// Arrange
		SetFsmConfigOptions options = new() {IsFsm = "on"};
		_validator.Validate(options).Returns(new ValidationResult(new List<ValidationFailure> {
			new("IsFsm", "Error message")
		}));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, "because validation failed");
	}

	/// <summary>
	///  Verifies that the command updates the config file and returns success when validation passes and the config exists
	///  (Linux path).
	/// </summary>
	[Test, Category("Unit")]
	[Description("Ensures the command updates the config file correctly for Linux paths when validation passes.")]
	public void Execute_ReturnsSuccess_WhenValidationPasses_AndConfigExists_LinuxPath() {
		// Arrange
		string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		Directory.CreateDirectory(tempDir);
		string configPath = Path.Combine(tempDir, "Web.config");
		File.WriteAllText(configPath, GetSampleWebConfig("true", "false"));

		SetFsmConfigOptions options = new() {IsFsm = "off", PhysicalPath = tempDir};
		_validator.Validate(options).Returns(new ValidationResult());

		EnvironmentSettings env = new() {Uri = "http://test.com", IsNetCore = false};
		_settingsRepository.GetEnvironment(options).Returns(env);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because config exists and should be updated");

		XmlDocument doc = new();
		doc.Load(configPath);
		string fileDesignMode = doc.SelectSingleNode("//terrasoft/fileDesignMode").Attributes["enabled"].Value;
		string useStaticFileContent
			= doc.SelectSingleNode("//appSettings/add[@key='UseStaticFileContent']").Attributes["value"].Value;

		fileDesignMode.Should().Be("false", "because IsFsm=off sets fileDesignMode enabled to false");
		useStaticFileContent.Should().Be("true", "because IsFsm=off sets UseStaticFileContent to true");

		Directory.Delete(tempDir, true);
	}

	/// <summary>
	///  Verifies that the command updates the config file and returns success when validation passes and the config exists
	///  (Windows path).
	/// </summary>
	[Test, Category("Unit")]
	[Description("Ensures the command updates the config file correctly for Windows paths when validation passes.")]
	public void Execute_ReturnsSuccess_WhenValidationPasses_AndConfigExists_WindowsPath() {
		// Arrange
		string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		Directory.CreateDirectory(tempDir);
		string configPath = Path.Combine(tempDir, "Web.config");
		File.WriteAllText(configPath, GetSampleWebConfig("false", "true"));

		SetFsmConfigOptions options = new() {IsFsm = "on", PhysicalPath = tempDir};
		_validator.Validate(options).Returns(new ValidationResult());

		EnvironmentSettings env = new() {Uri = "http://test.com", IsNetCore = false};
		_settingsRepository.GetEnvironment(options).Returns(env);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because config exists and should be updated");

		XmlDocument doc = new();
		doc.Load(configPath);
		string fileDesignMode = doc.SelectSingleNode("//terrasoft/fileDesignMode").Attributes["enabled"].Value;
		string useStaticFileContent
			= doc.SelectSingleNode("//appSettings/add[@key='UseStaticFileContent']").Attributes["value"].Value;

		fileDesignMode.Should().Be("true", "because IsFsm=on sets fileDesignMode enabled to true");
		useStaticFileContent.Should().Be("false", "because IsFsm=on sets UseStaticFileContent to false");

		Directory.Delete(tempDir, true);
	}

	/// <summary>
	///  Verifies that the command uses the correct config file name for .NET Core environments.
	/// </summary>
	[Test]
	[Category("Unit")]
	[Description("Verifies that the command uses the correct config file name for .NET Core environments.")]
	public void Execute_UsesCorrectWebConfigFileName_ForNetCore() {
		// Arrange
		string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		Directory.CreateDirectory(tempDir);
		string configPath = Path.Combine(tempDir, "Terrasoft.WebHost.dll.config");
		File.WriteAllText(configPath, GetSampleWebConfig("false", "true"));

		SetFsmConfigOptions options = new() {IsFsm = "on", PhysicalPath = tempDir};
		_validator.Validate(options).Returns(new ValidationResult());

		EnvironmentSettings env = new() {Uri = "http://test.com", IsNetCore = true};
		_settingsRepository.GetEnvironment(options).Returns(env);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because .NET Core config exists and should be updated");

		XmlDocument doc = new();
		doc.Load(configPath);
		string fileDesignMode = doc.SelectSingleNode("//terrasoft/fileDesignMode").Attributes["enabled"].Value;
		string useStaticFileContent
			= doc.SelectSingleNode("//appSettings/add[@key='UseStaticFileContent']").Attributes["value"].Value;

		fileDesignMode.Should().Be("true", "because IsFsm=on sets fileDesignMode enabled to true");
		useStaticFileContent.Should().Be("false", "because IsFsm=on sets UseStaticFileContent to false");

		Directory.Delete(tempDir, true);
	}

}
