using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;

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

	/// <summary>
	/// Emits the terminal <c>run-completed</c> event with <c>outcome=success</c>.
	/// </summary>
	/// <param name="summary">Short, non-secret human-readable summary of the run.</param>
	/// <param name="derivedUrl">Optional URL derived from the run (e.g. the deployed application URL).</param>
	/// <param name="derivedPath">Optional path derived from the run (e.g. the install directory).</param>
	void CompleteSuccess(string summary, string derivedUrl = null, string derivedPath = null);
}

/// <inheritdoc cref="IStageEventEmitter" />
public sealed class StageEventEmitter : IStageEventEmitter {

	/// <summary>Stable symbolic error code emitted for a stage that threw. Never a secret or raw exception text.</summary>
	private const string StageFailedErrorCode = "stage-execution-failed";

	/// <summary>Stable symbolic error code emitted for a stage whose underlying action returned a non-zero exit code.</summary>
	private const string StageReturnedErrorCode = "stage-returned-nonzero";

	private const string RedactedToken = "[redacted]";

	// Deny-list patterns applied to every string field at the single emission boundary. They target the
	// secret *value* portions of connection strings, credentials, and tokens while leaving non-secret
	// technical context (stage names, paths, plain URLs, symbolic codes) intact.
	private static readonly Regex[] SecretPatterns = [
		// key=value secrets in connection strings (password, pwd, user id, uid, redis password, token, secret, key)
		new(@"(?i)\b(password|pwd|user\s*id|uid|redis_password|access[_-]?token|secret|api[_-]?key)\s*=\s*[^;,\s]+",
			RegexOptions.Compiled | RegexOptions.CultureInvariant),
		// bearer / auth tokens
		new(@"(?i)\bbearer\s+[A-Za-z0-9\-._~+/]+=*",
			RegexOptions.Compiled | RegexOptions.CultureInvariant),
		// credentials embedded in a URL userinfo component (scheme://user:pass@host)
		new(@"(?i)://[^/\s:@]+:[^/\s:@]+@",
			RegexOptions.Compiled | RegexOptions.CultureInvariant)
	];

	private readonly HashSet<string> _emitted = new(StringComparer.Ordinal);
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

		int exitCode;
		try {
			exitCode = stage();
		}
		catch (Exception ex) {
			stopwatch.Stop();
			FailAndCascade(entry, stopwatch.ElapsedMilliseconds, ex.Message, StageFailedErrorCode);
			throw;
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

	private void CompleteFailure(string summary, string detail, string errorCode) {
		if (_completed) {
			return;
		}

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
		ClioStageEvent sequenced = Redact(stageEvent) with { Sequence = _sequence++ };
		_sink?.Invoke(sequenced);
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
