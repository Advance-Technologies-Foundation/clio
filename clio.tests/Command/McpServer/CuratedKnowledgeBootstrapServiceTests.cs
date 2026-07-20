using System;
using System.Collections.Generic;
using System.IO;
using Clio.Command;
using Clio.Command.McpServer.Knowledge;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class CuratedKnowledgeBootstrapServiceTests {
	private ISettingsRepository _settings = null!;
	private IKnowledgeSourceInstallationStore _store = null!;
	private IKnowledgeSourceManagementService _management = null!;
	private ICuratedKnowledgeBootstrapService _service = null!;

	[SetUp]
	public void SetUp() {
		_settings = Substitute.For<ISettingsRepository>();
		_store = Substitute.For<IKnowledgeSourceInstallationStore>();
		_management = Substitute.For<IKnowledgeSourceManagementService>();
		_settings.EnsureKnowledgeSource(
			Arg.Any<string>(),
			Arg.Any<KnowledgeSourceConfiguration>()).Returns(call => call.ArgAt<KnowledgeSourceConfiguration>(1));
		_settings.GetKnowledgeConfiguration().Returns(Configuration(
			(CuratedKnowledgeSourceDefaults.Alias, CuratedKnowledgeSourceDefaults.CreateConfiguration())));
		_management.GetInfo(
			Arg.Any<string>(),
			checkUpdates: false,
			Arg.Any<System.Threading.CancellationToken>()).Returns(new KnowledgeSourceInfoResult(
				true,
				"appsettings.json",
				"knowledge",
				Array.Empty<KnowledgeSourceInfo>()));
		_service = new CuratedKnowledgeBootstrapService(_settings, _store, _management);
	}

	[Test]
	[Description("Bootstrap persists the canonical Git source and installs it when no valid local checkout exists.")]
	public void Bootstrap_ShouldInstallCanonicalSource_WhenLocalCheckoutIsMissing() {
		// Arrange
		_management.Install(
			CuratedKnowledgeSourceDefaults.Alias,
			Arg.Any<System.Threading.CancellationToken>()).Returns(new KnowledgeSourceBatchResult(
				true,
				"installed",
				[new KnowledgeSourceOperationResult(
					CuratedKnowledgeSourceDefaults.Alias,
					true,
					"installed",
					"Curated knowledge was installed.")]));

		// Act
		CuratedKnowledgeBootstrapResult result = _service.Bootstrap();

		// Assert
		result.Success.Should().BeTrue(
			because: "a successful first clone makes curated guidance available to the same MCP session");
		result.Installed.Should().BeTrue(
			because: "bootstrap completed the missing local installation");
		_settings.Received(1).EnsureKnowledgeSource(
			CuratedKnowledgeSourceDefaults.Alias,
			Arg.Is<KnowledgeSourceConfiguration>(source =>
				source.LibraryId == CuratedKnowledgeSourceDefaults.LibraryId
				&& source.Type == KnowledgeSourceType.Git
				&& source.Location == CuratedKnowledgeSourceDefaults.Location
				&& source.Branch == CuratedKnowledgeSourceDefaults.Branch
				&& source.Enabled
				&& source.Priority == CuratedKnowledgeSourceDefaults.Priority
				&& source.Participation == KnowledgeSourceParticipation.Authoritative));
		_management.Received(1).Install(
			CuratedKnowledgeSourceDefaults.Alias,
			Arg.Any<System.Threading.CancellationToken>());
	}

	[Test]
	[Description("Bootstrap uses a valid local curated checkout without performing a Git update or reinstall.")]
	public void Bootstrap_ShouldUseLocalCache_WhenInstalledCheckoutIsValid() {
		// Arrange
		_management.GetInfo(
			CuratedKnowledgeSourceDefaults.Alias,
			checkUpdates: false,
			Arg.Any<System.Threading.CancellationToken>()).Returns(new KnowledgeSourceInfoResult(
				true,
				"appsettings.json",
				"knowledge",
				[SourceInfo(isInstalled: true, isValid: true)]));

		// Act
		CuratedKnowledgeBootstrapResult result = _service.Bootstrap();

		// Assert
		result.Success.Should().BeTrue(
			because: "a valid local checkout is sufficient to serve guidance immediately");
		result.Message.Should().Contain("local cache",
			because: "the diagnostic should make clear that startup performed no remote update");
		_management.DidNotReceiveWithAnyArgs().Install(default!, default);
	}

	[Test]
	[Description("Bootstrap migrates an existing checkout when the curated library was configured under an earlier alias.")]
	public void Bootstrap_ShouldMigrateCheckout_WhenCanonicalAliasReplacesExistingLibraryAlias() {
		// Arrange
		KnowledgeSourceConfiguration previous = CuratedKnowledgeSourceDefaults.CreateConfiguration();
		_settings.GetKnowledgeConfiguration().Returns(
			Configuration(("creatio-poc", previous)),
			Configuration((CuratedKnowledgeSourceDefaults.Alias, CuratedKnowledgeSourceDefaults.CreateConfiguration())));
		_store.TryMigrateGitRepository("creatio-poc", CuratedKnowledgeSourceDefaults.Alias).Returns(true);
		_management.GetInfo(
			CuratedKnowledgeSourceDefaults.Alias,
			checkUpdates: false,
			Arg.Any<System.Threading.CancellationToken>()).Returns(new KnowledgeSourceInfoResult(
				true,
				"appsettings.json",
				"knowledge",
				[SourceInfo(isInstalled: true, isValid: true)]));

		// Act
		CuratedKnowledgeBootstrapResult result = _service.Bootstrap();

		// Assert
		result.Success.Should().BeTrue(
			because: "a valid checkout from the earlier alias should remain usable without network access");
		_store.Received(1).TryMigrateGitRepository("creatio-poc", CuratedKnowledgeSourceDefaults.Alias);
		Received.InOrder(() => {
			_store.TryMigrateGitRepository("creatio-poc", CuratedKnowledgeSourceDefaults.Alias);
			_settings.EnsureKnowledgeSource(
				CuratedKnowledgeSourceDefaults.Alias,
				Arg.Any<KnowledgeSourceConfiguration>());
		});
		_management.DidNotReceiveWithAnyArgs().Install(default!, default);
	}

	[Test]
	[Description("Bootstrap leaves the previous alias configured when its checkout cannot be migrated, allowing the next startup to retry offline.")]
	public void Prepare_ShouldNotCanonicalizeSettings_WhenLegacyCheckoutMigrationFails() {
		// Arrange
		KnowledgeSourceConfiguration previous = CuratedKnowledgeSourceDefaults.CreateConfiguration();
		_settings.GetKnowledgeConfiguration().Returns(Configuration(("creatio-poc", previous)));
		_store.When(store => store.TryMigrateGitRepository(
			"creatio-poc",
			CuratedKnowledgeSourceDefaults.Alias)).Do(_ => throw new IOException("move failed"));

		// Act
		CuratedKnowledgeBootstrapResult result = _service.Prepare();

		// Assert
		result.Success.Should().BeFalse(
			because: "a failed local migration must be reported without claiming canonical bootstrap succeeded");
		_settings.DidNotReceiveWithAnyArgs().EnsureKnowledgeSource(default!, default!);
	}

	[Test]
	[Description("Bootstrap preserves an explicitly disabled curated source and performs no network-backed installation.")]
	public void Bootstrap_ShouldSkipInstallation_WhenCuratedSourceIsDisabled() {
		// Arrange
		KnowledgeSourceConfiguration disabled = CuratedKnowledgeSourceDefaults.CreateConfiguration();
		disabled.Enabled = false;
		_settings.EnsureKnowledgeSource(
			CuratedKnowledgeSourceDefaults.Alias,
			Arg.Any<KnowledgeSourceConfiguration>()).Returns(disabled);

		// Act
		CuratedKnowledgeBootstrapResult result = _service.Bootstrap();

		// Assert
		result.Success.Should().BeTrue(
			because: "disabling the built-in source is a supported operator choice rather than an error");
		result.Enabled.Should().BeFalse(
			because: "the bootstrap result must expose that the kill switch is active");
		_management.DidNotReceiveWithAnyArgs().GetInfo(default, default, default);
		_management.DidNotReceiveWithAnyArgs().Install(default!, default);
	}

	[Test]
	[Description("Bootstrap reports an installation failure without throwing so MCP can still start with other configured sources.")]
	public void Bootstrap_ShouldReturnFailure_WhenCuratedInstallFails() {
		// Arrange
		_management.Install(
			CuratedKnowledgeSourceDefaults.Alias,
			Arg.Any<System.Threading.CancellationToken>()).Returns(new KnowledgeSourceBatchResult(
				false,
				"clone failed",
				[new KnowledgeSourceOperationResult(
					CuratedKnowledgeSourceDefaults.Alias,
					false,
					"failed",
					"The repository is unavailable.")]));

		// Act
		CuratedKnowledgeBootstrapResult? result = null;
		Action act = () => result = _service.Bootstrap();

		// Assert
		act.Should().NotThrow(
			because: "a transient curated repository outage must not prevent MCP from serving other capabilities");
		result.Should().NotBeNull(
			because: "bootstrap failures are represented as structured results rather than exceptions");
		result!.Success.Should().BeFalse(
			because: "the host still needs an actionable warning that curated knowledge is unavailable");
		result.Message.Should().Contain("repository is unavailable",
			because: "the transport diagnostic should survive as a safe startup warning");
	}

	[Test]
	[Description("Unexpected non-fatal bootstrap exceptions become diagnostics rather than terminating the MCP process.")]
	public void Bootstrap_ShouldReturnFailure_WhenSettingsBootstrapThrowsUnexpectedException() {
		// Arrange
		_settings.When(repository => repository.EnsureKnowledgeSource(
			CuratedKnowledgeSourceDefaults.Alias,
			Arg.Any<KnowledgeSourceConfiguration>())).Do(_ => throw new NullReferenceException("unexpected failure"));
		CuratedKnowledgeBootstrapResult? result = null;
		Action act = () => result = _service.Bootstrap();

		// Act
		act.Should().NotThrow(
			because: "an unexpected non-fatal bootstrap defect must not make every Clio MCP capability unavailable");

		// Assert
		result.Should().NotBeNull(
			because: "the host needs a structured diagnostic for an unexpected bootstrap failure");
		result!.Success.Should().BeFalse(
			because: "unexpected bootstrap failures must be visible to host logging");
		result.Message.Should().Contain("unexpected failure",
			because: "the safe diagnostic should retain enough context for remediation");
	}

	private static KnowledgeSourceInfo SourceInfo(bool isInstalled, bool isValid) => new(
		CuratedKnowledgeSourceDefaults.Alias,
		CuratedKnowledgeSourceDefaults.LibraryId,
		"git",
		CuratedKnowledgeSourceDefaults.Location,
		null,
		null,
		true,
		CuratedKnowledgeSourceDefaults.Priority,
		"authoritative",
		null,
		CuratedKnowledgeSourceDefaults.Branch,
		null,
		null,
		isInstalled,
		isValid,
		"1.0.0",
		1,
		"digest",
		"0123456789abcdef0123456789abcdef01234567",
		"knowledge",
		null,
		null);

	private static KnowledgeConfiguration Configuration(
		params (string Alias, KnowledgeSourceConfiguration Source)[] sources) => new() {
		Sources = new Dictionary<string, KnowledgeSourceConfiguration>(StringComparer.OrdinalIgnoreCase) {
			[sources[0].Alias] = sources[0].Source
		}
	};
}
