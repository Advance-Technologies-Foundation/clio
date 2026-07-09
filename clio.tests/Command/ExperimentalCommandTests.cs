using System.Collections.Generic;
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
	[Description("Lists the attribute-less mcp-http credential-passthrough incubation key so it is discoverable via clio experimental.")]
	public void Execute_ShouldListMcpHttpPassthroughStandaloneKey_WhenNoArgumentsSupplied() {
		// Arrange
		const string passthroughKey = "mcp-http-credential-passthrough";
		_featureToggleService.IsFeatureEnabled(passthroughKey).Returns(false);
		ExperimentalOptions options = new();

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "listing feature flags always succeeds");
		_featureToggleService.Received().IsFeatureEnabled(passthroughKey);
	}

	[Test]
	[Description("Toggling the mcp-http credential-passthrough key does not warn it is unknown because it is a recognized standalone feature key.")]
	public void Execute_ShouldNotWarnUnknownKey_WhenTogglingMcpHttpPassthroughKey() {
		// Arrange
		const string passthroughKey = "mcp-http-credential-passthrough";
		ExperimentalOptions options = new() { Name = passthroughKey, Enable = true };

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "enabling a recognized standalone key succeeds");
		_settingsRepository.Received(1).SetFeature(passthroughKey, true);
		_logger.DidNotReceive().WriteWarning(Arg.Any<string>());
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
