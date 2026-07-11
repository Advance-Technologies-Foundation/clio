using Clio.Command;
using Clio.Common;
using Clio.UserEnvironment;
using ConsoleTables;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class ConfigCommandTests : BaseCommandTests<ConfigOptions> {

	private ISettingsRepository _settingsRepository;
	private ILogger _logger;
	private ConfigCommand _sut;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddSingleton(_settingsRepository);
		containerBuilder.AddSingleton(_logger);
	}

	public override void Setup() {
		base.Setup();
		_settingsRepository.GetDeployCreatioDefaults().Returns(new DeployCreatioDefaults());
		_settingsRepository.AppSettingsFilePath.Returns("appsettings.json");
		_sut = Container.GetRequiredService<ConfigCommand>();
	}

	public override void TearDown() {
		_settingsRepository.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("Clears the stored deploy-creatio defaults when --reset is supplied.")]
	public void Execute_ShouldClearDefaults_WhenResetSupplied() {
		// Arrange
		ConfigOptions options = new() { Reset = true };

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "resetting the configuration always succeeds");
		_settingsRepository.Received(1).SetDeployCreatioDefaults(null);
	}

	[Test]
	[Description("Reset takes precedence over set arguments supplied in the same call.")]
	public void Execute_ShouldClearDefaults_WhenResetAndSetArgumentsSupplied() {
		// Arrange
		ConfigOptions options = new() { Reset = true, DeployDbServerName = "my-local-postgres" };

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "reset wins over set arguments and always succeeds");
		_settingsRepository.Received(1).SetDeployCreatioDefaults(null);
		_settingsRepository.DidNotReceive().SetDeployCreatioDefaults(Arg.Is<DeployCreatioDefaults>(d => d != null));
	}

	[Test]
	[Description("Shows the current configuration without persisting when no arguments are supplied.")]
	public void Execute_ShouldShowDefaults_WhenNoArgumentsSupplied() {
		// Arrange
		ConfigOptions options = new();

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "showing the configuration always succeeds");
		_settingsRepository.DidNotReceive().SetDeployCreatioDefaults(Arg.Any<DeployCreatioDefaults>());
	}

	[Test]
	[Description("Persists the supplied db server name as a deploy-creatio default.")]
	public void Execute_ShouldPersistDbServerName_WhenDeployDbServerNameSupplied() {
		// Arrange
		ConfigOptions options = new() { DeployDbServerName = "my-local-postgres" };

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "a valid set operation succeeds");
		_settingsRepository.Received(1).SetDeployCreatioDefaults(
			Arg.Is<DeployCreatioDefaults>(d => d.DbServerName == "my-local-postgres"));
	}

	[Test]
	[Description("Persists the supplied site port as a deploy-creatio default.")]
	public void Execute_ShouldPersistSitePort_WhenDeploySitePortSupplied() {
		// Arrange
		ConfigOptions options = new() { DeploySitePort = 40018 };

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "a valid set operation succeeds");
		_settingsRepository.Received(1).SetDeployCreatioDefaults(
			Arg.Is<DeployCreatioDefaults>(d => d.SitePort == 40018));
	}

	[Test]
	[Description("Normalizes the deployment method to lower case before persisting it.")]
	public void Execute_ShouldPersistLowercasedDeploymentMethod_WhenValidDeploymentSupplied() {
		// Arrange
		ConfigOptions options = new() { DeployDeployment = "IIS" };

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "a valid deployment method succeeds");
		_settingsRepository.Received(1).SetDeployCreatioDefaults(
			Arg.Is<DeployCreatioDefaults>(d => d.DeploymentMethod == "iis"));
	}

	[Test]
	[Description("Preserves previously configured fields when updating only one field.")]
	public void Execute_ShouldPreserveOtherFields_WhenUpdatingSingleField() {
		// Arrange
		_settingsRepository.GetDeployCreatioDefaults()
			.Returns(new DeployCreatioDefaults { RedisServerName = "local-redis" });
		ConfigOptions options = new() { DeployDbServerName = "my-local-postgres" };

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "a valid set operation succeeds");
		_settingsRepository.Received(1).SetDeployCreatioDefaults(
			Arg.Is<DeployCreatioDefaults>(d => d.DbServerName == "my-local-postgres" && d.RedisServerName == "local-redis"));
	}

	[Test]
	[Description("Returns a validation error and does not persist when the deployment method is invalid.")]
	public void Execute_ShouldReturnError_WhenDeploymentMethodInvalid() {
		// Arrange
		ConfigOptions options = new() { DeployDeployment = "nonsense" };

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(1, because: "an unsupported deployment method is a validation error");
		_settingsRepository.DidNotReceive().SetDeployCreatioDefaults(Arg.Any<DeployCreatioDefaults>());
		_logger.Received().WriteError(Arg.Any<string>());
	}

	[Test]
	[Description("Returns a validation error and does not persist when the site port is out of range.")]
	public void Execute_ShouldReturnError_WhenSitePortOutOfRange() {
		// Arrange
		ConfigOptions options = new() { DeploySitePort = 0 };

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(1, because: "a site port outside the 1-65535 range is a validation error");
		_settingsRepository.DidNotReceive().SetDeployCreatioDefaults(Arg.Any<DeployCreatioDefaults>());
		_logger.Received().WriteError(Arg.Any<string>());
	}

	[Test]
	[Description("An explicit --show displays the configuration and does not persist even when set arguments are supplied.")]
	public void Execute_ShouldShowAndNotPersist_WhenShowSuppliedWithSetArguments() {
		// Arrange
		ConfigOptions options = new() { Show = true, DeployDbServerName = "my-local-postgres" };

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "an explicit --show takes precedence and always succeeds");
		_settingsRepository.DidNotReceive().SetDeployCreatioDefaults(Arg.Any<DeployCreatioDefaults>());
	}

	[Test]
	[Description("Prints a table of configured defaults after a successful update.")]
	public void Execute_ShouldPrintTable_WhenDefaultsAreConfigured() {
		// Arrange
		_settingsRepository.GetDeployCreatioDefaults()
			.Returns(new DeployCreatioDefaults { DbServerName = "my-local-postgres" });
		ConfigOptions options = new() { Show = true };

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "showing configured defaults succeeds");
		_logger.Received().PrintTable(Arg.Any<ConsoleTable>());
	}
}
