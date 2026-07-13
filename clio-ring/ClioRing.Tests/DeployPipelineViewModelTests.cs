using System;
using System.Collections.Generic;
using System.Linq;
using ClioRing.Ipc;
using ClioRing.ViewModels;
using FluentAssertions;
using NUnit.Framework;

namespace ClioRing.Tests;

/// <summary>
/// Unit tests for <see cref="DeployPipelineViewModel"/> (story 7): the manifest→step-list build, the
/// per-step state machine driven by <c>stage</c> events, the failure cascade, terminal rendering from
/// <c>run-completed</c>, and the robustness invariants (duplicate/out-of-order tolerance, unknown
/// stages/fields, the schemaVersion degrade gate, no cross-run leakage, and per-run sink disposal).
/// Synthetic typed <see cref="ClioStageEvent"/>s are fed straight into <see cref="DeployPipelineViewModel.Ingest"/>
/// so no clio process runs and the assertions never race a dispatcher.
/// </summary>
[TestFixture]
[Category("Unit")]
public sealed class DeployPipelineViewModelTests {
	private const int V = ClioStageEventContract.SchemaVersion;
	private static readonly string Deploy = ClioStageEventContract.Operations.Deploy;

	private DeployPipelineViewModel _sut = null!;
	private Guid _runId;
	private int _seq;

	[SetUp]
	public void SetUp() {
		_sut = new DeployPipelineViewModel();
		_runId = Guid.NewGuid();
		_seq = 0;
	}

	// ---- helpers: synthetic event builders ----

	private ClioStageEvent Manifest(Guid runId, params ClioStageManifestEntry[] stages) =>
		new(V, ClioStageEventContract.EventTypes.Manifest, runId, _seq++, Deploy, Stages: stages);

	private ClioStageEvent Stage(Guid runId, string stageId, string status,
		long? durationMs = null, string? message = null, string? detail = null,
		string? errorCode = null, string? skipReason = null, int? sequence = null) =>
		new(V, ClioStageEventContract.EventTypes.Stage, runId, sequence ?? _seq++, Deploy,
			Stage: new ClioStageDetail(stageId, stageId, 0, 3, status,
				DurationMs: durationMs, Message: message ?? stageId, Detail: detail, ErrorCode: errorCode, SkipReason: skipReason));

	private ClioStageEvent RunCompleted(Guid runId, string outcome, string summary,
		string? detail = null, string? errorCode = null, string? derivedUrl = null, string? derivedPath = null) =>
		new(V, ClioStageEventContract.EventTypes.RunCompleted, runId, _seq++, Deploy,
			RunCompleted: new ClioRunCompleted(outcome, summary, detail, errorCode, derivedUrl, derivedPath));

	private static ClioStageManifestEntry[] ThreeStageManifest() => new[] {
		new ClioStageManifestEntry("unzip", "Unzip distribution", 0, 3, false),
		new ClioStageManifestEntry("restore-db", "Restore database", 1, 3, false),
		new ClioStageManifestEntry("deploy-app", "Deploy application", 2, 3, false)
	};

	private PipelineStepViewModel Step(string stageId) =>
		_sut.Steps.Single(s => s.StageId == stageId);

	// ---- manifest → pending list ----

	[Test]
	[Description("A manifest event builds the ordered step list one-per-stage, all Pending, with no fabricated pending events (AC-01 / AC-09).")]
	public void Ingest_ShouldBuildPendingStepList_WhenManifestReceived() {
		// Arrange — a three-stage deploy manifest.
		ClioStageEvent manifest = Manifest(_runId, ThreeStageManifest());

		// Act — feed the manifest.
		_sut.Ingest(manifest);

		// Assert — one step per manifest stage, in order, all Pending; the run is now Running.
		_sut.Steps.Should().HaveCount(3, because: "the step list is built one-per-manifest-stage");
		_sut.Steps.Select(s => s.StageId).Should().ContainInOrder(new[] { "unzip", "restore-db", "deploy-app" },
			"steps preserve the manifest order");
		_sut.Steps.Should().OnlyContain(s => s.State == PipelineStepState.Pending,
			because: "every step starts Pending and is never fabricated as a completed event");
		_sut.RunState.Should().Be(PipelineRunState.Running, because: "a received manifest means the run is in progress");
		_sut.HasSteps.Should().BeTrue(because: "the pipeline now has steps to render");
	}

	// ---- running / done transitions ----

