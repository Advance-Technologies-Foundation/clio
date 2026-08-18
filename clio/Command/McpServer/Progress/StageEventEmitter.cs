using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Command.McpServer.Progress;

/// <summary>
/// Describes a single stage of a run before its <c>index</c>/<c>total</c> are assigned by the emitter.
/// </summary>
/// <remarks>
/// The command owning the execution path supplies the ordered list of descriptors (A-02: the manifest
/// is built from the resolved execution path, not a hardcoded contract inside the emitter). This is a
/// data-only carrier and may be created with <c>new</c>.
/// </remarks>
/// <param name="StageId">Stable kebab-case stage key from <see cref="StageIds"/>.</param>
/// <param name="Name">Human-readable stage name surfaced to the UI.</param>
/// <param name="Conditional"><c>true</c> when the stage is inert by condition for the resolved inputs.</param>
public sealed record StageDescriptor(string StageId, string Name, bool Conditional);

/// <summary>
/// Orchestrates the typed <see cref="ClioStageEvent"/> stream for one deploy/uninstall run.
/// </summary>
/// <remarks>
/// Holds the per-run <c>runId</c> and monotonic <c>sequence</c>, builds the manifest from the descriptors
/// supplied by the command, wraps each real stage boundary with <see cref="RunStage"/> to emit
/// <c>running</c>/<c>done</c> (or <c>failed</c> + the failure cascade) transitions, and emits the terminal
/// <c>run-completed</c> event. It is the <b>single redaction boundary</b> (ADR D3): every event passes
/// through one <see cref="Emit"/> chokepoint that scrubs credentials from every string field so a stage
/// body cannot leak a secret by omission.
/// </remarks>
public interface IStageEventEmitter {

	/// <summary>
	/// Starts a run: allocates a fresh <c>runId</c>, resets the sequence, materialises the manifest from
	/// <paramref name="stages"/> (assigning zero-based <c>index</c> and <c>total</c>), and emits the
	/// <c>manifest</c> event first through <paramref name="sink"/>.
	/// </summary>
	/// <param name="operation">One of <see cref="ClioStageEventContract.Operations"/>.</param>
	/// <param name="stages">The ordered stages that will run, from the resolved execution path.</param>
	/// <param name="sink">The callback that receives every raised (redacted, sequenced) event.</param>
	void Begin(string operation, IReadOnlyList<StageDescriptor> stages, Action<ClioStageEvent> sink);

	/// <summary>
	/// Wraps one real stage: emits <c>running</c>, runs <paramref name="stage"/>, then emits <c>done</c>.
	/// If <paramref name="stage"/> throws, emits <c>failed</c>, cascades every remaining manifest stage as
	/// <c>skipped</c> (<c>after-failure</c>), emits a failure <c>run-completed</c>, then rethrows so the
	/// caller's existing control flow is unchanged.
	/// </summary>
	/// <param name="stageId">The stage key; must be present in the manifest from <see cref="Begin"/>.</param>
	/// <param name="stage">The real stage work to execute and observe.</param>
	void RunStage(string stageId, Action stage);

	/// <summary>
	/// Wraps one real stage whose underlying action reports success/failure by an exit code: emits
	/// <c>running</c>, runs <paramref name="stage"/>, then inspects its return value. A <b>zero</b> return
	/// emits <c>done</c>; a <b>non-zero</b> return is an honest failure — it emits <c>failed</c> (with the
	/// exit code as detail), cascades every remaining manifest stage as <c>skipped</c>
	/// (<c>after-failure</c>), emits a failure <c>run-completed</c>, and returns the same non-zero code so
	/// the caller can stop the run with the real exit code (it does <b>not</b> throw). If
	/// <paramref name="stage"/> throws, it behaves exactly like <see cref="RunStage(string, Action)"/>:
	/// <c>failed</c> + cascade + failure <c>run-completed</c>, then rethrows.
	/// </summary>
	/// <param name="stageId">The stage key; must be present in the manifest from <see cref="Begin"/>.</param>
	/// <param name="stage">The real stage work to execute; its return value is the stage exit code.</param>
	/// <returns>The stage exit code: <c>0</c> on success, otherwise the non-zero code the stage returned.</returns>
	int RunStage(string stageId, Func<int> stage);

