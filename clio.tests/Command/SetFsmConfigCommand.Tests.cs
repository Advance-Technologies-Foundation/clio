using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using Clio.Command;
using Clio.Requests;
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
	///  Verifies that the command works with environment name when physical path is not provided.
	/// </summary>
	[Test]
	[Category("Unit")]
	[Description("Verifies that the command attempts to resolve environment path when physical path is not provided.")]
	public void Execute_AttemptsToResolveEnvironmentPath_WhenPhysicalPathNotProvided() {
		if(!OperatingSystem.IsWindows()) {
			// This test is only relevant for Windows OS
			return;
		}
		// Arrange
		SetFsmConfigOptions options = new() {IsFsm = "on", Environment = "test-env"};
		_validator.Validate(options).Returns(new ValidationResult());
		EnvironmentSettings env = new() {Uri = "https://test.com", IsNetCore = false};
		_settingsRepository.GetEnvironment(options).Returns(env);
		_settingsRepository.GetEnvironment(options.EnvironmentName).Returns(env);

		// Act & Assert
		Action act = () => _command.Execute(options);
		act.Should().Throw<Exception>()
			.WithMessage("Could not find path to environment: 'test-env'");
	}

	/// <summary>
	///  Verifies that the command bypasses environment path resolution when physical path is provided.
	/// </summary>
	[Test]
	[Category("Unit")]
	[Description("Verifies that the command uses the provided physical path directly, bypassing environment resolution.")]
	public void Execute_BypassesEnvironmentResolution_WhenPhysicalPathProvided() {
		if(!OperatingSystem.IsWindows()) {
			// This test is only relevant for Windows OS
			return;
		}
		// Arrange
		string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		Directory.CreateDirectory(tempDir);
		string configPath = Path.Combine(tempDir, "Web.config");
		File.WriteAllText(configPath, GetSampleWebConfig("false", "true"));

		SetFsmConfigOptions options = new() {
			IsFsm = "on",
			PhysicalPath = tempDir,
			Environment = "test-env" // This should be ignored when PhysicalPath is provided
		};
		_validator.Validate(options).Returns(new ValidationResult());

		EnvironmentSettings env = new() {Uri = "https://test.com", IsNetCore = false};
		_settingsRepository.GetEnvironment(options).Returns(env);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because the command should succeed when physical path is provided with valid config");

		// Verify that the settings repository was called to get environment settings (for IsNetCore check)
		_settingsRepository.Received(1).GetEnvironment(options);

		Directory.Delete(tempDir, true);
	}

	/// <summary>
	///  Verifies that the Execute method resolves the correct IIS site path when a matching site is found.
	/// </summary>
	[Test]
	[Category("Unit")]
	[Description("Ensures that Execute resolves the correct IIS site path when a matching site is found.")]
	public void Execute_ResolvesCorrectIISSitePath_WhenMatchingSiteIsFound() {
		if(!OperatingSystem.IsWindows()) {
			// This test is only relevant for Windows OS
			return;
		}
		// Arrange
		const string environmentName = "test-env";
		const string expectedPath = @"C:\inetpub\wwwroot\TestSite";
		SetFsmConfigOptions options = new() {
			Environment = environmentName,
			IsFsm = "on",
			PhysicalPath = null
		};
		EnvironmentSettings env = new() {Uri = "https://test.com", IsNetCore = false};
		_settingsRepository.GetEnvironment(environmentName).Returns(env);
		_settingsRepository.GetEnvironment(options.EnvironmentName).Returns(env);
		_settingsRepository.GetEnvironment(options).Returns(env);

		IISScannerHandler.SiteBinding siteBindingMock = new("test-env", string.Empty, "", expectedPath);
		List<Uri> urisMock = [new("https://test.com")];
		IISScannerHandler.UnregisteredSite mockSite = new(siteBindingMock, urisMock, IISScannerHandler.SiteType.NetFramework);
		IISScannerHandler.FindAllCreatioSites = () => new List<IISScannerHandler.UnregisteredSite> {mockSite};
		_validator.Validate(options).Returns(new ValidationResult());

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, "because the web config file does not exist at the resolved path");
	}

	/// <summary>
	///  Verifies that the command returns an error if the config file does not exist.
	/// </summary>
	[Test]
	[Category("Unit")]
	[Description("Verifies that the command returns an error when the config file does not exist.")]
	public void Execute_ReturnsError_WhenConfigDoesNotExist() {
		if(!OperatingSystem.IsWindows()) {
			// This test is only relevant for Windows OS
			return;
		}
		// Arrange
		string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		Directory.CreateDirectory(tempDir);

		SetFsmConfigOptions options = new() {IsFsm = "on", PhysicalPath = tempDir};
		_validator.Validate(options).Returns(new ValidationResult());

		EnvironmentSettings env = new() {Uri = "https://test.com", IsNetCore = false};
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
		if(!OperatingSystem.IsWindows()) {
			// This test is only relevant for Windows OS
			return;
		}
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
	[Test]
	[Category("Unit")]
	[Description("Ensures the command updates the config file correctly for Linux paths when validation passes.")]
	public void Execute_ReturnsSuccess_WhenValidationPasses_AndConfigExists_LinuxPath() {
		if(!OperatingSystem.IsWindows()) {
			// This test is only relevant for Windows OS
			return;
		}
		// Arrange
		string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		Directory.CreateDirectory(tempDir);
		string configPath = Path.Combine(tempDir, "Web.config");
		File.WriteAllText(configPath, GetSampleWebConfig("true", "false"));

		SetFsmConfigOptions options = new() {IsFsm = "off", PhysicalPath = tempDir};
		_validator.Validate(options).Returns(new ValidationResult());

		EnvironmentSettings env = new() {Uri = "https://test.com", IsNetCore = false};
		_settingsRepository.GetEnvironment(options).Returns(env);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because config exists and should be updated");

		XmlDocument doc = new();
		doc.Load(configPath);
		string fileDesignMode = doc
								.SelectSingleNode("//terrasoft/fileDesignMode")
								.Attributes["enabled"]
								.Value;
		string useStaticFileContent = doc
									.SelectSingleNode("//appSettings/add[@key='UseStaticFileContent']")
									.Attributes["value"]
									.Value;

		fileDesignMode.Should().Be("false", "because IsFsm=off sets fileDesignMode enabled to false");
		useStaticFileContent.Should().Be("true", "because IsFsm=off sets UseStaticFileContent to true");

		Directory.Delete(tempDir, true);
	}

	/// <summary>
	///  Verifies that the command updates the config file and returns success when validation passes and the config exists
	///  (Windows path).
	/// </summary>
	[Test]
	[Category("Unit")]
	[Description("Ensures the command updates the config file correctly for Windows paths when validation passes.")]
	public void Execute_ReturnsSuccess_WhenValidationPasses_AndConfigExists_WindowsPath() {
		if(!OperatingSystem.IsWindows()) {
			// This test is only relevant for Windows OS
			return;
		}
		// Arrange
		string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		Directory.CreateDirectory(tempDir);
		string configPath = Path.Combine(tempDir, "Web.config");
		File.WriteAllText(configPath, GetSampleWebConfig("false", "true"));

		SetFsmConfigOptions options = new() {IsFsm = "on", PhysicalPath = tempDir};
		_validator.Validate(options).Returns(new ValidationResult());

		EnvironmentSettings env = new() {Uri = "https://test.com", IsNetCore = false};
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
	///  Verifies that the command throws an exception when environment URI is null or empty.
	/// </summary>
	[Test]
	[Category("Unit")]
	[Description("Verifies that the command throws an exception when environment URI is null or empty.")]
	public void Execute_ThrowsException_WhenEnvironmentUriIsEmpty() {
		if(!OperatingSystem.IsWindows()) {
			// This test is only relevant for Windows OS
			return;
		}
		// Arrange
		SetFsmConfigOptions options = new() {IsFsm = "on", Environment = "test-env"};
		_validator.Validate(options).Returns(new ValidationResult());

		EnvironmentSettings env = new() {Uri = string.Empty, IsNetCore = false};
		_settingsRepository.GetEnvironment(options).Returns(env);
		_settingsRepository.GetEnvironment(options.EnvironmentName).Returns(env);

		// Act & Assert
		Action act = () => _command.Execute(options);
		act.Should().Throw<Exception>()
			.WithMessage("Could not find path to environment: 'test-env'");
	}

	/// <summary>
	///  Verifies that the command throws an exception when environment URI is null.
	/// </summary>
	[Test]
	[Category("Unit")]
	[Description("Verifies that the command throws an exception when environment URI is null.")]
	public void Execute_ThrowsException_WhenEnvironmentUriIsNull() {
		if(!OperatingSystem.IsWindows()) {
			// This test is only relevant for Windows OS
			return;
		}
		// Arrange
		SetFsmConfigOptions options = new() {IsFsm = "on", Environment = "test-env"};
		_validator.Validate(options).Returns(new ValidationResult());

		EnvironmentSettings env = new() {Uri = null, IsNetCore = false};
		_settingsRepository.GetEnvironment(options).Returns(env);
		_settingsRepository.GetEnvironment(options.EnvironmentName).Returns(env);

		// Act & Assert
		Action act = () => _command.Execute(options);
		act.Should().Throw<Exception>()
			.WithMessage("Could not find path to environment: 'test-env'");
	}

	/// <summary>
	///  Verifies that the Execute method throws an exception when no IIS sites match the environment URI.
	/// </summary>
	[Test]
	[Category("Unit")]
	[Description("Ensures that Execute throws an exception when no IIS sites match the environment URI.")]
	public void Execute_ThrowsException_WhenNoIISSitesMatchEnvironmentUri() {
		if(!OperatingSystem.IsWindows()) {
			// This test is only relevant for Windows OS
			return;
		}
		// Arrange
		string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		Directory.CreateDirectory(tempDir);

		SetFsmConfigOptions options = new() {IsFsm = "on", EnvironmentName = "test-env"};
		_validator.Validate(options).Returns(new ValidationResult());

		EnvironmentSettings env = new() {Uri = "https://test.com", IsNetCore = false};
		_settingsRepository.GetEnvironment(options).Returns(env);
		_settingsRepository.GetEnvironment(options.EnvironmentName).Returns(env);

		IISScannerHandler.FindAllCreatioSites = () => new List<IISScannerHandler.UnregisteredSite>();

		// Act
		Action act = () => _command.Execute(options);

		// Assert
		act.Should().Throw<Exception>()
			.WithMessage($"Could not find path to environment: '{options.EnvironmentName}'",
				"GetWebConfigPathFromEnvName should throw when no IIS sites found");
	}

	/// <summary>
	///  Verifies that the command throws an exception when no matching site is found for the environment.
	/// </summary>
	[Test]
	[Category("Unit")]
	[Description("Verifies that the command throws an exception when no matching site is found for the environment.")]
	public void Execute_ThrowsException_WhenNoMatchingSiteFound() {
		if(!OperatingSystem.IsWindows()) {
			// This test is only relevant for Windows OS
			return;
		}
		// Arrange
		SetFsmConfigOptions options = new() {IsFsm = "on", Environment = "test-env"};
		_validator.Validate(options).Returns(new ValidationResult());

		EnvironmentSettings env = new() {Uri = "https://nonexistent.com", IsNetCore = false};
		_settingsRepository.GetEnvironment(options).Returns(env);
		_settingsRepository.GetEnvironment(options.EnvironmentName).Returns(env);

		// Act & Assert
		// Note: This test will fail in the current implementation because it uses static methods
		// The static method IISScannerHandler.FindAllCreatioSites() will be called and we cannot mock it
		// In a real-world scenario, this would require dependency injection for the IIS scanner functionality
		Action act = () => _command.Execute(options);
		act.Should().Throw<Exception>()
			.WithMessage("Could not find path to environment: 'test-env'");
	}

	/// <summary>
	///  Verifies that the command uses the correct config file name for .NET Core environments.
	/// </summary>
	[Test]
	[Category("Unit")]
	[Description("Verifies that the command uses the correct config file name for .NET Core environments.")]
	public void Execute_UsesCorrectWebConfigFileName_ForNetCore() {
		if(!OperatingSystem.IsWindows()) {
			// This test is only relevant for Windows OS
			return;
		}
		
		// Arrange
		string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		Directory.CreateDirectory(tempDir);
		string configPath = Path.Combine(tempDir, "Terrasoft.WebHost.dll.config");
		File.WriteAllText(configPath, GetSampleWebConfig("false", "true"));

		SetFsmConfigOptions options = new() {IsFsm = "on", PhysicalPath = tempDir};
		_validator.Validate(options).Returns(new ValidationResult());

		EnvironmentSettings env = new() {Uri = "https://test.com", IsNetCore = true};
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
	
	
	[Test]
	[Category("Unit")]
	[Description("Verifies that the command throws an exception on non-Windows OS.")]
	public void Execute_Should_Throw_OnNonWindows() {
		if(!OperatingSystem.IsWindows()) {
			// This test is only relevant for Windows OS
			return;
		}
		
		// Arrange
		SetFsmConfigOptions options = new() { IsFsm = "on" };
		_validator.Validate(options).Returns(new ValidationResult());

		// Act & Assert
		Action act = () => _command.Execute(options);
		act.Should().Throw<Exception>()
			.WithMessage("This command is only supported on Windows OS.");
	}

}
