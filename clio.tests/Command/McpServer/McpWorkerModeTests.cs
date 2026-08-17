using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clio;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Knowledge;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Common.McpWorker;
using Clio.Common.Telemetry;
using Clio.Tests.Infrastructure;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Stage 3 of the MCP worker execution boundary (ENG-95262): worker mode runs no host bootstrap, carries the
/// parent's frozen feature generation, and composes its deadline environment asymmetrically by lifetime.
/// </summary>
/// <remarks>
/// The suppression tests assert on the exact seams <see cref="McpServerCommand.Execute"/> calls, rather than on
/// a boolean predicate: a predicate test would keep passing if someone re-added a direct bootstrap call next to
/// the gated one.
/// </remarks>
[TestFixture]
[Property("Module", "McpServer")]
// NonParallelizable because CompositionRoot_ShouldResolveTheFrozenService_InWorkerMode mutates the
// process-global McpWorkerEnvironment.IsWorkerProcess, and the assembly runs fixtures in parallel
// (TestAssemblySetup declares [assembly: Parallelizable(ParallelScope.Fixtures)]). Restoring the flag in a
// finally is not enough: a fixture running CONCURRENTLY can observe the mutated value and resolve the frozen
// feature-toggle service, which reads every gated feature as off.
[NonParallelizable]
public sealed class McpWorkerModeTests {

	private const string ProcessDesignerFeature = "process-designer";
	private const string RingFeature = "ring";

	private static McpServerCommandOptions WorkerOptions() => new() { Worker = true };

	private static McpServerCommandOptions HostOptions() => new() { Worker = false };

	// ─────────────────────────────────────────────────────────────────────────────────────────────────
	// TC-U-301 — no host bootstrap in worker mode
	// ─────────────────────────────────────────────────────────────────────────────────────────────────

