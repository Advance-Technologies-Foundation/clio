using System;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Clio.Command.McpServer.Progress;
using ModelContextProtocol.Protocol;

namespace Clio.Command.McpServer.Relay;

/// <summary>
/// What one terminal-stage run had said about itself at the moment the parent had to decide.
/// </summary>
/// <param name="TerminalObserved">
/// <see langword="true"/> when a <c>run-completed</c> event for the run's ROOT <c>runId</c> was seen.
/// </param>
/// <param name="Outcome">
/// The terminal outcome — one of <see cref="ClioStageEventContract.RunOutcomes"/> — or
/// <see langword="null"/> when none was reached.
/// </param>
/// <param name="Summary">The terminal event's own summary, or <see langword="null"/>.</param>
/// <param name="LastStageId">
/// The stage key of the last <c>stage</c> transition seen, or <see langword="null"/> when no stage started.
/// </param>
/// <param name="LastStageStatus">
/// That stage's status — one of <see cref="ClioStageEventContract.StageStatuses"/> — or
/// <see langword="null"/>.
/// </param>
/// <param name="LastStageDescription">
/// A human-readable sentence fragment naming the last stage reached. NEVER empty: when nothing was
/// observed it says so, because "the last stage reached" is the one fact an operator needs from an
/// indeterminate deploy and an empty string there reads as a missing field rather than as an answer.
/// </param>
/// <param name="StageEventCount">How many stage events of any kind the run emitted.</param>
/// <remarks>A data-only carrier, so it is a <see langword="record"/> per the DI policy.</remarks>
internal sealed record TerminalStageObservation(
	bool TerminalObserved,
	string Outcome,
	string Summary,
	string LastStageId,
	string LastStageStatus,
	string LastStageDescription,
	int StageEventCount);

/// <summary>
/// Watches one worker's stage-event stream for the authoritative terminal event, and — when the caller
/// supplied no progress token of its own — CONSUMES the synthetic-token traffic instead of letting it
/// reach a client that opted out of progress.
/// </summary>
/// <remarks>
/// <para>
/// <b>Per-call runtime state, deliberately not a DI service.</b> Like <see cref="WorkerRelaySession"/>,
/// this exists for the duration of one relayed call and carries no <c>Clio.*</c> interface: the assembly
/// interface scan in <c>BindingsModule</c> auto-registers any class that does, and a type whose whole
/// purpose is to hold one run's mutable observation could not be resolved from a container anyway.
/// </para>
/// <para>
/// <b>Terminal detection is the shipped vocabulary, not the ADR's first draft.</b> A terminal event is a
/// <c>notifications/progress</c> whose <c>_meta.clioStageEvent.eventType</c> is
/// <see cref="ClioStageEventContract.EventTypes.RunCompleted"/> and whose <c>runId</c> equals the run's
/// ROOT id — the id of the first stage event seen, which is the manifest the emitter always sends first
/// (<see cref="IStageEventEmitter.Begin"/>). A per-stage <c>failed</c> is NOT terminal: a best-effort run
/// may continue past it, which is exactly why <c>warning</c> and <c>skipped</c> exist beside it. There is
/// no <c>cancelled</c> outcome in the contract at all, so a cancelled run emits no terminal event and
/// resolves through the indeterminate path (ADR §3.3).
/// </para>
/// <para>
/// <b>Time is measured with <see cref="Stopwatch"/> rather than wall clock</b>, because both bounds this
/// feeds — the stage-event silence timer and the post-terminal exit grace — must not move when the host's
/// clock is stepped by NTP or by a daylight-saving transition in the middle of a long deploy.
/// </para>
/// </remarks>
internal sealed class TerminalStageWatch {

	private readonly string _syntheticProgressToken;
	private readonly TaskCompletionSource _terminalReached =
		new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly object _stateLock = new();