	[Test]
	[Description("Stage events transition a step Pending→Running→Done, capturing duration and friendly message (AC-02 / AC-05).")]
	public void Ingest_ShouldTransitionRunningThenDone_WhenStageEventsArrive() {
		// Arrange — a manifest then running + done events for the first stage.
		_sut.Ingest(Manifest(_runId, ThreeStageManifest()));

		// Act — running then done.
		_sut.Ingest(Stage(_runId, "unzip", ClioStageEventContract.StageStatuses.Running, message: "Unpacking…"));
		PipelineStepState afterRunning = Step("unzip").State;
		_sut.Ingest(Stage(_runId, "unzip", ClioStageEventContract.StageStatuses.Done, durationMs: 3200, message: "Unpacked"));

		// Assert — the step ran, then completed with a duration + message.
		afterRunning.Should().Be(PipelineStepState.Running, because: "a running event moves the step out of Pending");
		PipelineStepViewModel step = Step("unzip");
		step.State.Should().Be(PipelineStepState.Done, because: "a done event completes the step");
		step.DurationMs.Should().Be(3200, because: "the done event carries the elapsed duration");
		step.Message.Should().Be("Unpacked", because: "the friendly message is captured from the done event");
	}

	[Test]
	[Description("A done stage event carrying detail/errorCode exposes them behind the expander via HasTechnicalDetail (AC-02).")]
	public void Ingest_ShouldExposeTechnicalDetail_WhenStageCarriesDetail() {
		// Arrange.
		_sut.Ingest(Manifest(_runId, ThreeStageManifest()));

		// Act — a done event with technical detail.
		_sut.Ingest(Stage(_runId, "unzip", ClioStageEventContract.StageStatuses.Done, detail: "7z extracted 4213 files"));

		// Assert.
		Step("unzip").HasTechnicalDetail.Should().BeTrue(because: "detail present means there is something to reveal behind the expander");
	}

	// ---- failure cascade ----

	[Test]
	[Description("On a stage failure the active step is Failed and every remaining step cascades to Skipped(after-failure) (AC-03 / AC-10).")]
	public void Ingest_ShouldFailActiveAndSkipRemaining_WhenStageFails() {
		// Arrange — manifest, first stage done, second stage running.
		_sut.Ingest(Manifest(_runId, ThreeStageManifest()));
		_sut.Ingest(Stage(_runId, "unzip", ClioStageEventContract.StageStatuses.Done, durationMs: 100));

		// Act — the second stage fails.
		_sut.Ingest(Stage(_runId, "restore-db", ClioStageEventContract.StageStatuses.Failed,
			durationMs: 8800, message: "Restore failed", detail: "pg_restore exit 1", errorCode: "DB_RESTORE_FAILED"));

		// Assert — active is Failed with its technical detail; the remaining step is Skipped(after-failure).
		Step("restore-db").State.Should().Be(PipelineStepState.Failed, because: "the failing stage shows Failed");
		Step("restore-db").ErrorCode.Should().Be("DB_RESTORE_FAILED", because: "the error code is captured for the expander");
		PipelineStepViewModel tail = Step("deploy-app");
		tail.State.Should().Be(PipelineStepState.Skipped, because: "steps after a failure cascade to skipped");
		tail.IsSkippedAfterFailure.Should().BeTrue(because: "the cascade uses skipReason=after-failure");
		Step("unzip").State.Should().Be(PipelineStepState.Done, because: "a step already completed before the failure is untouched");
	}

	[Test]
	[Description("A skip-by-condition (not-applicable) and a skip-after-failure are rendered distinctly, never conflated (AC-05).")]
	public void Ingest_ShouldDistinguishSkipReasons_WhenBothKindsOccur() {
		// Arrange — a manifest whose first stage is conditional.
		_sut.Ingest(Manifest(_runId,
			new ClioStageManifestEntry("stage-build", "Build source", 0, 3, true),
			new ClioStageManifestEntry("restore-db", "Restore database", 1, 3, false),
			new ClioStageManifestEntry("deploy-app", "Deploy application", 2, 3, false)));

		// Act — the conditional stage is skipped (not-applicable), then restore-db fails (cascading deploy-app).
		_sut.Ingest(Stage(_runId, "stage-build", ClioStageEventContract.StageStatuses.Skipped,
			skipReason: ClioStageEventContract.SkipReasons.NotApplicable, message: "Not a network source"));
		_sut.Ingest(Stage(_runId, "restore-db", ClioStageEventContract.StageStatuses.Failed, message: "boom", errorCode: "X"));

		// Assert — the two skips carry different reason flags.
		Step("stage-build").IsSkippedNotApplicable.Should().BeTrue(because: "a condition-off stage is skipped not-applicable");
		Step("stage-build").IsSkippedAfterFailure.Should().BeFalse(because: "not-applicable must not be conflated with after-failure");
		Step("deploy-app").IsSkippedAfterFailure.Should().BeTrue(because: "a cascade skip is distinctly after-failure");
	}