	/// <summary>
	/// Emits a <c>skipped</c> transition for a stage that is inert for the resolved inputs.
	/// </summary>
	/// <param name="stageId">The stage key; must be present in the manifest from <see cref="Begin"/>.</param>
	/// <param name="skipReason">One of <see cref="ClioStageEventContract.SkipReasons"/> (e.g. <c>not-applicable</c>).</param>
	void SkipStage(string stageId, string skipReason);

	/// <summary>Emits a non-fatal warning transition without triggering the failed-stage cascade.</summary>
	/// <param name="stageId">The stage key; must be present in the current manifest.</param>
	/// <param name="message">Friendly warning message.</param>
	/// <param name="detail">Safe technical detail.</param>
	/// <param name="errorCode">Stable machine-readable warning code.</param>
	void WarnStage(string stageId, string message, string detail, string errorCode);

	/// <summary>
	/// Emits the terminal <c>run-completed</c> event with <c>outcome=success</c>.
	/// </summary>
	/// <param name="summary">Short, non-secret human-readable summary of the run.</param>
	/// <param name="derivedUrl">Optional URL derived from the run (e.g. the deployed application URL).</param>
	/// <param name="derivedPath">Optional path derived from the run (e.g. the install directory).</param>
	void CompleteSuccess(string summary, string derivedUrl = null, string derivedPath = null);

	/// <summary>Emits a successful-with-warnings terminal after one or more warning stages.</summary>
	/// <param name="summary">Short, non-secret human-readable summary of the run.</param>
	/// <param name="detail">Optional safe warning detail.</param>
	/// <param name="errorCode">Optional stable machine-readable warning code.</param>
	/// <param name="derivedPath">Optional path derived from the run.</param>
	/// <param name="derivedUrl">Optional URL derived from the run.</param>
	void CompleteSuccessWithWarnings(string summary, string detail = null, string errorCode = null,
		string derivedPath = null, string derivedUrl = null);

	/// <summary>
	/// Completes a run that failed outside an individual stage wrapper. Any manifest stages that have not
	/// already reached a terminal state are emitted as skipped before the single terminal failure event.
	/// Repeated completion calls are ignored.
	/// </summary>
	/// <param name="summary">Short, non-secret human-readable failure summary.</param>
	/// <param name="detail">Non-secret failure detail.</param>
	/// <param name="errorCode">Stable symbolic error code.</param>
	void CompleteFailure(string summary, string detail, string errorCode);
}

/// <inheritdoc cref="IStageEventEmitter" />
public sealed class StageEventEmitter : IStageEventEmitter {

	/// <summary>Stable symbolic error code emitted for a stage that threw. Never a secret or raw exception text.</summary>
	private const string StageFailedErrorCode = "stage-execution-failed";

	/// <summary>Stable symbolic error code emitted for a stage whose underlying action returned a non-zero exit code.</summary>
	private const string StageReturnedErrorCode = "stage-returned-nonzero";

	private const string RedactedToken = "[redacted]";

	/// <summary>
	/// How often a stage that is still running re-announces itself while its action executes.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>This exists because a healthy deploy does NOT stream continuously.</b> One stage produces one
	/// <c>running</c> event, then the stage's work, then its terminal status — so a legitimately long
	/// stage (a database restore, a large file copy, an installer step) emits nothing for its whole
	/// duration. The parent of a worker-executed deploy bounds the call by stage-event SILENCE
	/// (<c>McpWorkerCallDispatcher.DefaultStageEventSilenceBound</c>, ADR §3.3), so without this refresh a
	/// six-minute restore is indistinguishable from a dead child: the parent reports the run
	/// INDETERMINATE, marks the environment possibly half-installed and kills a worker that was fine —
	/// manufacturing the exact damage the protocol exists to prevent.
	/// </para>
	/// <para>
	/// <b>The relationship to that bound is enforced, not merely intended.</b> The child cannot read the
	/// parent's bound — <c>CLIO_MCP_WORKER_STAGE_SILENCE_SECONDS</c> configures the SUPERVISOR and is
	/// deliberately outside the worker's inherited-variable allowlist — so the two values cannot be
	/// derived from one another at run time. They are instead pinned by
	/// <c>StageEventLivenessHeartbeatTests.DefaultStageEventSilenceBound_ShouldBeAComfortableMultipleOfTheStageLivenessRefreshInterval</c>,
	/// which fails the build if either number moves so that a refresh no longer fits several times over
	/// inside the silence bound. On the shipped defaults it fits ten times (30 s against 300 s).
	/// </para>
	/// </remarks>
	public static readonly TimeSpan StageLivenessRefreshInterval = TimeSpan.FromSeconds(30);

