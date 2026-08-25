using System;
using Clio.Command;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class AddCustomLoggingCommandTests : BaseCommandTests<AddCustomLoggingOptions> {
	private AddCustomLoggingCommand _command;
	private ICustomLoggingConfigurator _configurator;
	private IEnvironmentRestartService _restartService;
	private ISettingsRepository _settingsRepository;
	private ILogger _logger;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<AddCustomLoggingCommand>();
	}

	public override void TearDown() {
		_configurator.ClearReceivedCalls();
		_restartService.ClearReceivedCalls();
		_settingsRepository.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection services) {
		base.AdditionalRegistrations(services);
		_configurator = Substitute.For<ICustomLoggingConfigurator>();
		_restartService = Substitute.For<IEnvironmentRestartService>();
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_logger = Substitute.For<ILogger>();
		services.AddTransient<IValidator<AddCustomLoggingOptions>, AddCustomLoggingOptionsValidator>();
		services.AddTransient(_ => _configurator);
		services.AddTransient(_ => _restartService);
		services.AddTransient(_ => _settingsRepository);
		services.AddTransient(_ => _logger);
	}

	[Test]
	[Description("Configures a registered local environment and explains that restart remains explicit.")]
	public void Execute_ShouldConfigureWithoutRestart_WhenRestartWasNotRequested() {
		// Arrange
		AddCustomLoggingOptions options = ValidOptions();
		_settingsRepository.FindCurrentEnvironment("dev").Returns(new EnvironmentSettings {
			EnvironmentPath = "app-root", Uri = "http://localhost"
		});
		_configurator.Configure("app-root", "MyPackage", "Info", null).Returns(Success(changed: true));

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a validated two-file configuration is successful");
		_restartService.DidNotReceive().Restart(Arg.Any<EnvironmentSettings>());
		_logger.Received().WriteInfo(Arg.Is<string>(message => message.Contains("Restart is required", StringComparison.Ordinal)));
		_logger.Received().WriteInfo("Configured log path: ${TodayLogPath}/MyPackage.log");
		_logger.Received().WriteInfo("Configured environment 'dev'.");
	}

	[Test]
	[Description("Restarts the selected environment only when restart-environment is explicitly true.")]
	public void Execute_ShouldRestart_WhenExplicitlyRequested() {
		// Arrange
		AddCustomLoggingOptions options = ValidOptions();
		options.RestartEnvironment = true;
		_settingsRepository.FindCurrentEnvironment("dev").Returns(new EnvironmentSettings {
			EnvironmentPath = "app-root", Uri = "http://localhost"
		});
		_configurator.Configure("app-root", "MyPackage", "Info", null).Returns(Success(changed: false));
		_restartService.Restart(Arg.Any<EnvironmentSettings>()).Returns(0);

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the explicitly requested restart completed successfully");
		_restartService.Received(1).Restart(Arg.Is<EnvironmentSettings>(settings => settings.EnvironmentPath == "app-root"));
	}

	[Test]
	[Description("Returns the restart failure code after logging was configured.")]
	public void Execute_ShouldFail_WhenExplicitRestartFails() {
		// Arrange
		AddCustomLoggingOptions options = ValidOptions();
		options.RestartEnvironment = true;
		_settingsRepository.FindCurrentEnvironment("dev").Returns(new EnvironmentSettings {
			EnvironmentPath = "app-root", Uri = "http://localhost"
		});
		_configurator.Configure("app-root", "MyPackage", "Info", null).Returns(Success(changed: true));
		_restartService.Restart(Arg.Any<EnvironmentSettings>()).Returns(1);

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a requested restart failure must propagate to the caller");
	}

	[Test]
	[Description("Rejects restart when the registered local environment has no URI.")]
	public void Execute_ShouldFail_WhenRestartEnvironmentHasNoUri() {
		// Arrange
		AddCustomLoggingOptions options = ValidOptions();
		options.RestartEnvironment = true;
		_settingsRepository.FindCurrentEnvironment("dev").Returns(new EnvironmentSettings { EnvironmentPath = "app-root" });

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "restart requires a registered application URI");
		_configurator.DidNotReceive().Configure(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
		_logger.Received().WriteError(Arg.Is<string>(message => message.Contains("does not have a Uri", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Rejects an unregistered environment before touching local configuration files.")]
	public void Execute_ShouldFail_WhenEnvironmentIsNotRegistered() {
		// Arrange
		AddCustomLoggingOptions options = ValidOptions();
		_settingsRepository.FindCurrentEnvironment("dev").Returns((EnvironmentSettings)null);

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "the command can only mutate a registered local installation");
		_configurator.DidNotReceive().Configure(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
		_logger.Received().WriteError(Arg.Is<string>(message => message.Contains("not registered", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Rejects a registered environment that has no local installation path.")]
	public void Execute_ShouldFail_WhenRegisteredEnvironmentHasNoEnvironmentPath() {
		// Arrange
		AddCustomLoggingOptions options = ValidOptions();
		_settingsRepository.FindCurrentEnvironment("dev").Returns(new EnvironmentSettings { EnvironmentPath = " " });

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "the command can only edit a registered local Creatio installation");
		_configurator.DidNotReceive().Configure(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
		_logger.Received().WriteError(Arg.Is<string>(message => message.Contains("does not have a local EnvironmentPath", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Rejects file-name paths and NLog layout expressions before filesystem access.")]
	public void Execute_ShouldFailValidation_WhenFileNameIsUnsafe() {
		// Arrange
		AddCustomLoggingOptions options = ValidOptions();
		options.FileName = "../${basedir}/escape.log";

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "file-name overrides must stay beneath TodayLogPath");
		_settingsRepository.DidNotReceive().FindCurrentEnvironment(Arg.Any<string>());
	}

	[Test]
	[Description("Rejects direct connection overrides so configuration and restart cannot target different environments.")]
	public void Execute_ShouldFailValidation_WhenUriOverridesRegisteredEnvironment() {
		// Arrange
		AddCustomLoggingOptions options = ValidOptions();
		options.Uri = "https://different.example";
		options.RestartEnvironment = true;

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1,
			because: "the file mutation and optional restart must resolve from the same registered environment snapshot");
		_settingsRepository.DidNotReceive().FindCurrentEnvironment(Arg.Any<string>());
		_configurator.DidNotReceive().Configure(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
		_restartService.DidNotReceive().Restart(Arg.Any<EnvironmentSettings>());
	}

	[Test]
	[Description("Rejects a direct environment-path override before resolving the registered installation.")]
	public void Execute_ShouldFailValidation_WhenEnvironmentPathOverridesRegisteredEnvironment() {
		// Arrange
		AddCustomLoggingOptions options = ValidOptions();
		options.EnvironmentPath = "different-root";

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "the command must use only the path from the selected registration");
		_settingsRepository.DidNotReceive().FindCurrentEnvironment(Arg.Any<string>());
	}

	private static AddCustomLoggingOptions ValidOptions() => new() {
		Environment = "dev",
		PackageName = "MyPackage",
		MinLevel = "Info"
	};

	private static CustomLoggingConfigurationResult Success(bool changed) => new(
		true, changed, "MyPackageApp", "myPackageAppender", "${TodayLogPath}/MyPackage.log", null);
}

[TestFixture]
[Property("Module", "Command")]
public sealed class EnvironmentRestartServiceTests : BaseClioModuleTests {
	private IApplicationClient _applicationClient;
	private IApplicationClientFactory _applicationClientFactory;
	private IEnvironmentRestartService _restartService;

	public override void Setup() {
		base.Setup();
		_restartService = Container.GetRequiredService<IEnvironmentRestartService>();
	}

	protected override void AdditionalRegistrations(IServiceCollection services) {
		base.AdditionalRegistrations(services);
		_applicationClient = Substitute.For<IApplicationClient>();
		_applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		_applicationClientFactory.CreateEnvironmentClient(Arg.Any<EnvironmentSettings>()).Returns(_applicationClient);
		services.AddTransient(_ => _applicationClientFactory);
	}

	[TestCase(true, "https://creatio.example/ServiceModel/AppInstallerService.svc/RestartApp")]
	[TestCase(false, "https://creatio.example/0/ServiceModel/AppInstallerService.svc/UnloadAppDomain")]
	[Description("Restarts the exact refreshed runtime snapshot through the established Creatio endpoints.")]
	public void Restart_ShouldUseRefreshedEnvironmentSnapshot_WhenRuntimeVaries(bool isNetCore, string expectedUrl) {
		// Arrange
		EnvironmentSettings environment = new() {
			Uri = "https://creatio.example/",
			IsNetCore = isNetCore
		};

		// Act
		int exitCode = _restartService.Restart(environment);

		// Assert
		exitCode.Should().Be(0, because: "the restart request completed without a client failure");
		_applicationClientFactory.Received(1).CreateEnvironmentClient(environment);
		_applicationClient.Received(1).ExecutePostRequest(
			expectedUrl,
			"{}",
			100_000,
			3,
			1);
	}
}
