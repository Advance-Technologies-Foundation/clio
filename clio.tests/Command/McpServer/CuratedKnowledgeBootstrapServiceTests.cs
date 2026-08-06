using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
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
		_service = new CuratedKnowledgeBootstrapService(_settings, _store, _management, TimeProvider.System);
	}

	[Test]
	[Description("Bootstrap persists the canonical Git source and installs it when no valid local checkout exists.")]
	public void Bootstrap_ShouldInstallCanonicalSource_WhenLocalCheckoutIsMissing() {
		// Arrange
		// First run: nothing is persisted yet, so preparation has to take the settings write lock.
		_settings.GetKnowledgeConfiguration().Returns(
			Configuration(),
			Configuration((CuratedKnowledgeSourceDefaults.Alias, CuratedKnowledgeSourceDefaults.CreateConfiguration())));
		_management.Install(
			CuratedKnowledgeSourceDefaults.Alias,
			Arg.Any<int>(),
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
				&& source.Type == KnowledgeSourceType.GitHubRelease
				&& source.Location == CuratedKnowledgeSourceDefaults.Location
				&& source.RepositoryOwner == CuratedKnowledgeSourceDefaults.RepositoryOwner
				&& source.RepositoryName == CuratedKnowledgeSourceDefaults.RepositoryName
				&& source.AssetName == CuratedKnowledgeSourceDefaults.AssetName
				&& source.Branch == null
				&& source.Enabled
				&& source.Priority == CuratedKnowledgeSourceDefaults.Priority
				&& source.Participation == KnowledgeSourceParticipation.Authoritative));
		_management.Received(1).Install(
			CuratedKnowledgeSourceDefaults.Alias,
			Arg.Any<int>(),
			Arg.Any<System.Threading.CancellationToken>());
	}

	[Test]
	[Description("Bootstrap serves the locally published curated generation without contacting GitHub.")]
	public void Bootstrap_ShouldUseLocalCache_WhenInstalledCheckoutIsValid() {
		// Arrange
		_store.IsBundleGenerationInstalled(CuratedKnowledgeSourceDefaults.Alias).Returns(true);

		// Act
		CuratedKnowledgeBootstrapResult result = _service.Bootstrap();

		// Assert
		result.Success.Should().BeTrue(
			because: "a present local generation is sufficient to serve guidance immediately, with no network call");
		result.Message.Should().Contain("local cache",
			because: "the diagnostic should make clear that startup performed no remote update");
		_management.DidNotReceiveWithAnyArgs().Install(default!, default, default);
		_management.DidNotReceiveWithAnyArgs().GetInfo(default, default, default);
		_settings.DidNotReceiveWithAnyArgs().EnsureKnowledgeSource(default!, default!);
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
		_store.IsBundleGenerationInstalled(CuratedKnowledgeSourceDefaults.Alias).Returns(true);

		// Act
		CuratedKnowledgeBootstrapResult result = _service.Bootstrap();

		// Assert
		result.Success.Should().BeTrue(
			because: "a cached generation should remain usable without network access after the alias migration");
		// Twice: preparation attempts the migration, and installation retries it in case the source
		// mutation lock was briefly held. Both attempts are non-blocking, so neither can overrun the
		// startup budget the way the previous lock-waiting migration could.
		_store.Received(2).TryMigrateGitRepository("creatio-poc", CuratedKnowledgeSourceDefaults.Alias);
		Received.InOrder(() => {
			_store.TryMigrateGitRepository("creatio-poc", CuratedKnowledgeSourceDefaults.Alias);
			_settings.EnsureKnowledgeSource(
				CuratedKnowledgeSourceDefaults.Alias,
				Arg.Any<KnowledgeSourceConfiguration>());
			_store.TryMigrateGitRepository("creatio-poc", CuratedKnowledgeSourceDefaults.Alias);
		});
		_management.DidNotReceiveWithAnyArgs().Install(default!, default, default);
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
		_settings.GetKnowledgeConfiguration().Returns(
			Configuration((CuratedKnowledgeSourceDefaults.Alias, disabled)));

		// Act
		CuratedKnowledgeBootstrapResult result = _service.Bootstrap();

		// Assert
		result.Success.Should().BeTrue(
			because: "disabling the built-in source is a supported operator choice rather than an error");
		result.Enabled.Should().BeFalse(
			because: "the bootstrap result must expose that the kill switch is active");
		_management.DidNotReceiveWithAnyArgs().GetInfo(default, default, default);
		_management.DidNotReceiveWithAnyArgs().Install(default!, default, default);
	}

	[Test]
	[Description("Bootstrap reports an installation failure without throwing so MCP can still start with other configured sources.")]
	public void Bootstrap_ShouldReturnFailure_WhenCuratedInstallFails() {
		// Arrange
		_management.Install(
			CuratedKnowledgeSourceDefaults.Alias,
			Arg.Any<int>(),
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
		_settings.GetKnowledgeConfiguration().Returns(Configuration());
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
		KnowledgeSourceTypeNames.GitHubRelease,
		CuratedKnowledgeSourceDefaults.Location,
		null,
		null,
		true,
		CuratedKnowledgeSourceDefaults.Priority,
		"authoritative",
		null,
		CuratedKnowledgeSourceDefaults.RepositoryOwner,
		CuratedKnowledgeSourceDefaults.RepositoryName,
		CuratedKnowledgeSourceDefaults.AssetName,
		null,
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

	[Test]
	[Description("The advertised startup budget bounds the whole bootstrap, not each phase, when a step is slow.")]
	public void Bootstrap_ShouldStopBeforeInstalling_WhenAnEarlierPhaseConsumedTheStartupBudget() {
		// Arrange
		BootstrapClock clock = new();
		ICuratedKnowledgeBootstrapService service = new CuratedKnowledgeBootstrapService(
			_settings, _store, _management, clock);
		// A contended source mutation lock is the realistic way an early phase eats the budget: the
		// migration attempt returns, but only after the whole pre-serve allowance is gone.
		_store.When(store => store.TryMigrateGitRepository(
				Arg.Any<string>(),
				CuratedKnowledgeSourceDefaults.Alias))
			.Do(_ => clock.Advance(TimeSpan.FromMilliseconds(
				CuratedKnowledgeSourceDefaults.StartupInstallDeadlineMilliseconds + 1)));

		// Act
		CuratedKnowledgeBootstrapResult result = service.Bootstrap();

		// Assert
		result.Success.Should().BeFalse(
			because: "a bootstrap that ran out of its pre-serve budget has not made curated guidance available");
		result.Message.Should().Contain("startup budget",
			because: "the operator needs to see that the bound was hit rather than a transport failure");
		_management.DidNotReceiveWithAnyArgs().GetInfo(default, default, default);
		_management.DidNotReceiveWithAnyArgs().Install(default!, default, default);
	}

	[Test]
	[Description("Installation receives only the startup budget left after the earlier phases, not a fresh allowance.")]
	public void Bootstrap_ShouldPassRemainingBudgetToInstall_WhenEarlierPhasesConsumedPartOfIt() {
		// Arrange
		const int spentMilliseconds = 3_000;
		BootstrapClock clock = new();
		ICuratedKnowledgeBootstrapService service = new CuratedKnowledgeBootstrapService(
			_settings, _store, _management, clock);
		_store.When(store => store.TryMigrateGitRepository(
				Arg.Any<string>(),
				CuratedKnowledgeSourceDefaults.Alias))
			.Do(_ => clock.Advance(TimeSpan.FromMilliseconds(spentMilliseconds / 2)));
		List<int> observedDeadlines = [];
		_management.Install(
			CuratedKnowledgeSourceDefaults.Alias,
			Arg.Any<int>(),
			Arg.Any<System.Threading.CancellationToken>()).Returns(call => {
				observedDeadlines.Add(call.ArgAt<int>(1));
				return new KnowledgeSourceBatchResult(
					true,
					"installed",
					[new KnowledgeSourceOperationResult(
						CuratedKnowledgeSourceDefaults.Alias,
						true,
						"installed",
						"Curated knowledge was installed.")]);
			});

		// Act
		service.Bootstrap();

		// Assert
		observedDeadlines.Should().ContainSingle(
			because: "the prepared source is installed exactly once per bootstrap");
		observedDeadlines[0].Should().BePositive(
			because: "an install that still has budget left must be given it rather than being skipped");
		observedDeadlines[0].Should().BeLessThanOrEqualTo(
			CuratedKnowledgeSourceDefaults.StartupInstallDeadlineMilliseconds - spentMilliseconds,
			because: "installation gets what the earlier phases left, not a fresh startup allowance");
	}

	[Test]
	[Description("A stalled settings write or checkout inspection cannot delay startup, because a prepared source reaches neither.")]
	public void Bootstrap_ShouldReturnImmediately_WhenSettingsWriteAndInspectionWouldStall() {
		// Arrange
		// Wall clock on purpose. Both collaborators block for far longer than the pre-serve budget:
		// the settings write lock waits on a contended appsettings file, and single-source GetInfo
		// runs with a fixed thirty-second operation deadline plus Git validation underneath. Neither
		// takes a cancellation token that startup controls, so the only real bound is not calling
		// them at all once the source is canonical and its checkout is present.
		TimeSpan stall = TimeSpan.FromSeconds(30);
		// A gate that is never opened models a held lock more honestly than a sleep, and keeps the
		// stub out of Sonar's Thread.Sleep-in-a-test rule.
		using ManualResetEventSlim neverReleased = new(initialState: false);
		_settings.When(repository => repository.EnsureKnowledgeSource(
				Arg.Any<string>(),
				Arg.Any<KnowledgeSourceConfiguration>()))
			.Do(_ => neverReleased.Wait(stall));
		_management.When(management => management.GetInfo(
				Arg.Any<string>(),
				Arg.Any<bool>(),
				Arg.Any<System.Threading.CancellationToken>()))
			.Do(_ => neverReleased.Wait(stall));
		_store.IsBundleGenerationInstalled(CuratedKnowledgeSourceDefaults.Alias).Returns(true);
		Stopwatch elapsed = Stopwatch.StartNew();

		// Act
		CuratedKnowledgeBootstrapResult result = _service.Bootstrap();
		elapsed.Stop();

		// Assert
		result.Success.Should().BeTrue(
			because: "a canonical source with a present cached generation is ready without touching either blocking path");
		elapsed.Elapsed.Should().BeLessThan(
			TimeSpan.FromMilliseconds(CuratedKnowledgeSourceDefaults.StartupInstallDeadlineMilliseconds),
			because: "startup must stay inside the advertised pre-serve budget even when both collaborators would block for thirty seconds");
	}

	/// <summary>A manually advanced clock, so budget exhaustion is deterministic rather than timing-dependent.</summary>
	private sealed class BootstrapClock : TimeProvider {
		private long _timestamp;

		public override long GetTimestamp() => _timestamp;

		public override long TimestampFrequency => TimeSpan.TicksPerSecond;

		internal void Advance(TimeSpan elapsed) => _timestamp += elapsed.Ticks;
	}

	private static KnowledgeConfiguration Configuration(
		params (string Alias, KnowledgeSourceConfiguration Source)[] sources) {
		Dictionary<string, KnowledgeSourceConfiguration> map =
			new(StringComparer.OrdinalIgnoreCase);
		foreach ((string alias, KnowledgeSourceConfiguration source) in sources) {
			map[alias] = source;
		}
		return new KnowledgeConfiguration { Sources = map };
	}
}