	// Deny-list patterns applied to every string field at the single emission boundary. They target the
	// secret *value* portions of connection strings, credentials, and tokens while leaving non-secret
	// technical context (stage names, paths, plain URLs, symbolic codes) intact.
	private static readonly Regex[] SecretPatterns = [
		// key=value secrets in connection strings (password, pwd, user id, uid, redis password, token, secret, key)
		new(@"(?i)\b(password|pwd|user\s*id|uid|redis_password|access[_-]?token|secret|api[_-]?key)\s*=\s*[^;,\s]+",
			RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
		// bearer / auth tokens
		new(@"(?i)\bbearer\s+[A-Za-z0-9\-._~+/]+=*",
			RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
		// credentials embedded in a URL userinfo component (scheme://user:pass@host)
		new(@"(?i)://[^/\s:@]+:[^/\s:@]+@",
			RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
	];

	private readonly HashSet<string> _emitted = new(StringComparer.Ordinal);

	/// <summary>
	/// Guards the sequencing chokepoint. The in-stage liveness refresh emits from its own thread, so the
	/// counter increment AND the sink invocation are serialised here: two events sharing one
	/// <c>sequence</c> would stall ClioRing's ordered replay, which buffers until the next contiguous
	/// number arrives.
	/// </summary>
	/// <remarks>
	/// <b>It guards emission only, and that is sufficient because of where the boundary runs.</b> The
	/// refresh reaches exactly one member — <c>EmitStage</c> — and touches no other state; every mutation
	/// of <see cref="_emitted"/>, <see cref="_manifest"/>, <see cref="_completed"/> and the cascade happens
	/// on the stage's own thread. Anything added to the beat path that reads or writes those fields needs
	/// this lock too.
	/// </remarks>
	private readonly object _emitLock = new();

	/// <summary>
	/// Test seam: the in-stage liveness interval this instance runs with.
	/// </summary>
	/// <remarks>
	/// Production never sets it — <see cref="StageLivenessRefreshInterval"/> is the shipped value, and a
	/// second knob would be a second thing that can drift away from the parent's silence bound. A test
	/// scales it down so a refresh can be observed without a stage that runs for a minute.
	/// </remarks>
	internal TimeSpan LivenessRefreshInterval { get; set; } = StageLivenessRefreshInterval;

	/// <summary>
	/// Floor on how long <see cref="StopLivenessRefresh"/> waits for an in-flight beat before carrying on.
	/// </summary>
	/// <remarks>
	/// A beat has only one sink call to finish, so a second is generous for every healthy sink and short
	/// enough that a sink which is not returning cannot hold a stage open. It is a FLOOR rather than the
	/// bound itself so that a test scaling the interval down to milliseconds still joins reliably.
	/// </remarks>
	private static readonly TimeSpan MinimumLivenessJoinBound = TimeSpan.FromSeconds(1);
	private Action<ClioStageEvent> _sink;
	private string _operation = string.Empty;
	private Guid _runId;
	private int _sequence;
	private IReadOnlyList<ClioStageManifestEntry> _manifest = [];
	private bool _completed;

	/// <inheritdoc />
	public void Begin(string operation, IReadOnlyList<StageDescriptor> stages, Action<ClioStageEvent> sink) {
		ArgumentNullException.ThrowIfNull(stages);
		_sink = sink;
		_operation = operation;
		_runId = Guid.NewGuid();
		_sequence = 0;
		_completed = false;
		_emitted.Clear();

		int total = stages.Count;
		List<ClioStageManifestEntry> entries = new(total);
		for (int index = 0; index < total; index++) {
			StageDescriptor descriptor = stages[index];
			entries.Add(new ClioStageManifestEntry(descriptor.StageId, descriptor.Name, index, total,
				descriptor.Conditional));
		}

		_manifest = entries;
		Emit(new ClioStageEvent(ClioStageEventContract.SchemaVersion, ClioStageEventContract.EventTypes.Manifest,
			_runId, 0, _operation, entries));
	}

	/// <inheritdoc />
	public void RunStage(string stageId, Action stage) {
		ArgumentNullException.ThrowIfNull(stage);
		RunStage(stageId, () => {
			stage();
			return 0;
		});
	}

	/// <inheritdoc />
	public int RunStage(string stageId, Func<int> stage) {
		ArgumentNullException.ThrowIfNull(stage);
		ClioStageManifestEntry entry = Find(stageId);

		DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;
		Stopwatch stopwatch = Stopwatch.StartNew();
		EmitStage(entry, ClioStageEventContract.StageStatuses.Running, startedAtUtc: startedAtUtc,
			message: entry.Name);

		// While the stage's work runs it says nothing, and a parent that bounds a worker by stage-event
		// SILENCE cannot tell a six-minute database restore from a child that died. The refresh below is
		// what makes silence mean "this worker has stopped talking".
		using CancellationTokenSource liveness = new();
		Task refreshing = StartLivenessRefresh(entry, startedAtUtc, liveness.Token);

		int exitCode;
		try {
			exitCode = stage();
		}
		catch (Exception ex) {
			stopwatch.Stop();
			// STOPPED BEFORE THE CASCADE, not merely before the method returns: the cascade ends in the
			// run's terminal event, and a beat landing after that would report a finished run as working.
			// The `finally` below repeats this harmlessly; it cannot replace it.
			StopLivenessRefresh(liveness, refreshing);
			FailAndCascade(entry, stopwatch.ElapsedMilliseconds, ex.Message, StageFailedErrorCode);
			throw;
		}
		finally {
			StopLivenessRefresh(liveness, refreshing);
		}

		stopwatch.Stop();
		if (exitCode != 0) {
			// A non-zero exit code is a genuine stage failure that must be reported as honestly as a thrown
			// stage: the run cannot end in success just because the failing action returned instead of threw.
			FailAndCascade(entry, stopwatch.ElapsedMilliseconds, $"Stage exited with code {exitCode}",
				StageReturnedErrorCode);
			return exitCode;
		}

		_emitted.Add(entry.StageId);
		EmitStage(entry, ClioStageEventContract.StageStatuses.Done, durationMs: stopwatch.ElapsedMilliseconds,
			message: entry.Name);
		return 0;
	}

	/// <summary>
	/// Starts the in-stage liveness refresh for <paramref name="entry"/>.
	/// </summary>
	/// <param name="entry">The stage that is about to run.</param>
	/// <param name="startedAtUtc">The stage's own start, repeated verbatim on every refresh.</param>
	/// <param name="cancellationToken">Cancelled the moment the stage ends.</param>
	/// <returns>The refresh task, or <see langword="null"/> when refreshing is switched off.</returns>
	/// <remarks>
	/// The refresh is an ORDINARY <c>running</c> transition for the CURRENT stage, byte-identical to the
	/// one the stage opened with apart from its <c>sequence</c>. That is deliberate and it is the whole
	/// compatibility argument: the stage vocabulary is unchanged, no field is added, and the
	/// <c>(runId, sequence)</c> pair ClioRing correlates and orders on keeps its meaning — a refresh is
	/// simply the next event of the run.
	/// </remarks>
	private Task StartLivenessRefresh(ClioStageManifestEntry entry, DateTimeOffset startedAtUtc,
		CancellationToken cancellationToken) {
		TimeSpan interval = LivenessRefreshInterval;
		if (interval <= TimeSpan.Zero) {
			return null;
		}

		// CancellationToken.None on Task.Run rather than the stage's token: a task cancelled before its
		// body ran would surface as a faulted join instead of an orderly stop.
		return Task.Run(async () => {
			while (true) {
				try {
					await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
				}
				catch (OperationCanceledException) {
					return;
				}

				if (cancellationToken.IsCancellationRequested) {
					return;
				}

				EmitStage(entry, ClioStageEventContract.StageStatuses.Running, startedAtUtc: startedAtUtc,
					message: entry.Name);
			}
		}, CancellationToken.None);
	}

	/// <summary>
	/// Stops the refresh and waits — BRIEFLY — for it, so that in practice no beat is in flight when the
	/// stage's own terminal transition is emitted. Idempotent, because the failure path stops it before
	/// the cascade and the <c>finally</c> stops it again.
	/// </summary>
	/// <param name="liveness">The refresh's cancellation source.</param>
	/// <param name="refreshing">The refresh task, or <see langword="null"/>.</param>
	/// <remarks>
	/// <b>The wait is BOUNDED, and the weaker guarantee is the deliberate half of that trade.</b> The
	/// refresh emits through the caller's sink, which in a worker writes to a pipe; a parent that has
	/// stopped reading can leave that write outstanding, and an unbounded join here would wedge the stage
	/// thread inside a <c>finally</c> — a deploy that then produces no further stage event and no result,
	/// which is exactly the "possibly half-installed" report this whole refresh exists to prevent, re-entered
	/// through a different door. So the join gives up after <see cref="MinimumLivenessJoinBound"/> (or the
	/// refresh interval, whichever is longer) and lets the stage finish. A beat landing just after the
	/// terminal transition is a cosmetic oddity the consumer already drops — ClioRing ignores a
	/// non-advancing step transition — and is strictly cheaper than a wedged stage.
	/// </remarks>
	private void StopLivenessRefresh(CancellationTokenSource liveness, Task refreshing) {
		if (refreshing is null || liveness.IsCancellationRequested) {
			return;
		}

		liveness.Cancel();
		TimeSpan joinBound = LivenessRefreshInterval > MinimumLivenessJoinBound
			? LivenessRefreshInterval
			: MinimumLivenessJoinBound;
		try {
			// Returns false rather than throwing when the bound expires; an in-flight beat needs only to
			// finish one sink call, so reaching the bound means the sink itself is not returning.
			_ = refreshing.Wait(joinBound);
		}
		catch (AggregateException) {
			// A refresh that could not reach the sink is a progress problem, never a stage problem: it must
			// not replace the stage's own outcome (or its exception) on the way out.
		}
	}

	// Shared failure path for both the thrown-stage and non-zero-return cases: emit the active stage as
	// failed, cascade every remaining manifest stage as skipped, then emit the terminal failure run-completed.
	private void FailAndCascade(ClioStageManifestEntry entry, long durationMs, string detail, string errorCode) {
		_emitted.Add(entry.StageId);
		EmitStage(entry, ClioStageEventContract.StageStatuses.Failed, durationMs: durationMs,
			message: $"{entry.Name} failed", detail: detail, errorCode: errorCode);
		CascadeSkip(entry.Index);
		CompleteFailure($"{entry.Name} failed", detail, errorCode);
	}

	/// <inheritdoc />
	public void SkipStage(string stageId, string skipReason) {
		ClioStageManifestEntry entry = Find(stageId);
		_emitted.Add(entry.StageId);
		EmitStage(entry, ClioStageEventContract.StageStatuses.Skipped, message: $"{entry.Name} skipped",
			skipReason: skipReason);
	}

	/// <inheritdoc />
	public void WarnStage(string stageId, string message, string detail, string errorCode) {
		ClioStageManifestEntry entry = Find(stageId);
		_emitted.Add(entry.StageId);
		EmitStage(entry, ClioStageEventContract.StageStatuses.Warning, message: message, detail: detail,
			errorCode: errorCode);
	}

	/// <inheritdoc />
	public void CompleteSuccess(string summary, string derivedUrl = null, string derivedPath = null) {
		if (_completed) {
			return;
		}

		_completed = true;
		Emit(new ClioStageEvent(ClioStageEventContract.SchemaVersion, ClioStageEventContract.EventTypes.RunCompleted,
			_runId, 0, _operation,
			RunCompleted: new ClioRunCompleted(ClioStageEventContract.RunOutcomes.Success, summary,
				DerivedUrl: derivedUrl, DerivedPath: derivedPath)));
	}

	/// <inheritdoc />
	public void CompleteSuccessWithWarnings(string summary, string detail = null, string errorCode = null,
		string derivedPath = null, string derivedUrl = null) {
		if (_completed) {
			return;
		}

		_completed = true;
		Emit(new ClioStageEvent(ClioStageEventContract.SchemaVersion, ClioStageEventContract.EventTypes.RunCompleted,
			_runId, 0, _operation,
			RunCompleted: new ClioRunCompleted(ClioStageEventContract.RunOutcomes.SuccessWithWarnings, summary,
				Detail: detail, ErrorCode: errorCode, DerivedUrl: derivedUrl, DerivedPath: derivedPath)));
	}

	/// <inheritdoc />
	public void CompleteFailure(string summary, string detail, string errorCode) {
		if (_completed) {
			return;
		}

		CascadeSkip(-1);
		_completed = true;
		Emit(new ClioStageEvent(ClioStageEventContract.SchemaVersion, ClioStageEventContract.EventTypes.RunCompleted,
			_runId, 0, _operation,
			RunCompleted: new ClioRunCompleted(ClioStageEventContract.RunOutcomes.Failure, summary, Detail: detail,
				ErrorCode: errorCode)));
	}

	private void CascadeSkip(int failedIndex) {
		foreach (ClioStageManifestEntry entry in _manifest) {
			if (entry.Index > failedIndex && !_emitted.Contains(entry.StageId)) {
				_emitted.Add(entry.StageId);
				EmitStage(entry, ClioStageEventContract.StageStatuses.Skipped, message: $"{entry.Name} skipped",
					skipReason: ClioStageEventContract.SkipReasons.AfterFailure);
			}
		}
	}

	private ClioStageManifestEntry Find(string stageId) {
		foreach (ClioStageManifestEntry entry in _manifest) {
			if (string.Equals(entry.StageId, stageId, StringComparison.Ordinal)) {
				return entry;
			}
		}

		throw new InvalidOperationException(
			$"Stage '{stageId}' is not part of the current run manifest. Call Begin with a manifest that contains it.");
	}

	private void EmitStage(ClioStageManifestEntry entry, string status, DateTimeOffset? startedAtUtc = null,
		long? durationMs = null, string message = "", string detail = null, string errorCode = null,
		string skipReason = null) {
		Emit(new ClioStageEvent(ClioStageEventContract.SchemaVersion, ClioStageEventContract.EventTypes.Stage,
			_runId, 0, _operation,
			Stage: new ClioStageDetail(entry.StageId, entry.Name, entry.Index, entry.Total, status, startedAtUtc,
				durationMs, message, detail, errorCode, skipReason)));
	}

	// The single redaction + sequencing chokepoint: every event is scrubbed of secrets and stamped with the
	// next monotonic sequence before it reaches the sink. A null/absent sink makes emission a pure no-op.
	private void Emit(ClioStageEvent stageEvent) {
		// The increment and the sink invocation are inside ONE lock, not two: the sequence is what
		// ClioRing de-duplicates and orders on, and a sink invoked out of sequence order would deliver a
		// correctly numbered stream in the wrong order.
		lock (_emitLock) {
			ClioStageEvent sequenced = Redact(stageEvent) with { Sequence = _sequence++ };
			_sink?.Invoke(sequenced);
		}
	}

	private static ClioStageEvent Redact(ClioStageEvent stageEvent) {
		ClioStageDetail stage = stageEvent.Stage is null
			? null
			: stageEvent.Stage with {
				Message = RedactText(stageEvent.Stage.Message),
				Detail = RedactText(stageEvent.Stage.Detail),
				ErrorCode = RedactText(stageEvent.Stage.ErrorCode)
			};

		ClioRunCompleted runCompleted = stageEvent.RunCompleted is null
			? null
			: stageEvent.RunCompleted with {
				Summary = RedactText(stageEvent.RunCompleted.Summary),
				Detail = RedactText(stageEvent.RunCompleted.Detail),
				ErrorCode = RedactText(stageEvent.RunCompleted.ErrorCode),
				DerivedUrl = RedactText(stageEvent.RunCompleted.DerivedUrl),
				DerivedPath = RedactText(stageEvent.RunCompleted.DerivedPath)
			};

		return stageEvent with { Stage = stage, RunCompleted = runCompleted };
	}

	private static string RedactText(string value) {
		if (string.IsNullOrEmpty(value)) {
			return value;
		}

		string redacted = value;
		foreach (Regex pattern in SecretPatterns) {
			redacted = pattern.Replace(redacted, RedactedToken);
		}

		return redacted;
	}
}