	// ---- run-completed terminal ----

	[Test]
	[Description("A run-completed success renders the terminal outcome, summary and derived URL/path with no error affordance (AC-04).")]
	public void Ingest_ShouldRenderSuccessTerminal_WhenRunCompletedSuccess() {
		// Arrange — a manifest and all stages done.
		_sut.Ingest(Manifest(_runId, ThreeStageManifest()));
		foreach (string id in new[] { "unzip", "restore-db", "deploy-app" }) {
			_sut.Ingest(Stage(_runId, id, ClioStageEventContract.StageStatuses.Done, durationMs: 10));
		}

		// Act — the terminal success event with derived outputs.
		_sut.Ingest(RunCompleted(_runId, ClioStageEventContract.RunOutcomes.Success, "Deployment completed",
			derivedUrl: "http://localhost:40000/0", derivedPath: @"C:\creatio"));

		// Assert.
		_sut.RunState.Should().Be(PipelineRunState.Succeeded, because: "outcome=success is a succeeded terminal");
		_sut.Summary.Should().Be("Deployment completed", because: "the friendly summary is surfaced");
		_sut.HasDerivedUrl.Should().BeTrue(because: "a derivedUrl is present on success");
		_sut.DerivedUrl.Should().Be("http://localhost:40000/0", because: "the derived URL is captured for the Open affordance");
		_sut.DerivedPath.Should().Be(@"C:\creatio", because: "the derived path is captured");
		_sut.HasTerminalDetail.Should().BeFalse(because: "a happy path shows no error/expander noise");
	}

	[Test]
	[Description("A run-completed failure renders the failed terminal with exactly one summary message and the technical detail behind the expander (AC-03 / AC-ERR).")]
	public void Ingest_ShouldRenderFailedTerminal_WhenRunCompletedFailure() {
		// Arrange — manifest, one stage failed.
		_sut.Ingest(Manifest(_runId, ThreeStageManifest()));
		_sut.Ingest(Stage(_runId, "unzip", ClioStageEventContract.StageStatuses.Failed, message: "boom", errorCode: "E1"));

		// Act — the terminal failure event.
		_sut.Ingest(RunCompleted(_runId, ClioStageEventContract.RunOutcomes.Failure,
			"Deployment failed. Free disk space and retry.", detail: "disk full", errorCode: "E1"));

		// Assert.
		_sut.RunState.Should().Be(PipelineRunState.Failed, because: "outcome=failure is a failed terminal");
		_sut.Summary.Should().Be("Deployment failed. Free disk space and retry.", because: "one human message + corrective action is shown");
		_sut.HasTerminalDetail.Should().BeTrue(because: "the technical detail is available behind the expander");
	}

	[Test]
	[Description("If intermediate stage events are lost but manifest + run-completed(success) arrive, the pipeline reconciles pending steps to done rather than stalling (AC-ERR).")]
	public void Ingest_ShouldReconcilePendingToDone_WhenSuccessArrivesWithLostStageEvents() {
		// Arrange — a manifest with NO per-stage events at all.
		_sut.Ingest(Manifest(_runId, ThreeStageManifest()));

		// Act — only the terminal success arrives.
		_sut.Ingest(RunCompleted(_runId, ClioStageEventContract.RunOutcomes.Success, "Done"));

		// Assert — no step is left stalled in Pending.
		_sut.RunState.Should().Be(PipelineRunState.Succeeded, because: "the terminal success is honoured");
		_sut.Steps.Should().OnlyContain(s => s.State == PipelineStepState.Done,
			because: "reconciling against the manifest resolves lost stage events to done rather than stalling");
	}

	[Test]
	[Description("If the failing stage event is lost but run-completed(failure) arrives, the active step is failed and the rest skipped, reflecting the outcome (AC-ERR).")]
	public void Ingest_ShouldReconcileFailure_WhenFailureArrivesWithLostStageEvents() {
		// Arrange — a manifest, first stage running, then the failing stage event is dropped.
		_sut.Ingest(Manifest(_runId, ThreeStageManifest()));
		_sut.Ingest(Stage(_runId, "unzip", ClioStageEventContract.StageStatuses.Running));

		// Act — only the terminal failure arrives.
		_sut.Ingest(RunCompleted(_runId, ClioStageEventContract.RunOutcomes.Failure, "Failed", errorCode: "E"));

		// Assert — the active step is failed and later steps cascade to skipped.
		_sut.RunState.Should().Be(PipelineRunState.Failed, because: "the terminal failure is honoured");
		Step("unzip").State.Should().Be(PipelineStepState.Failed, because: "the active running step is failed when the failing event was lost");
		Step("deploy-app").IsSkippedAfterFailure.Should().BeTrue(because: "remaining steps still cascade to skipped after a failure");
	}

