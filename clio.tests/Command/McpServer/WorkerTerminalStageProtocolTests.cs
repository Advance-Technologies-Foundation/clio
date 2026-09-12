using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Progress;
using Clio.Command.McpServer.Relay;
using Clio.Common;
using Clio.Common.McpWorker;
using Clio.UserEnvironment;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Story 8 / ADR §3.3 — the <c>terminal-stage</c> protocol: <c>deploy-creatio</c> and
/// <c>uninstall-creatio</c> bounded by the run's own <c>run-completed</c> stage event rather than by a
/// stopwatch, so a budget expiry can never leave a half-installed environment.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is real here.</b> The dispatcher, the transport owner, the relay and the SDK's stream transport
/// are the production types, and the stage events are serialised through the shipped
/// <see cref="ClioStageEvent"/> contract; only the supervisor is substituted, because a real one spawns a
/// real clio — and a real <c>deploy-creatio</c> would install Creatio. The child is scripted over an
/// ordinary pipe pair, following <c>WorkerProgressStreamingTests.ScriptedWorkerChild</c>, so a stage event
/// is framed, written, read off a pipe and observed exactly as a worker's would be. <b>No test in this
/// fixture performs, or can perform, a real deploy or uninstall</b> (story 8 AC-04).
/// </para>
/// <para>
/// <b>Why the assertions are counters rather than a Creatio delta.</b> The test plan makes backend counter
/// assertions load-bearing, but these two tools are local-only commands with no
/// <see cref="Clio.Common.IApplicationClient"/> — <c>deploy-creatio</c> CREATES the instance — so
/// <b>there is no Creatio backend counter to sample</b>. The substitutes are the ones that can actually
/// catch the defects: SPAWN COUNT EXACTLY 1 (the only assertion that can see an automatic retry — a retry
/// loop is invisible to any timing or result assertion), kill count exactly 1 and its ORDINAL POSITION
/// relative to composing the result, and the scripted child's own emitted-event log stopping after the
/// kill.
/// </para>
/// </remarks>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class WorkerTerminalStageProtocolTests {

	/// <summary>The terminal-stage tool the protocol is driven with. Never actually executed.</summary>
	private const string DeployToolName = "deploy-creatio";

	/// <summary>A caller-supplied progress token, distinctive so a rebuilt one is visible.</summary>
	private const string CallerProgressToken = "caller-progress-token-8";

	/// <summary>
	/// Ceiling on every wait in this fixture. Generous, because it must never be the thing that fails: a
	/// scripted child answers in milliseconds, so reaching this bound means nothing answered at all.
	/// </summary>
	private static readonly TimeSpan AssertionTimeout = TimeSpan.FromSeconds(30);

	/// <summary>
	/// The silence bound for every test whose subject is NOT expiry — deliberately generous.
	/// </summary>
	/// <remarks>
	/// <see cref="TerminalStageWatch"/> starts its silence clock when it is CONSTRUCTED, which is before
	/// the transport is connected and the relay opened. So this bound is charged the SDK client
	/// construction, the initialize round trip, the tools/call dispatch and the child's first emit — none
	/// of which this fixture is measuring. At the tight expiry value that budget is a coin flip on a cold
	/// assembly under three-way fixture parallelism, and it would flip the HAPPY-PATH tests into spurious
	/// indeterminates. The shipped default is 300 s and swallows the handshake completely; only a test
	/// that scales it down can be bitten, so only the tests about expiry take that risk.
	/// </remarks>
	private static readonly TimeSpan SilenceBound = TimeSpan.FromSeconds(20);

	/// <summary>The scaled-down bound used ONLY by the tests whose subject is the silence timer expiring.</summary>
	private static readonly TimeSpan ExpirySilenceBound = TimeSpan.FromMilliseconds(400);

	/// <summary>
	/// The silence bound the LONG-STAGE test runs with, and the one number it is really about.
	/// </summary>
	/// <remarks>
	/// Deliberately far above <see cref="ExpirySilenceBound"/>: this test's subject is a HEALTHY run, so
	/// the handshake that <see cref="TerminalStageWatch"/> charges to the first window must never be able
	/// to expire it. The child's single stage then runs for <see cref="LongStageDuration"/> — three times
	/// this bound — which is what makes the test discriminating: nothing but an in-stage liveness refresh
	/// can carry a run across that gap.
	/// </remarks>
	private static readonly TimeSpan LongStageSilenceBound = TimeSpan.FromSeconds(1);

	/// <summary>How long the child's one long stage takes: three silence windows.</summary>
	private static readonly TimeSpan LongStageDuration = TimeSpan.FromSeconds(3);

	/// <summary>The post-terminal exit grace the fixture runs with — the 30 s default, scaled down.</summary>
	private static readonly TimeSpan ExitGrace = TimeSpan.FromMilliseconds(400);

	private ILogger _logger;
	private ISettingsRepository _settingsRepository;
	private IWorkerProcessSupervisor _supervisor;
	private List<string> _orderedEvents;
	private object _orderedEventsLock;
	private int _spawnCount;

	[SetUp]
	public void SetUp() {
		_orderedEvents = [];
		_orderedEventsLock = new object();
		_spawnCount = 0;
		_logger = Substitute.For<ILogger>();
		_logger.When(logger => logger.WriteWarning(Arg.Any<string>()))
			.Do(call => Record("warning:" + call.Arg<string>()));
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_supervisor = Substitute.For<IWorkerProcessSupervisor>();
	}

	[TearDown]
	public void TearDown() {
		_logger.ClearReceivedCalls();
		_settingsRepository.ClearReceivedCalls();
		_supervisor.ClearReceivedCalls();
	}

	[Test]
	[Category("Unit")]
	[Description("A deploy that streams its stages and reports run-completed gets the worker's OWN answer back, and the parent never kills it — the happy path the whole protocol exists to leave alone.")]
	public async Task DispatchAsync_ShouldReturnTheWorkersOwnAnswer_WhenTheRunReachesItsTerminalStage() {
		// Arrange
		using StageStreamingWorkerChild worker = ArrangeWorker(ChildBehaviour.AnswersAfterTerminalStage);
		RecordingClientSession client = new();
		McpWorkerCallDispatcher sut = CreateSut();

		// Act
		CallToolResult result = await DispatchAsync(sut, client, CallerProgressToken);

		// Assert
		result.IsError.Should().NotBeTrue(
			because: "the run reported outcome=success and the worker answered its own call, so the parent has nothing to add and nothing to override");
		ReadStructured(result)?["answered-by"]?.GetValue<string>().Should().Be("worker",
			because: "an authoritative answer from the command that ran the operation outranks anything the parent could infer from the progress stream, so it must be relayed rather than rebuilt");
		_spawnCount.Should().Be(1,
			because: "one tool call must spawn exactly one worker — this is the only assertion in the fixture that can see an automatic retry, which no timing or result assertion could");
		_supervisor.DidNotReceive().KillContained(Arg.Any<IWorkerLease>());
		worker.CallCount.Should().Be(1,
			because: "a second tools/call would mean the parent retried a destructive operation, which ADR §3.3 forbids outright");
	}

	[Test]
	[Category("Unit")]
	[Description("Neither bound truncates a healthy long deploy: the ORDINARY worker budget does not apply at all, and every stage event RESTARTS the silence timer — so a run that streams for longer than the silence bound, in gaps shorter than it, reaches its terminal stage unkilled.")]
	public async Task DispatchAsync_ShouldNotKillAtTheOrdinaryBudget_WhenTheDeployKeepsStreamingStages() {
		// Arrange — an ordinary budget so small that any budget-based bound fires immediately, and a silence
		// bound SHORTER than the run's total but longer than any single gap in it: a timer that did not reset
		// on each stage event would expire mid-run, which is the second half of what this test discriminates.
		using StageStreamingWorkerChild worker = ArrangeWorker(ChildBehaviour.StreamsPastTheOrdinaryBudget);
		RecordingClientSession client = new();
		McpWorkerCallDispatcher sut = CreateSut(ordinaryBudget: TimeSpan.FromMilliseconds(1),
			silenceBound: TimeSpan.FromMilliseconds(1200));

		// Act
		CallToolResult result = await DispatchAsync(sut, client, CallerProgressToken);

		// Assert
		result.IsError.Should().NotBeTrue(
			because: "the deploy completed; a generic kill at the 1 ms budget would have answered with a timeout envelope instead and left the environment half-installed");
		_supervisor.DidNotReceive().KillContained(Arg.Any<IWorkerLease>());
		worker.EmittedStageIds.Should().Contain(ClioStageEventContract.EventTypes.RunCompleted,
			because: "the run must actually have reached its terminal stage, or this test would be asserting that nothing was killed during a call that ended for some other reason");
		_spawnCount.Should().Be(1,
			because: "a killed-and-retried deploy would also finish eventually, and the spawn count is what tells the two apart");
	}

	[Test]
	[Category("Unit")]
	[Description("A child that goes silent past the stage-event bound produces an explicit INDETERMINATE error naming the last stage reached — never a success, never the retry-safe timeout class, and never an automatic retry.")]
	public async Task DispatchAsync_ShouldReportIndeterminateNamingTheLastStage_WhenTheChildGoesSilent() {
		// Arrange — expiry IS the subject here, so the scaled-down bound is used on both the dispatcher and
		// the child. Everywhere else it is generous; see SilenceBound.
		using StageStreamingWorkerChild worker =
			ArrangeWorker(ChildBehaviour.FallsSilentThenBurstsAgain, ExpirySilenceBound);
		RecordingClientSession client = new();
		McpWorkerCallDispatcher sut = CreateSut(silenceBound: ExpirySilenceBound);

		// Act
		CallToolResult result = await DispatchAsync(sut, client, CallerProgressToken);

		// Assert
		JsonNode payload = ReadStructured(result);
		result.IsError.Should().BeTrue(
			because: "ClioRing classifies a no-terminal result from the payload but the released host still reads IsError first, and a possibly half-installed environment must never be reported as an ordinary answer");
		payload?["success"]?.GetValue<bool>().Should().BeFalse(
			because: "Ring's DescribeUnstreamedFailure reads success/error rather than trusting IsError alone, so the envelope has to carry both");
		payload?["outcome"]?.GetValue<string>().Should().Be("indeterminate",
			because: "the additive outcome field is the only place clio can say 'this may have half-completed' in a form a future Ring branch can key on");
		payload?["error"]?.GetValue<string>().Should().NotBeNullOrWhiteSpace(
			because: "a non-empty error is the third thing Ring's classifier looks for; without it the result lands in its 'outcome genuinely unknown' branch");
		payload?["error-class"]?.GetValue<string>().Should().NotBe("creatio-timeout",
			because: "the budget-expiry class ships guidance that says the call is safe to retry, which for a possibly half-installed environment is the single most damaging instruction available");
		payload?["last-stage-id"]?.GetValue<string>().Should().Be(StageStreamingWorkerChild.LastStageBeforeSilence,
			because: "naming the last stage reached is what turns 'something went wrong' into an instruction an operator can act on");
		payload?["error"]?.GetValue<string>().Should().Contain(StageStreamingWorkerChild.LastStageBeforeSilence,
			because: "the human-readable sentence is what lands in a chat transcript and a bug report, so the stage has to be in the prose and not only in a field");
		payload?["environment-state"]?.GetValue<string>().Should().Be("possibly-half-installed",
			because: "the operator-facing consequence of an indeterminate deploy is the state of the target, not the state of the call");
		_spawnCount.Should().Be(1,
			because: "retry-on-ambiguity is how one half-installed environment becomes two, and a retry loop is invisible to every other assertion here");
	}

	[Test]
	[Category("Unit")]
	[Description("On silence-timer expiry the indeterminate error is composed and reported BEFORE the child is killed, so the last stage it reached is captured — killing first would close the pipes and answer with a relay failure that names no stage at all.")]
	public async Task DispatchAsync_ShouldReportBeforeKilling_WhenTheSilenceBoundExpires() {
		// Arrange — expiry IS the subject here; see the sibling test above for why the bound is paired.
		using StageStreamingWorkerChild worker =
			ArrangeWorker(ChildBehaviour.FallsSilentThenBurstsAgain, ExpirySilenceBound);
		RecordingClientSession client = new();
		McpWorkerCallDispatcher sut = CreateSut(silenceBound: ExpirySilenceBound);

		// Act
		CallToolResult result = await DispatchAsync(sut, client, CallerProgressToken);

		// Assert
		_supervisor.Received(1).KillContained(Arg.Any<IWorkerLease>());
		IReadOnlyList<string> ordered = OrderedEvents();
		int indeterminateReported = ordered.ToList().FindIndex(entry =>
			entry.StartsWith("warning:", StringComparison.Ordinal)
			&& entry.Contains(StageStreamingWorkerChild.LastStageBeforeSilence, StringComparison.Ordinal)
			&& entry.Contains("INDETERMINATE", StringComparison.Ordinal));
		int killed = ordered.ToList().FindIndex(entry => entry == "kill");
		indeterminateReported.Should().BeGreaterThanOrEqualTo(0,
			because: "the last stage reached has to be reported somewhere an operator can see it, not only inside a tool result the client may discard");
		killed.Should().BeGreaterThan(indeterminateReported,
			because: "ADR §3.3 orders these two: the child is killed only AFTER its last stage has been captured, because the kill is what makes that information unobtainable");
		worker.EmittedStageIds.Should().NotContain(StageStreamingWorkerChild.StageAfterSilence,
			because: "a killed child stops emitting — its own log must end at the kill, or the parent is composing an answer about a worker that is still running");
		result.IsError.Should().BeTrue(
			because: "the surviving answer is still the indeterminate error; the ordering assertion above must not be passing on some other result shape");
	}

	[Test]
	[Category("Unit")]
	[Description("A child that emits its terminal stage and then hangs is killed after the exit grace, and the tool result is the TERMINAL OUTCOME rather than an error (story 8 AC-06).")]
	public async Task DispatchAsync_ShouldAnswerWithTheTerminalOutcome_WhenTheChildHangsAfterCompleting() {
		// Arrange
		using StageStreamingWorkerChild worker = ArrangeWorker(ChildBehaviour.HangsAfterTerminalStage);
		RecordingClientSession client = new();
		McpWorkerCallDispatcher sut = CreateSut();

		// Act
		CallToolResult result = await DispatchAsync(sut, client, CallerProgressToken);

		// Assert
		JsonNode payload = ReadStructured(result);
		result.IsError.Should().NotBeTrue(
			because: "the run reported outcome=success through its own authoritative terminal stage, and a child that is merely slow to exit afterwards cannot make that less true");
		payload?["outcome"]?.GetValue<string>().Should().Be(ClioStageEventContract.RunOutcomes.Success,
			because: "the answer is the terminal outcome the run reported, taken verbatim rather than re-derived");
		payload?["success"]?.GetValue<bool>().Should().BeTrue(
			because: "a completed deploy must not be reported as a failure just because the worker process outlived it");
		payload?["terminal-stage-synthesized"]?.GetValue<bool>().Should().BeTrue(
			because: "the caller has to be able to tell an answer composed from the stage stream from one the tool itself returned");
		_supervisor.Received(1).KillContained(Arg.Any<IWorkerLease>());
		_spawnCount.Should().Be(1,
			because: "a hung child is killed, not replaced — respawning would run the deploy a second time");
	}

	[Test]
	[Category("Unit")]
	[Description("A run whose terminal stage says 'failure' is answered as a DEFINITE failure, not as indeterminate: the outcome is known, so the guidance is 'fix the cause and retry' rather than 'inspect a possibly half-installed environment'.")]
	public async Task DispatchAsync_ShouldAnswerWithADefiniteFailure_WhenTheTerminalOutcomeIsFailure() {
		// Arrange
		using StageStreamingWorkerChild worker = ArrangeWorker(ChildBehaviour.HangsAfterFailingTerminalStage);
		RecordingClientSession client = new();
		McpWorkerCallDispatcher sut = CreateSut();

		// Act
		CallToolResult result = await DispatchAsync(sut, client, CallerProgressToken);

		// Assert
		JsonNode payload = ReadStructured(result);
		result.IsError.Should().BeTrue(
			because: "the run said it failed, and a failure reported as a success is the worst answer available");
		payload?["outcome"]?.GetValue<string>().Should().Be(ClioStageEventContract.RunOutcomes.Failure,
			because: "the terminal outcome travels verbatim; collapsing it into the indeterminate class would tell an operator the result is unknown when the run stated it");
		payload?["error-class"]?.GetValue<string>().Should().NotBe("clio-deploy-indeterminate",
			because: "a definite failure and an ambiguous one call for opposite next actions, so they must never share an error class");
	}

	[Test]
	[Category("Unit")]
	[Description("A child that exits without ever reporting run-completed produces the indeterminate error naming the last stage it reached — the lost-child case that distinguishes this protocol from a generic kill (story 8 AC-05).")]
	public async Task DispatchAsync_ShouldReportIndeterminate_WhenTheChildExitsWithoutATerminalStage() {
		// Arrange
		using StageStreamingWorkerChild worker = ArrangeWorker(ChildBehaviour.ExitsWithoutTerminalStage);
		RecordingClientSession client = new();
		McpWorkerCallDispatcher sut = CreateSut();

		// Act
		CallToolResult result = await DispatchAsync(sut, client, CallerProgressToken);

		// Assert
		JsonNode payload = ReadStructured(result);
		result.IsError.Should().BeTrue(
			because: "a worker that died mid-deploy must never be reported as an answer");
		payload?["outcome"]?.GetValue<string>().Should().Be("indeterminate",
			because: "a crashed child and a cancelled run are indistinguishable at the wire — the contract has no cancelled outcome at all — so both resolve here honestly rather than being guessed apart");
		payload?["last-stage-id"]?.GetValue<string>().Should().Be(StageStreamingWorkerChild.LastStageBeforeSilence,
			because: "the last stage reached is exactly what an operator needs in order to know where the environment was left");
		payload?["retry-guidance"]?.GetValue<string>().Should().Contain("Do NOT retry",
			because: "the guidance field is what an agent reads to decide its next action, and this is the one case where retrying is the damaging choice");
		_spawnCount.Should().Be(1,
			because: "the parent must not retry a deploy whose outcome it cannot establish");
	}

	[Test]
	[Category("Unit")]
	[Description("A caller that supplied NO progress token still makes the child stream — a synthetic token is injected on the child leg — and the resulting notifications are CONSUMED at the relay rather than pushed at a client that opted out of progress (ADR §3.3, the one deliberate exception to rule 1).")]
	public async Task DispatchAsync_ShouldInjectASyntheticTokenAndConsumeItsProgress_WhenTheCallerSuppliedNone() {
		// Arrange
		using StageStreamingWorkerChild worker = ArrangeWorker(ChildBehaviour.AnswersAfterTerminalStage);
		RecordingClientSession client = new();
		McpWorkerCallDispatcher sut = CreateSut();

		// Act
		CallToolResult result = await DispatchAsync(sut, client, callerProgressToken: null);

		// Assert
		result.IsError.Should().NotBeTrue(
			because: "the deploy completed normally; without the synthetic token the child would have streamed nothing and a silence-bounded protocol would have called this healthy deploy indeterminate");
		worker.ObservedProgressToken.Should().StartWith("clio-worker-terminal-stage-",
			because: "StageEventProgressForwarder.Subscribe is inert without a token, so the child only streams because the parent gave it one");
		client.ProgressNotifications.Should().BeEmpty(
			because: "the only client that reaches this path explicitly declined progress, so a stream it never asked for has no consumer and can only confuse — the suppression is confined to this last hop");
		worker.EmittedStageIds.Should().Contain(ClioStageEventContract.EventTypes.RunCompleted,
			because: "the child must genuinely have streamed; an empty client log would otherwise pass for the wrong reason");
	}

	[Test]
	[Category("Unit")]
	[Description("When the caller DID supply a progress token, every stage event still reaches the client under that exact token: the suppression applies only to synthetic traffic, so ADR rule 1 is untouched for real progress consumers such as ClioRing.")]
	public async Task DispatchAsync_ShouldForwardEveryStageEvent_WhenTheCallerSuppliedItsOwnToken() {
		// Arrange
		using StageStreamingWorkerChild worker = ArrangeWorker(ChildBehaviour.AnswersAfterTerminalStage);
		RecordingClientSession client = new();
		McpWorkerCallDispatcher sut = CreateSut();

		// Act
		CallToolResult result = await DispatchAsync(sut, client, CallerProgressToken);

		// Assert
		result.IsError.Should().NotBeTrue(
			because: "a failed call would make the notification counts below meaningless");
		client.ProgressNotifications.Should().HaveCount(worker.EmittedStageIds.Count,
			because: "ClioRing renders the deploy from these events, and a parent that swallowed one because it was also watching for the terminal event would silently degrade the UI");
		client.ProgressTokens.Should().OnlyContain(token => token == $"\"{CallerProgressToken}\"",
			because: "Ring correlates the token ORDINALLY and drops a mismatch silently, so a token re-issued anywhere along the relay makes the whole run invisible");
		client.StageEventTypes.Should().Contain(ClioStageEventContract.EventTypes.RunCompleted,
			because: "the terminal event is the one the consumer bounds itself on, and consuming it in the parent would take it away from the client that asked for it");
	}

	[Test]
	[Category("Unit")]
	[Description("A child that reports run-completed and then DIES before answering still yields the terminal outcome, not the indeterminate error: the run stated how it ended, and a worker that failed to reply afterwards cannot make a completed deploy 'possibly half-installed'.")]
	public async Task DispatchAsync_ShouldAnswerWithTheTerminalOutcome_WhenTheChildDiesAfterCompleting() {
		// Arrange
		using StageStreamingWorkerChild worker = ArrangeWorker(ChildBehaviour.ExitsAfterTerminalStage);
		RecordingClientSession client = new();
		McpWorkerCallDispatcher sut = CreateSut();

		// Act
		CallToolResult result = await DispatchAsync(sut, client, CallerProgressToken);

		// Assert
		JsonNode payload = ReadStructured(result);
		result.IsError.Should().NotBeTrue(
			because: "reporting a deploy that reported success as a failure is the worst answer available, and a closed pipe after the terminal event is not evidence about the operation");
		payload?["outcome"]?.GetValue<string>().Should().Be(ClioStageEventContract.RunOutcomes.Success,
			because: "the authoritative run-completed event stated the outcome, so the parent relays it instead of falling back to the ambiguity branch");
		payload?["error-class"]?.GetValue<string>().Should().NotBe("clio-deploy-indeterminate",
			because: "indeterminate means clio could not establish the outcome — here it could, and telling an operator to inspect a healthy environment is the false negative ADR section 3.3 calls the worst one");
		_spawnCount.Should().Be(1,
			because: "a dead worker is never replaced: respawning would run the deploy a second time against an environment that already has it");
	}

	[Test]
	[Description("A run-completed carrying a DIFFERENT runId is a NESTED run finishing, not this one: the parent must keep waiting and ultimately report indeterminate, never answer a deploy that is still installing.")]
	public async Task DispatchAsync_ShouldNotTreatANestedRunsTerminalEventAsItsOwn_WhenTheRunIdDiffers() {
		// Arrange — expiry is the subject once the nested event is correctly ignored, so the bound is the
		// scaled-down one on both sides.
		using StageStreamingWorkerChild worker =
			ArrangeWorker(ChildBehaviour.EmitsANestedTerminalEventThenFallsSilent, ExpirySilenceBound);
		RecordingClientSession client = new();
		McpWorkerCallDispatcher sut = CreateSut(silenceBound: ExpirySilenceBound);

		// Act
		CallToolResult result = await DispatchAsync(sut, client, CallerProgressToken);

		// Assert
		JsonNode structured = ReadStructured(result);
		structured?["outcome"]?.GetValue<string>().Should().Be("indeterminate",
			because: "the terminal event belonged to another run, so this deploy never reported its own outcome and the honest answer is that we do not know — accepting a nested run's completion would answer a deploy that is still installing");
		result.IsError.Should().BeTrue(
			because: "an unknown outcome on a possibly half-installed environment must not reach the caller as a success");
		structured?["last-stage-reached"]?.GetValue<string>().Should().Contain(
			StageStreamingWorkerChild.LastStageBeforeSilence,
			because: "the caller needs the last stage THIS run actually reached, which is the stage before it went quiet — not anything the nested run reported");
	}

	[Test]
	[Description("An outcome outside the contract's three values is evidence of life, not of completion: it must never be scored as a success, because 'not failure' is not the same as 'succeeded' and the contract deliberately has no cancelled outcome.")]
	public async Task DispatchAsync_ShouldNotReportSuccess_WhenTheTerminalOutcomeIsOutsideTheContract() {
		// Arrange — expiry decides the answer once the malformed terminal event is correctly ignored.
		using StageStreamingWorkerChild worker =
			ArrangeWorker(ChildBehaviour.EmitsAnUnknownOutcomeThenDies, ExpirySilenceBound);
		RecordingClientSession client = new();
		McpWorkerCallDispatcher sut = CreateSut(silenceBound: ExpirySilenceBound);

		// Act
		CallToolResult result = await DispatchAsync(sut, client, CallerProgressToken);

		// Assert
		JsonNode structured = ReadStructured(result);
		structured?["success"]?.GetValue<bool>().Should().BeFalse(
			because: "the run's outcome is unknown, and an unvalidated comparison against only 'failure' would answer success:true for a deploy nobody can vouch for");
		structured?["outcome"]?.GetValue<string>().Should().Be("indeterminate",
			because: "an out-of-vocabulary outcome leaves the run unterminated, which is exactly what the indeterminate path is for");
		result.IsError.Should().BeTrue(
			because: "a possibly half-installed environment must not reach the caller as a successful result");
	}

	[Test]
	[Description("A deploy refused because every worker slot is in use must arrive as retryable SATURATION, not as a relay failure telling the operator that retrying is unlikely to help — nothing was spawned, so nothing was deployed.")]
	public async Task DispatchAsync_ShouldReportSaturation_WhenTheTerminalStageSpawnWaitsOutTheQueueBound() {
		// Arrange — the supervisor's named refusal on the terminal-stage admission path.
		_supervisor
			.SpawnContainedAsync(Arg.Any<WorkerSpawnRequest>(), Arg.Any<CancellationToken>())
			.Returns<Task<IWorkerLease>>(_ => throw new WorkerQueueWaitExpiredException(
				waitEndured: TimeSpan.FromSeconds(60), configuredBound: TimeSpan.FromSeconds(60),
				concurrencyCap: 4, queueDepth: 7));
		RecordingClientSession client = new();
		McpWorkerCallDispatcher sut = CreateSut();

		// Act
		CallToolResult result = await DispatchAsync(sut, client, CallerProgressToken);

		// Assert
		JsonNode structured = ReadStructured(result);
		structured?["error-class"]?.GetValue<string>().Should().Be("clio-worker-saturated",
			because: "the saturation envelope was added on the per-call path first and this branch was left flattening the same exception into a relay failure — the deploy family is exactly where wrong retry guidance costs the most");
		structured?["queue-depth"]?.GetValue<int>().Should().Be(7,
			because: "the admission numbers are what tell an operator to wait rather than to go looking for a defect in clio");
	}

	[Test]
	[Category("Unit")]
	[Description("The stage-event silence bound falls back to its 300 s default for a missing, blank, non-numeric or out-of-range override, and honours a valid one — parsed in invariant culture so a host locale cannot change the bound.")]
	public void ResolveStageEventSilenceBound_ShouldFallBackToTheDefault_WhenTheOverrideIsUnusable() {
		// Arrange
		string[] unusable = [null, "", "   ", "not-a-number", "0", "-5", "3601", "300,5"];

		// Act
		IReadOnlyList<TimeSpan> resolved =
			[.. unusable.Select(McpWorkerCallDispatcher.ResolveStageEventSilenceBound)];
		TimeSpan honoured = McpWorkerCallDispatcher.ResolveStageEventSilenceBound("42.5");

		// Assert
		resolved.Should().OnlyContain(bound => bound == TimeSpan.FromSeconds(300),
			because: "an unreadable override must never silently shorten the bound — a deploy killed because someone typed a comma would look exactly like a broken environment");
		honoured.Should().Be(TimeSpan.FromSeconds(42.5),
			because: "the override exists so an operator can widen the tolerated GAP between stages on a slow host, without touching the total the ordinary budget bounds");
	}

	[Test]
	[Category("Unit")]
	[Description("Injecting the synthetic progress token copies the caller's params rather than mutating them, and carries every settable member across — a rebuilt Name or dropped Arguments would change what the worker executes.")]
	public void WithSyntheticProgressToken_ShouldCopyTheParams_WhenTheCallerSuppliedNoToken() {
		// Arrange
		JsonObject callerMeta = new() { ["some-other-key"] = "kept" };
		CallToolRequestParams parameters = new() {
			Name = "clio-run",
			Arguments = new Dictionary<string, JsonElement> {
				["command"] = JsonSerializer.SerializeToElement(DeployToolName)
			},
			Meta = callerMeta
		};

		// Act
		CallToolRequestParams childParameters =
			McpWorkerCallDispatcher.WithSyntheticProgressToken(parameters, "synthetic-1");

		// Assert
		childParameters.Should().NotBeSameAs(parameters,
			because: "the caller's params belong to a request context the host still owns, and writing a progress token into it would re-issue a token on the caller's own request");
		callerMeta.Should().NotContainKey("progressToken",
			because: "mutating the caller's _meta in place is the same defect one level down — it would make the host believe the client asked for progress");
		childParameters.Name.Should().Be("clio-run",
			because: "the relayed call is the caller's own, and rebuilding it under the unwrapped inner name would double-wrap the arguments the child's clio-run expects");
		childParameters.Arguments.Should().BeSameAs(parameters.Arguments,
			because: "arguments are relayed verbatim; a copy that rebuilt them field by field is how a deploy loses a parameter");
		childParameters.Meta?["progressToken"]?.GetValue<string>().Should().Be("synthetic-1",
			because: "the token is what makes the child's forwarder stream at all — without it the run emits nothing and the parent bounds an invisible deploy");
		childParameters.Meta?["some-other-key"]?.GetValue<string>().Should().Be("kept",
			because: "everything else the caller put in _meta rides through untouched, exactly as ADR rule 1 requires");
	}

	[Test]
	[Category("Unit")]
	[Description("A single stage that runs three times longer than the whole silence bound is carried by the emitter's in-stage liveness refresh: the healthy worker answers for itself and is never killed, so a long database restore can no longer be reported as a possibly half-installed environment.")]
	public async Task DispatchAsync_ShouldReturnTheWorkersOwnAnswer_WhenOneStageOutlastsTheSilenceBound() {
		// Arrange — the child drives the PRODUCTION StageEventEmitter over the pipe, so what keeps this run
		// alive is the shipped emitter rather than a beat the fixture scripted for itself. Its one long
		// stage sleeps for three silence windows while the worker is perfectly healthy.
		using StageStreamingWorkerChild worker =
			ArrangeWorker(ChildBehaviour.RunsOneLongStageThroughTheProductionEmitter, LongStageSilenceBound);
		RecordingClientSession client = new();
		McpWorkerCallDispatcher sut = CreateSut(silenceBound: LongStageSilenceBound);

		// Act
		CallToolResult result = await DispatchAsync(sut, client, CallerProgressToken);

		// Assert
		result.IsError.Should().NotBeTrue(
			because: "the deploy was healthy throughout; an error here is the parent reporting a working run as INDETERMINATE because one of its stages was long");
		ReadStructured(result)?["answered-by"]?.GetValue<string>().Should().Be("worker",
			because: "the worker answered its own call, and a result composed by the parent instead means the call was resolved by the silence bound rather than by the run");
		ReadStructured(result)?["outcome"]?.GetValue<string>().Should()
			.NotBe(McpWorkerCallDispatcher.IndeterminateOutcome,
				because: "'possibly half-installed, do not retry' is the most damaging thing clio can say about an environment, and saying it about a deploy that simply took its time manufactures the very damage this protocol exists to prevent");
		_supervisor.DidNotReceive().KillContained(Arg.Any<IWorkerLease>());
		worker.StageDuration.Should().BeGreaterThan(LongStageSilenceBound,
			because: "the stage must genuinely have outlasted the bound, or this test would be asserting nothing about the silence timer at all");
		worker.RunningEventsForTheLongStage.Should().BeGreaterThan(1,
			because: "the refresh is what carried the run across that gap; one running event for the whole stage is precisely the silence the parent cannot tell apart from a dead child");
		client.StageEventTypes.Should().Contain(ClioStageEventContract.EventTypes.RunCompleted,
			because: "the caller supplied its own progress token, so every event of the run — refreshes included — must reach it unchanged (ADR rule 1)");
		_spawnCount.Should().Be(1,
			because: "a killed-and-retried deploy also finishes eventually, and the spawn count is what tells the two apart");
	}

	// ---------------------------------------------------------------------------------------------
	// Helpers
	// ---------------------------------------------------------------------------------------------

	private void Record(string entry) {
		lock (_orderedEventsLock) {
			_orderedEvents.Add(entry);
		}
	}

	private IReadOnlyList<string> OrderedEvents() {
		lock (_orderedEventsLock) {
			return [.. _orderedEvents];
		}
	}

	private static JsonNode ReadStructured(CallToolResult result) =>
		result?.StructuredContent is { } structured
			? JsonNode.Parse(structured.GetRawText())
			: null;

	private McpWorkerCallDispatcher CreateSut(TimeSpan? ordinaryBudget = null, TimeSpan? silenceBound = null) {
		StickyWorkerRegistry stickyWorkers = new(_logger);
		SharedResourceReservation reservations = new();
		return new McpWorkerCallDispatcher(_supervisor, new WorkerChildTransportOwner(),
			new WorkerMcpRelay(_logger), _settingsRepository, stickyWorkers,
			new StickyWorkerPoll(_supervisor, stickyWorkers, _logger), reservations,
			Substitute.For<Clio.Command.McpServer.Tools.IToolCommandResolver>(), _logger,
			ordinaryBudget ?? TimeSpan.FromSeconds(30), silenceBound ?? SilenceBound, ExitGrace);
	}

	private static async Task<CallToolResult> DispatchAsync(
		McpWorkerCallDispatcher sut, IParentMcpSession client, string callerProgressToken) {
		JsonObject meta = callerProgressToken is null
			? null
			: new JsonObject { ["progressToken"] = callerProgressToken };
		return await sut.DispatchAsync(
			new McpExecutionRoute(DeployToolName, McpToolExecutionLocation.Worker,
				McpExecutionDisposition.Worker,
				new McpToolExecutionMetadata(
					McpToolExecutionLocation.Worker,
					McpToolExecutionLifetime.PerCall,
					McpToolOperationFamily.Deploy,
					McpToolBudgetPolicy.TerminalStage,
					McpToolClientRequests.Progress,
					McpToolSharedFileResource.None)),
			new CallToolRequestParams { Name = DeployToolName, Meta = meta },
			client,
			CancellationToken.None).AsTask().WaitAsync(AssertionTimeout);
	}

	/// <summary>
	/// Arranges the substituted supervisor to hand out a lease over a scripted child's pipes, and to make
	/// <see cref="IWorkerProcessSupervisor.KillContained"/> actually STOP that child.
	/// </summary>
	/// <remarks>
	/// The kill has to have an effect or three assertions become vacuous: "the child's emitted log stops
	/// after the kill" would hold for a child nobody stopped, and the silence path would hang waiting for a
	/// call the killed worker was still free to answer.
	/// </remarks>
	/// <param name="behaviour">What the scripted child does once it is asked to run the tool.</param>
	/// <returns>The scripted child, so a test can state what it saw and what it emitted.</returns>
	private StageStreamingWorkerChild ArrangeWorker(ChildBehaviour behaviour, TimeSpan? silenceBound = null) {
		// The child's own silence is measured against the SAME bound the dispatcher runs with; passing them
		// separately is how a test would accidentally arrange a child that never out-waits the timer.
		StageStreamingWorkerChild worker = new(behaviour, silenceBound ?? SilenceBound);
		_supervisor
			.SpawnContainedAsync(Arg.Any<WorkerSpawnRequest>(), Arg.Any<CancellationToken>())
			.Returns(_ => {
				Interlocked.Increment(ref _spawnCount);
				worker.Start();
				return Task.FromResult(worker.Lease);
			});
		_supervisor
			.When(supervisor => supervisor.KillContained(Arg.Any<IWorkerLease>()))
			.Do(_ => {
				Record("kill");
				worker.SimulateKill();
			});
		return worker;
	}

	/// <summary>What the scripted child does after it is asked to run the terminal-stage tool.</summary>
	private enum ChildBehaviour {

		/// <summary>Streams the manifest, three stages and run-completed, then answers the call.</summary>
		AnswersAfterTerminalStage,

		/// <summary>Streams while sleeping far longer than a tiny ordinary budget, then completes.</summary>
		StreamsPastTheOrdinaryBudget,

		/// <summary>
		/// Runs a real manifest through the PRODUCTION <see cref="StageEventEmitter"/>, one of whose
		/// stages takes three silence windows, then answers the call.
		/// </summary>
		RunsOneLongStageThroughTheProductionEmitter,

		/// <summary>Streams two stages, goes quiet past the silence bound, then tries to stream again.</summary>
		FallsSilentThenBurstsAgain,

		/// <summary>
		/// Streams two stages, then emits a run-completed carrying a DIFFERENT runId — a nested run
		/// finishing inside the deploy — and then goes silent without ever completing its own run.
		/// </summary>
		EmitsANestedTerminalEventThenFallsSilent,

		/// <summary>
		/// Streams two stages, then emits a run-completed whose outcome is NOT one of the three the
		/// contract defines, then dies without answering — a version-skewed or malformed emitter.
		/// </summary>
		EmitsAnUnknownOutcomeThenDies,

		/// <summary>Streams to a successful run-completed, then never answers and never exits.</summary>
		HangsAfterTerminalStage,

		/// <summary>Streams to a FAILED run-completed, then never answers and never exits.</summary>
		HangsAfterFailingTerminalStage,

		/// <summary>Streams to a successful run-completed, then DIES without answering the call.</summary>
		ExitsAfterTerminalStage,

		/// <summary>Streams two stages, then closes its output — a child that died mid-deploy.</summary>
		ExitsWithoutTerminalStage
	}

	/// <summary>
	/// The client leg: records the raw notifications the relay hands upward, the way an MCP client's
	/// progress sink would see them.
	/// </summary>
	private sealed class RecordingClientSession : IParentMcpSession {

		private readonly List<JsonRpcNotification> _notifications = [];
		private readonly object _notificationsLock = new();

		/// <inheritdoc/>
		public bool SupportsSampling => false;

		/// <summary>Gets the progress notifications the client received, in arrival order.</summary>
		internal IReadOnlyList<JsonRpcNotification> ProgressNotifications {
			get {
				lock (_notificationsLock) {
					return [.. _notifications.Where(notification =>
						notification.Method == NotificationMethods.ProgressNotification)];
				}
			}
		}

		/// <summary>Gets each received beat's progress token, as raw JSON text so its KIND is compared too.</summary>
		internal IReadOnlyList<string> ProgressTokens =>
			[.. ProgressNotifications.Select(notification =>
				notification.Params?["progressToken"]?.ToJsonString())];

		/// <summary>Gets the stage-event discriminator of each received beat that carried one.</summary>
		internal IReadOnlyList<string> StageEventTypes =>
			[.. ProgressNotifications
				.Select(notification =>
					notification.Params?["_meta"]?["clioStageEvent"]?["eventType"]?.GetValue<string>())
				.Where(eventType => eventType is not null)];

		/// <inheritdoc/>
		public Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken) {
			if (message is JsonRpcNotification notification) {
				lock (_notificationsLock) {
					_notifications.Add(notification);
				}
			}
			return Task.CompletedTask;
		}

		// MCP9005: the sampling payload types are deprecated in SDK 2.2.0 (SEP-2577). This client advertises
		// no sampling capability, so the member exists only to satisfy the interface.
