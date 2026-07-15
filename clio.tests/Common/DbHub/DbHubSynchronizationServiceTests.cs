using System.Collections.Generic;
using Clio.Common.DbHub;
using Clio.Tests.Command;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.DbHub;

[TestFixture]
[Property("Module", "Common")]
public sealed class DbHubSynchronizationServiceTests : BaseClioModuleTests {
	private ISettingsRepository _settingsRepository;
	private IDbHubConnectionSourceFactory _sourceFactory;
	private IDbHubTomlStore _tomlStore;
	private IDbHubHttpClient _httpClient;
	private IDbHubSynchronizationService _sut;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_sourceFactory = Substitute.For<IDbHubConnectionSourceFactory>();
		_tomlStore = Substitute.For<IDbHubTomlStore>();
		_httpClient = Substitute.For<IDbHubHttpClient>();
		containerBuilder.AddSingleton(_settingsRepository);
		containerBuilder.AddSingleton(_sourceFactory);
		containerBuilder.AddSingleton(_tomlStore);
		containerBuilder.AddSingleton(_httpClient);
	}

	public override void Setup() {
		base.Setup();
		_settingsRepository.GetDbHubSettings().Returns(EnabledSettings());
		_httpClient.VerifySource(Arg.Any<DbHubSettings>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
			.Returns(new DbHubVerificationResult(true, true));
		_tomlStore.GetOwnedSources(Arg.Any<string>()).Returns(new DbHubOwnedSourcesResult([]));
		_sut = Container.GetRequiredService<IDbHubSynchronizationService>();
	}

	public override void TearDown() {
		_settingsRepository.ClearReceivedCalls();
		_sourceFactory.ClearReceivedCalls();
		_tomlStore.ClearReceivedCalls();
		_httpClient.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("Full reconciliation removes clio-owned sources whose environments are no longer registered.")]
	public void Synchronize_ShouldRemoveStaleOwnedSources() {
		// Arrange
		EnvironmentSettings environment = new() { EnvironmentPath = "local" };
		_settingsRepository.GetAllEnvironments().Returns(new Dictionary<string, EnvironmentSettings> {
			["current"] = environment
		});
		DbHubSourceDefinition source = Source("current");
		_sourceFactory.Create("current", environment).Returns(new DbHubSourceDiscoveryResult(source));
		_tomlStore.Upsert("dbhub.toml", source).Returns(new DbHubSyncResult(false, false));
		_tomlStore.GetOwnedSources("dbhub.toml").Returns(new DbHubOwnedSourcesResult(["current", "stale"]));
		_tomlStore.Remove("dbhub.toml", "stale").Returns(new DbHubSyncResult(true, false));

		// Act
		DbHubSyncSummary result = _sut.Synchronize();

		// Assert
		result.Changed.Should().Be(1, because: "one stale clio-owned block was removed");
		_tomlStore.Received(1).Remove("dbhub.toml", "stale");
		_tomlStore.DidNotReceive().Remove("dbhub.toml", "current");
	}

	[Test]
	[Description("Full reconciliation retains an owned source when its registered environment cannot be rediscovered.")]
	public void Synchronize_ShouldRetainOwnedSource_WhenDiscoveryFails() {
		// Arrange
		EnvironmentSettings environment = new() { EnvironmentPath = "temporarily-unreadable" };
		_settingsRepository.GetAllEnvironments().Returns(new Dictionary<string, EnvironmentSettings> {
			["current"] = environment
		});
		_sourceFactory.Create("current", environment).Returns(new DbHubSourceDiscoveryResult(null,
			new DbHubWarning("Source skipped.", "ConnectionStrings.config is unavailable.", "DBHUB_CONNECTION_CONFIG_UNAVAILABLE")));
		_tomlStore.GetOwnedSources("dbhub.toml").Returns(new DbHubOwnedSourcesResult(["current"]));

		// Act
		DbHubSyncSummary result = _sut.Synchronize();

		// Assert
		result.Skipped.Should().Be(1, because: "the unreadable registered environment cannot be reconciled safely");
		_tomlStore.DidNotReceive().Remove("dbhub.toml", "current");
	}

	[Test]
	[Description("A single-environment reconciliation never removes unrelated managed sources.")]
	public void Synchronize_ShouldNotRemoveStaleSources_WhenEnvironmentSelected() {
		// Arrange
		EnvironmentSettings environment = new() { EnvironmentPath = "local" };
		_settingsRepository.GetAllEnvironments().Returns(new Dictionary<string, EnvironmentSettings> {
			["current"] = environment
		});
		DbHubSourceDefinition source = Source("current");
		_sourceFactory.Create("current", environment).Returns(new DbHubSourceDiscoveryResult(source));
		_tomlStore.Upsert("dbhub.toml", source).Returns(new DbHubSyncResult(false, false));

		// Act
		_sut.Synchronize("current");

		// Assert
		_tomlStore.DidNotReceive().GetOwnedSources(Arg.Any<string>());
		_tomlStore.DidNotReceive().Remove(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("Automatic synchronization is a no-op when the persisted opt-in is disabled.")]
	public void SynchronizeEnvironment_ShouldSkip_WhenAutomaticSyncDisabled() {
		// Arrange
		_settingsRepository.GetDbHubSettings().Returns(EnabledSettings(withAutomaticSync: false));

		// Act
		DbHubSyncResult result = _sut.SynchronizeEnvironment("dev");

		// Assert
		result.Skipped.Should().BeTrue(because: "deploy hooks must honor the persisted automatic-sync opt-in");
		_sourceFactory.DidNotReceive().Create(Arg.Any<string>(), Arg.Any<EnvironmentSettings>());
	}

	[Test]
	[Description("Normalization collisions skip both environments instead of overwriting one source.")]
	public void Synchronize_ShouldSkipNormalizationCollisions() {
		// Arrange
		_settingsRepository.GetAllEnvironments().Returns(new Dictionary<string, EnvironmentSettings> {
			["dev-one"] = new() { EnvironmentPath = "one" },
			["dev one"] = new() { EnvironmentPath = "two" }
		});
		_sourceFactory.Create("dev-one", Arg.Any<EnvironmentSettings>())
			.Returns(new DbHubSourceDiscoveryResult(Source("dev-one") with { SourceId = "dev_one" }));
		_sourceFactory.Create("dev one", Arg.Any<EnvironmentSettings>())
			.Returns(new DbHubSourceDiscoveryResult(Source("dev one") with { SourceId = "dev_one" }));

		// Act
		DbHubSyncSummary result = _sut.Synchronize();

		// Assert
		result.Skipped.Should().Be(2, because: "neither colliding environment may claim the shared id");
		result.Warnings.Should().OnlyContain(warning => warning.ErrorCode == "DBHUB_SOURCE_ID_COLLISION",
			because: "each skip must explain the deterministic collision");
		_tomlStore.DidNotReceive().Upsert(Arg.Any<string>(), Arg.Any<DbHubSourceDefinition>());
	}

	private static DbHubSourceDefinition Source(string environment) => new(environment, environment, "postgres",
		"localhost", 5432, "creatio", "app", "secret");

	private static DbHubSettings EnabledSettings(bool withAutomaticSync = true) => new() {
		Enabled = true,
		ConfigPath = "dbhub.toml",
		Host = DbHubSettings.DefaultHost,
		Port = DbHubSettings.DefaultPort,
		SyncLocalEnvironments = withAutomaticSync
	};
}
