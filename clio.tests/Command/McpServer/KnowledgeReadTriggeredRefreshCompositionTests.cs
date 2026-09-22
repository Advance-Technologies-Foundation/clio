using System;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Knowledge;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// The read-triggered refresh across the real guidance source and the real trigger, with only the
/// publisher and the settings file replaced.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="KnowledgeGuidanceSourceTests"/> substitutes the trigger and
/// <see cref="KnowledgeRefreshTriggerTests"/> substitutes the read surface, so each proves one half
/// against a mock of the other and neither states the behaviour an operator actually observes: a
/// guidance read answers from the generation that is already installed, and a LATER read answers from
/// the generation the refresh installed meanwhile, without the process restarting. That is the claim
/// ENG-99899 makes, and it is asserted here over the two real classes composed as
/// <see cref="Clio.BindingsModule"/> composes them.
/// </para>
/// <para>
/// What this still does not cover, deliberately: a real host process reading its own
/// <c>appsettings.json</c> and a real publisher serving a signed bundle. That was verified by hand on
/// mcp-http against the live publisher (1.15.47 to 1.15.51) during review; automating it needs an
/// isolated-host harness this repository does not yet have.
/// </para>
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class KnowledgeReadTriggeredRefreshCompositionTests {
	private const string ArticleName = "routing";
	private const string FirstGeneration = "Guidance as of the cached generation.";
	private const string RefreshedGeneration = "Guidance as of the generation the publisher moved to.";

	private ISettingsRepository _settings = null!;
	private IKnowledgeSourceManagementService _management = null!;
	private ILogger _logger = null!;
	private InstalledKnowledge _installed = null!;

	[SetUp]
	public void SetUp() {
		_settings = Substitute.For<ISettingsRepository>();
		_management = Substitute.For<IKnowledgeSourceManagementService>();
		_logger = Substitute.For<ILogger>();
		_installed = new InstalledKnowledge(FirstGeneration);
		_settings.TryScheduleAutoupdate(
			AutoUpdateTarget.Knowledge, Arg.Any<DateTimeOffset>()).Returns(true);
		// The publisher moving is what installing a new generation looks like from the read surface. It
		// is held behind a gate the test opens, so the first read is taken while the refresh is still
		// running rather than whenever the scheduler happens to let it finish — without that, the
		// background task can install the new generation before the read reaches the runtime and the
		// test asserts a race instead of the behaviour.
		_management.Update(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => {
			_installed.WaitUntilPublisherReleased();
			_installed.Install(RefreshedGeneration);
			return new KnowledgeSourceBatchResult(
				true,
				"Knowledge source 'creatio-curated' was updated.",
				[new KnowledgeSourceOperationResult(
					CuratedKnowledgeSourceDefaults.Alias, true, "updated", "updated")]);
		});
	}

	[Test]
	[Description("A guidance read serves the installed generation at once and a later read serves the one the refresh installed, with no restart.")]
	public async Task GuidanceRead_ShouldServeTheRefreshedGeneration_OnTheNextReadWithoutARestart() {
		// Arrange
		(KnowledgeGuidanceSource source, IKnowledgeRefreshTrigger trigger) = NewComposition();

		// Act - the first read answers while the publisher is still held, so it cannot have waited on it.
		KnowledgeArticleLookup beforeRefresh = source.FindByName(ArticleName);
		Task? second = trigger.TriggerIfDue();
		second.Should().BeNull(
			because: "the read itself already started the refresh, so the damper suppresses a second one");
		_installed.ReleasePublisher();
		await _installed.WaitForInstall();
		KnowledgeArticleLookup afterRefresh = source.FindByName(ArticleName);

		// Assert
		beforeRefresh.Status.Should().Be(KnowledgeArticleLookupStatus.Active);
		beforeRefresh.Article!.Text.Should().Be(FirstGeneration,
			because: "no request may wait on the publisher — the cached generation is what answers");
		afterRefresh.Article!.Text.Should().Be(RefreshedGeneration,
			because: "the newly installed generation must be served without the host restarting");
		_management.Received(1).Update(null, Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("An operator who disabled knowledge autoupdate keeps being served, and no publisher call happens on a read.")]
	public void GuidanceRead_ShouldServeWithoutContactingThePublisher_WhenTheScheduleIsDisabled() {
		// Arrange - a disabled or not-yet-due policy is what TryScheduleAutoupdate answers false for.
		_settings.TryScheduleAutoupdate(
			AutoUpdateTarget.Knowledge, Arg.Any<DateTimeOffset>()).Returns(false);
		(KnowledgeGuidanceSource source, _) = NewComposition();

		// Act
		KnowledgeArticleLookup lookup = source.FindByName(ArticleName);

		// Assert
		lookup.Article!.Text.Should().Be(FirstGeneration);
		_management.DidNotReceive().Update(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[TestCase(true, false, TestName = "GuidanceRead_ShouldServeWithoutContactingThePublisher_WhenUpdateChecksAreSuppressed")]
	[TestCase(false, true, TestName = "GuidanceRead_ShouldServeWithoutContactingThePublisher_OutsideAnMcpHost")]
	[Description("Both process-level opt-outs leave the read answering and the publisher untouched.")]
	public void GuidanceRead_ShouldServeWithoutContactingThePublisher_UnderAProcessOptOut(
		bool updatesSuppressed, bool outsideHost) {
		// Arrange
		(KnowledgeGuidanceSource source, _) = NewComposition(
			transport: outsideHost ? McpHostTransportKind.Unknown : McpHostTransportKind.Http,
			updatesSuppressed: updatesSuppressed);

		// Act
		KnowledgeArticleLookup lookup = source.FindByName(ArticleName);

		// Assert
		lookup.Article!.Text.Should().Be(FirstGeneration);
		_settings.DidNotReceive().TryScheduleAutoupdate(
			Arg.Any<AutoUpdateTarget>(), Arg.Any<DateTimeOffset>());
		_management.DidNotReceive().Update(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	private (KnowledgeGuidanceSource Source, IKnowledgeRefreshTrigger Trigger) NewComposition(
		McpHostTransportKind transport = McpHostTransportKind.Http,
		bool updatesSuppressed = false) {
		IKnowledgeRefreshTrigger trigger = new KnowledgeRefreshTrigger(
			_settings, _management, TimeProvider.System, _logger,
			() => transport,
			() => false,
			() => updatesSuppressed);
		IKnowledgeBundleActivator activator = Substitute.For<IKnowledgeBundleActivator>();
		IFeatureToggleService features = Substitute.For<IFeatureToggleService>();
		IKnowledgeBundleRuntime runtime = Substitute.For<IKnowledgeBundleRuntime>();
		runtime.Find(Arg.Any<string>(), Arg.Any<Func<KnowledgeArticle, bool>?>()).Returns(_ =>
			new KnowledgeArticleLookup(
				KnowledgeArticleLookupStatus.Active,
				new KnowledgeArticle(
					ArticleName, "docs://knowledge/com.creatio.clio/routing", _installed.Text),
				1));
		runtime.GetNames(Arg.Any<Func<KnowledgeArticle, bool>?>()).Returns([ArticleName]);
		runtime.GetArticlesByRole(Arg.Any<string>()).Returns([]);
		runtime.SnapshotToken.Returns(new object());
		return (new KnowledgeGuidanceSource(activator, trigger, runtime, features), trigger);
	}

	/// <summary>
	/// The generation currently installed, as the read surface sees it. Substituting
	/// <see cref="IKnowledgeBundleRuntime"/> and answering <c>Find</c> from this holder keeps the two
	/// classes under test real while the bundle machinery around them stays out of the way.
	/// </summary>
	private sealed class InstalledKnowledge {
		private readonly TaskCompletionSource _installed =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly ManualResetEventSlim _publisherReleased = new(false);
		private string _text;

		internal InstalledKnowledge(string text) => _text = text;

		internal string Text => Volatile.Read(ref _text);

		internal void Install(string text) {
			Volatile.Write(ref _text, text);
			_installed.TrySetResult();
		}

		/// <summary>Holds the background refresh until the test has taken its first read.</summary>
		internal void WaitUntilPublisherReleased() =>
			_publisherReleased.Wait(TimeSpan.FromSeconds(10));

		internal void ReleasePublisher() => _publisherReleased.Set();

		/// <summary>Completes once a generation has been installed, so nothing here sleeps.</summary>
		internal Task WaitForInstall() => _installed.Task;
	}
}