#pragma warning disable MCP9005
		/// <inheritdoc/>
		public ValueTask<CreateMessageResult> SampleAsync(CreateMessageRequestParams requestParams,
			CancellationToken cancellationToken) =>
			throw new NotSupportedException("This client advertises no sampling capability.");
#pragma warning restore MCP9005
	}

	/// <summary>
	/// A worker child that speaks real newline-framed JSON-RPC over a real pipe pair and streams the SHIPPED
	/// <see cref="ClioStageEvent"/> contract, the way a deploy running inside a worker does.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The events are built from the production record and serialised with
	/// <see cref="ClioStageEventContract.SerializerOptions"/>, so a change to the wire shape breaks these
	/// tests rather than passing against a hand-written imitation of it.
	/// </para>
	/// <para>
	/// <see cref="SimulateKill"/> stands in for the supervisor's real kill by disposing the child's output
	/// pipe: the child does not co-operate with it and has no flag to check, so an emission attempted after
	/// the kill simply fails to reach anyone — which is what a terminated process looks like, and what makes
	/// "its emitted log stops after the kill" a real assertion rather than a tautology.
	/// </para>
	/// </remarks>
	private sealed class StageStreamingWorkerChild : IDisposable {

		/// <summary>The last stage a silenced or crashed child reports before it stops.</summary>
		internal const string LastStageBeforeSilence = "stage-restore-db";

		/// <summary>The stage a killed child must never be observed emitting.</summary>
		internal const string StageAfterSilence = "stage-configure-site";

		private const string FirstStage = "stage-unpack";
		private const int ManifestTotal = 3;

		private readonly ChildBehaviour _behaviour;
		private readonly TimeSpan _silenceBound;
		private readonly Guid _runId = Guid.NewGuid();
		private readonly AnonymousPipeServerStream _parentToChildReader;
		private readonly AnonymousPipeClientStream _parentToChildWriter;
		private readonly AnonymousPipeServerStream _childToParentWriter;
		private readonly AnonymousPipeClientStream _childToParentReader;
		private readonly IWorkerLease _lease;
		private readonly List<string> _emitted = [];
		private readonly object _emittedLock = new();
		private int _callCount;
		private int _sequence;
		private int _killed;
		private int _runningEventsForTheLongStage;
		private TimeSpan _stageDuration;

		internal StageStreamingWorkerChild(ChildBehaviour behaviour, TimeSpan silenceBound) {
			_behaviour = behaviour;
			_silenceBound = silenceBound;
			_parentToChildReader = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
			_parentToChildWriter =
				new AnonymousPipeClientStream(PipeDirection.Out, _parentToChildReader.GetClientHandleAsString());
			_childToParentWriter = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);
			_childToParentReader =
				new AnonymousPipeClientStream(PipeDirection.In, _childToParentWriter.GetClientHandleAsString());
			_lease = Substitute.For<IWorkerLease>();
			_lease.ProcessId.Returns(27182);
			_lease.StandardInput.Returns(_parentToChildWriter);
			_lease.StandardOutput.Returns(_childToParentReader);
			// Stream.Null so the dispatcher's standard-error drain reaches end of stream at once: a stream
			// that never completed would cost every test in this fixture the drain's 250 ms stop bound.
			_lease.StandardError.Returns(Stream.Null);
			_lease.HasExited.Returns(_ => Volatile.Read(ref _killed) == 1);
			// Deliberately generous and deliberately unread: nothing in the terminal-stage path may consult
			// the lease's budget, and a value that would have expired long ago is how that stays honest.
			_lease.BudgetExpiresAtUtc.Returns(_ => DateTimeOffset.UtcNow.AddSeconds(-1));
		}

		/// <summary>Gets the lease the substituted supervisor hands to the dispatcher.</summary>
		internal IWorkerLease Lease => _lease;

		/// <summary>Gets the progress token the child found on the tools/call, or <c>null</c>.</summary>
		internal string ObservedProgressToken { get; private set; }

		/// <summary>Gets how many <c>tools/call</c> requests the child answered.</summary>
		internal int CallCount => Volatile.Read(ref _callCount);

		/// <summary>
		/// Gets how many <c>running</c> transitions the production emitter produced for the long stage —
		/// one for the stage itself plus one per liveness refresh.
		/// </summary>
		internal int RunningEventsForTheLongStage => Volatile.Read(ref _runningEventsForTheLongStage);

		/// <summary>Gets how long the long stage's action actually took, as the child measured it.</summary>
		internal TimeSpan StageDuration => _stageDuration;

		/// <summary>
		/// Gets the stage ids (and the <c>manifest</c> / <c>run-completed</c> discriminators) the child
		/// actually managed to write, in emission order.
		/// </summary>
		internal IReadOnlyList<string> EmittedStageIds {
			get {
				lock (_emittedLock) {
					return [.. _emitted];
				}
			}
		}

		/// <summary>Starts the child.</summary>
		internal void Start() => _ = Task.Run(RunAsync, CancellationToken.None);

		/// <summary>
		/// Stands in for the supervisor's kill: the child's output pipe is closed, so nothing it writes
		/// afterwards reaches the parent and nothing further is recorded.
		/// </summary>
		internal void SimulateKill() {
			if (Interlocked.Exchange(ref _killed, 1) == 1) {
				return;
			}
			try {
				_childToParentWriter.Dispose();
			}
			catch (Exception) {
				// A pipe already torn down is exactly what a killed process leaves behind.
			}
		}

		/// <inheritdoc/>
		public void Dispose() {
			_parentToChildWriter.Dispose();
			_parentToChildReader.Dispose();
			_childToParentReader.Dispose();
			try {
				_childToParentWriter.Dispose();
			}
			catch (Exception) {
				// SimulateKill may already have disposed it.
			}
		}

		private async Task RunAsync() {
			try {
				using StreamReader fromParent = new(_parentToChildReader);
				await using StreamWriter toParent = new(_childToParentWriter) { AutoFlush = true, NewLine = "\n" };
				string line;
				while ((line = await fromParent.ReadLineAsync().ConfigureAwait(false)) is not null) {
					await AnswerAsync(toParent, line).ConfigureAwait(false);
				}
			}
			catch (Exception) {
				// The parent closing its end IS how a worker's stdin ends, and a killed child's writes fail;
				// neither is a failure of the fixture.
			}
		}

		private async Task AnswerAsync(StreamWriter toParent, string line) {
			JsonNode request = JsonNode.Parse(line);
			string method = request?["method"]?.GetValue<string>();
			if (method == "initialize") {
				await WriteAsync(toParent, new JsonObject {
					["jsonrpc"] = "2.0",
					["id"] = request["id"]?.DeepClone(),
					["result"] = new JsonObject {
						["protocolVersion"] = WorkerRelayOptions.MeasuredProtocolVersion,
						["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
						["serverInfo"] = new JsonObject { ["name"] = "stage-streaming-worker", ["version"] = "1" }
					}
				}).ConfigureAwait(false);
				return;
			}
			if (method != "tools/call") {
				return;
			}
			Interlocked.Increment(ref _callCount);
			JsonNode progressToken = request["params"]?["_meta"]?["progressToken"];
			ObservedProgressToken = progressToken is JsonValue value && value.TryGetValue(out string text)
				? text
				: null;
			await RunScriptAsync(toParent, progressToken, request["id"]?.DeepClone()).ConfigureAwait(false);
		}

		private async Task RunScriptAsync(StreamWriter toParent, JsonNode progressToken, JsonNode requestId) {
			switch (_behaviour) {
				case ChildBehaviour.AnswersAfterTerminalStage:
					await StreamToTerminalAsync(toParent, progressToken,
						ClioStageEventContract.RunOutcomes.Success).ConfigureAwait(false);
					await AnswerCallAsync(toParent, requestId).ConfigureAwait(false);
					return;
				case ChildBehaviour.StreamsPastTheOrdinaryBudget:
					await EmitManifestAsync(toParent, progressToken).ConfigureAwait(false);
					for (int index = 0; index < ManifestTotal; index++) {
						// Each gap is comfortably UNDER the silence bound the test arranges, while three of them
						// add up to comfortably OVER it — so a silence timer that failed to reset on each stage
						// event would expire here, and so would any bound derived from the 1 ms ordinary budget.
						await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
						await EmitStageAsync(toParent, progressToken, StageIdAt(index), index).ConfigureAwait(false);
					}
					await EmitRunCompletedAsync(toParent, progressToken,
						ClioStageEventContract.RunOutcomes.Success).ConfigureAwait(false);
					await AnswerCallAsync(toParent, requestId).ConfigureAwait(false);
					return;
				case ChildBehaviour.RunsOneLongStageThroughTheProductionEmitter:
					RunProductionEmitterScript(toParent, progressToken);
					await AnswerCallAsync(toParent, requestId).ConfigureAwait(false);
					return;
				case ChildBehaviour.EmitsANestedTerminalEventThenFallsSilent:
					await EmitManifestAsync(toParent, progressToken).ConfigureAwait(false);
					await EmitStageAsync(toParent, progressToken, FirstStage, 0).ConfigureAwait(false);
					await EmitStageAsync(toParent, progressToken, LastStageBeforeSilence, 1)
						.ConfigureAwait(false);
					// A run-completed for a DIFFERENT run. The parent must not treat somebody else's terminal
					// event as its own, so this child never completes and must be reported indeterminate.
					await EmitRunCompletedAsync(toParent, progressToken,
						ClioStageEventContract.RunOutcomes.Success, Guid.NewGuid()).ConfigureAwait(false);
					await Task.Delay(_silenceBound * 6).ConfigureAwait(false);
					return;
				case ChildBehaviour.EmitsAnUnknownOutcomeThenDies:
					await EmitManifestAsync(toParent, progressToken).ConfigureAwait(false);
					await EmitStageAsync(toParent, progressToken, FirstStage, 0).ConfigureAwait(false);
					await EmitStageAsync(toParent, progressToken, LastStageBeforeSilence, 1)
						.ConfigureAwait(false);
					// Not one of success / failure / success-with-warnings. Deliberately the shape a newer or
					// buggier emitter produces, which an unvalidated read would score as "not failure".
					await EmitRunCompletedAsync(toParent, progressToken, "cancelled").ConfigureAwait(false);
					return;
				case ChildBehaviour.FallsSilentThenBurstsAgain:
					await EmitManifestAsync(toParent, progressToken).ConfigureAwait(false);
					await EmitStageAsync(toParent, progressToken, FirstStage, 0).ConfigureAwait(false);
					await EmitStageAsync(toParent, progressToken, LastStageBeforeSilence, 1).ConfigureAwait(false);
					// Quiet for well past the bound, then noisy again — a badly behaved child, so that "the
					// emitted log stops after the kill" has something it could have recorded had the kill missed.
					await Task.Delay(_silenceBound * 6).ConfigureAwait(false);
					await EmitStageAsync(toParent, progressToken, StageAfterSilence, 2).ConfigureAwait(false);
					return;
				case ChildBehaviour.HangsAfterTerminalStage:
					await StreamToTerminalAsync(toParent, progressToken,
						ClioStageEventContract.RunOutcomes.Success).ConfigureAwait(false);
					return;
				case ChildBehaviour.HangsAfterFailingTerminalStage:
					await StreamToTerminalAsync(toParent, progressToken,
						ClioStageEventContract.RunOutcomes.Failure).ConfigureAwait(false);
					return;
				case ChildBehaviour.ExitsAfterTerminalStage:
					await StreamToTerminalAsync(toParent, progressToken,
						ClioStageEventContract.RunOutcomes.Success).ConfigureAwait(false);
					// Dead before it could reply: the read loop ends and the pending call faults, which is
					// the branch that must still answer with the terminal outcome rather than indeterminate.
					SimulateKill();
					return;
				case ChildBehaviour.ExitsWithoutTerminalStage:
					await EmitManifestAsync(toParent, progressToken).ConfigureAwait(false);
					await EmitStageAsync(toParent, progressToken, FirstStage, 0).ConfigureAwait(false);
					await EmitStageAsync(toParent, progressToken, LastStageBeforeSilence, 1).ConfigureAwait(false);
					// Closing the output pipe is how a dead child looks to the parent: the read loop ends and
					// every pending request faults.
					SimulateKill();
					return;
			}
		}

		/// <summary>
		/// Runs a real three-stage manifest through the PRODUCTION emitter, wired to this child's pipe the
		/// way <see cref="StageEventProgressForwarder"/> wires it inside a real worker.
		/// </summary>
		/// <remarks>
		/// The refresh interval is scaled to a third of the silence bound — the same RATIO the shipped
		/// numbers hold (30 s inside 300 s) at a size a unit test can wait out. Nothing else about the
		/// emitter is arranged: if the shipped emitter stops refreshing, this child falls silent for three
		/// whole silence windows and the parent kills it.
		/// </remarks>
		/// <param name="toParent">The child's output.</param>
		/// <param name="progressToken">The token the call carried.</param>
		private void RunProductionEmitterScript(StreamWriter toParent, JsonNode progressToken) {
			StageEventEmitter emitter = new() { LivenessRefreshInterval = _silenceBound / 3 };
			emitter.Begin(ClioStageEventContract.Operations.Deploy, [
				new StageDescriptor(FirstStage, "Unpack", false),
				new StageDescriptor(LastStageBeforeSilence, "Restore database", false),
				new StageDescriptor(StageAfterSilence, "Configure site", false)
			], stageEvent => Emit(toParent, progressToken, stageEvent));
			emitter.RunStage(FirstStage, () => { });
			Stopwatch stageClock = Stopwatch.StartNew();
			emitter.RunStage(LastStageBeforeSilence, () => Thread.Sleep(LongStageDuration));
			stageClock.Stop();
			_stageDuration = stageClock.Elapsed;
			emitter.RunStage(StageAfterSilence, () => { });
			emitter.CompleteSuccess("The deploy completed.");
		}

		/// <summary>
		/// Writes one emitter-produced event to the pipe. Synchronous because the emitter's sink is, and
		/// safe because that sink is invoked under the emitter's own sequencing lock — the stage thread and
		/// the refresh thread never reach this writer together.
		/// </summary>
		/// <param name="toParent">The child's output.</param>
		/// <param name="progressToken">The token the call carried.</param>
		/// <param name="stageEvent">The event the production emitter raised.</param>
		private void Emit(StreamWriter toParent, JsonNode progressToken, ClioStageEvent stageEvent) {
			if (progressToken is null) {
				return;
			}
			if (stageEvent.Stage is { } stage
				&& stage.StageId == LastStageBeforeSilence
				&& stage.Status == ClioStageEventContract.StageStatuses.Running) {
				Interlocked.Increment(ref _runningEventsForTheLongStage);
			}
			toParent.WriteLine(new JsonObject {
				["jsonrpc"] = "2.0",
				["method"] = NotificationMethods.ProgressNotification,
				["params"] = new JsonObject {
					["progressToken"] = progressToken.DeepClone(),
					["progress"] = stageEvent.Sequence,
					["_meta"] = new JsonObject {
						["clioStageEvent"] =
							JsonSerializer.SerializeToNode(stageEvent, ClioStageEventContract.SerializerOptions)
					}
				}
			}.ToJsonString());
			lock (_emittedLock) {
				_emitted.Add(stageEvent.EventType);
			}
		}

		private async Task StreamToTerminalAsync(StreamWriter toParent, JsonNode progressToken, string outcome) {
			await EmitManifestAsync(toParent, progressToken).ConfigureAwait(false);
			for (int index = 0; index < ManifestTotal; index++) {
				await EmitStageAsync(toParent, progressToken, StageIdAt(index), index).ConfigureAwait(false);
			}
			await EmitRunCompletedAsync(toParent, progressToken, outcome).ConfigureAwait(false);
		}

		private static string StageIdAt(int index) => index switch {
			0 => FirstStage,
			1 => LastStageBeforeSilence,
			_ => StageAfterSilence
		};

		private Task EmitManifestAsync(StreamWriter toParent, JsonNode progressToken) =>
			EmitAsync(toParent, progressToken, ClioStageEventContract.EventTypes.Manifest,
				new ClioStageEvent(
					ClioStageEventContract.SchemaVersion,
					ClioStageEventContract.EventTypes.Manifest,
					_runId,
					Interlocked.Increment(ref _sequence) - 1,
					ClioStageEventContract.Operations.Deploy,
					Stages: [
						new ClioStageManifestEntry(FirstStage, "Unpack", 0, ManifestTotal, false),
						new ClioStageManifestEntry(LastStageBeforeSilence, "Restore database", 1, ManifestTotal, false),
						new ClioStageManifestEntry(StageAfterSilence, "Configure site", 2, ManifestTotal, false)
					]));

		private Task EmitStageAsync(StreamWriter toParent, JsonNode progressToken, string stageId, int index) =>
			EmitAsync(toParent, progressToken, stageId,
				new ClioStageEvent(
					ClioStageEventContract.SchemaVersion,
					ClioStageEventContract.EventTypes.Stage,
					_runId,
					Interlocked.Increment(ref _sequence) - 1,
					ClioStageEventContract.Operations.Deploy,
					Stage: new ClioStageDetail(stageId, stageId, index, ManifestTotal,
						ClioStageEventContract.StageStatuses.Running, Message: $"{stageId} is running")));

		private Task EmitRunCompletedAsync(StreamWriter toParent, JsonNode progressToken, string outcome) =>
			EmitRunCompletedAsync(toParent, progressToken, outcome, _runId);

		private Task EmitRunCompletedAsync(
			StreamWriter toParent, JsonNode progressToken, string outcome, Guid runId) =>
			EmitAsync(toParent, progressToken, ClioStageEventContract.EventTypes.RunCompleted,
				new ClioStageEvent(
					ClioStageEventContract.SchemaVersion,
					ClioStageEventContract.EventTypes.RunCompleted,
					runId,
					Interlocked.Increment(ref _sequence) - 1,
					ClioStageEventContract.Operations.Deploy,
					RunCompleted: new ClioRunCompleted(outcome, $"The run ended with {outcome}.")));

		private async Task EmitAsync(
			StreamWriter toParent, JsonNode progressToken, string emittedId, ClioStageEvent stageEvent) {
			if (progressToken is null) {
				// No token means the forwarder inside a real worker is inert, so a real child emits nothing.
				return;
			}
			await WriteAsync(toParent, new JsonObject {
				["jsonrpc"] = "2.0",
				["method"] = NotificationMethods.ProgressNotification,
				["params"] = new JsonObject {
					["progressToken"] = progressToken.DeepClone(),
					["progress"] = stageEvent.Sequence,
					["_meta"] = new JsonObject {
						["clioStageEvent"] =
							JsonSerializer.SerializeToNode(stageEvent, ClioStageEventContract.SerializerOptions)
					}
				}
			}).ConfigureAwait(false);
			lock (_emittedLock) {
				_emitted.Add(emittedId);
			}
		}

		private static Task AnswerCallAsync(StreamWriter toParent, JsonNode requestId) =>
			WriteAsync(toParent, new JsonObject {
				["jsonrpc"] = "2.0",
				["id"] = requestId,
				["result"] = new JsonObject {
					["content"] = new JsonArray(new JsonObject {
						["type"] = "text",
						["text"] = "{\"success\":true}"
					}),
					["structuredContent"] = new JsonObject {
						["success"] = true,
						["answered-by"] = "worker"
					},
					["isError"] = false
				}
			});

		private static Task WriteAsync(StreamWriter toParent, JsonObject message) =>
			toParent.WriteLineAsync(message.ToJsonString());
	}
}
