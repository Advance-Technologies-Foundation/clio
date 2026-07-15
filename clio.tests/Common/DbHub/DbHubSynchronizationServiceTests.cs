using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
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
		_httpClient.VerifyServer(Arg.Any<DbHubSettings>()).Returns(new DbHubVerificationResult(true, true));
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
	[Description("Full reconciliation removes an old owned source when an environment becomes remote-only.")]
	public void Synchronize_ShouldRemoveOwnedSource_WhenEnvironmentBecomesRemote() {
		// Arrange
		EnvironmentSettings environment = new();
		_settingsRepository.GetAllEnvironments().Returns(new Dictionary<string, EnvironmentSettings> {
			["remote"] = environment
		});
		_sourceFactory.Create("remote", environment).Returns(new DbHubSourceDiscoveryResult(null,
			new DbHubWarning("Source skipped.", "The environment is remote-only.", "DBHUB_CONNECTION_CONFIG_UNAVAILABLE")));
		_tomlStore.GetOwnedSources("dbhub.toml").Returns(new DbHubOwnedSourcesResult(["remote"]));
		_tomlStore.Remove("dbhub.toml", "remote").Returns(new DbHubSyncResult(true, false));

		// Act
		DbHubSyncSummary result = _sut.Synchronize();

		// Assert
		result.Changed.Should().Be(1, because: "remote-only registrations are no longer eligible local sources");
		_tomlStore.Received(1).Remove("dbhub.toml", "remote");
	}

	[Test]
	[Description("Offline full reconciliation probes HTTP once and still commits every valid TOML source.")]
	public void Synchronize_ShouldProbeOfflineServerOnce_AndContinueTomlUpdates() {
		// Arrange
		EnvironmentSettings firstEnvironment = new() { EnvironmentPath = "first" };
		EnvironmentSettings secondEnvironment = new() { EnvironmentPath = "second" };
		_settingsRepository.GetAllEnvironments().Returns(new Dictionary<string, EnvironmentSettings> {
			["first"] = firstEnvironment,
			["second"] = secondEnvironment
		});
		DbHubSourceDefinition first = Source("first");
		DbHubSourceDefinition second = Source("second");
		_sourceFactory.Create("first", firstEnvironment).Returns(new DbHubSourceDiscoveryResult(first));
		_sourceFactory.Create("second", secondEnvironment).Returns(new DbHubSourceDiscoveryResult(second));
		_tomlStore.Upsert("dbhub.toml", Arg.Any<DbHubSourceDefinition>())
			.Returns(new DbHubSyncResult(true, false));
		_httpClient.VerifyServer(Arg.Any<DbHubSettings>()).Returns(new DbHubVerificationResult(false, false,
			new DbHubWarning("dbHub offline.", ErrorCode: "DBHUB_LIVE_VERIFICATION_SKIPPED")));

		// Act
		DbHubSyncSummary result = _sut.Synchronize();

		// Assert
		result.Changed.Should().Be(2, because: "offline live verification must not block valid TOML commits");
		result.Warnings.Should().ContainSingle(warning => warning.ErrorCode == "DBHUB_LIVE_VERIFICATION_SKIPPED",
			because: "one server probe is sufficient for the whole reconciliation run");
		_httpClient.Received(1).VerifyServer(Arg.Any<DbHubSettings>());
		_httpClient.DidNotReceive().VerifySource(Arg.Any<DbHubSettings>(), Arg.Any<string>(),
			Arg.Any<bool>(), Arg.Any<bool>());
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
	[Description("Automatic deployment synchronization refuses two registered environments with the same normalized source id.")]
	public void SynchronizeEnvironment_ShouldSkipNormalizedCollision() {
		// Arrange
		_settingsRepository.GetAllEnvironments().Returns(new Dictionary<string, EnvironmentSettings> {
			["dev-one"] = new() { EnvironmentPath = "first" },
			["dev one"] = new() { EnvironmentPath = "second" }
		});

		// Act
		DbHubSyncResult result = _sut.SynchronizeEnvironment("dev one");

		// Assert
		result.Skipped.Should().BeTrue(because: "automatic sync must not steal another registered source identity");
		result.Warning.ErrorCode.Should().Be("DBHUB_SOURCE_ID_COLLISION",
			because: "the collision must use the same stable diagnostic as full reconciliation");
		_tomlStore.DidNotReceive().Upsert(Arg.Any<string>(), Arg.Any<DbHubSourceDefinition>());
	}

	[Test]
	[Description("Manual synchronization refuses a non-loopback endpoint configured outside the installer.")]
	public void Synchronize_ShouldRefuseUnsafeEndpoint() {
		// Arrange
		_settingsRepository.GetDbHubSettings().Returns(EnabledSettings(host: "0.0.0.0"));

		// Act
		DbHubSyncSummary result = _sut.Synchronize();

		// Assert
		result.Warnings.Should().ContainSingle(warning => warning.ErrorCode == "DBHUB_UNSAFE_ENDPOINT",
			because: "manual settings edits must not broaden the unauthenticated HTTP trust boundary");
		_sourceFactory.DidNotReceive().Create(Arg.Any<string>(), Arg.Any<EnvironmentSettings>());
	}

	[Test]
	[Description("Manual synchronization converts unexpected failures into a credential-safe summary.")]
	public void Synchronize_ShouldReturnSafeSummary_WhenSettingsReadThrows() {
		// Arrange
		_settingsRepository.GetDbHubSettings().Returns(_ => throw new InvalidOperationException(
			"Password=super-secret-value"));

		// Act
		DbHubSyncSummary result = _sut.Synchronize();

		// Assert
		result.Warnings.Should().ContainSingle(warning => warning.ErrorCode == "DBHUB_SYNC_FAILED",
			because: "manual reconciliation should fail as a safe command result rather than an exception");
		string warningText = string.Join(" ", result.Warnings.Select(warning =>
			$"{warning.Message} {warning.Detail}"));
		warningText.Should().NotContain("super-secret-value",
			because: "settings failures can carry credential-shaped data in exception messages");
	}

	[Test]
	[Description("Automatic deployment synchronization converts unexpected integration errors into safe warnings.")]
	public void SynchronizeEnvironment_ShouldReturnSafeWarning_WhenDependencyThrows() {
		// Arrange
		_settingsRepository.GetAllEnvironments().Returns(_ => throw new HttpRequestException(
			"Password=super-secret-value"));

		// Act
		DbHubSyncResult result = _sut.SynchronizeEnvironment("dev");

		// Assert
		result.Warning.ErrorCode.Should().Be("DBHUB_AUTOMATIC_SYNC_FAILED",
			because: "a best-effort lifecycle hook must retain primary deployment success");
		$"{result.Warning.Message} {result.Warning.Detail}".Should().NotContain("super-secret-value",
			because: "dependency exception text may contain connection secrets and must not escape");
	}

	[Test]
	[Description("Automatic uninstall cleanup converts unexpected integration errors into safe warnings.")]
	public void RemoveEnvironmentSource_ShouldReturnSafeWarning_WhenDependencyThrows() {
		// Arrange
		_tomlStore.Remove("dbhub.toml", "dev").Returns(_ => throw new IOException(
			"Password=super-secret-value"));

		// Act
		DbHubSyncResult result = _sut.RemoveEnvironmentSource("dev");

		// Assert
		result.Warning.ErrorCode.Should().Be("DBHUB_AUTOMATIC_SYNC_FAILED",
			because: "a best-effort lifecycle hook must retain primary uninstall success");
		$"{result.Warning.Message} {result.Warning.Detail}".Should().NotContain("super-secret-value",
			because: "filesystem exception text may contain sensitive local data and must not escape");
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

	private static DbHubSettings EnabledSettings(bool withAutomaticSync = true,
		string host = DbHubSettings.DefaultHost) => new() {
		Enabled = true,
		ConfigPath = "dbhub.toml",
		Host = host,
		Port = DbHubSettings.DefaultPort,
		SyncLocalEnvironments = withAutomaticSync
	};
}