	[Test]
	[Category("Unit")]
	[Description("TC-U-301: a worker never runs the curated-knowledge bootstrap, while an ordinary host still does.")]
	public void BootstrapCuratedKnowledgeForHost_ShouldRunOnlyForTheHost() {
		// Arrange
		ICuratedKnowledgeBootstrapService bootstrap = Substitute.For<ICuratedKnowledgeBootstrapService>();
		ILogger logger = Substitute.For<ILogger>();
		bootstrap.Bootstrap(Arg.Any<CancellationToken>())
			.Returns(new CuratedKnowledgeBootstrapResult(true, true, true, "ready"));

		// Act
		McpServerCommand.BootstrapCuratedKnowledgeForHost(WorkerOptions(), bootstrap, logger);

		// Assert
		bootstrap.DidNotReceive().Bootstrap(Arg.Any<CancellationToken>());

		// Act — the same seam for an ordinary host
		McpServerCommand.BootstrapCuratedKnowledgeForHost(HostOptions(), bootstrap, logger);

		// Assert
		bootstrap.Received(1).Bootstrap(Arg.Any<CancellationToken>());
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-301: a worker never schedules the startup telemetry flush, while an ordinary host still does.")]
	public void ScheduleStartupTelemetryFlush_ShouldRunOnlyForTheHost() {
		// Arrange
		ITelemetryFlushScheduler flushScheduler = Substitute.For<ITelemetryFlushScheduler>();

		// Act
		McpServerCommand.ScheduleStartupTelemetryFlush(WorkerOptions(), flushScheduler);

		// Assert
		flushScheduler.DidNotReceive().TryScheduleFlush();

		// Act
		McpServerCommand.ScheduleStartupTelemetryFlush(HostOptions(), flushScheduler);

		// Assert
		flushScheduler.Received(1).TryScheduleFlush();
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-301: a worker runs neither shutdown drain (telemetry upload, component-registry refresh), while an ordinary host still drains both.")]
	public void DrainHostBackgroundWork_ShouldRunOnlyForTheHost() {
		// Arrange
		ITelemetryFlushScheduler flushScheduler = Substitute.For<ITelemetryFlushScheduler>();
		flushScheduler.DrainAsync(Arg.Any<TimeSpan>()).Returns(Task.CompletedTask);

		// Act
		McpServerCommand.DrainHostBackgroundWork(WorkerOptions(), flushScheduler);

		// Assert
		flushScheduler.DidNotReceive().DrainAsync(Arg.Any<TimeSpan>());

		// Act
		McpServerCommand.DrainHostBackgroundWork(HostOptions(), flushScheduler);

		// Assert
		flushScheduler.Received(1).DrainAsync(Arg.Any<TimeSpan>());
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-301: send-telemetry stays classified as an in-process tool, so telemetry cannot reach the upload endpoint from N workers through the tool path even though the host's own flush is suppressed.")]
	public void SendTelemetryTool_ShouldStayClassifiedInProcess() {
		// Arrange
		IReadOnlyDictionary<string, McpToolExecutionMetadata> declared =
			McpToolExecutionMetadataReader.ReadDeclaredMetadataOrNull([typeof(SendTelemetryTool)]);

		// Act
		bool found = declared.TryGetValue(SendTelemetryTool.ToolName, out McpToolExecutionMetadata metadata);

		// Assert
		found.Should().BeTrue(
			because: "send-telemetry must stay classified, or the routing default decides where telemetry runs");
		metadata.Location.Should().Be(McpToolExecutionLocation.InProcess,
			because: "suppressing the host's own flush in a worker is worthless if the tool itself can be "
				+ "routed to N workers that each post to the telemetry endpoint");
	}

	// ─────────────────────────────────────────────────────────────────────────────────────────────────
	// TC-U-302 — frozen tool generation
	// ─────────────────────────────────────────────────────────────────────────────────────────────────

	[Test]
	[Category("Unit")]
	[Description("TC-U-302: a mid-session appsettings.json toggle change moves the live feature-toggle service but leaves a worker's frozen generation unchanged, so the worker's tool set cannot drift away from the parent's.")]
	public void FrozenFeatureToggleService_ShouldIgnoreAMidSessionToggleChange() {
		// Arrange — one repository behind both services; the frozen one captures the state at spawn.
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.IsFeatureEnabled(ProcessDesignerFeature).Returns(true);
		IFeatureToggleService live = new FeatureToggleService(settingsRepository);
		IFeatureToggleService frozen = new FrozenFeatureToggleService(
			new Dictionary<string, bool> { [ProcessDesignerFeature] = true });
		bool liveBefore = live.IsFeatureEnabled(ProcessDesignerFeature);
		bool frozenBefore = frozen.IsFeatureEnabled(ProcessDesignerFeature);

		// Act — the operator disables the feature while the session is running.
		settingsRepository.IsFeatureEnabled(ProcessDesignerFeature).Returns(false);

		// Assert
		liveBefore.Should().BeTrue(because: "the live service must reflect the settings file it reads");
		frozenBefore.Should().BeTrue(because: "the frozen generation carries the parent's value at spawn");
		live.IsFeatureEnabled(ProcessDesignerFeature).Should().BeFalse(
			because: "the live service is the control: it must actually observe the mid-session change, or this "
				+ "test could pass against a service that reads nothing at all");
		frozen.IsFeatureEnabled(ProcessDesignerFeature).Should().BeTrue(
			because: "a worker must keep the generation the parent resolved; a tool advertised by the parent and "
				+ "missing in the worker surfaces as an unroutable call, not as an error");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-302: the frozen generation round-trips every feature the assembly gates, including the CLI-only ring flag, and keeps the case-insensitive key comparison the settings repository uses.")]
	public void FrozenFeatures_ShouldRoundTripEveryGatedFeatureCaseInsensitively() {
		// Arrange — the whole map, not a hand-picked subset: a worker also dispatches CLI verbs, so a
		// CLI-only flag is in scope even though it gates no MCP tool.
		Dictionary<string, bool> parentFeatures = new(StringComparer.OrdinalIgnoreCase) {
			["deploy-identity"] = true,
			[ProcessDesignerFeature] = false,
			["mobile-page-converter"] = true,
			["watch-compilation"] = false,
			[RingFeature] = true
		};

		// Act
		string payload = McpWorkerEnvironment.Format(parentFeatures);
		IReadOnlyDictionary<string, bool> parsed = McpWorkerEnvironment.Parse(payload);
		IFeatureToggleService frozen = new FrozenFeatureToggleService(parsed);

		// Assert
		parsed.Should().BeEquivalentTo(parentFeatures,
			because: "every flag the parent resolved must reach the worker, or the two disagree about the surface");
		payload.Should().Contain($"{RingFeature}=1",
			because: "the CLI-only ring flag is in scope: a worker dispatches CLI verbs through clio-run");
		frozen.IsFeatureEnabled("DEPLOY-IDENTITY").Should().BeTrue(
			because: "the settings repository compares feature keys case-insensitively, so an ordinal lookup "
				+ "here would read a case-differing name as absent while the parent read it as enabled");
		frozen.IsFeatureEnabled(ProcessDesignerFeature).Should().BeFalse(
			because: "a frozen-off flag must stay off");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-302: an absent frozen payload freezes every gated feature OFF instead of falling back to appsettings.json, and an ungated type stays enabled.")]
	public void FrozenFeatureToggleService_ShouldFailClosed_WhenThePayloadIsAbsent() {
		// Arrange
		IReadOnlyDictionary<string, bool> parsed = McpWorkerEnvironment.Parse(rawValue: null);

		// Act
		IFeatureToggleService frozen = new FrozenFeatureToggleService(parsed);

		// Assert
		parsed.Should().BeEmpty(
			because: "an absent payload must not be read as 'consult the settings file', which is the very "
				+ "parent/child disagreement the frozen generation prevents");
		frozen.IsFeatureEnabled(ProcessDesignerFeature).Should().BeFalse(
			because: "an unknown flag reads as disabled, matching the settings repository for an absent key");
		frozen.IsEnabled(typeof(McpWorkerModeTests)).Should().BeTrue(
			because: "a type carrying no FeatureToggleAttribute is always enabled, frozen or not");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-302: the composition root resolves the frozen feature-toggle service in worker mode and the live settings-backed one otherwise, so the parser gate and the MCP tool surface are built from the same generation.")]
	public void CompositionRoot_ShouldResolveTheFrozenService_InWorkerMode() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		bool originalWorkerMode = McpWorkerEnvironment.IsWorkerProcess;
		try {
			// Act
			McpWorkerEnvironment.IsWorkerProcess = true;
			IServiceProvider workerProvider = new BindingsModule(fileSystem)
				.Register(profile: BindingsModuleRegistrationProfile.Bootstrap, registerMcpHost: false);
			object workerService = workerProvider.GetService(typeof(IFeatureToggleService));
			McpWorkerEnvironment.IsWorkerProcess = false;
			IServiceProvider hostProvider = new BindingsModule(fileSystem)
				.Register(profile: BindingsModuleRegistrationProfile.Bootstrap, registerMcpHost: false);
			object hostService = hostProvider.GetService(typeof(IFeatureToggleService));

			// Assert
			workerService.Should().BeOfType<FrozenFeatureToggleService>(
				because: "a worker must resolve the parent's frozen generation, never the settings file");
			hostService.Should().BeOfType<FeatureToggleService>(
				because: "an ordinary clio must keep reading appsettings.json, so the flag is the only difference");
		} finally {
			// The full unit suite shares one process; a leaked worker flag would make unrelated fixtures
			// resolve the frozen service and see every gated feature as off.
			McpWorkerEnvironment.IsWorkerProcess = originalWorkerMode;
		}
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-302: worker mode is selected from the argument vector, so the mode never inherits down the process tree the way an environment variable would.")]
	public void IsWorkerModeArgv_ShouldDetectTheFlagOnly_WhenPresent() {
		// Arrange
		string[] workerArgs = ["mcp-server", McpWorkerEnvironment.WorkerFlag];
		string[] hostArgs = ["mcp-server"];

		// Act
		bool worker = McpWorkerEnvironment.IsWorkerModeArgv(workerArgs);
		bool host = McpWorkerEnvironment.IsWorkerModeArgv(hostArgs);

		// Assert
		worker.Should().BeTrue(because: "the flag in argv is what selects worker mode");
		host.Should().BeFalse(because: "an ordinary host must never be taken for a worker");
		McpWorkerEnvironment.WorkerFlag.Should().Be("--worker",
			because: "the long name is bare kebab-case; a literal-dash option name would produce '----worker'");
	}

	// ─────────────────────────────────────────────────────────────────────────────────────────────────
	// TC-U-303 — the deliberate deadline asymmetry
	// ─────────────────────────────────────────────────────────────────────────────────────────────────

	[TestCase(null, 150)]
	[TestCase("", 150)]
	[TestCase("not-a-number", 150)]
	[TestCase("0", 150)]
	[TestCase("601", 150)]
	[TestCase("25", 25)]
	[TestCase("600", 600)]
	[Category("Unit")]
	[Description("TC-U-303: the response-deadline override is parsed through a pure seam, because the default is captured at type load and no later environment mutation can be observed.")]
	public void ResolveResponseDeadline_ShouldApplyTheDocumentedParseRules(string rawValue, int expectedSeconds) {
		// Arrange
		TimeSpan expected = TimeSpan.FromSeconds(expectedSeconds);

		// Act
		TimeSpan resolved = McpProgressHeartbeat.ResolveResponseDeadline(rawValue);

		// Assert
		resolved.Should().Be(expected,
			because: "an out-of-range or unparseable override must fall back to the 150 s default rather than "
				+ "silently producing an unbounded or zero budget");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-303: a sticky worker inherits CLIO_MCP_RESPONSE_DEADLINE_SECONDS verbatim because its in-progress envelope is what returns the call, while an ordinary worker inherits no deadline override at all — the parent bounds it by killing.")]
	public void ComposeChildEnvironment_ShouldCarryTheResponseDeadline_OnlyForAStickyWorker() {
		// Arrange
		Dictionary<string, string> parentEnvironment = new(StringComparer.Ordinal) {
			[McpWorkerEnvironment.ResponseDeadlineVariableName] = "25",
			[McpWorkerEnvironment.ReadDeadlineVariableName] = "12"
		};
		string ReadParent(string name) => parentEnvironment.GetValueOrDefault(name);
		Dictionary<string, bool> features = new(StringComparer.OrdinalIgnoreCase) { [RingFeature] = true };

		// Act
		IReadOnlyDictionary<string, string> sticky = McpWorkerEnvironment.ComposeChildEnvironment(
			features, McpWorkerLifetime.Sticky, ReadParent);
		IReadOnlyDictionary<string, string> ordinary = McpWorkerEnvironment.ComposeChildEnvironment(
			features, McpWorkerLifetime.PerCall, ReadParent);

		// Assert
		sticky.Should().Contain(
			new KeyValuePair<string, string>(McpWorkerEnvironment.ResponseDeadlineVariableName, "25"),
			because: "stripping the response deadline from a sticky worker turned a 25 s backend call into a "
				+ "77 s block in the prototype: its in-progress envelope is what returns the call");
		sticky.Should().NotContainKey(McpWorkerEnvironment.ReadDeadlineVariableName,
			because: "an in-child read deadline abandons work while keeping the per-tenant monitor, which is the "
				+ "wedge this feature removes");
		ordinary.Should().NotContainKey(McpWorkerEnvironment.ReadDeadlineVariableName,
			because: "the parent bounds an ordinary worker call by KILLING the child; a second in-child deadline "
				+ "would only re-create the abandoned-work failure mode");
		ordinary.Should().NotContainKey(McpWorkerEnvironment.ResponseDeadlineVariableName,
			because: "the asymmetry is deliberate — only a sticky worker owns an in-progress envelope");
		sticky.Should().ContainKey(McpWorkerEnvironment.FrozenFeaturesVariableName,
			because: "both lifetimes carry the frozen generation; only the deadline handling differs");
		ordinary.Should().ContainKey(McpWorkerEnvironment.FrozenFeaturesVariableName,
			because: "both lifetimes carry the frozen generation; only the deadline handling differs");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-303: the supervisor's inherited-variable allowlist carries neither deadline variable, which is what makes 'copy the parent environment minus the read deadline' true without any subtraction step.")]
	public void SupervisorAllowlist_ShouldCarryNeitherDeadlineVariable() {
		// Arrange
		IReadOnlyCollection<string> allowlist = WorkerProcessSupervisor.DefaultInheritedEnvironmentVariableAllowlist;

		// Act
		bool carriesReadDeadline = Enumerable.Contains(allowlist, McpWorkerEnvironment.ReadDeadlineVariableName);
		bool carriesResponseDeadline = Enumerable.Contains(allowlist, McpWorkerEnvironment.ResponseDeadlineVariableName);

		// Assert
		carriesReadDeadline.Should().BeFalse(
			because: "adding the read deadline to the allowlist would silently give every worker an in-child "
				+ "deadline the composition rule deliberately withholds");
		carriesResponseDeadline.Should().BeFalse(
			because: "the response deadline must reach a worker only through the sticky composition path, or the "
				+ "sticky/ordinary asymmetry stops being observable");
	}

	// ─────────────────────────────────────────────────────────────────────────────────────────────────
	// Worker-side containment arming
	// ─────────────────────────────────────────────────────────────────────────────────────────────────

	[Test]
	[Category("Unit")]
	[Description("Worker-side parent-death signalling resolves to the mechanism the running platform actually has: prctl on Linux, kqueue on macOS, and nothing on Windows, where the parent's job object already covers parent death.")]
	public void ResolveSignallingMode_ShouldMatchThePlatform() {
		// Arrange
		ParentDeathSignallingMode expected = OperatingSystem.IsWindows()
			? ParentDeathSignallingMode.NotSupported
			: OperatingSystem.IsMacOS()
				? ParentDeathSignallingMode.KqueueProcessExit
				: ParentDeathSignallingMode.PrctlParentDeathSignal;

		// Act
		ParentDeathSignallingMode mode = UnixParentDeathWatch.ResolveSignallingMode();

		// Assert
		mode.Should().Be(expected,
			because: "macOS has no prctl at all (no sys/prctl.h, no setsid(1)), so a single Unix path would "
				+ "leave macOS workers with no parent-death detection whatsoever");
	}

	[Test]
	[Category("Unit")]
	[Description("Arming the worker-side watch on macOS registers a kqueue NOTE_EXIT watch on the live parent and reports it, without promoting the calling process's group or reporting a dead parent.")]
	public void Arm_ShouldRegisterAKqueueWatchOnTheLiveParent_OnMacOs() {
		// Arrange — macOS only: the kqueue path adds a background watcher thread and changes nothing
		// process-wide, whereas the Linux path sets PR_SET_PDEATHSIG and a SIGTERM disposition on the test
		// host itself. Group promotion is suppressed for the same reason.
		if (!OperatingSystem.IsMacOS()) {
			Assert.Ignore("The kqueue arming path exists only on macOS; ResolveSignallingMode covers the mapping.");
		}
		bool parentDeathObserved = false;

		// Act
		ParentDeathWatchResult result = UnixParentDeathWatch.Arm(
			onParentDeath: () => parentDeathObserved = true,
			promoteProcessGroup: false);

		// Assert
		result.Mode.Should().Be(ParentDeathSignallingMode.KqueueProcessExit,
			because: "a macOS worker detects parent death through EVFILT_PROC / NOTE_EXIT or not at all");
		result.ParentProcessId.Should().BeGreaterThan(1,
			because: "a live parent is the subject of the watch; pid 1 would mean this process was already reparented");
		result.ParentAlreadyExited.Should().BeFalse(
			because: "the test host's parent is alive, and the getppid re-check must not report otherwise");
		result.ProcessGroupPromoted.Should().BeFalse(
			because: "promotion was suppressed: a test process that made itself a group leader would change how "
				+ "the harness receives an interrupt");
		parentDeathObserved.Should().BeFalse(
			because: "the handler must fire only on actual parent death, never as part of arming");
	}

	[Test]
	[Category("Unit")]
	[Description("The MCP host never arms worker containment: an ordinary mcp-server is nobody's child and must not promote its own process group.")]
	public void ArmWorkerContainment_ShouldDoNothing_ForAnOrdinaryHost() {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();

		// Act
		ParentDeathWatchResult result = McpServerCommand.ArmWorkerContainment(HostOptions(), logger);

		// Assert
		result.Should().BeNull(
			because: "a host that promoted its own group would change how the launching shell's interrupt reaches it");
	}
}
