using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Clio.Command.McpServer.Progress;
using Clio.Command.McpServer.Relay;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// ENG-95262: the IN-STAGE liveness refresh — a stage that is still running re-announces itself, so
/// stage-event silence means "this worker has stopped talking" rather than "this stage is long".
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect this fixture pins.</b> <see cref="StageEventEmitter.RunStage(string, Action)"/> emits one
/// <c>running</c> event, runs the stage, then emits the stage's terminal status — nothing in between. A
/// worker-executed deploy is bounded by a stage-event SILENCE timer
/// (<see cref="McpWorkerCallDispatcher.DefaultStageEventSilenceBound"/>, ADR §3.3), so a single
/// legitimately long stage used to trip that timer: the parent reported the run indeterminate, declared
/// the environment possibly half-installed and killed a perfectly healthy worker.
/// </para>
/// <para>
/// <b>The refresh must not change the wire contract.</b> A refresh is an ORDINARY <c>stage</c> event with
/// status <c>running</c> and the next <c>sequence</c> — no new event type, no new status, no new field —
/// because ClioRing correlates on <c>(runId, sequence)</c> and replays them ordered. The assertions below
/// are written against those two properties rather than against a bespoke marker, so a refresh that
/// invented one would fail here.
/// </para>
/// </remarks>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class StageEventLivenessHeartbeatTests {

	/// <summary>The stage the long-running action is attached to.</summary>
	private const string LongStageId = "restore-db";

	/// <summary>A stage that is never entered, so the cascade has something to skip.</summary>
	private const string LaterStageId = "configure-site";

	/// <summary>
	/// The scaled refresh interval. Small enough that a sub-second stage out-lasts it several times over,
	/// large enough that a loaded CI agent still gets past the first one.
	/// </summary>
	private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(100);

	/// <summary>
	/// How long the observed stage runs: several refresh intervals, so "more than one refresh" is not a
	/// race with the scheduler.
	/// </summary>
	private static readonly TimeSpan LongStageDuration = TimeSpan.FromMilliseconds(700);

	[Test]
	[Category("Unit")]
	[Description("A stage whose action outlasts the refresh interval re-announces itself as running, so a long stage keeps the stream alive instead of looking like a dead worker.")]
	public void RunStage_ShouldReAnnounceTheRunningStage_WhenItsActionOutlastsTheRefreshInterval() {
		// Arrange
		(StageEventEmitter emitter, List<ClioStageEvent> events) = CreateEmitter();

		// Act
		emitter.RunStage(LongStageId, () => Thread.Sleep(LongStageDuration));

		// Assert
		List<ClioStageEvent> running = RunningEventsFor(events, LongStageId);
		running.Count.Should().BeGreaterThan(1,
			because: "the stage ran for several refresh intervals, and a single running event over that whole window is exactly the silence a parent cannot tell apart from a child that has stopped talking");
		running.Should().OnlyContain(
			stageEvent => stageEvent.EventType == ClioStageEventContract.EventTypes.Stage,
			because: "a refresh is an ORDINARY stage event: a new event type would be a stage-event contract change that ClioRing mirrors, not a local decision");
		running.Should().OnlyContain(stageEvent => stageEvent.Stage.StageId == LongStageId,
			because: "a refresh re-announces the CURRENT stage; naming any other stage would move the parent's 'last stage reached' onto a stage that never ran");
		events.Select(stageEvent => stageEvent.Sequence).Should().OnlyHaveUniqueItems(
			because: "ClioRing de-duplicates by (runId, sequence) and buffers until the next contiguous number, so a repeated sequence stalls its replay outright");
		events.Select(stageEvent => stageEvent.Sequence).Should().BeInAscendingOrder(
			because: "the refresh shares the emitter's monotonic counter rather than a stream of its own");
		events.Last().Stage.Status.Should().Be(ClioStageEventContract.StageStatuses.Done,
			because: "the stage's own terminal transition must still be the last thing the stage emits — a refresh landing after it would report a finished stage as running");
	}

	[Test]
	[Category("Unit")]
	[Description("The liveness refresh stops with the stage: nothing further is emitted once RunStage has returned, so an idle emitter never speaks for a stage that has ended.")]
	public void RunStage_ShouldStopTheLivenessRefresh_WhenTheStageEnds() {
		// Arrange
		(StageEventEmitter emitter, List<ClioStageEvent> events) = CreateEmitter();
		emitter.RunStage(LongStageId, () => Thread.Sleep(LongStageDuration));
		int emittedByTheStage = events.Count;

		// Act — several refresh intervals with no stage running at all.
		Thread.Sleep(RefreshInterval * 5);

		// Assert
		events.Count.Should().Be(emittedByTheStage,
			because: "a refresh that outlives its stage would report progress for work that has finished, and on a failed run would keep talking after the terminal event");
	}

	[Test]
	[Category("Unit")]
	[Description("No refresh follows the terminal event of a run whose stage failed: the failure cascade and run-completed are the last events, so the parent never sees life after the run ended.")]
	public void RunStage_ShouldEmitNothingAfterTheTerminalEvent_WhenTheLongStageFails() {
		// Arrange
		(StageEventEmitter emitter, List<ClioStageEvent> events) = CreateEmitter();

		// Act
		Action failing = () => emitter.RunStage(LongStageId, () => {
			Thread.Sleep(LongStageDuration);
			throw new InvalidOperationException("the restore failed");
		});
		failing.Should().Throw<InvalidOperationException>(
			because: "RunStage rethrows so the caller's existing control flow is unchanged, and the arrangement depends on the stage genuinely failing");
		int emittedByTheRun = events.Count;
		Thread.Sleep(RefreshInterval * 5);

		// Assert
		events.Last().EventType.Should().Be(ClioStageEventContract.EventTypes.RunCompleted,
			because: "the terminal event must be the last event of the run — a refresh after it would make a completed run look like it was still working");
		events.Count.Should().Be(emittedByTheRun,
			because: "the refresh must have been stopped before the failure cascade ran, not merely before the process ended");
		RunningEventsFor(events, LongStageId).Count.Should().BeGreaterThan(1,
			because: "the failing stage still ran long enough to be refreshed, so this test would prove nothing if the refresh had never started");
	}

	[Test]
	[Category("Unit")]
	[Description("A long stage that RETURNS a non-zero exit code — the other route to the failure cascade, and the one that reaches it after the stop rather than before — also ends at run-completed with no refresh behind it.")]
	public void RunStage_ShouldEmitNothingAfterTheTerminalEvent_WhenTheLongStageReturnsNonZero() {
		// Arrange
		(StageEventEmitter emitter, List<ClioStageEvent> events) = CreateEmitter();

		// Act
		int exitCode = emitter.RunStage(LongStageId, () => {
			Thread.Sleep(LongStageDuration);
			return 3;
		});
		int emittedByTheRun = events.Count;
		Thread.Sleep(RefreshInterval * 5);

		// Assert
		exitCode.Should().Be(3,
			because: "the stage's real exit code must still reach the caller — the refresh must not change what RunStage returns");
		events.Last().EventType.Should().Be(ClioStageEventContract.EventTypes.RunCompleted,
			because: "a non-zero return is an honest failure, and its terminal event must be the last word of the run exactly as a thrown stage's is");
		events.Count.Should().Be(emittedByTheRun,
			because: "this branch reaches the cascade AFTER the stop rather than before it, so it is a different ordering from the thrown-stage path and has to be checked rather than inferred");
		RunningEventsFor(events, LongStageId).Count.Should().BeGreaterThan(1,
			because: "the stage ran long enough to be refreshed, so a run with no refresh in it would make this test prove nothing");
	}

	[Test]
	[Category("Unit")]
	[Description("A stage that returns immediately still emits exactly running then done — the refresh adds nothing to an ordinary fast stage.")]
	public void RunStage_ShouldEmitOnlyRunningAndDone_WhenTheStageReturnsImmediately() {
		// Arrange
		(StageEventEmitter emitter, List<ClioStageEvent> events) = CreateEmitter();

		// Act
		emitter.RunStage(LongStageId, () => { });

		// Assert
		events.Where(stageEvent => stageEvent.EventType == ClioStageEventContract.EventTypes.Stage)
			.Should().HaveCount(2,
				because: "a fast stage emits running then done and nothing else; every existing stage-event assertion in this repository counts on that, so the refresh must be invisible to a stage that outlasts nothing");
	}

	[Test]
	[Category("Unit")]
	[Description("The emitter's sequence counter survives a refresh racing the stage's own transition: every event of a run carries a distinct, contiguous sequence.")]
	public void Emit_ShouldAssignEveryEventADistinctContiguousSequence_WhenRefreshesRaceTheStagesOwnTransitions() {
		// Arrange — a refresh interval short enough that a beat is in flight when the stage ends, which is
		// the moment two threads reach the emitter's single sequencing chokepoint together.
		(StageEventEmitter emitter, List<ClioStageEvent> events) = CreateEmitter(TimeSpan.FromMilliseconds(1));

		// Act
		emitter.RunStage(LongStageId, () => Thread.Sleep(TimeSpan.FromMilliseconds(200)));
		emitter.CompleteSuccess("done");

		// Assert
		events.Select(stageEvent => stageEvent.Sequence).Should().Equal(Enumerable.Range(0, events.Count),
			because: "a lost update on the sequence counter gives two events one number, and ClioRing then waits forever for a contiguous number that was never sent");
	}

	[Test]
	[Category("Unit")]
	[Description("The shipped silence bound is a comfortable multiple of the refresh interval, so a healthy long stage can never be mistaken for a silent worker — the one relationship the two numbers cannot derive at run time.")]
	public void DefaultStageEventSilenceBound_ShouldBeAComfortableMultipleOfTheStageLivenessRefreshInterval() {
		// Arrange — the child cannot read the parent's bound: CLIO_MCP_WORKER_STAGE_SILENCE_SECONDS
		// configures the supervisor and is deliberately outside the worker's inherited-variable allowlist.
		// So the relationship is pinned here instead of computed there.
		const int minimumRefreshesPerSilenceWindow = 4;

		// Act
		double refreshesPerWindow = McpWorkerCallDispatcher.DefaultStageEventSilenceBound
			/ StageEventEmitter.StageLivenessRefreshInterval;

		// Assert
		refreshesPerWindow.Should().BeGreaterThanOrEqualTo(minimumRefreshesPerSilenceWindow,
			because: "several refreshes must fit inside one silence window: at a ratio near one a single dropped or delayed beat expires the bound and kills a healthy deploy, and these two numbers cannot check each other at run time");
		StageEventEmitter.StageLivenessRefreshInterval.Should().BePositive(
			because: "a zero or negative interval would either spin the emitter or disable the refresh silently");
	}

	private static (StageEventEmitter Emitter, List<ClioStageEvent> Events) CreateEmitter(
		TimeSpan? refreshInterval = null) {
		StageEventEmitter emitter = new() { LivenessRefreshInterval = refreshInterval ?? RefreshInterval };
		List<ClioStageEvent> events = [];
		List<ClioStageEvent> sink = events;
		emitter.Begin(ClioStageEventContract.Operations.Deploy, [
			new StageDescriptor(LongStageId, "Restore database", false),
			new StageDescriptor(LaterStageId, "Configure site", false)
		], stageEvent => {
			// The refresh runs on its own thread, so the recording sink is guarded exactly as a real one
			// would have to be; an unguarded List here would fail on the collection rather than on the code
			// under test.
			lock (sink) {
				sink.Add(stageEvent);
			}
		});
		return (emitter, events);
	}

	private static List<ClioStageEvent> RunningEventsFor(List<ClioStageEvent> events, string stageId) {
		lock (events) {
			return [.. events.Where(stageEvent =>
				stageEvent.Stage is { } stage
				&& stage.StageId == stageId
				&& stage.Status == ClioStageEventContract.StageStatuses.Running)];
		}
	}
}
