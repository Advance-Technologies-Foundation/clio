using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Progress;
using Clio.Common.McpWorker;
using ModelContextProtocol.Protocol;

namespace Clio.Command.McpServer.Relay;

/// <summary>
/// The <c>terminal-stage</c> half of the worker dispatcher: the deploy/uninstall family, bounded by the
/// worker's own authoritative <c>run-completed</c> stage event instead of by a stopwatch (ADR §3.3).
/// </summary>
public sealed partial class McpWorkerCallDispatcher {

	/// <summary>
	/// Environment variable overriding <see cref="DefaultStageEventSilenceBound"/>, in seconds (invariant
	/// culture, accepted range 0 &lt; n ≤ 3600).
	/// </summary>
	/// <remarks>
	/// Separate from <see cref="BudgetOverrideEnvVar"/> because it bounds a different thing. The budget is
	/// a TOTAL: raise it and every worker call may run longer. This is a GAP: raise it and a deploy is
	/// allowed to go quiet for longer between stages, while a healthy deploy that streams continuously is
	/// unaffected however long it takes. Tuning one to fix the other is how a deploy ends up killed at a
	/// stopwatch again.
	/// </remarks>
	internal const string StageEventSilenceOverrideEnvVar = "CLIO_MCP_WORKER_STAGE_SILENCE_SECONDS";

	/// <summary>
	/// Machine-readable error class emitted when a <c>terminal-stage</c> run produced no terminal event, so
	/// clio cannot say whether the operation completed.
	/// </summary>
	/// <remarks>
	/// <b>Deliberately NOT <see cref="BudgetExpiredErrorClass"/>.</b> That class's shipped guidance says the
	/// call is safe to retry, which for a possibly half-installed environment is the single most damaging
	/// instruction available: retry-on-ambiguity is how one half-installed environment becomes two.
	/// </remarks>
	internal const string IndeterminateErrorClass = "clio-deploy-indeterminate";

	/// <summary>
	/// The ADDITIVE <c>outcome</c> value carried by an indeterminate result. Additive so no released
	/// ClioRing breaks on it (ADR §3.3: clio and Ring are never upgraded atomically); it is a field of
	/// clio's own tool-result payload and is NOT a fourth
	/// <see cref="ClioStageEventContract.RunOutcomes"/> member, which would be a stage-event contract change.
	/// </summary>
	internal const string IndeterminateOutcome = "indeterminate";

	/// <summary>
	/// Machine-readable error class emitted when the run DID reach its terminal stage and that stage said
	/// the run failed. A definite failure, so it never carries the indeterminate class.
	/// </summary>
	internal const string TerminalFailureErrorClass = "clio-deploy-failed";

	/// <summary>Prefix of every synthetic progress token, so one is recognisable in a log or a trace.</summary>
	internal const string SyntheticProgressTokenPrefix = "clio-worker-terminal-stage-";

	/// <summary>
	/// Default stage-event SILENCE bound: how long a terminal-stage call tolerates no stage event of any
	/// kind before it treats the child as lost.
	/// </summary>
	/// <remarks>
	/// It is not an operation timer and must never be turned into one. Every stage event restarts it, so
	/// the only run it can end is one that has genuinely stopped saying anything — a healthy deploy streams
	/// continuously and may run for as long as it needs. 300 s per ADR §3.3.
	/// </remarks>
	internal static readonly TimeSpan DefaultStageEventSilenceBound = TimeSpan.FromSeconds(300);

	/// <summary>
	/// How long the parent waits after the terminal stage event for the worker to answer and exit, before
	/// killing it and answering with the terminal outcome.
	/// </summary>
	/// <remarks>
	/// Safe precisely because the operation has already terminated: a child 30 s past its own
	/// <c>run-completed</c> has nothing left to lose, and the result the caller receives is the terminal
	/// outcome rather than an error (ADR §3.3, story 8 AC-06).
	/// </remarks>
	internal static readonly TimeSpan DefaultPostTerminalExitGrace = TimeSpan.FromSeconds(30);

	/// <summary>How a terminal-stage wait ended.</summary>
	private enum TerminalStageWaitOutcome {

		/// <summary>The worker answered the tool call (successfully or by faulting the task).</summary>
		Answered,

		/// <summary>No stage event arrived within the silence bound.</summary>
		SilenceExpired,