	private long _lastStageEventTimestamp = Stopwatch.GetTimestamp();
	private long _terminalTimestamp;
	private Guid? _rootRunId;
	private bool _terminalObserved;
	private string _outcome;
	private string _summary;
	private string _lastStageId;
	private string _lastStageStatus;
	private string _lastStageDescription;
	private bool _manifestObserved;
	private int _stageEventCount;

	/// <summary>
	/// Initializes a new instance of the <see cref="TerminalStageWatch"/> class.
	/// </summary>
	/// <param name="syntheticProgressToken">
	/// The token the dispatcher injected on the CHILD leg because the caller supplied none, or
	/// <see langword="null"/> when the caller's own token is in use. Notifications carrying it are
	/// consumed here and never forwarded — the single deliberate exception to ADR rule 1, taken because
	/// the only client that reaches this path is one that explicitly declined progress.
	/// </param>
	internal TerminalStageWatch(string syntheticProgressToken) =>
		_syntheticProgressToken = syntheticProgressToken;

	/// <summary>
	/// Gets a task that completes as soon as the run's terminal event is observed, so the parent can drop
	/// from the silence bound to the post-terminal exit grace without polling.
	/// </summary>
	internal Task TerminalReached => _terminalReached.Task;

	/// <summary>Gets a value indicating whether the run's terminal event has been observed.</summary>
	internal bool TerminalObserved {
		get {
			lock (_stateLock) {
				return _terminalObserved;
			}
		}
	}

	/// <summary>
	/// Gets how long it has been since ANY stage event arrived — the quantity the silence bound measures.
	/// Counted from the moment the watch was created, so a worker that never emits anything is bounded
	/// exactly like one that fell silent halfway.
	/// </summary>
	internal TimeSpan SinceLastStageEvent {
		get {
			lock (_stateLock) {
				return Stopwatch.GetElapsedTime(_lastStageEventTimestamp);
			}
		}
	}

	/// <summary>
	/// Gets how long it has been since the terminal event, or <see cref="TimeSpan.Zero"/> when there has
	/// not been one.
	/// </summary>
	internal TimeSpan SinceTerminal {
		get {
			lock (_stateLock) {
				return _terminalObserved ? Stopwatch.GetElapsedTime(_terminalTimestamp) : TimeSpan.Zero;
			}
		}
	}

	/// <summary>
	/// Observes one notification the worker sent and says whether the relay may forward it upward.
	/// </summary>
	/// <param name="notification">The worker's notification, exactly as it came off the pipe.</param>
	/// <returns>
	/// <see langword="true"/> to forward (the default, and everything ADR rule 1 covers);
	/// <see langword="false"/> only for the synthetic-token progress this watch itself asked the child to
	/// produce.
	/// </returns>
	internal bool Observe(JsonRpcNotification notification) {
		if (notification is null
			|| !string.Equals(notification.Method, NotificationMethods.ProgressNotification,
				StringComparison.Ordinal)) {
			return true;
		}
		JsonNode stageEvent = notification.Params?["_meta"]?["clioStageEvent"];
		if (stageEvent is not null) {
			RecordStageEvent(stageEvent);
		}
		return !CarriesSyntheticToken(notification.Params?["progressToken"]);
	}

	/// <summary>
	/// Takes one consistent account of what the run has said. Every field is read under the same lock,
	/// because the parent composes a result and a log line from this snapshot and the two must describe
	/// the same instant.
	/// </summary>
	/// <returns>The observation.</returns>
	internal TerminalStageObservation Snapshot() {
		lock (_stateLock) {
			return new TerminalStageObservation(
				_terminalObserved,
				_outcome,
				_summary,
				_lastStageId,
				_lastStageStatus,
				_lastStageDescription ?? DescribeAbsentStage(_manifestObserved),
				_stageEventCount);
		}
	}

