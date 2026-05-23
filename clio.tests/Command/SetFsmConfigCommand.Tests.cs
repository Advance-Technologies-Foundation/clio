using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Xml;
using Clio.Common;
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
[Property("Module", "Command")]
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
		_fileSystem = new Clio.Common.FileSystem(new System.IO.Abstractions.FileSystem());
		_logger = Substitute.For<ILogger>();
		_iisScanner = Substitute.For<IIisScanner>();
		_fsmModeStatusService = Substitute.For<IFsmModeStatusService>();
		_fileDesignModePackages = Substitute.For<Clio.Package.IFileDesignModePackages>();
		_fileDesignModePackages.SetFileDesignMode(Arg.Any<bool>()).Returns(
			new Clio.Package.SetFileDesignModeResult(
				EndpointAvailable: false,
				Success: false,
				PreviousFileDesignMode: null,
				NewFileDesignMode: null,
				WebConfigPath: null,
				ErrorMessage: "endpoint not available in tests"));
		_command = new SetFsmConfigCommand(_validator, _settingsRepository, _fileSystem, _logger, _iisScanner,
			_fsmModeStatusService, _fileDesignModePackages);
	}

	#endregion

	#region Fields: Private

	private IValidator<SetFsmConfigOptions> _validator;
	private ISettingsRepository _settingsRepository;
	private Clio.Common.IFileSystem _fileSystem;
	private ILogger _logger;
	private IIisScanner _iisScanner;
	private IFsmModeStatusService _fsmModeStatusService;
	private Clio.Package.IFileDesignModePackages _fileDesignModePackages;
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

		SiteBinding siteBindingMock = new("test-env", string.Empty, "", expectedPath);
		List<Uri> urisMock = [new("https://test.com")];
		UnregisteredSite mockSite = new(siteBindingMock, urisMock, SiteType.NetFramework);
		_iisScanner.FindAllCreatioSites().Returns(new List<UnregisteredSite> {mockSite});
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

		_iisScanner.FindAllCreatioSites().Returns(new List<UnregisteredSite>());

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
	[Description("Verifies that on macOS/Linux + .NET Framework the command treats already-on FSM as a no-op and exits 0.")]
	public void Execute_OnNonWindowsNetFx_ReturnsZeroAndSkipsConfigEdit_WhenServerFsmAlreadyOn() {
		if (OperatingSystem.IsWindows()) {
			return;
		}

		// Arrange
		SetFsmConfigOptions options = new() { IsFsm = "on", Environment = "test-env" };
		_validator.Validate(options).Returns(new ValidationResult());
		_settingsRepository.GetEnvironment(options).Returns(new EnvironmentSettings { IsNetCore = false });
		_fsmModeStatusService.GetStatus("test-env").Returns(
			new FsmModeStatusResult("test-env", "on", false, null));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because server FSM already matches requested 'on' state");
		_fsmModeStatusService.Received(1).GetStatus("test-env");
		_logger.Received().WriteLine(Arg.Is<string>(s => s.Contains("FSM already on") && s.Contains("test-env")));
	}

	[Test]
	[Category("Unit")]
	[Description("Verifies that on macOS/Linux + .NET Framework the command treats already-off FSM as a no-op and exits 0.")]
	public void Execute_OnNonWindowsNetFx_ReturnsZeroAndSkipsConfigEdit_WhenServerFsmAlreadyOff() {
		if (OperatingSystem.IsWindows()) {
			return;
		}

		// Arrange
		SetFsmConfigOptions options = new() { IsFsm = "off", Environment = "test-env" };
		_validator.Validate(options).Returns(new ValidationResult());
		_settingsRepository.GetEnvironment(options).Returns(new EnvironmentSettings { IsNetCore = false });
		_fsmModeStatusService.GetStatus("test-env").Returns(
			new FsmModeStatusResult("test-env", "off", true, null));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because server FSM already matches requested 'off' state");
		_logger.Received().WriteLine(Arg.Is<string>(s => s.Contains("FSM already off") && s.Contains("test-env")));
	}

	[Test]
	[Category("Unit")]
	[Description("Verifies that on macOS/Linux + .NET Framework an 'on' request with server FSM off produces an actionable error.")]
	public void Execute_OnNonWindowsNetFx_ReturnsErrorWithActionableMessage_WhenServerFsmDiffers_OnRequest() {
		if (OperatingSystem.IsWindows()) {
			return;
		}

		// Arrange
		SetFsmConfigOptions options = new() { IsFsm = "on", Environment = "ts1-dev04" };
		_validator.Validate(options).Returns(new ValidationResult());
		_settingsRepository.GetEnvironment(options).Returns(new EnvironmentSettings { IsNetCore = false });
		_fsmModeStatusService.GetStatus("ts1-dev04").Returns(
			new FsmModeStatusResult("ts1-dev04", "off", true, null));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, "because server FSM is off but request is on and no remote toggle is supported");
		_logger.Received().WriteError(Arg.Is<string>(s =>
			s.Contains("(a)") && s.Contains("(b)") && s.Contains("(c)")
			&& s.Contains("ts1-dev04") && s.Contains("get-fsm-mode")));
	}

	[Test]
	[Category("Unit")]
	[Description("Verifies that on macOS/Linux + .NET Framework an 'off' request with server FSM on produces an actionable error.")]
	public void Execute_OnNonWindowsNetFx_ReturnsErrorWithActionableMessage_WhenServerFsmDiffers_OffRequest() {
		if (OperatingSystem.IsWindows()) {
			return;
		}

		// Arrange
		SetFsmConfigOptions options = new() { IsFsm = "off", Environment = "ts1-dev04" };
		_validator.Validate(options).Returns(new ValidationResult());
		_settingsRepository.GetEnvironment(options).Returns(new EnvironmentSettings { IsNetCore = false });
		_fsmModeStatusService.GetStatus("ts1-dev04").Returns(
			new FsmModeStatusResult("ts1-dev04", "on", false, null));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, "because server FSM is on but request is off and no remote toggle is supported");
		_logger.Received().WriteError(Arg.Is<string>(s =>
			s.Contains("(a)") && s.Contains("(b)") && s.Contains("(c)") && s.Contains("ts1-dev04")));
	}

	[Test]
	[Category("Unit")]
	[Description("Verifies that on macOS/Linux + .NET Framework an 'on' request with mismatched server FSM toggles via cliogate when endpoint is available.")]
	public void Execute_OnNonWindowsNetFx_TogglesViaCliogate_WhenEndpointAvailable_OnRequest() {
		if (OperatingSystem.IsWindows()) {
			return;
		}

		// Arrange
		SetFsmConfigOptions options = new() { IsFsm = "on", Environment = "ts1-dev04" };
		_validator.Validate(options).Returns(new ValidationResult());
		_settingsRepository.GetEnvironment(options).Returns(new EnvironmentSettings { IsNetCore = false });
		_fsmModeStatusService.GetStatus("ts1-dev04").Returns(
			new FsmModeStatusResult("ts1-dev04", "off", true, null));
		_fileDesignModePackages.SetFileDesignMode(true).Returns(
			new Clio.Package.SetFileDesignModeResult(
				EndpointAvailable: true,
				Success: true,
				PreviousFileDesignMode: "false",
				NewFileDesignMode: "true",
				WebConfigPath: @"C:\WebAppRoot\studioenu\Web.config",
				ErrorMessage: null));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because cliogate successfully toggled the flag remotely");
		_fileDesignModePackages.Received(1).SetFileDesignMode(true);
		_logger.Received().WriteLine(Arg.Is<string>(s =>
			s.Contains("toggled remotely") && s.Contains("ts1-dev04") && s.Contains("false") && s.Contains("true")));
	}

	[Test]
	[Category("Unit")]
	[Description("Verifies that on macOS/Linux + .NET Framework an 'off' request with mismatched server FSM toggles via cliogate when endpoint is available.")]
	public void Execute_OnNonWindowsNetFx_TogglesViaCliogate_WhenEndpointAvailable_OffRequest() {
		if (OperatingSystem.IsWindows()) {
			return;
		}

		// Arrange
		SetFsmConfigOptions options = new() { IsFsm = "off", Environment = "ts1-dev04" };
		_validator.Validate(options).Returns(new ValidationResult());
		_settingsRepository.GetEnvironment(options).Returns(new EnvironmentSettings { IsNetCore = false });
		_fsmModeStatusService.GetStatus("ts1-dev04").Returns(
			new FsmModeStatusResult("ts1-dev04", "on", false, null));
		_fileDesignModePackages.SetFileDesignMode(false).Returns(
			new Clio.Package.SetFileDesignModeResult(
				EndpointAvailable: true,
				Success: true,
				PreviousFileDesignMode: "true",
				NewFileDesignMode: "false",
				WebConfigPath: @"C:\WebAppRoot\studioenu\Web.config",
				ErrorMessage: null));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because cliogate successfully toggled the flag remotely");
		_fileDesignModePackages.Received(1).SetFileDesignMode(false);
	}

	[Test]
	[Category("Unit")]
	[Description("Verifies fallback to actionable error when cliogate endpoint is available but reports failure.")]
	public void Execute_OnNonWindowsNetFx_FallsBackToActionableError_WhenCliogateEndpointFails() {
		if (OperatingSystem.IsWindows()) {
			return;
		}

		// Arrange
		SetFsmConfigOptions options = new() { IsFsm = "on", Environment = "ts1-dev04" };
		_validator.Validate(options).Returns(new ValidationResult());
		_settingsRepository.GetEnvironment(options).Returns(new EnvironmentSettings { IsNetCore = false });
		_fsmModeStatusService.GetStatus("ts1-dev04").Returns(
			new FsmModeStatusResult("ts1-dev04", "off", true, null));
		_fileDesignModePackages.SetFileDesignMode(true).Returns(
			new Clio.Package.SetFileDesignModeResult(
				EndpointAvailable: true,
				Success: false,
				PreviousFileDesignMode: null,
				NewFileDesignMode: null,
				WebConfigPath: null,
				ErrorMessage: "Permission denied writing to Web.config"));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1);
		_logger.Received().WriteError(Arg.Is<string>(s => s.Contains("Permission denied")));
		_logger.Received().WriteError(Arg.Is<string>(s =>
			s.Contains("(a)") && s.Contains("(b)") && s.Contains("(c)")));
	}

	[Test]
	[Category("Unit")]
	[Description("Verifies that probe failures on macOS/Linux + .NET Framework are wrapped with context and produce exit 1.")]
	public void Execute_OnNonWindowsNetFx_PropagatesProbeFailure() {
		if (OperatingSystem.IsWindows()) {
			return;
		}

		// Arrange
		SetFsmConfigOptions options = new() { IsFsm = "on", Environment = "test-env" };
		_validator.Validate(options).Returns(new ValidationResult());
		_settingsRepository.GetEnvironment(options).Returns(new EnvironmentSettings { IsNetCore = false });
		_fsmModeStatusService.GetStatus("test-env")
			.Returns(_ => throw new InvalidOperationException("Connection refused"));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, "because the probe could not determine server FSM state");
		_logger.Received().WriteError(Arg.Is<string>(s =>
			s.Contains("Could not determine current FSM state")
			&& s.Contains("test-env")
			&& s.Contains("Connection refused")));
	}

	[Test]
	[Category("Unit")]
	[Description("Verifies that --physical-path bypasses the probe on macOS/Linux + .NET Framework even when env is registered.")]
	public void Execute_OnNonWindowsNetFx_BypassesProbe_WhenPhysicalPathProvided() {
		if (OperatingSystem.IsWindows()) {
			return;
		}

		// Arrange
		string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		Directory.CreateDirectory(tempDir);
		string configPath = Path.Combine(tempDir, "Web.config");
		File.WriteAllText(configPath, GetSampleWebConfig("false", "true"));

		SetFsmConfigOptions options = new() { IsFsm = "on", Environment = "test-env", PhysicalPath = tempDir };
		_validator.Validate(options).Returns(new ValidationResult());
		_settingsRepository.GetEnvironment(options).Returns(new EnvironmentSettings { IsNetCore = false });

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because --physical-path bypasses the probe and edits the file directly");
		_fsmModeStatusService.DidNotReceiveWithAnyArgs().GetStatus(default!);

		Directory.Delete(tempDir, true);
	}

	[Test]
	[Category("Unit")]
	[Description("Verifies that non-Windows platforms resolve EnvironmentPath for NET8 environments.")]
	public void Execute_UsesEnvironmentPath_OnNonWindows_WhenNetCore() {
		if(OperatingSystem.IsWindows()) {
			return;
		}

		// Arrange
		string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		Directory.CreateDirectory(tempDir);
		string configPath = Path.Combine(tempDir, "Terrasoft.WebHost.dll.config");
		File.WriteAllText(configPath, GetSampleWebConfig("false", "true"));

		SetFsmConfigOptions options = new() { IsFsm = "on", Environment = "test-env" };
		_validator.Validate(options).Returns(new ValidationResult());
		_settingsRepository.GetEnvironment(options).Returns(new EnvironmentSettings { IsNetCore = true, EnvironmentPath = tempDir, Uri = "http://localhost" });

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because non-Windows NET8 environments should use EnvironmentPath directly");

		Directory.Delete(tempDir, true);
	}

	[Test]
	[Category("Unit")]
	[Description("Verifies that a direct physical path works without registered environment settings when the config file exists.")]
	public void Execute_UsesPhysicalPath_WithoutRegisteredEnvironment_WhenConfigExists() {
		// Arrange
		string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		Directory.CreateDirectory(tempDir);
		string configName = OperatingSystem.IsWindows() ? "Web.config" : "Terrasoft.WebHost.dll.config";
		string configPath = Path.Combine(tempDir, configName);
		File.WriteAllText(configPath, GetSampleWebConfig("false", "true"));

		SetFsmConfigOptions options = new() { IsFsm = "on", PhysicalPath = tempDir };
		_validator.Validate(options).Returns(new ValidationResult());
		_settingsRepository.GetEnvironment(options).Returns((EnvironmentSettings)null!);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because the command should infer the config file from the provided physical path");

		Directory.Delete(tempDir, true);
	}

}


