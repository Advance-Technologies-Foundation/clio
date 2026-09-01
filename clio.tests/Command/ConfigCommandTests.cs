using System;
using System.Collections.Generic;
using System.Linq;
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
	private IKnowledgeFeedbackPolicyService _feedbackPolicyService;
	private ILogger _logger;
	private ConfigCommand _sut;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_feedbackPolicyService = Substitute.For<IKnowledgeFeedbackPolicyService>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddSingleton(_settingsRepository);
		containerBuilder.AddSingleton(_feedbackPolicyService);
		containerBuilder.AddSingleton(_logger);
	}

	public override void Setup() {
		base.Setup();
		_settingsRepository.GetDeployCreatioDefaults().Returns(new DeployCreatioDefaults());
		_settingsRepository.AppSettingsFilePath.Returns("appsettings.json");
		_feedbackPolicyService.GetPolicy().Returns(new KnowledgeFeedbackPolicy(
			"ask",
			"ask",
			"https://github.com/Advance-Technologies-Foundation/clio",
			"sanitized",
			"sha256:current",
			null,
			"ask-each-time"));
		_sut = Container.GetRequiredService<ConfigCommand>();
	}

	public override void TearDown() {
		_settingsRepository.ClearReceivedCalls();
		_feedbackPolicyService.ClearReceivedCalls();
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
	[Description("Persists a valid inclusive site-port range and clears a fixed port so automatic selection becomes effective.")]
	public void Execute_ShouldPersistSitePortRangeAndClearFixedPort_WhenRangeSupplied() {
		// Arrange
		_settingsRepository.GetDeployCreatioDefaults()
			.Returns(new DeployCreatioDefaults { SitePort = 40018 });
		ConfigOptions options = new() { DeploySitePortRange = [41000, 41010] };

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "a valid inclusive range should be accepted");
		_settingsRepository.Received(1).SetDeployCreatioDefaults(
			Arg.Is<DeployCreatioDefaults>(defaults => defaults.SitePort == null
				&& defaults.SitePortRange.SequenceEqual(new[] { 41000, 41010 })));
	}

	[TestCaseSource(nameof(InvalidSitePortRanges))]
	[Description("Rejects site-port ranges that do not contain exactly two ordered valid TCP ports.")]
	public void Execute_ShouldReturnError_WhenSitePortRangeInvalid(int[] range) {
		// Arrange
		ConfigOptions options = new() { DeploySitePortRange = range };

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(1, because: "invalid ranges must fail before settings are persisted");
		_settingsRepository.DidNotReceive().SetDeployCreatioDefaults(Arg.Any<DeployCreatioDefaults>());
		_logger.Received().WriteError(Arg.Is<string>(message => message.Contains("1 <= start <= end <= 65535")));
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

	[Test]
	[Description("Persists automatic full-report approval through the shared knowledge-feedback policy service.")]
	public void Execute_ShouldConfigureAutomaticFullFeedback_WhenPolicyArgumentsSupplied() {
		// Arrange
		ConfigOptions options = new() {
			KnowledgeFeedbackMode = "auto",
			KnowledgeFeedbackDestination = "https://creatio.ghe.com/engineering/clio-feedback",
			KnowledgeFeedbackReportingScope = "full"
		};

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "a valid standing-approval update should succeed");
		_feedbackPolicyService.Received(1).Configure(Arg.Is<KnowledgeFeedbackPolicyUpdate>(update =>
			update.Mode == "auto"
			&& update.Destination == "https://creatio.ghe.com/engineering/clio-feedback"
			&& update.ReportingScope == "full"));
	}

	[Test]
	[Description("Returns a validation error when knowledge-feedback configuration is rejected.")]
	public void Execute_ShouldReturnError_WhenKnowledgeFeedbackConfigurationInvalid() {
		// Arrange
		_feedbackPolicyService
			.When(service => service.Configure(Arg.Any<KnowledgeFeedbackPolicyUpdate>()))
			.Do(_ => throw new ArgumentException("Invalid feedback policy."));
		ConfigOptions options = new() { KnowledgeFeedbackMode = "invalid" };

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(1, because: "invalid feedback policy must not be persisted as success");
		_logger.Received(1).WriteError("Invalid feedback policy.");
	}

	private static IEnumerable<int[]> InvalidSitePortRanges() {
		yield return [40100];
		yield return [40199, 40100];
		yield return [0, 40100];
		yield return [40100, 65536];
	}
}
