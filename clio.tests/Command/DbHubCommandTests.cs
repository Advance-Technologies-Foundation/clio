using System.Collections.Generic;
using Clio.Command;
using Clio.Common;
using Clio.Common.DbHub;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class InstallDbHubCommandTests : BaseCommandTests<InstallDbHubOptions> {
	private IDbHubInstallerService _installerService;
	private ISettingsRepository _settingsRepository;
	private ILogger _logger;
	private InstallDbHubCommand _sut;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_installerService = Substitute.For<IDbHubInstallerService>();
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddSingleton(_installerService);
		containerBuilder.AddSingleton(_settingsRepository);
		containerBuilder.AddSingleton(_logger);
	}

	public override void Setup() {
		base.Setup();
		_settingsRepository.GetDbHubSettings().Returns(new DbHubSettings());
		_sut = Container.GetRequiredService<InstallDbHubCommand>();
	}

	public override void TearDown() {
		_installerService.ClearReceivedCalls();
		_settingsRepository.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("Persists dbHub settings only after installation and health verification succeed.")]
	public void Execute_ShouldPersistSettings_WhenInstallationSucceeds() {
		// Arrange
		DbHubSettings settings = new() { Enabled = true, ConfigPath = "dbhub.toml", Host = "127.0.0.1", Port = 7999 };
		_installerService.InstallOrRepair(Arg.Any<DbHubInstallRequest>())
			.Returns(new DbHubInstallationResult(true, "healthy", settings));
		InstallDbHubOptions options = new() { ConfigPath = "dbhub.toml" };

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "the installer verified the local dbHub server");
		_settingsRepository.Received(1).SetDbHubSettings(settings);
	}

	[Test]
	[Description("Does not persist a partial dbHub configuration when installation fails.")]
	public void Execute_ShouldNotPersistSettings_WhenInstallationFails() {
		// Arrange
		_installerService.InstallOrRepair(Arg.Any<DbHubInstallRequest>())
			.Returns(new DbHubInstallationResult(false, "failed"));
		InstallDbHubOptions options = new() { ConfigPath = "dbhub.toml" };

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(1, because: "failed installation must be visible to scripts");
		_settingsRepository.DidNotReceive().SetDbHubSettings(Arg.Any<DbHubSettings>());
		_logger.Received(1).WriteError("failed");
	}
}

[TestFixture]
[Property("Module", "Command")]
public sealed class SyncDbHubCommandTests : BaseCommandTests<SyncDbHubOptions> {
	private IDbHubSynchronizationService _synchronizationService;
	private ILogger _logger;
	private SyncDbHubCommand _sut;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_synchronizationService = Substitute.For<IDbHubSynchronizationService>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddSingleton(_synchronizationService);
		containerBuilder.AddSingleton(_logger);
	}

	public override void Setup() {
		base.Setup();
		_sut = Container.GetRequiredService<SyncDbHubCommand>();
	}

	public override void TearDown() {
		_synchronizationService.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("Returns success for best-effort source warnings and logs a safe summary.")]
	public void Execute_ShouldSucceed_WithPerSourceWarnings() {
		// Arrange
		_synchronizationService.Synchronize("dev").Returns(new DbHubSyncSummary(1, 0, 1,
			[new DbHubWarning("source skipped", "unsupported authentication", "DBHUB_SQL_AUTH_UNSUPPORTED")]));
		SyncDbHubOptions options = new() { Environment = "dev" };

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "individual ineligible sources are best-effort warnings");
		_logger.Received(1).WriteWarning("source skipped unsupported authentication");
		_logger.Received(1).WriteInfo("dbHub synchronization completed: 1 changed, 0 unchanged, 1 skipped.");
	}

	[Test]
	[Description("Returns an error when dbHub integration has not been configured.")]
	public void Execute_ShouldFail_WhenDbHubNotConfigured() {
		// Arrange
		_synchronizationService.Synchronize(null).Returns(new DbHubSyncSummary(0, 0, 1,
			[new DbHubWarning("not configured", ErrorCode: "DBHUB_NOT_CONFIGURED")]));

		// Act
		int result = _sut.Execute(new SyncDbHubOptions());

		// Assert
		result.Should().Be(1, because: "scripts need to distinguish missing installation from source skips");
	}
}