	// ---- duplicate / out-of-order tolerance ----

	[Test]
	[Description("A duplicate and an out-of-order (lower sequence) stage event are dropped so a completed step never regresses (duplicate/out-of-order tolerance).")]
	public void Ingest_ShouldNotRegressStep_WhenDuplicateOrOutOfOrderArrives() {
		// Arrange — manifest (seq 0), then unzip done at seq 5.
		_sut.Ingest(Manifest(_runId, ThreeStageManifest()));            // seq 0
		_sut.Ingest(Stage(_runId, "unzip", ClioStageEventContract.StageStatuses.Done, durationMs: 100, sequence: 5));

		// Act — a stale "running" for unzip at seq 2 (out-of-order) and a duplicate done at seq 5.
		_sut.Ingest(Stage(_runId, "unzip", ClioStageEventContract.StageStatuses.Running, sequence: 2));
		_sut.Ingest(Stage(_runId, "unzip", ClioStageEventContract.StageStatuses.Done, durationMs: 100, sequence: 5));

		// Assert — the step stays Done (no regress to Running, no double-apply).
		Step("unzip").State.Should().Be(PipelineStepState.Done,
			because: "a non-advancing sequence is dropped so a stale beat cannot regress a completed step");
	}

	// ---- unknown future stages / fields ----

	[Test]
	[Description("A stage event whose stageId is not in the manifest (an unknown/future stage) is ignored: no crash, no fabricated step, existing steps intact (unknown-stage tolerance).")]
	public void Ingest_ShouldIgnoreUnknownStage_WhenStageIdNotInManifest() {
		// Arrange — a three-stage manifest.
		_sut.Ingest(Manifest(_runId, ThreeStageManifest()));

		// Act — a stage event for a stage that was never in the manifest.
		Action act = () => _sut.Ingest(Stage(_runId, "future-stage-xyz", ClioStageEventContract.StageStatuses.Done, durationMs: 1));

		// Assert — no throw, no extra step, no corruption of the rendered list.
		act.Should().NotThrow(because: "an unknown/future stageId must be tolerated, never crash");
		_sut.Steps.Should().HaveCount(3, because: "an unknown stage is not fabricated into the list");
		_sut.Steps.Select(s => s.StageId).Should().NotContain("future-stage-xyz", because: "only manifest stages are rendered");
	}

	// ---- schemaVersion degrade gate (AC-06, owned by this story) ----

	[Test]
	[Description("An event with an unsupported (newer) schemaVersion degrades to a clear human message and does not mis-render or crash (AC-06).")]
	public void Ingest_ShouldDegradeGracefully_WhenSchemaVersionUnsupported() {
		// Arrange — a manifest stamped with a future schema version.
		var future = new ClioStageEvent(ClioStageEventContract.SchemaVersion + 1,
			ClioStageEventContract.EventTypes.Manifest, _runId, 0, Deploy, Stages: ThreeStageManifest());

		// Act.
		Action act = () => _sut.Ingest(future);

		// Assert — a human-readable warning is surfaced and no steps are built from the unknown format.
		act.Should().NotThrow(because: "an unknown schema must degrade, not crash");
		_sut.IsSchemaUnsupported.Should().BeTrue(because: "the schemaVersion gate trips on a newer format");
		_sut.UnsupportedSchemaMessage.Should().Contain("Update the ring", because: "the message tells the user how to recover")
			.And.Contain((ClioStageEventContract.SchemaVersion + 1).ToString(), because: "it names the unsupported version");
		_sut.Steps.Should().BeEmpty(because: "an unsupported event must not be mis-rendered into steps");
	}

	// ---- no cross-run leakage ----

