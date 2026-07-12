using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using ClioLauncher.Ipc;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClioLauncher.ViewModels;

/// <summary>Terminal / interim state of a whole deploy or uninstall run, driving the pipeline header.</summary>
public enum PipelineRunState {
	/// <summary>No run yet — the pipeline is empty.</summary>
	Idle,

	/// <summary>A run is in progress (a manifest has been received).</summary>
	Running,

	/// <summary>The run finished successfully (<c>run-completed outcome=success</c>).</summary>
	Succeeded,

	/// <summary>The run finished with a failure (<c>run-completed outcome=failure</c>).</summary>
	Failed,

	/// <summary>The run was cancelled (defensive — not in the wire contract, tolerated if emitted).</summary>
	Cancelled
}

/// <summary>
/// The GitHub-Actions-style pipeline view-model for a Creatio deploy or uninstall (ADR D5, story 7). It is
/// <b>operation-agnostic</b>: the ordered <see cref="Steps"/> are built entirely from the typed <c>manifest</c>
/// event (never fabricated), each step is transitioned by <c>stage</c> events, and the terminal header is
/// rendered from <c>run-completed</c>. The VM consumes the story-6 <see cref="IProgress{ClioStageEvent}"/>
/// stream: <see cref="BeginRun"/> hands back a per-run sink that marshals each decoded event onto the UI
/// thread and into <see cref="Ingest"/>; the sink is scoped to the run and disposed on the terminal event
/// (or when a new run begins) so a completed run never leaves a live handler.
/// </summary>
/// <remarks>
/// Robustness invariants (mirroring the adapter's tolerance so the VM never assumes a clean stream):
/// <list type="bullet">
/// <item>Duplicate / out-of-order <c>sequence</c> for the current run is dropped (never regresses a step).</item>
/// <item>A stage event whose <c>stageId</c> is not in the manifest (an unknown/future stage) is ignored, not fabricated.</item>
/// <item>Events bearing a prior/foreign <c>runId</c> never mutate the current run; a new manifest resets the pipeline.</item>
/// <item>An event with an unsupported <c>schemaVersion</c> (≠1) degrades to a clear human message instead of mis-rendering.</item>
/// <item>If intermediate <c>stage</c> events are lost, <c>run-completed</c> reconciles the steps against the manifest rather than stalling.</item>
/// </list>
/// </remarks>
public sealed partial class DeployPipelineViewModel : ViewModelBase {
	/// <summary>Headless debugger seam: ingest on the reporting thread when no Avalonia UI loop exists.</summary>
	public bool SynchronousIngestion { get; private set; }

	/// <summary>Enables synchronous ingestion for a headless debugger harness with no Avalonia UI loop.</summary>
	public void EnableSynchronousIngestionForHarness() => SynchronousIngestion = true;
	private readonly Dictionary<string, PipelineStepViewModel> _stepsById = new(StringComparer.Ordinal);
	private Guid? _currentRunId;
	private int _maxSequence = -1;
	private RunSink? _activeSink;

	/// <summary>The ordered pipeline steps, built from the manifest (empty until the manifest arrives).</summary>
	public ObservableCollection<PipelineStepViewModel> Steps { get; } = new();