		/// <summary>The terminal event arrived, but the worker did not answer within the exit grace.</summary>
		ExitGraceExpired
	}

	/// <summary>
	/// Parses a raw seconds override into a stage-event silence bound, falling back to
	/// <see cref="DefaultStageEventSilenceBound"/> for null / empty / non-numeric / out-of-range values.
	/// Pure, so the parse rules are testable without touching the environment.
	/// </summary>
	/// <param name="rawValue">The raw override value.</param>
	/// <returns>The resolved silence bound.</returns>
	internal static TimeSpan ResolveStageEventSilenceBound(string rawValue) {
		if (!string.IsNullOrWhiteSpace(rawValue)
			&& double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
			&& seconds > 0 && seconds <= 3600) {
			return TimeSpan.FromSeconds(seconds);
		}
		return DefaultStageEventSilenceBound;
	}

	/// <summary>
	/// Runs one call of the deploy/uninstall family in a worker, bounded by ADR §3.3 rather than by the
	/// ordinary kill budget.
	/// </summary>
	/// <param name="toolName">The canonical tool name the routing decision was made under.</param>
	/// <param name="parameters">The caller's params, relayed to the worker.</param>
	/// <param name="parentSession">The parent leg — the live session the real client is on.</param>
	/// <param name="cancellationToken">The caller's token.</param>
	/// <returns>The worker's answer, the terminal outcome, or an explicit indeterminate error.</returns>
	private async ValueTask<CallToolResult> DispatchTerminalStageAsync(
		string toolName,
		CallToolRequestParams parameters,
		IParentMcpSession parentSession,
		CancellationToken cancellationToken) {
		IReadOnlyDictionary<string, string> childEnvironment = McpWorkerEnvironment.ComposeChildEnvironment(
			ReadFrozenFeatures(), McpWorkerLifetime.PerCall);
		// The silence bound is recorded on the lease as its "budget" because it is the only fixed interval
		// this policy has. NOTHING in this path reads IWorkerLease.BudgetExpiresAtUtc: a terminal-stage call
		// has no total bound at all, and deriving one from the lease is exactly the generic kill this
		// protocol replaces.
		WorkerSpawnRequest spawnRequest = ComposeSpawnRequest(childEnvironment, _stageEventSilenceBound);

		IWorkerLease lease;
		try {
			lease = await _supervisor.SpawnContainedAsync(spawnRequest, cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) {
			throw;
		}
		catch (WorkerQueueWaitExpiredException exception) {
			// Saturation, not a defect. Same reasoning as the per-call path: "the worker process could not
			// be started" sends an agent hunting a clio bug when the host is simply at its cap, and throws
			// away the numbers R-10 promises. Round 4 fixed only the per-call branch; this is the rest of
			// the same fix.
			_logger.WriteWarning($"MCP worker for '{toolName}' was not started: {exception.Message}");
			return WorkerSaturationResult(toolName, exception);
		}
		catch (Exception exception) {
			// Nothing was spawned, so nothing was deployed: this is a plain relay failure and must NOT be
			// reported as indeterminate, which would send an operator to inspect an environment clio never
			// touched.
			_logger.WriteWarning(
				$"MCP worker for '{toolName}' could not be started: "
				+ SensitiveErrorTextRedactor.Redact(exception.Message));
			return RelayFailureResult(toolName, "the worker process could not be started", exception.Message, null);
		}

		WorkerStandardErrorDrain standardError = new(lease.StandardError, StandardErrorTailLimit);
		// Every lease consumer must drain, not only the ordinary one (ADR §3.4): a worker that fills its
		// standard-error pipe blocks on the write and goes silent, which this protocol would then correctly
		// but uselessly report as a lost child.
		standardError.Start();

		// The hole the CALLER opens, not the child: StageEventProgressForwarder.Subscribe is inert without a
		// progress token, so a client that called deploy-creatio without one would emit zero stage events and
		// a silence-bounded protocol would declare that perfectly healthy deploy indeterminate. A synthetic
		// token on the CHILD leg makes it stream; the resulting notifications are consumed at the relay
		// (TerminalStageWatch) rather than pushed at a client that opted out — ADR §3.3's one deliberate
		// exception to rule 1.
		string syntheticToken = CarriesProgressToken(parameters) ? null : NewSyntheticProgressToken();
		TerminalStageWatch watch = new(syntheticToken);
		WorkerRelayOptions relayOptions = new() { NotificationTap = watch.Observe };

		WorkerRelaySession session = null;
		Task<CallToolResult> call = null;
		CancellationTokenSource callSource = null;
		try {
			try {
				// The HANDSHAKE is bounded, and it is the only part of this call that is. A child that never
				// completes `initialize` has not started the operation, so bounding it cannot half-install
				// anything; leaving it unbounded would hang the call before the stage stream exists to bound it.
				using CancellationTokenSource handshakeSource =
					CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				handshakeSource.CancelAfter(_stageEventSilenceBound);
				ITransport childTransport = await _transportOwner
					.ConnectAsync(lease.StandardInput, lease.StandardOutput, handshakeSource.Token)
					.ConfigureAwait(false);
				session = await _relay
					.OpenAsync(childTransport, parentSession, relayOptions, handshakeSource.Token)
					.ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
				KillQuietly(lease, toolName);
				_logger.WriteWarning(
					$"MCP worker for '{toolName}' (pid {lease.ProcessId}) did not complete its handshake within "
					+ $"{FormatSeconds(_stageEventSilenceBound)} s and was killed before the operation started.");
				return RelayFailureResult(toolName,
					"the worker did not complete its MCP handshake, so the operation never started",
					detail: null, standardError.Tail());
			}

			CallToolRequestParams childParameters = WithoutParentSessionMetadata(parameters);
			if (syntheticToken is not null) {
				childParameters = WithSyntheticProgressToken(childParameters, syntheticToken);
			}
			callSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			call = session.CallToolAsync(childParameters, callSource.Token);
			TerminalStageWaitOutcome waited =
				await AwaitTerminalStageAsync(call, watch, cancellationToken).ConfigureAwait(false);
			switch (waited) {
				case TerminalStageWaitOutcome.SilenceExpired: {
					// LAST LOOK BEFORE GIVING UP. The wait decides "answered" and "expired" at two different
					// instants, and nothing re-read the call in between. A worker that stayed quiet through a
					// legitimately long stage and then answered in the microseconds between those two checks
					// would have its REAL result — possibly a success — thrown away, replaced by
					// "possibly half-installed, do not retry", and be killed. The re-check costs nothing and
					// only ever converts a discarded answer into the answer.
					if (call.IsCompleted) {
						// Break, not return: the code after this switch is the answered path, and reusing it
						// keeps one place that decides what a worker's own answer means.
						break;
					}
					// The error is composed and REPORTED FIRST and the child killed after, so the last stage it
					// reached is captured: killing first closes the pipes, ends the read loop, and the answer
					// would then be a relay failure with no stage named in it.
					TerminalStageObservation observation = watch.Snapshot();
					_logger.WriteWarning(
						$"MCP tool '{toolName}' (pid {lease.ProcessId}) emitted no stage event for "
						+ $"{FormatSeconds(_stageEventSilenceBound)} s. Last stage reached: "
						+ $"{observation.LastStageDescription}. The outcome is INDETERMINATE and clio will not "
						+ "retry it; the worker is being killed now that its last stage has been captured.");
					CallToolResult indeterminate = IndeterminateResult(toolName, observation,
						$"it emitted no stage event for {FormatSeconds(_stageEventSilenceBound)} s and clio "
						+ "stopped waiting",
						standardError.Tail());
					KillQuietly(lease, toolName);
					return indeterminate;
				}
				case TerminalStageWaitOutcome.ExitGraceExpired: {
					TerminalStageObservation observation = watch.Snapshot();
					_logger.WriteWarning(
						$"MCP tool '{toolName}' (pid {lease.ProcessId}) reported terminal outcome "
						+ $"'{observation.Outcome}' but did not answer or exit within the "
						+ $"{FormatSeconds(_postTerminalExitGrace)} s post-terminal grace, so it was killed. The "
						+ "operation itself had already terminated, so the terminal outcome is the answer.");
					CallToolResult terminal = TerminalOutcomeResult(toolName, observation,
						lease.HasExited
							? "the worker had exited without answering the call"
							: $"the worker did not answer or exit within the "
								+ $"{FormatSeconds(_postTerminalExitGrace)} s post-terminal grace and was killed",
						standardError.Tail());
					KillQuietly(lease, toolName);
					return terminal;
				}
			}

			CallToolResult result = await call.ConfigureAwait(false);
			if (result is not null) {
				// The tool itself answered. The parent does not second-guess that answer against the stage
				// stream: an authoritative result from the command that ran the operation outranks the parent's
				// inference from progress traffic.
				return result;
			}
			// A worker answering `{"result":null}` is a defect, and it is named rather than smoothed over —
			// but if the run reached its terminal stage, that stage is the honest answer to give the caller.
			_logger.WriteWarning($"MCP worker for '{toolName}' returned a null tool result.");
			TerminalStageObservation nullResultObservation = watch.Snapshot();
			return nullResultObservation.TerminalObserved
				? TerminalOutcomeResult(toolName, nullResultObservation,
					"the worker returned a null tool result after its terminal stage", standardError.Tail())
				: IndeterminateResult(toolName, nullResultObservation,
					"the worker returned a null tool result and never reported a terminal stage",
					standardError.Tail());
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
			// The CALLER gave up. Cancellation stays cancellation — it is never reported as a timeout or as a
			// result — but the state it leaves behind is still indeterminate, so the last stage reached is
			// recorded where an operator can find it before the worker is killed.
			// WHETHER THIS IS INDETERMINATE DEPENDS ON WHAT THE RUN ALREADY SAID. Cancellation can land during
			// the post-terminal exit grace — the run has reported run-completed and the child is merely slow
			// to exit — and telling an operator that a deploy which ANNOUNCED ITS OWN SUCCESS may be
			// half-installed and must not be reused is a false alarm of the worst kind: it is the mirror of
			// the failure this protocol exists to prevent, and it costs somebody an inspection or a rebuild
			// of a healthy environment.
			TerminalStageObservation observation = watch.Snapshot();
			_logger.WriteWarning(observation.TerminalObserved
				? $"MCP tool '{toolName}' (pid {lease.ProcessId}) was cancelled by its caller AFTER the run "
					+ $"reported terminal outcome '{observation.Outcome}'. The operation itself had already "
					+ "finished, so the environment is NOT indeterminate; only the answer was abandoned."
				: $"MCP tool '{toolName}' (pid {lease.ProcessId}) was cancelled by its caller before reporting "
					+ $"a terminal stage. Last stage reached: {observation.LastStageDescription}. The outcome "
					+ "is INDETERMINATE: the target environment may be half-installed and must not be reused "
					+ "without inspection.");
			KillQuietly(lease, toolName);
			throw;
		}
		catch (Exception exception) {
			// NOTHING WAS EVER ASKED OF THE WORKER. `call` is only assigned once the handshake succeeded and
			// the tools/call was dispatched, so a null here means the failure happened before any request
			// reached the child — a protocol-revision mismatch, an initialize result with no protocolVersion,
			// a broken pipe on connect. No request means nothing could have been installed, and reporting
			// "possibly half-installed, inspect the target and remove what is there" for an environment clio
			// never spoke to sends an operator to dismantle a working system. This is the same distinction
			// the spawn-failure branch above already makes; it just has to survive the handshake too.
			if (call is null) {
				_logger.WriteWarning(
					$"MCP worker for '{toolName}' (pid {lease.ProcessId}) failed before the operation was "
					+ $"requested, so nothing was deployed: "
					+ SensitiveErrorTextRedactor.Redact(exception.Message));
				CallToolResult beforeRequest = RelayFailureResult(toolName,
					"the worker failed before the operation was requested, so nothing was deployed",
					detail: exception.Message, standardError.Tail());
				KillQuietly(lease, toolName);
				return beforeRequest;
			}
			// The child crashed, was killed, or closed its pipe. Whether that is an answer or an ambiguity is
			// decided by ONE question: did the run report its terminal stage first?
			TerminalStageObservation observation = watch.Snapshot();
			if (observation.TerminalObserved) {
				_logger.WriteWarning(
					$"MCP tool '{toolName}' (pid {lease.ProcessId}) reported terminal outcome "
					+ $"'{observation.Outcome}' and then ended without answering the call: "
					+ SensitiveErrorTextRedactor.Redact(exception.Message));
				CallToolResult terminal = TerminalOutcomeResult(toolName, observation,
					"the worker ended without answering the call after its terminal stage", standardError.Tail());
				KillQuietly(lease, toolName);
				return terminal;
			}
			_logger.WriteWarning(
				$"MCP tool '{toolName}' (pid {lease.ProcessId}) ended without reporting a terminal stage. Last "
				+ $"stage reached: {observation.LastStageDescription}. The outcome is INDETERMINATE and clio "
				+ "will not retry it: "
				+ SensitiveErrorTextRedactor.Redact(exception.Message));
			CallToolResult indeterminate = IndeterminateResult(toolName, observation,
				"the worker process ended before reporting a terminal stage", standardError.Tail());
			KillQuietly(lease, toolName);
			return indeterminate;
		}
		finally {
			if (callSource is not null) {
				// Releases an awaiter left behind by an expired silence bound or exit grace, and tells the
				// worker its request was abandoned. Its failure is then OBSERVED below so a faulted task that
				// nobody awaits cannot reach the finalizer as an unobserved exception.
				await callSource.CancelAsync().ConfigureAwait(false);
				ObserveAbandoned(call);
				callSource.Dispose();
			}
			if (session is not null) {
				await session.DisposeAsync().ConfigureAwait(false);
			}
			await standardError.StopAsync().ConfigureAwait(false);
			// The ONLY thing that returns the concurrency slot, so it runs on every path — including the one
			// where KillContained already ran, where it is a second, harmless attempt at the same kill.
			lease.Dispose();
		}
	}

	/// <summary>
	/// Waits for the worker's answer under the two bounds of ADR §3.3 — neither of them an operation timer.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Before the terminal event the deadline is <c>last stage event + silence bound</c>, and every stage
	/// event MOVES IT OUT, so a deploy that keeps streaming is never truncated. After the terminal event the
	/// deadline collapses to <c>terminal + exit grace</c>; the watch signals that transition through a task
	/// rather than being polled for it, so a terminal event arriving early in a 300 s wait is acted on at once.
	/// </para>
	/// <para>
	/// The caller's own token is checked at the top of every iteration rather than being allowed to cancel
	/// the interval delay silently — a cancelled delay would otherwise complete instantly and spin this loop.
	/// </para>
	/// </remarks>
	/// <param name="call">The in-flight tool call.</param>
	/// <param name="watch">The run's stage-event observation.</param>
	/// <param name="cancellationToken">The caller's token.</param>
	/// <returns>How the wait ended.</returns>
	private async Task<TerminalStageWaitOutcome> AwaitTerminalStageAsync(
		Task<CallToolResult> call, TerminalStageWatch watch, CancellationToken cancellationToken) {
		while (true) {
			cancellationToken.ThrowIfCancellationRequested();
			if (call.IsCompleted) {
				return TerminalStageWaitOutcome.Answered;
			}
			bool terminalObserved = watch.TerminalObserved;
			TimeSpan remaining = terminalObserved
				? _postTerminalExitGrace - watch.SinceTerminal
				: _stageEventSilenceBound - watch.SinceLastStageEvent;
			if (remaining <= TimeSpan.Zero) {
				return terminalObserved
					? TerminalStageWaitOutcome.ExitGraceExpired
					: TerminalStageWaitOutcome.SilenceExpired;
			}
			using CancellationTokenSource interval =
				CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			Task expiry = Task.Delay(remaining, interval.Token);
			Task finished = terminalObserved
				? await Task.WhenAny(call, expiry).ConfigureAwait(false)
				: await Task.WhenAny(call, expiry, watch.TerminalReached).ConfigureAwait(false);
			// The timer is cancelled and then OBSERVED rather than left to expire: one live timer per
			// iteration of a 300 s wait is a leak in a path that may run for an hour.
			await interval.CancelAsync().ConfigureAwait(false);
			try {
				await expiry.ConfigureAwait(false);
			}
			catch (OperationCanceledException) {
				// Cancelling the interval is how it is stopped; awaiting it here is only how that is observed.
			}
			if (ReferenceEquals(finished, call)) {
				return TerminalStageWaitOutcome.Answered;
			}
			// Either the interval expired — and a stage event may have moved the deadline out since it was
			// computed — or the terminal event just arrived. Both are answered by recomputing.
		}
	}

	/// <summary>
	/// Observes the exception of a call abandoned by an expired bound, so a task nobody awaits cannot
	/// surface later as an unobserved exception.
	/// </summary>
	/// <param name="call">The abandoned call, or <see langword="null"/> when none was issued.</param>
	private static void ObserveAbandoned(Task<CallToolResult> call) {
		if (call is null || call.IsCompleted) {
			_ = call?.Exception;
			return;
		}
		_ = call.ContinueWith(static abandoned => _ = abandoned.Exception, CancellationToken.None,
			TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
			TaskScheduler.Default);
	}

	/// <summary>
	/// Whether the caller opted into progress, which is what decides if a synthetic token is needed.
	/// </summary>
	/// <param name="parameters">The caller's params.</param>
	/// <returns><see langword="true"/> when the caller supplied a progress token.</returns>
	internal static bool CarriesProgressToken(CallToolRequestParams parameters) =>
		parameters?.Meta?["progressToken"] is not null;

	/// <summary>Builds a fresh synthetic progress token, unique per call.</summary>
	/// <returns>The token.</returns>
	private static string NewSyntheticProgressToken() =>
		SyntheticProgressTokenPrefix + Guid.NewGuid().ToString("N");

	/// <summary>
	/// Returns the params to send to the worker with a synthetic progress token attached, so the child
	/// streams its stage events even though the caller declined progress.
	/// </summary>
	/// <remarks>
	/// A COPY, always: the caller's own <c>_meta</c> object may be shared with the request context the host
	/// still owns, and <see cref="RequestParams.ProgressToken"/> is a read-only view over
	/// <c>Meta["progressToken"]</c>, so writing the token in place would re-issue a token on the caller's
	/// own request. Every settable member is carried across for the same reason
	/// <see cref="WithoutParentSessionMetadata"/> carries them.
	/// </remarks>
	/// <param name="parameters">The params heading for the worker.</param>
	/// <param name="syntheticToken">The token to attach.</param>
	/// <returns>A copy carrying the synthetic token.</returns>
	internal static CallToolRequestParams WithSyntheticProgressToken(
		CallToolRequestParams parameters, string syntheticToken) {
		JsonObject meta = parameters?.Meta is null
			? new JsonObject()
			: parameters.Meta.DeepClone().AsObject();
		meta["progressToken"] = syntheticToken;
		return new CallToolRequestParams {
			Name = parameters?.Name,
			Arguments = parameters?.Arguments,
			InputResponses = parameters?.InputResponses,
			RequestState = parameters?.RequestState,
			Meta = meta
		};
	}

	/// <summary>
	/// Builds the result returned when a terminal-stage run produced no terminal event, so clio cannot say
	/// whether the operation completed.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The shape is constrained by the CONSUMER, not chosen freely. ClioRing classifies a no-terminal result
	/// itself (<c>InstallFormViewModel.DescribeUnstreamedFailure</c>) by reading the payload rather than
	/// trusting <c>IsError</c> alone, so this sets <c>IsError</c> AND <c>success: false</c> AND a non-empty
	/// <c>error</c> — the shape <see cref="BudgetExpiredResult"/> and <see cref="RelayFailureResult"/>
	/// already produce. Anything else lands in Ring's "outcome genuinely unknown" branch, which for a
	/// possibly half-installed environment is the wrong message.
	/// </para>
	/// <para>
	/// <c>outcome: "indeterminate"</c> is ADDITIVE: the released Ring ignores it and renders its
	/// definite-failure wording — imprecise, but safe in effect, because an operator told the install did not
	/// complete inspects the environment. A fourth Ring branch that recognises the field is owed and tracked
	/// (ADR §3.3), not assumed.
	/// </para>
	/// </remarks>
	/// <param name="toolName">Canonical tool name.</param>
	/// <param name="observation">What the run had said when the parent gave up on it.</param>
	/// <param name="reason">Why the parent stopped waiting, as a sentence fragment.</param>
	/// <param name="standardErrorTail">Bounded tail of the worker's standard error, or <c>null</c>.</param>
	/// <returns>The structured result.</returns>
	internal static CallToolResult IndeterminateResult(
		string toolName, TerminalStageObservation observation, string reason,
		WorkerStandardErrorTail standardErrorTail) {
		string text = WithStandardErrorBoundNote(
			$"MCP tool '{toolName}' never reported a terminal stage: {reason}. The last stage reached was "
			+ $"{observation.LastStageDescription}. The outcome of this operation is INDETERMINATE "
			+ $"(outcome={IndeterminateOutcome}, error-class={IndeterminateErrorClass}) — it may have "
			+ "completed, and it may have stopped part-way, so the target environment must be treated as "
			+ "POSSIBLY HALF-INSTALLED. clio did not retry it and neither should you: inspect the "
			+ "environment and remove or repair it before reusing the name.",
			standardErrorTail);
		JsonObject payload = new() {
			["success"] = false,
			["error-class"] = IndeterminateErrorClass,
			["outcome"] = IndeterminateOutcome,
			["tool"] = toolName,
			["environment-state"] = "possibly-half-installed",
			["stage-events-observed"] = observation.StageEventCount,
			["last-stage-reached"] = observation.LastStageDescription,
			["error"] = text,
			["retry-guidance"] =
				"Do NOT retry this call automatically. The operation may have partly completed, and repeating "
				+ "it against a half-installed environment turns one damaged environment into two. Inspect the "
				+ "target (site, application pool, database, files) and remove or repair what is there before "
				+ "any new attempt."
		};
		if (observation.LastStageId is not null) {
			payload["last-stage-id"] = observation.LastStageId;
		}
		if (observation.LastStageStatus is not null) {
			payload["last-stage-status"] = observation.LastStageStatus;
		}
		AttachWorkerDiagnostics(payload, standardErrorTail);
		return new CallToolResult {
			IsError = true,
			Content = [new TextContentBlock { Text = text }],
			StructuredContent = JsonSerializer.SerializeToElement(payload)
		};
	}

	/// <summary>
	/// Builds the result returned when the run DID reach its terminal stage but the worker never answered
	/// the call — it hung past the exit grace, or it ended after emitting the terminal event.
	/// </summary>
	/// <remarks>
	/// The answer is the TERMINAL OUTCOME, not an error (story 8 AC-06). The parent is not guessing: the
	/// authoritative <c>run-completed</c> event for the run's root id said how the operation ended, and a
	/// child that is merely slow to exit afterwards cannot make that less true.
	/// </remarks>
	/// <param name="toolName">Canonical tool name.</param>
	/// <param name="observation">The observed terminal outcome.</param>
	/// <param name="reason">Why the worker's own answer is missing, as a sentence fragment.</param>
	/// <param name="standardErrorTail">Bounded tail of the worker's standard error, or <c>null</c>.</param>
	/// <returns>The structured result.</returns>
	internal static CallToolResult TerminalOutcomeResult(
		string toolName, TerminalStageObservation observation, string reason,
		WorkerStandardErrorTail standardErrorTail) {
		bool failed = string.Equals(observation.Outcome, ClioStageEventContract.RunOutcomes.Failure,
			StringComparison.Ordinal);
		string summary = string.IsNullOrWhiteSpace(observation.Summary)
			? "The run reported no summary."
			: observation.Summary;
		string text = WithStandardErrorBoundNote(
			$"MCP tool '{toolName}' completed with terminal outcome '{observation.Outcome}': {summary} "
			+ $"({reason}, so this answer was composed from the run's own terminal stage event rather than "
			+ "from the worker's reply.)",
			standardErrorTail);
		JsonObject payload = new() {
			["success"] = !failed,
			["outcome"] = observation.Outcome,
			["tool"] = toolName,
			["summary"] = summary,
			["terminal-stage-synthesized"] = true,
			["last-stage-reached"] = observation.LastStageDescription,
			["stage-events-observed"] = observation.StageEventCount
		};
		if (failed) {
			payload["error-class"] = TerminalFailureErrorClass;
			payload["error"] = text;
			payload["retry-guidance"] =
				"The run reported a definite failure through its own terminal stage, so the outcome is known "
				+ "rather than ambiguous. Read the failing stage above, fix the cause, and only then retry.";
		}
		AttachWorkerDiagnostics(payload, standardErrorTail);
		return new CallToolResult {
			IsError = failed,
			Content = [new TextContentBlock { Text = text }],
			StructuredContent = JsonSerializer.SerializeToElement(payload)
		};
	}
}