	[Test]
	[Description("A new run (new runId + new manifest) resets the pipeline so no state leaks from the prior run (no cross-run leakage).")]
	public void Ingest_ShouldResetPipeline_WhenNewRunManifestArrives() {
		// Arrange — a completed first run.
		_sut.Ingest(Manifest(_runId, ThreeStageManifest()));
		_sut.Ingest(Stage(_runId, "unzip", ClioStageEventContract.StageStatuses.Done, durationMs: 10));
		_sut.Ingest(RunCompleted(_runId, ClioStageEventContract.RunOutcomes.Success, "First run done"));

		// Act — a brand new run begins with its own manifest.
		var secondRun = Guid.NewGuid();
		_seq = 0;
		_sut.Ingest(Manifest(secondRun,
			new ClioStageManifestEntry("stop-iis", "Stop IIS", 0, 2, false),
			new ClioStageManifestEntry("drop-db", "Drop database", 1, 2, false)));

		// Assert — the pipeline reflects only the new run.
		_sut.Steps.Select(s => s.StageId).Should().ContainInOrder(new[] { "stop-iis", "drop-db" },
			"a new manifest rebuilds the step list for the new run");
		_sut.Steps.Should().OnlyContain(s => s.State == PipelineStepState.Pending, because: "the new run starts fresh");
		_sut.RunState.Should().Be(PipelineRunState.Running, because: "the new run is in progress");
		_sut.Summary.Should().BeEmpty(because: "the prior run's summary must not leak into the new run");
	}

	[Test]
	[Description("A stage event bearing a PRIOR runId does not mutate the current run's steps (no cross-run leakage).")]
	public void Ingest_ShouldIgnorePriorRunEvent_WhenRunIdDoesNotMatchCurrent() {
		// Arrange — start run A, then start run B.
		_sut.Ingest(Manifest(_runId, ThreeStageManifest()));
		var runB = Guid.NewGuid();
		_seq = 0;
		_sut.Ingest(Manifest(runB, ThreeStageManifest()));

		// Act — a late 'done' from the superseded run A arrives.
		_sut.Ingest(Stage(_runId, "unzip", ClioStageEventContract.StageStatuses.Done, durationMs: 999, sequence: 50));

		// Assert — the current (run B) step is untouched.
		Step("unzip").State.Should().Be(PipelineStepState.Pending,
			because: "an event from a prior runId must never mutate the current run");
	}

	// ---- per-run sink scoping / disposal ----

	[Test]
	[Description("BeginRun returns a sink whose Report is a no-op once disposed, so a completed run leaves no live handler and cannot mutate the VM (handler disposal / no leak).")]
	public void BeginRun_ShouldReturnSinkThatNoOpsAfterDispose_WhenDisposed() {
		// Arrange — begin a run and dispose the sink (as a caller would in finally).
		PipelineRunSink sink = _sut.BeginRun();
		sink.Dispose();

		// Act — a beat arrives on the disposed sink.
		Action act = () => sink.Report(Manifest(_runId, ThreeStageManifest()));

		// Assert — nothing is applied to the VM.
		act.Should().NotThrow(because: "a disposed sink must swallow late beats");
		_sut.Steps.Should().BeEmpty(because: "a disposed sink is a dead handler and must not mutate the VM");
		_sut.RunState.Should().Be(PipelineRunState.Idle, because: "no event reached the VM through a dead handler");
	}

	[Test]
	[Description("Starting a new run disposes the previous sink so the superseded run's beats no longer mutate the VM (no cross-run handler leak).")]
	public void BeginRun_ShouldDisposePreviousSink_WhenNewRunBegins() {
		// Arrange — a first sink, then a second run begins.
		PipelineRunSink first = _sut.BeginRun();
		PipelineRunSink second = _sut.BeginRun();

		// Act — a beat arrives on the superseded first sink.
		first.Report(Manifest(_runId, ThreeStageManifest()));

		// Assert — the first sink is dead; only the (still-live) second sink would apply.
		_sut.Steps.Should().BeEmpty(because: "the superseded sink was disposed when the new run began");
		second.Should().NotBeSameAs(first, because: "each run gets its own scoped sink");
	}

	[Test]
	[Description("Ingesting the terminal run-completed auto-disposes the active sink so a completed run leaves no live handler even if the caller never disposes (handler disposal).")]
	public void Ingest_ShouldAutoDisposeSink_WhenRunCompletes() {
		// Arrange — a run driven through its scoped sink to a terminal state.
		PipelineRunSink sink = _sut.BeginRun();
		_sut.Ingest(Manifest(_runId, ThreeStageManifest()));
		_sut.Ingest(RunCompleted(_runId, ClioStageEventContract.RunOutcomes.Success, "Done"));
		int stepsAfterRun = _sut.Steps.Count;

		// Act — a stray beat arrives on the (now auto-disposed) sink after completion.
		var stray = Guid.NewGuid();
		_seq = 0;
		sink.Report(Manifest(stray, ThreeStageManifest()));

		// Assert — the completed run's sink is dead; the stray beat did not reset/mutate the VM.
		_sut.Steps.Count.Should().Be(stepsAfterRun, because: "the sink auto-disposed on the terminal event, so later beats are ignored");
		_sut.RunState.Should().Be(PipelineRunState.Succeeded, because: "the terminal state is preserved after the run's sink is disposed");
	}
}