	/// <summary>
	/// Raised for every non-null <see cref="ClioStageEvent"/> the moment it arrives at <see cref="Ingest"/>
	/// — before any de-dup / guard — so a side-observer sees <b>literally the wire stream the UI renders</b>
	/// (story 10, ADR D6). The deployment receipt subscribes to this to append the on-disk NDJSON, guaranteeing
	/// the receipt and the UI cannot disagree (they are the same stream, not a second derivation). Observers
	/// must never throw; the receipt sink is best-effort by contract.
	/// </summary>
	public event Action<ClioStageEvent>? StageEventObserved;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(RunStateLabel))]
	[NotifyPropertyChangedFor(nameof(IsRunning))]
	[NotifyPropertyChangedFor(nameof(IsSucceeded))]
	[NotifyPropertyChangedFor(nameof(IsFailed))]
	[NotifyPropertyChangedFor(nameof(HasTerminalOutcome))]
	private PipelineRunState _runState = PipelineRunState.Idle;

	[ObservableProperty]
	private string _operation = string.Empty;

	[ObservableProperty]
	private string _title = "Pipeline";

	[ObservableProperty]
	private string _summary = string.Empty;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasTerminalDetail))]
	private string? _terminalDetail;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasErrorCode))]
	private string? _errorCode;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasDerivedUrl))]
	private string? _derivedUrl;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasDerivedPath))]
	private string? _derivedPath;

	/// <summary>Set when an event carries an unsupported schema version; drives the "update the ring" banner (AC-06).</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsSchemaUnsupported))]
	private string? _unsupportedSchemaMessage;

	/// <summary>Whether the pipeline has any steps yet (a manifest has been rendered).</summary>
	public bool HasSteps => Steps.Count > 0;

	/// <summary>True while the run is in progress.</summary>
	public bool IsRunning => RunState == PipelineRunState.Running;

	/// <summary>True once the run has succeeded.</summary>
	public bool IsSucceeded => RunState == PipelineRunState.Succeeded;

	/// <summary>True once the run has failed.</summary>
	public bool IsFailed => RunState == PipelineRunState.Failed;

	/// <summary>Whether the run has reached any terminal outcome (success / failure / cancelled).</summary>
	public bool HasTerminalOutcome =>
		RunState is PipelineRunState.Succeeded or PipelineRunState.Failed or PipelineRunState.Cancelled;

	/// <summary>Whether a derived URL is available on success (drives the "Open" affordance).</summary>
	public bool HasDerivedUrl => !string.IsNullOrEmpty(DerivedUrl);

	/// <summary>Whether a derived filesystem path is available.</summary>
	public bool HasDerivedPath => !string.IsNullOrEmpty(DerivedPath);

	/// <summary>Whether a terminal technical detail is present (behind the failure expander).</summary>
	public bool HasTerminalDetail => !string.IsNullOrEmpty(TerminalDetail);

	/// <summary>Whether a terminal error code is present.</summary>
	public bool HasErrorCode => !string.IsNullOrEmpty(ErrorCode);

	/// <summary>Whether the stream carried an unsupported schema version (render the degrade banner instead of steps).</summary>
	public bool IsSchemaUnsupported => !string.IsNullOrEmpty(UnsupportedSchemaMessage);

	/// <summary>Header label for the run-state badge.</summary>
	public string RunStateLabel => RunState switch {
		PipelineRunState.Running => "RUNNING",
		PipelineRunState.Succeeded => "SUCCEEDED",
		PipelineRunState.Failed => "FAILED",
		PipelineRunState.Cancelled => "CANCELLED",
		_ => string.Empty
	};

	/// <summary>
	/// Starts a fresh run: disposes any previous sink, resets the pipeline, and returns a per-run sink to
	/// hand to the story-6 <c>CallToolAsync(…, IProgress&lt;ClioStageEvent&gt;)</c> overload. The returned
	/// object is also <see cref="IDisposable"/> — the caller may dispose it in a <c>finally</c>; it is
	/// additionally auto-disposed when the terminal <c>run-completed</c> event is ingested.
	/// </summary>
	public PipelineRunSink BeginRun() {
		_activeSink?.Dispose();
		Reset();
		var sink = new RunSink(this);
		_activeSink = sink;
		return sink;
	}

	/// <summary>
	/// Applies a single typed stage event to the pipeline. This is the whole state machine and is fully
	/// synchronous — unit tests feed synthetic events here directly; the live path routes through the
	/// per-run sink (which marshals to the UI thread first). Never throws on a hostile/degenerate event.
	/// </summary>
	public void Ingest(ClioStageEvent? stageEvent) {
		if (stageEvent is null) {
			return;
		}

		// Fan the raw event out to any side-observer (the receipt) FIRST — this is the authoritative wire
		// stream as it arrives, before de-dup/guards, so the on-disk NDJSON is literally what drove the UI.
		StageEventObserved?.Invoke(stageEvent);

		// AC-06 schema gate: an unknown/newer schema is surfaced as a human message, never mis-rendered.
		if (stageEvent.SchemaVersion != ClioStageEventContract.SchemaVersion) {
			UnsupportedSchemaMessage =
				$"This clio speaks a newer stage-event format (schemaVersion {stageEvent.SchemaVersion}; " +
				$"this ring supports {ClioStageEventContract.SchemaVersion}). Update the ring to view this run.";
			return;
		}

		bool isManifest = stageEvent.EventType == ClioStageEventContract.EventTypes.Manifest;

		// A manifest for a different run starts a new pipeline (reset). Same-run duplicates fall through to
		// the sequence guard below and are dropped there.
		if (isManifest && _currentRunId != stageEvent.RunId) {
			Reset();
			_currentRunId = stageEvent.RunId;
		}

		// No current run yet and this is not a manifest → cannot be mapped to a step; ignore.
		if (_currentRunId is null) {
			return;
		}

		// Cross-run leakage guard: once a run is current, events from any other run are ignored.
		if (stageEvent.RunId != _currentRunId.Value) {
			return;
		}

		// De-dup + out-of-order drop for the current run (do not regress a step on a stale beat).
		if (stageEvent.Sequence <= _maxSequence) {
			return;
		}
		_maxSequence = stageEvent.Sequence;

		switch (stageEvent.EventType) {
			case ClioStageEventContract.EventTypes.Manifest:
				ApplyManifest(stageEvent);
				break;
			case ClioStageEventContract.EventTypes.Stage:
				ApplyStage(stageEvent.Stage);
				break;
			case ClioStageEventContract.EventTypes.RunCompleted:
				ApplyRunCompleted(stageEvent.RunCompleted);
				break;
			default:
				// Unknown event type from a future clio: tolerate, do not mutate.
				break;
		}
	}

	private void ApplyManifest(ClioStageEvent stageEvent) {
		Steps.Clear();
		_stepsById.Clear();
		Operation = stageEvent.Operation;
		Title = string.Equals(stageEvent.Operation, ClioStageEventContract.Operations.Uninstall, StringComparison.Ordinal)
			? "Uninstall Creatio"
			: "Deploy Creatio";

		if (stageEvent.Stages is not null) {
			foreach (ClioStageManifestEntry entry in stageEvent.Stages) {
				var step = new PipelineStepViewModel(entry);
				Steps.Add(step);
				// Last writer wins on a duplicate id (should not happen); keeps the map non-throwing.
				_stepsById[entry.StageId] = step;
			}
		}

		OnPropertyChanged(nameof(HasSteps));
		RunState = PipelineRunState.Running;
	}

	private void ApplyStage(ClioStageDetail? stage) {
		if (stage is null) {
			return;
		}

		// Unknown/future stageId not present in the manifest → tolerate silently (never fabricate a step).
		if (!_stepsById.TryGetValue(stage.StageId, out PipelineStepViewModel? step)) {
			return;
		}

		switch (stage.Status) {
			case ClioStageEventContract.StageStatuses.Running:
				step.MarkRunning(stage.Message);
				break;
			case ClioStageEventContract.StageStatuses.Done:
				step.MarkDone(stage.Message, stage.DurationMs, stage.Detail);
				break;
			case ClioStageEventContract.StageStatuses.Failed:
				step.MarkFailed(stage.Message, stage.DurationMs, stage.Detail, stage.ErrorCode);
				// AC-03: on a failure the remaining steps cascade to skipped(after-failure). The emitter also
				// sends explicit skipped events; applying the cascade here first is idempotent with those.
				CascadeAfterFailure();
				break;
			case ClioStageEventContract.StageStatuses.Skipped:
				step.MarkSkipped(
					stage.SkipReason ?? ClioStageEventContract.SkipReasons.NotApplicable,
					stage.Message);
				break;
			default:
				// Unknown status from a future clio: tolerate, leave the step unchanged.
				break;
		}
	}

	private void ApplyRunCompleted(ClioRunCompleted? completed) {
		if (completed is null) {
			return;
		}

		Summary = completed.Summary;
		TerminalDetail = completed.Detail;
		ErrorCode = completed.ErrorCode;
		DerivedUrl = completed.DerivedUrl;
		DerivedPath = completed.DerivedPath;

		switch (completed.Outcome) {
			case ClioStageEventContract.RunOutcomes.Success:
				// Reconcile (AC-ERR): if intermediate 'done' events were lost, do not stall — any step still
				// pending/running on a successful run is treated as done.
				foreach (PipelineStepViewModel step in Steps) {
					if (step.State is PipelineStepState.Pending or PipelineStepState.Running) {
						step.MarkDone(step.Message, step.DurationMs, step.Detail);
					}
				}
				RunState = PipelineRunState.Succeeded;
				break;

			case ClioStageEventContract.RunOutcomes.Failure:
				// Reconcile a failure even when the failing 'stage' event was lost: if nothing is marked
				// failed yet, fail the active (running) step so the pipeline reflects the outcome.
				if (!AnyFailed()) {
					PipelineStepViewModel? active = FirstInState(PipelineStepState.Running)
						?? FirstInState(PipelineStepState.Pending);
					active?.MarkFailed(completed.Summary, active.DurationMs, completed.Detail, completed.ErrorCode);
				}
				CascadeAfterFailure();
				RunState = PipelineRunState.Failed;
				break;

			default:
				// Defensive: a "cancelled" (or any non-standard) terminal outcome — surface it, leave the
				// step list as-is (do not fabricate a failure cascade for a cancel).
				RunState = string.Equals(completed.Outcome, "cancelled", StringComparison.OrdinalIgnoreCase)
					? PipelineRunState.Cancelled
					: PipelineRunState.Failed;
				break;
		}

		// The run reached a terminal state: scope the sink to this run so no live handler outlives it.
		_activeSink?.Dispose();
		_activeSink = null;
	}

	private void CascadeAfterFailure() {
		foreach (PipelineStepViewModel step in Steps) {
			if (step.State is PipelineStepState.Pending or PipelineStepState.Running) {
				step.MarkSkipped(ClioStageEventContract.SkipReasons.AfterFailure, "Skipped after a previous stage failed");
			}
		}
	}

	private bool AnyFailed() {
		foreach (PipelineStepViewModel step in Steps) {
			if (step.State == PipelineStepState.Failed) {
				return true;
			}
		}
		return false;
	}

	private PipelineStepViewModel? FirstInState(PipelineStepState state) {
		foreach (PipelineStepViewModel step in Steps) {
			if (step.State == state) {
				return step;
			}
		}
		return null;
	}

	private void Reset() {
		Steps.Clear();
		_stepsById.Clear();
		_currentRunId = null;
		_maxSequence = -1;
		Operation = string.Empty;
		Summary = string.Empty;
		TerminalDetail = null;
		ErrorCode = null;
		DerivedUrl = null;
		DerivedPath = null;
		UnsupportedSchemaMessage = null;
		RunState = PipelineRunState.Idle;
		OnPropertyChanged(nameof(HasSteps));
	}

	/// <summary>Opens the derived application URL in the OS default browser (best-effort, success path only).</summary>
	[RelayCommand]
	private void OpenDerivedUrl() {
		string? url = DerivedUrl;
		if (string.IsNullOrEmpty(url)) {
			return;
		}
		try {
			System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
		}
		catch (System.ComponentModel.Win32Exception) {
			// No shell handler / blocked — silently ignore; the URL is still shown as text.
		}
		catch (System.PlatformNotSupportedException) {
			// Non-desktop platform — ignore.
		}
	}

	// ---- design/screenshot seams (no live clio) ----

	/// <summary>
	/// Design seam: drive the VM with a realistic deploy run (manifest → a few stage transitions → a
	/// terminal outcome) so the view can be screenshotted/soaked without a live clio.
	/// </summary>
	/// <param name="succeed">When true, ends on a success terminal; when false, fails mid-way with a cascade.</param>
	public void DesignPopulate(bool succeed = true) {
		Reset();
		var runId = Guid.NewGuid();
		int seq = 0;

		var manifest = new[] {
			new ClioStageManifestEntry("stage-build", "Build source", 0, 8, true),
			new ClioStageManifestEntry("unzip", "Unzip distribution", 1, 8, false),
			new ClioStageManifestEntry("copy-files", "Copy files", 2, 8, false),
			new ClioStageManifestEntry("restore-db", "Restore database", 3, 8, false),
			new ClioStageManifestEntry("deploy-app", "Deploy application", 4, 8, false),
			new ClioStageManifestEntry("configure-conn-strings", "Configure connection strings", 5, 8, false),
			new ClioStageManifestEntry("register-env", "Register environment", 6, 8, false),
			new ClioStageManifestEntry("wait-ready", "Wait until ready", 7, 8, false)
		};

		Ingest(new ClioStageEvent(1, ClioStageEventContract.EventTypes.Manifest, runId, seq++,
			ClioStageEventContract.Operations.Deploy, Stages: manifest));

		// stage-build is inert for a non-network source → skipped(not-applicable).
		Ingest(Stage(runId, seq++, "stage-build", "Build source", 0, ClioStageEventContract.StageStatuses.Skipped,
			skipReason: ClioStageEventContract.SkipReasons.NotApplicable, message: "Source is not a network build"));
		Ingest(Stage(runId, seq++, "unzip", "Unzip distribution", 1, ClioStageEventContract.StageStatuses.Done, durationMs: 3200, message: "Unpacked 1.2 GB"));
		Ingest(Stage(runId, seq++, "copy-files", "Copy files", 2, ClioStageEventContract.StageStatuses.Done, durationMs: 5400, message: "Copied application files"));

		if (succeed) {
			Ingest(Stage(runId, seq++, "restore-db", "Restore database", 3, ClioStageEventContract.StageStatuses.Done, durationMs: 42123, message: "Database restored"));
			Ingest(Stage(runId, seq++, "deploy-app", "Deploy application", 4, ClioStageEventContract.StageStatuses.Running, message: "Deploying application…"));
			Ingest(new ClioStageEvent(1, ClioStageEventContract.EventTypes.RunCompleted, runId, seq++,
				ClioStageEventContract.Operations.Deploy,
				RunCompleted: new ClioRunCompleted(ClioStageEventContract.RunOutcomes.Success, "Deployment completed",
					DerivedUrl: "http://localhost:40000/0", DerivedPath: @"C:\inetpub\wwwroot\creatio")));
		}
		else {
			Ingest(Stage(runId, seq++, "restore-db", "Restore database", 3, ClioStageEventContract.StageStatuses.Failed,
				durationMs: 8800, message: "Could not restore the database backup",
				detail: "pg_restore exited with code 1: role \"creatio\" does not exist", errorCode: "DB_RESTORE_FAILED"));
			Ingest(new ClioStageEvent(1, ClioStageEventContract.EventTypes.RunCompleted, runId, seq++,
				ClioStageEventContract.Operations.Deploy,
				RunCompleted: new ClioRunCompleted(ClioStageEventContract.RunOutcomes.Failure,
					"Deployment failed while restoring the database. Fix the database role, then retry.",
					Detail: "Stage 'restore-db' failed after 8.8s.", ErrorCode: "DB_RESTORE_FAILED")));
		}
	}

	private static ClioStageEvent Stage(Guid runId, int sequence, string stageId, string name, int index,
		string status, long? durationMs = null, string? message = null, string? detail = null,
		string? errorCode = null, string? skipReason = null) =>
		new(1, ClioStageEventContract.EventTypes.Stage, runId, sequence, ClioStageEventContract.Operations.Deploy,
			Stage: new ClioStageDetail(stageId, name, index, 8, status,
				DurationMs: durationMs, Message: message ?? name, Detail: detail, ErrorCode: errorCode, SkipReason: skipReason));

	/// <summary>
	/// A per-run sink that forwards decoded <see cref="ClioStageEvent"/>s from the story-6 stream into the
	/// VM on the UI thread. Disposing it detaches the VM so any further beat is a no-op — this is what keeps
	/// a completed run from leaving a live handler, and stops a superseded run mutating a newer one.
	/// </summary>
	private sealed class RunSink : PipelineRunSink {
		private DeployPipelineViewModel? _owner;
		private readonly object _reportLock = new();

		public RunSink(DeployPipelineViewModel owner) => _owner = owner;

		public override void Report(ClioStageEvent value) {
			lock (_reportLock) {
				DeployPipelineViewModel? owner = _owner;
				if (owner is null) {
					return; // disposed → dead handler, no leak, no cross-run mutation
				}
				if (owner.SynchronousIngestion || Dispatcher.UIThread.CheckAccess()) {
					owner.Ingest(value);
				}
				else {
					Dispatcher.UIThread.Post(() => {
						lock (_reportLock) {
							_owner?.Ingest(value);
						}
					});
				}
			}
		}

		public override void Dispose() {
			lock (_reportLock) {
				_owner = null;
			}
		}
	}
}

/// <summary>
/// A disposable progress sink for one pipeline run. Handed to the story-6
/// <c>CallToolAsync(…, IProgress&lt;ClioStageEvent&gt;)</c> overload; scoped to a single run and disposed
/// when the run reaches its terminal event or a new run begins.
/// </summary>
public abstract class PipelineRunSink : IProgress<ClioStageEvent>, IDisposable {
	/// <inheritdoc />
	public abstract void Report(ClioStageEvent value);

	/// <inheritdoc />
	public abstract void Dispose();
}