	// A terminal event is only terminal if its outcome is one this contract defines. ClioRunCompleted.Outcome
	// is declared non-nullable, but System.Text.Json does not enforce that — an emitter that omits the field
	// deserialises to null, and a null is not "failure", so an unvalidated read would answer success:true for
	// a deploy whose outcome is genuinely unknown. The same holds for any token a newer or buggier emitter
	// invents. §3.3 makes "there is no cancelled outcome" load-bearing, which makes an out-of-vocabulary
	// value exactly the case that must NOT be mapped to success: it is evidence of life, not of completion,
	// so the run stays unterminated and resolves through the indeterminate path.
	private static bool IsKnownRunOutcome(string outcome) =>
		outcome is ClioStageEventContract.RunOutcomes.Success
			or ClioStageEventContract.RunOutcomes.Failure
			or ClioStageEventContract.RunOutcomes.SuccessWithWarnings;

	private static string DescribeAbsentStage(bool manifestObserved) =>
		manifestObserved
			? "none — the run announced its stage manifest but no stage ever started"
			: "none — the worker emitted no stage event at all";

	private void RecordStageEvent(JsonNode stageEventNode) {
		ClioStageEvent stageEvent;
		try {
			stageEvent = stageEventNode.Deserialize<ClioStageEvent>(ClioStageEventContract.SerializerOptions);
		}
		catch (JsonException) {
			// An unreadable envelope is still EVIDENCE OF LIFE, so it must reset the silence bound rather
			// than be discarded: a worker whose events this parent cannot parse is a version-skew defect,
			// and reporting it as "possibly half-installed" would be a far worse answer than carrying on.
			lock (_stateLock) {
				_lastStageEventTimestamp = Stopwatch.GetTimestamp();
				_stageEventCount++;
			}
			return;
		}
		if (stageEvent is null) {
			return;
		}
		bool signalTerminal = false;
		lock (_stateLock) {
			_lastStageEventTimestamp = Stopwatch.GetTimestamp();
			_stageEventCount++;
			// The FIRST stage event names the root run. The emitter sends the manifest before anything else
			// (IStageEventEmitter.Begin), so this is that manifest for every real run; taking the first event
			// whatever it is keeps a run whose manifest was lost from being unbounded.
			_rootRunId ??= stageEvent.RunId;
			switch (stageEvent.EventType) {
				case ClioStageEventContract.EventTypes.Manifest:
					_manifestObserved = true;
					break;
				case ClioStageEventContract.EventTypes.Stage when stageEvent.Stage is { } stage:
					_lastStageId = stage.StageId;
					_lastStageStatus = stage.Status;
					_lastStageDescription = Describe(stage);
					break;
				case ClioStageEventContract.EventTypes.RunCompleted
					when stageEvent.RunCompleted is { } completed && stageEvent.RunId == _rootRunId
						&& IsKnownRunOutcome(completed.Outcome):
					if (_terminalObserved) {
						// A second terminal event for the same run cannot make the first one less true, and
						// re-arming the grace would let a chatty child extend it indefinitely.
						break;
					}
					_terminalObserved = true;
					_terminalTimestamp = Stopwatch.GetTimestamp();
					_outcome = completed.Outcome;
					_summary = completed.Summary;
					signalTerminal = true;
					break;
			}
		}
		if (signalTerminal) {
			// Outside the lock: the continuation runs the parent's deadline recomputation, and a
			// continuation that ran inline under this lock would hold it across the parent's own work.
			_terminalReached.TrySetResult();
		}
	}

	private static string Describe(ClioStageDetail stage) =>
		string.Create(CultureInfo.InvariantCulture,
			$"'{stage.StageId}' ({stage.Name}, stage {stage.Index + 1} of {stage.Total}, status {stage.Status})");

	private bool CarriesSyntheticToken(JsonNode progressToken) {
		if (_syntheticProgressToken is null || progressToken is null) {
			return false;
		}
		// Compared as the STRING the dispatcher injected, and only when the wire value is a string: a
		// numeric token is necessarily the caller's own, because the synthetic one is always a string.
		return progressToken is JsonValue value
			&& value.TryGetValue(out string text)
			&& string.Equals(text, _syntheticProgressToken, StringComparison.Ordinal);
	}
}
