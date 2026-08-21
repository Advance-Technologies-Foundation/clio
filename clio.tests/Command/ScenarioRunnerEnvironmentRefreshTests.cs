namespace Clio.Tests.Command;

using System;
using System.Collections.Generic;
using Clio.Command;
using Clio.Command.CreatioInstallCommand;
using Clio.Common;
using Clio.UserEnvironment;
using Clio.YAML;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;
using YamlDotNet.Serialization;

[TestFixture]
[NonParallelizable]
[Property("Module", "Command")]
public class ScenarioRunnerEnvironmentRefreshTests : BaseCommandTests<ScenarioRunnerOptions> {
	private const string ScenarioFileName = "YAML/Script/deploy_then_install_gate.yaml";
	private Func<object, int> _originalExecuteCommandWithOption;
	private IServiceProvider _originalContainer;
	private ILogger _logger;
	private ISettingsRepository _settingsRepository;
	private ScenarioRunnerCommand _sut;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.AddSingleton(_logger);
		containerBuilder.AddSingleton(_settingsRepository);
		containerBuilder.AddSingleton<IScenario>(new Scenario(new DeserializerBuilder().Build()));
	}

	public override void Setup() {
		_logger = Substitute.For<ILogger>();
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_originalExecuteCommandWithOption = Program.ExecuteCommandWithOption;
		_originalContainer = Program.Container;
		base.Setup();
		_sut = Container.GetRequiredService<ScenarioRunnerCommand>();
	}

	public override void TearDown() {
		Program.ExecuteCommandWithOption = _originalExecuteCommandWithOption;
		Program.Container = _originalContainer;
		base.TearDown();
	}

	[Test]
	[Description("Reloads settings after deployment and gives the next scenario step the newly registered environment.")]
	public void Execute_ShouldReloadEnvironmentCreatedByEarlierStep() {
		// Arrange
		EnvironmentSettings freshEnvironment = new() {
			Uri = "https://localhost:40001",
			Login = "Supervisor",
			Password = "Supervisor",
			IsNetCore = true,
			EnvironmentPath = "C:/Creatio/fresh-environment"
		};
		bool settingsReloaded = false;
		_settingsRepository.Reload().Returns(_ => {
			settingsReloaded = true;
			return new SettingsReloadResult(true, null, null);
		});
		_settingsRepository.FindEnvironment("fresh-environment")
			.Returns(_ => settingsReloaded ? freshEnvironment : null);
		List<object> receivedOptions = [];
		EnvironmentSettings receivedSettings = null;
		Program.ExecuteCommandWithOption = option => {
			receivedOptions.Add(option);
			if (option is InstallGateOptions) {
				receivedSettings = Program.Container.GetRequiredService<EnvironmentSettings>();
			}
			return 0;
		};

		// Act
		int result = _sut.Execute(new ScenarioRunnerOptions { FileName = ScenarioFileName });

		// Assert
		result.Should().Be(0, because: "both scenario steps should execute successfully");
		receivedOptions.Should().HaveCount(2, because: "deploy-creatio and install-gate should both be dispatched");
		receivedOptions[0].Should().BeOfType<PfInstallerOptions>(because: "the first step deploys Creatio");
		receivedOptions[1].Should().BeOfType<InstallGateOptions>(because: "the second step installs cliogate");
		receivedSettings.Should().BeEquivalentTo(freshEnvironment,
			because: "the environment-scoped container must use the settings persisted by deployment");
		_settingsRepository.Received(1).Reload();
	}

	[Test]
	[Description("Lets reg-web-app provision a new environment without resolving that environment first.")]
	public void Execute_ShouldDispatchEnvironmentProvisionerWithoutReload() {
		// Arrange
		List<object> receivedOptions = [];
		Program.ExecuteCommandWithOption = option => {
			receivedOptions.Add(option);
			return 0;
		};

		// Act
		int result = _sut.Execute(new ScenarioRunnerOptions {
			FileName = "YAML/Script/register_new_environment.yaml"
		});

		// Assert
		result.Should().Be(0, because: "reg-web-app must be allowed to create a previously unknown environment");
		receivedOptions.Should().ContainSingle().Which.Should().BeOfType<RegAppOptions>(
			because: "the provisioning step must be dispatched without pre-resolution");
		_settingsRepository.DidNotReceive().Reload();
		_settingsRepository.DidNotReceive().FindEnvironment(Arg.Any<string>());
	}

	[Test]
	[Description("Refreshes an explicitly targeted optional environment command after deployment.")]
	public void Execute_ShouldRefreshOptionalEnvironmentConsumer() {
		// Arrange
		EnvironmentSettings freshEnvironment = new() {
			Uri = "https://localhost:40001",
			Login = "Supervisor",
			Password = "Supervisor",
			IsNetCore = true,
			EnvironmentPath = "C:/Creatio/fresh-environment"
		};
		bool settingsReloaded = false;
		_settingsRepository.Reload().Returns(_ => {
			settingsReloaded = true;
			return new SettingsReloadResult(true, null, null);
		});
		_settingsRepository.FindEnvironment("fresh-environment")
			.Returns(_ => settingsReloaded ? freshEnvironment : null);
		EnvironmentSettings receivedSettings = null;
		Program.ExecuteCommandWithOption = option => {
			if (option is Link4RepoOptions) {
				receivedSettings = Program.Container.GetRequiredService<EnvironmentSettings>();
			}
			return 0;
		};

		// Act
		int result = _sut.Execute(new ScenarioRunnerOptions {
			FileName = "YAML/Script/deploy_then_link_repository.yaml"
		});

		// Assert
		result.Should().Be(0, because: "the explicitly targeted optional command should resolve after deployment");
		receivedSettings.Should().BeEquivalentTo(freshEnvironment,
			because: "l4r services must be scoped to the newly deployed environment");
		_settingsRepository.Received(1).Reload();
	}

	[Test]
	[Description("Applies the scenario-level environment to a step that omits its own target.")]
	public void Execute_ShouldInheritScenarioEnvironment() {
		// Arrange
		EnvironmentSettings inheritedEnvironment = new() {
			Uri = "https://localhost:40003",
			Login = "Supervisor",
			Password = "Supervisor"
		};
		_settingsRepository.Reload().Returns(new SettingsReloadResult(true, null, null));
		_settingsRepository.FindEnvironment("inherited-environment").Returns(inheritedEnvironment);
		EnvironmentOptions receivedOptions = null;
		EnvironmentSettings receivedSettings = null;
		Program.ExecuteCommandWithOption = option => {
			receivedOptions = (EnvironmentOptions)option;
			receivedSettings = Program.Container.GetRequiredService<EnvironmentSettings>();
			return 0;
		};

		// Act
		int result = _sut.Execute(new ScenarioRunnerOptions {
			FileName = "YAML/Script/single_restart.yaml",
			Environment = "inherited-environment"
		});

		// Assert
		result.Should().Be(0, because: "the scenario default names a registered environment");
		receivedOptions.Environment.Should().Be("inherited-environment",
			because: "the step should inherit the scenario-level environment name");
		receivedSettings.Uri.Should().Be(inheritedEnvironment.Uri,
			because: "the step container must use the inherited environment");
	}

	[Test]
	[Description("Refreshes and resolves the active environment when neither the step nor scenario names one.")]
	public void Execute_ShouldResolveActiveEnvironment() {
		// Arrange
		EnvironmentSettings activeEnvironment = new() {
			Uri = "https://localhost:40004",
			Login = "Supervisor",
			Password = "Supervisor"
		};
		_settingsRepository.Reload().Returns(new SettingsReloadResult(true, null, null));
		_settingsRepository.FindEnvironment(null).Returns(activeEnvironment);
		EnvironmentSettings receivedSettings = null;
		Program.ExecuteCommandWithOption = _ => {
			receivedSettings = Program.Container.GetRequiredService<EnvironmentSettings>();
			return 0;
		};

		// Act
		int result = _sut.Execute(new ScenarioRunnerOptions { FileName = "YAML/Script/single_restart.yaml" });

		// Assert
		result.Should().Be(0, because: "a configured active environment satisfies the required step");
		receivedSettings.Uri.Should().Be(activeEnvironment.Uri,
			because: "the step container must use the refreshed active environment");
		_settingsRepository.Received(1).FindEnvironment(null);
	}

	[Test]
	[Description("Fails a Safe environment step cleanly and still completes scenario accounting.")]
	public void Execute_ShouldFailSafeEnvironmentStepWithoutEscapingScenarioLoop() {
		// Arrange
		EnvironmentSettings safeEnvironment = new() {
			Uri = "https://production.example.com",
			Login = "Supervisor",
			Password = "Supervisor",
			Safe = true
		};
		_settingsRepository.Reload().Returns(new SettingsReloadResult(true, null, null));
		_settingsRepository.FindEnvironment(null).Returns(safeEnvironment);
		List<object> receivedOptions = [];
		Program.ExecuteCommandWithOption = option => {
			receivedOptions.Add(option);
			return 0;
		};

		// Act
		int result = _sut.Execute(new ScenarioRunnerOptions { FileName = "YAML/Script/single_restart.yaml" });

		// Assert
		result.Should().Be(1,
			because: "non-interactive scenarios must fail closed when a Safe environment needs confirmation");
		receivedOptions.Should().BeEmpty(
			because: "the protected command must not run without explicit Safe-environment confirmation");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("Safe environment confirmation required", StringComparison.Ordinal)));
		// The runner must reach its terminal log instead of letting the confirmation exception escape.
		_logger.Received(1).WriteInfo(Arg.Is<string>(message => message.EndsWith("Scenario finished")));
	}

	[Test]
	[Description("Uses explicit direct URI credentials without requiring a registered environment refresh.")]
	public void Execute_ShouldUseDirectUriWithoutReload() {
		// Arrange
		EnvironmentSettings receivedSettings = null;
		Program.ExecuteCommandWithOption = _ => {
			receivedSettings = Program.Container.GetRequiredService<EnvironmentSettings>();
			return 0;
		};

		// Act
		int result = _sut.Execute(new ScenarioRunnerOptions {
			FileName = "YAML/Script/direct_uri_restart.yaml",
			Environment = "scenario-default"
		});

		// Assert
		result.Should().Be(0, because: "a direct URI supplies a complete target without registration");
		receivedSettings.Uri.Should().Be("https://localhost:40002",
			because: "the direct URI should be preserved in the step container");
		receivedSettings.Login.Should().Be("Supervisor",
			because: "the direct credentials should be preserved in the step container");
		_settingsRepository.DidNotReceive().Reload();
	}

	[Test]
	[Description("Rejects a step that combines a named environment with a direct URI.")]
	public void Execute_ShouldRejectNamedEnvironmentWithDirectUri() {
		// Arrange
		EnvironmentSettings storedEnvironment = new() {
			Uri = "https://localhost:40005",
			Login = "Supervisor",
			Password = "stored-password"
		};
		_settingsRepository.Reload().Returns(new SettingsReloadResult(true, null, null));
		_settingsRepository.FindEnvironment("stored-environment").Returns(storedEnvironment);
		List<object> receivedOptions = [];
		Program.ExecuteCommandWithOption = option => {
			receivedOptions.Add(option);
			return 0;
		};

		// Act
		int result = _sut.Execute(new ScenarioRunnerOptions {
			FileName = "YAML/Script/named_environment_with_uri.yaml"
		});

		// Assert
		result.Should().Be(1, because: "the step has two conflicting target identities");
		receivedOptions.Should().BeEmpty(
			because: "an ambiguous target must not reach command dispatch with stored credentials");
		_logger.Received(1).WriteError(
			"A scenario step cannot combine a named environment with a direct application or authentication URI.");
		_settingsRepository.DidNotReceive().Reload();
	}

	[Test]
	[Description("Rejects an authentication endpoint override for a named OAuth environment.")]
	public void Execute_ShouldRejectNamedEnvironmentWithAuthenticationUri() {
		// Arrange
		EnvironmentSettings storedEnvironment = new() {
			Uri = "https://localhost:40005",
			ClientId = "stored-client",
			ClientSecret = "stored-secret",
			AuthAppUri = "https://localhost:40006/connect/token"
		};
		_settingsRepository.FindEnvironment("stored-environment").Returns(storedEnvironment);
		List<object> receivedOptions = [];
		Program.ExecuteCommandWithOption = option => {
			receivedOptions.Add(option);
			return 0;
		};

		// Act
		int result = _sut.Execute(new ScenarioRunnerOptions {
			FileName = "YAML/Script/named_environment_with_auth_uri.yaml"
		});

		// Assert
		result.Should().Be(1, because: "the authentication endpoint conflicts with the named target identity");
		receivedOptions.Should().BeEmpty(
			because: "stored OAuth credentials must not be dispatched toward an overridden token endpoint");
		_logger.Received(1).WriteError(
			"A scenario step cannot combine a named environment with a direct application or authentication URI.");
		_settingsRepository.DidNotReceive().Reload();
	}

	[Test]
	[Description("Rejects a direct authentication endpoint that omits its direct application endpoint.")]
	public void Execute_ShouldRejectIncompleteDirectOAuthTarget() {
		// Arrange
		List<object> receivedOptions = [];
		Program.ExecuteCommandWithOption = option => {
			receivedOptions.Add(option);
			return 0;
		};

		// Act
		int result = _sut.Execute(new ScenarioRunnerOptions {
			FileName = "YAML/Script/auth_uri_without_application_uri.yaml"
		});

		// Assert
		result.Should().Be(1, because: "a token endpoint alone does not identify the Creatio application");
		receivedOptions.Should().BeEmpty(because: "an incomplete direct target must not reach command dispatch");
		_logger.Received(1).WriteError(
			"A direct authentication URI requires a direct application URI in the same scenario step.");
		_settingsRepository.DidNotReceive().Reload();
	}

	[Test]
	[Description("Applies non-URI overrides to a named environment without changing its endpoint.")]
	public void Execute_ShouldApplyNamedEnvironmentOverrides() {
		// Arrange
		EnvironmentSettings storedEnvironment = new() {
			Uri = "https://localhost:40005",
			Login = "Supervisor",
			Password = "stored-password"
		};
		_settingsRepository.Reload().Returns(new SettingsReloadResult(true, null, null));
		_settingsRepository.FindEnvironment("stored-environment").Returns(storedEnvironment);
		EnvironmentSettings receivedSettings = null;
		Program.ExecuteCommandWithOption = _ => {
			receivedSettings = Program.Container.GetRequiredService<EnvironmentSettings>();
			return 0;
		};

		// Act
		int result = _sut.Execute(new ScenarioRunnerOptions {
			FileName = "YAML/Script/named_environment_with_overrides.yaml"
		});

		// Assert
		result.Should().Be(0, because: "the named environment and its overrides form one unambiguous target");
		receivedSettings.Uri.Should().Be(storedEnvironment.Uri,
			because: "credential overrides must not change the registered endpoint");
		receivedSettings.Login.Should().Be("ScenarioUser",
			because: "named environment steps support explicit login overrides");
		receivedSettings.Password.Should().Be("scenario-password",
			because: "named environment steps support explicit password overrides");
	}

	[Test]
	[Description("Does not dispatch an environment-dependent scenario step when its named environment is absent after refresh.")]
	public void Execute_ShouldFailNamedStepWhenEnvironmentIsMissingAfterReload() {
		// Arrange
		_settingsRepository.Reload().Returns(new SettingsReloadResult(true, null, null));
		_settingsRepository.FindEnvironment("fresh-environment").Returns((EnvironmentSettings)null);
		List<object> receivedOptions = [];
		Program.ExecuteCommandWithOption = option => {
			receivedOptions.Add(option);
			return 0;
		};

		// Act
		int result = _sut.Execute(new ScenarioRunnerOptions { FileName = ScenarioFileName });

		// Assert
		result.Should().Be(1, because: "a missing named environment must fail the scenario");
		receivedOptions.Should().ContainSingle().Which.Should().BeOfType<PfInstallerOptions>(
			because: "install-gate must not be dispatched with a fabricated localhost environment");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("Environment with key 'fresh-environment' not found.", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Does not dispatch an environment-dependent scenario step when settings cannot be refreshed safely.")]
	public void Execute_ShouldFailNamedStepWhenSettingsReloadFails() {
		// Arrange
		const string warning = "Settings file could not be read.";
		_settingsRepository.Reload().Returns(new SettingsReloadResult(false, null, warning));
		List<object> receivedOptions = [];
		Program.ExecuteCommandWithOption = option => {
			receivedOptions.Add(option);
			return 0;
		};

		// Act
		int result = _sut.Execute(new ScenarioRunnerOptions { FileName = ScenarioFileName });

		// Assert
		result.Should().Be(1, because: "an unsafe settings refresh must fail the environment-dependent step");
		receivedOptions.Should().ContainSingle().Which.Should().BeOfType<PfInstallerOptions>(
			because: "install-gate must not run against stale or bootstrap settings");
		_logger.Received(1).WriteWarning(warning);
		_logger.Received(1).WriteError("Scenario cannot refresh clio settings before an environment-dependent step.");
		_settingsRepository.DidNotReceive().FindEnvironment(Arg.Any<string>());
	}
}
