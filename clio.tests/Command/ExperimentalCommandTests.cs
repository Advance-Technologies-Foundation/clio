using System.Collections.Generic;
using Clio.Command;
using Clio.Command.McpServer.Knowledge;
using Clio.Common;
using Clio.UserEnvironment;
using ConsoleTables;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class ExperimentalCommandTests : BaseCommandTests<ExperimentalOptions> {

	private ISettingsRepository _settingsRepository;
	private IFeatureToggleService _featureToggleService;
	private ILogger _logger;
	private ExperimentalCommand _sut;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_featureToggleService = Substitute.For<IFeatureToggleService>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddSingleton(_settingsRepository);
		containerBuilder.AddSingleton(_featureToggleService);
		containerBuilder.AddSingleton(_logger);
	}

	public override void Setup() {
		base.Setup();
		_settingsRepository.GetFeatures().Returns(new Dictionary<string, bool>());
		_featureToggleService.GetCatalog(Arg.Any<IEnumerable<System.Type>>())
			.Returns(new List<FeatureToggleInfo>());
		_sut = Container.GetRequiredService<ExperimentalCommand>();
	}

	public override void TearDown() {
		_settingsRepository.ClearReceivedCalls();
		_featureToggleService.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("Enabling a feature persists the flag as true and reports the enabled state.")]
	public void Execute_ShouldPersistEnabledFlag_WhenNameAndEnableSupplied() {
		// Arrange
		ExperimentalOptions options = new() { Name = "ai-assist", Enable = true };

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "a valid enable toggle should succeed");
		_settingsRepository.Received(1).SetFeature("ai-assist", true);
		_logger.Received().WriteInfo(Arg.Is<string>(message => message.Contains("ENABLED")));
	}

	[Test]
	[Description("Disabling a feature persists the flag as false and reports the disabled state.")]
	public void Execute_ShouldPersistDisabledFlag_WhenNameAndDisableSupplied() {
		// Arrange
		ExperimentalOptions options = new() { Name = "ai-assist", Disable = true };

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "a valid disable toggle should succeed");
		_settingsRepository.Received(1).SetFeature("ai-assist", false);
		_logger.Received().WriteInfo(Arg.Is<string>(message => message.Contains("DISABLED")));
	}

	[Test]
	[Description("Returns a validation error and does not persist when both --enable and --disable are supplied.")]
	public void Execute_ShouldReturnError_WhenBothEnableAndDisableSupplied() {
		// Arrange
		ExperimentalOptions options = new() { Name = "ai-assist", Enable = true, Disable = true };

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(1, because: "exactly one of --enable/--disable must be supplied when toggling");
		_settingsRepository.DidNotReceive().SetFeature(Arg.Any<string>(), Arg.Any<bool>());
		_logger.Received().WriteError(Arg.Any<string>());
	}

	[Test]
	[Description("Returns a validation error and does not persist when --name is supplied without --enable or --disable.")]
	public void Execute_ShouldReturnError_WhenNameSuppliedWithoutToggle() {
		// Arrange
		ExperimentalOptions options = new() { Name = "ai-assist" };

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(1, because: "a feature name with neither --enable nor --disable is ambiguous");
		_settingsRepository.DidNotReceive().SetFeature(Arg.Any<string>(), Arg.Any<bool>());
		_logger.Received().WriteError(Arg.Any<string>());
	}

	[Test]
	[Description("Returns a validation error when --enable is supplied without a feature name.")]
	public void Execute_ShouldReturnError_WhenEnableSuppliedWithoutName() {
		// Arrange
		ExperimentalOptions options = new() { Enable = true };

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(1, because: "--enable requires a feature key supplied via --name");
		_settingsRepository.DidNotReceive().SetFeature(Arg.Any<string>(), Arg.Any<bool>());
		_logger.Received().WriteError(Arg.Any<string>());
	}

	[Test]
	[Description("Warns when toggling a feature key that no command or MCP tool references.")]
	public void Execute_ShouldWarn_WhenTogglingUnknownFeatureKey() {
		// Arrange
		ExperimentalOptions options = new() { Name = "totally-unknown-key", Enable = true };

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "toggling an unreferenced key is allowed");
		_settingsRepository.Received(1).SetFeature("totally-unknown-key", true);
		_logger.Received().WriteWarning(Arg.Is<string>(message => message.Contains("totally-unknown-key")));
	}

	[Test]
	[Description("Enabling the mobile-page-converter feature emits the Beta-mode enablement warning (ENG-94250).")]
	public void Execute_ShouldWarnBetaMode_WhenEnablingMobilePageConverter() {
		// Arrange
		ExperimentalOptions options = new() { Name = "mobile-page-converter", Enable = true };

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "enabling a known feature succeeds");
		_settingsRepository.Received(1).SetFeature("mobile-page-converter", true);
		// enabling the mobile-page-converter feature must warn the user it activates Beta mode
		_logger.Received().WriteWarning(Arg.Is<string>(message => message.Contains("Beta mode")));
	}

	[Test]
	[Description("Disabling the mobile-page-converter feature does NOT emit the Beta-mode enablement warning.")]
	public void Execute_ShouldNotWarnBetaMode_WhenDisablingMobilePageConverter() {
		// Arrange
		ExperimentalOptions options = new() { Name = "mobile-page-converter", Disable = true };

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "disabling a known feature succeeds");
		_settingsRepository.Received(1).SetFeature("mobile-page-converter", false);
		// the Beta-mode heads-up is shown only when the feature is turned on, never on disable
		_logger.DidNotReceive().WriteWarning(Arg.Is<string>(message => message.Contains("Beta mode")));
	}

	[Test]
	[Description("Enabling a feature that carries no enable notice emits no Beta-mode warning.")]
	public void Execute_ShouldNotWarnBetaMode_WhenEnablingFeatureWithoutNotice() {
		// Arrange
		ExperimentalOptions options = new() { Name = "ai-assist", Enable = true };

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "enabling any feature succeeds");
		// only features with a registered enable notice emit the Beta-mode heads-up
		_logger.DidNotReceive().WriteWarning(Arg.Is<string>(message => message.Contains("Beta mode")));
	}

	[Test]
	[Description("Lists feature flags by printing a table when no toggle arguments are supplied.")]
	public void Execute_ShouldListFeatures_WhenNoArgumentsSupplied() {
		// Arrange
		_featureToggleService.GetCatalog(Arg.Any<IEnumerable<System.Type>>())
			.Returns(new List<FeatureToggleInfo> { new("ai-assist", true) });
		ExperimentalOptions options = new();

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "listing feature flags always succeeds");
		_settingsRepository.DidNotReceive().SetFeature(Arg.Any<string>(), Arg.Any<bool>());
		_logger.Received().PrintTable(Arg.Any<ConsoleTable>());
	}

	[Test]
	[Description("The knowledge-allow-unsequenced key is a recognized standalone feature, not an unknown key.")]
	public void Execute_ShouldNotWarnUnknownKey_WhenTogglingKnowledgeAllowUnsequenced() {
		// Arrange
		ExperimentalOptions options = new() {
			Name = KnowledgeUnsequencedGitOptions.FeatureName,
			Enable = true
		};

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "enabling a recognized standalone feature succeeds");
		_settingsRepository.Received(1).SetFeature(KnowledgeUnsequencedGitOptions.FeatureName, true);
		// the key gates a DI registration value rather than an attributed type, so without the
		// StandaloneFeatureKeys entry clio would call a live toggle unreferenced
		_logger.DidNotReceive().WriteWarning(Arg.Is<string>(message =>
			message.Contains("No command or MCP tool currently references")));
	}

	[Test]
	[Description("Enabling knowledge-allow-unsequenced warns which integrity guard it relaxes and that it persists.")]
	public void Execute_ShouldWarnRelaxedIntegrity_WhenEnablingKnowledgeAllowUnsequenced() {
		// Arrange
		ExperimentalOptions options = new() {
			Name = KnowledgeUnsequencedGitOptions.FeatureName,
			Enable = true
		};

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "enabling a known feature succeeds");
		// the flag survives in appsettings.json and relaxes a security-relevant check, so the operator
		// must be told both facts at the moment they turn it on
		_logger.Received().WriteWarning(Arg.Is<string>(message =>
			message.Contains("content-integrity") && message.Contains("disable it when you are done")));
	}

	[Test]
	[Description("Disabling knowledge-allow-unsequenced does NOT emit the relaxed-integrity enable notice.")]
	public void Execute_ShouldNotWarnRelaxedIntegrity_WhenDisablingKnowledgeAllowUnsequenced() {
		// Arrange
		ExperimentalOptions options = new() {
			Name = KnowledgeUnsequencedGitOptions.FeatureName,
			Disable = true
		};

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "disabling a known feature succeeds");
		_settingsRepository.Received(1).SetFeature(KnowledgeUnsequencedGitOptions.FeatureName, false);
		_logger.DidNotReceive().WriteWarning(Arg.Is<string>(message => message.Contains("content-integrity")));
	}

	[Test]
	[Description("Lists an orphan flag stored in settings that no command references.")]
	public void Execute_ShouldListOrphanFlag_WhenSettingsKeyHasNoAttribute() {
		// Arrange
		_featureToggleService.GetCatalog(Arg.Any<IEnumerable<System.Type>>())
			.Returns(new List<FeatureToggleInfo>());
		_settingsRepository.GetFeatures()
			.Returns(new Dictionary<string, bool> { ["leftover-key"] = true });
		ExperimentalOptions options = new();

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "listing always succeeds even when only orphan flags exist");
		_logger.Received().PrintTable(Arg.Any<ConsoleTable>());
	}
}
