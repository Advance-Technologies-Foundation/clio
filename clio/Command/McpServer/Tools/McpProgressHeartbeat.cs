using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Keeps a long-running MCP tool call alive by emitting periodic
/// <c>notifications/progress</c> while a synchronous backend operation runs.
/// </summary>
/// <remarks>
/// MCP clients reset their inactivity timeout whenever a progress notification carrying the
/// request's <see cref="ProgressToken"/> arrives. Application tools call the Creatio backend
/// synchronously and, without heartbeats, stay silent on the wire — so clients time out
/// (the "still running after 240 seconds" message) while the server is still working. This
/// helper bridges that gap without changing the AI-facing contract: the tool still returns
/// its final structured result synchronously. See
/// <c>spec/adr/adr-mcp-progress-heartbeat.md</c> (ENG-91274) and the existing
/// <c>StartTool</c> progress pattern.
/// <para>
/// The helper is a stateless utility (same shape as <see cref="McpLogNotifier"/> and
/// <see cref="McpToolExecutionLock"/>); it carries no injected behavior dependencies, so it
/// does not require DI registration.
/// </para>
/// </remarks>
internal static class McpProgressHeartbeat {

	/// <summary>
	/// Environment variable that overrides <see cref="DefaultInterval"/>, expressed in seconds
	/// (invariant culture, accepted range 0 &lt; n ≤ 600). This is an operational / testing lever:
	/// for example E2E tests set a small value so a single backend round-trip deterministically
	/// produces at least one beat. Invalid or out-of-range values fall back to the 15 s default.
	/// </summary>
	internal const string IntervalOverrideEnvVar = "CLIO_MCP_HEARTBEAT_INTERVAL_SECONDS";

	/// <summary>
	/// Default cadence between heartbeats. Chosen below common MCP client inactivity
	/// thresholds (30 s / 240 s) so a beat always lands before the client gives up.
	/// Overridable via <see cref="IntervalOverrideEnvVar"/>.
	/// </summary>
	internal static readonly TimeSpan DefaultInterval = ResolveDefaultInterval();

	private static TimeSpan ResolveDefaultInterval() {
		string raw = Environment.GetEnvironmentVariable(IntervalOverrideEnvVar);
		if (!string.IsNullOrWhiteSpace(raw)
			&& double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
			&& seconds > 0 && seconds <= 600) {
			return TimeSpan.FromSeconds(seconds);
		}

		return TimeSpan.FromSeconds(15);
	}

	/// <summary>
	/// Environment variable that overrides <see cref="DefaultResponseDeadline"/>, expressed in
	/// seconds (invariant culture, accepted range 0 &lt; n ≤ 600). Lets operators tune the response
	/// deadline to a client's hard request ceiling (for example a larger value for a client that
	/// permits longer calls). Invalid or out-of-range values fall back to the 150 s default.
	/// </summary>
	internal const string ResponseDeadlineOverrideEnvVar = "CLIO_MCP_RESPONSE_DEADLINE_SECONDS";

	/// <summary>
	/// Default wall-clock budget for the whole MCP <em>response</em> on long-running create tools.
	/// Chosen below GitHub Copilot CLI's hard ~180 s per-request ceiling (which, unlike an
	/// inactivity timeout, <em>is not</em> reset by <c>notifications/progress</c> — see
	/// <c>spec/adr/adr-create-app-section-response-deadline.md</c>, ENG-91316). When the backend
	/// work exceeds this budget the tool returns an actionable "in-progress, poll" envelope before
	/// the client gives up with <c>-32001 Request timed out</c>, while the work keeps running on the
	/// long-lived server. Overridable via <see cref="ResponseDeadlineOverrideEnvVar"/>.
	/// </summary>
	internal static readonly TimeSpan DefaultResponseDeadline = ResolveDefaultResponseDeadline();

	private static TimeSpan ResolveDefaultResponseDeadline() {
		string raw = Environment.GetEnvironmentVariable(ResponseDeadlineOverrideEnvVar);
		if (!string.IsNullOrWhiteSpace(raw)
			&& double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
			&& seconds > 0 && seconds <= 600) {
			return TimeSpan.FromSeconds(seconds);
		}

		return TimeSpan.FromSeconds(150);
	}

	/// <summary>
	/// Runs <paramref name="work"/> synchronously while emitting <c>notifications/progress</c>
	/// for the current request every <paramref name="interval"/>. When the caller did not send
	/// a <paramref name="progressToken"/> (or <paramref name="server"/> is <see langword="null"/>),
	/// <paramref name="work"/> runs inline with no heartbeats — preserving the exact behavior
	/// clients that do not request progress see today.
	/// </summary>
	/// <typeparam name="TResult">The synchronous result type produced by <paramref name="work"/>.</typeparam>
	/// <param name="server">The active MCP server used to send progress notifications.</param>
	/// <param name="progressToken">The progress token from the current request, or <see langword="null"/>.</param>
	/// <param name="operationName">Human-readable operation label used in the beat message (e.g. the tool name).</param>
	/// <param name="work">The synchronous backend operation to execute.</param>
	/// <param name="cancellationToken">Stops the heartbeat when the request is cancelled or the server shuts down.</param>
	/// <param name="interval">Beat cadence; defaults to <see cref="DefaultInterval"/>.</param>
	/// <returns>The value returned by <paramref name="work"/>.</returns>
	internal static Task<TResult> RunWithProgressAsync<TResult>(
		ModelContextProtocol.Server.McpServer server,
		ProgressToken? progressToken,
		string operationName,
		Func<TResult> work,
		CancellationToken cancellationToken = default,
		TimeSpan? interval = null) {
		ArgumentNullException.ThrowIfNull(work);
		// The explicit `Action<string>` parameter type is load-bearing: it forces overload resolution
		// to the reporter overload below. Simplifying it to `() => work()` would rebind to THIS SAME
		// Func<TResult> overload and recurse forever — a runtime stack overflow, NOT a compile error,
		// because both signatures are valid targets for a parameterless lambda. On the no-marker path
		// the ProgressChannel counter increments like the old per-beat counter, so this is behavior-preserving.
		return RunWithProgressAsync(
			server, progressToken, operationName, (Action<string> _) => work(), cancellationToken, interval);
	}

	/// <summary>
	/// Reporter-aware overload of <see cref="RunWithProgressAsync{TResult}(ModelContextProtocol.Server.McpServer, ProgressToken?, string, Func{TResult}, CancellationToken, TimeSpan?)"/>:
	/// <paramref name="work"/> receives an <see cref="Action{String}"/> to push stage markers, which share
	/// one monotonic counter with the timer heartbeats. No-op reporter and inline run when there is no token.
	/// </summary>
	internal static Task<TResult> RunWithProgressAsync<TResult>(
		ModelContextProtocol.Server.McpServer server,
		ProgressToken? progressToken,
		string operationName,
		Func<Action<string>, TResult> work,
		CancellationToken cancellationToken = default,
		TimeSpan? interval = null) {
		return RunWithProgressAsync(
			CreateChannel(server, progressToken), operationName, work, cancellationToken, interval);
	}

	/// <summary>
	/// Core, transport-agnostic heartbeat loop. Runs <paramref name="work"/> on the calling thread,
	/// passing it a stage reporter, while a background task beats through <paramref name="channel"/>
	/// every <paramref name="interval"/>. A <see langword="null"/> <paramref name="channel"/> means "no
	/// progress token": <paramref name="work"/> runs inline with a no-op reporter. Exceptions from
	/// <paramref name="work"/> propagate unchanged; the heartbeat is always stopped (success, throw, or
	/// cancellation). Exposed as <c>internal</c> so unit tests can drive the cadence with a fake sink
	/// channel (the transport-injection seam that replaced the old <c>RunWithBeatAsync</c>).
	/// </summary>
	internal static async Task<TResult> RunWithProgressAsync<TResult>(
		ProgressChannel channel,
		string operationName,
		Func<Action<string>, TResult> work,
		CancellationToken cancellationToken = default,
		TimeSpan? interval = null) {
		ArgumentNullException.ThrowIfNull(work);
		Action<string> reportStage = BuildStageReporter(channel);
		if (channel is null) {
			return work(reportStage);
		}

		TimeSpan effectiveInterval = interval ?? DefaultInterval;
		using CancellationTokenSource heartbeatCts =
			CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		// Pump on a background task so its timer continuations never depend on the calling
		// thread, which blocks inside the synchronous work() below.
		Task pump = Task.Run(
			() => PumpChannelAsync(channel, operationName, effectiveInterval, heartbeatCts.Token), CancellationToken.None);
		try {
			return work(reportStage);
		}
		finally {
			await heartbeatCts.CancelAsync().ConfigureAwait(false);
			try {
				await pump.ConfigureAwait(false);
			}
			catch (OperationCanceledException) {
				// Defensive: PumpChannelAsync self-handles cancellation and SafeSendAsync swallows send faults, so
				// pump completes normally; this guards a future change that could let it fault.
			}
		}
	}

	/// <summary>
	/// Runs <paramref name="work"/> on a background thread while emitting heartbeats, but bounds the
	/// <em>response</em> by <paramref name="deadline"/>: if the work finishes first its result (or
	/// exception) is returned/propagated unchanged; if the deadline elapses first the method throws
	/// <see cref="McpResponseDeadlineExceededException"/> and <strong>leaves the work running</strong>.
	/// </summary>
	/// <remarks>
	/// This is the hard-ceiling counterpart to <see cref="RunWithProgressAsync{TResult}"/>: heartbeats
	/// keep inactivity-timeout clients alive, while the deadline lets the tool return an actionable
	/// "in-progress, poll" envelope before a hard-ceiling client (GitHub Copilot CLI, ~180 s) abandons
	/// the request with <c>-32001</c>. The work is started on the thread pool with
	/// <see cref="CancellationToken.None"/> so it survives both the deadline and a client disconnect
	/// — the clio MCP server is a long-lived process, so the backend operation completes and becomes
	/// visible to a later read tool (for example <c>list-app-sections</c>). When the deadline (or
	/// <paramref name="cancellationToken"/>) wins, the abandoned task's exception is observed in a
	/// continuation so it never surfaces as an <c>UnobservedTaskException</c>.
	/// <para>
	/// <strong>Concurrency assumption (intentionally unbounded).</strong> Each over-deadline call
	/// detaches its <paramref name="work"/> with no semaphore, queue, or fan-out cap. This is safe
	/// under clio's current execution model: the MCP server is a single-session, single-client
	/// stdio process (one agent driving one connection). The single client issues calls sequentially
	/// and waits for each response, so detached work cannot fan out in parallel — it can only accumulate
	/// across <em>sequentially</em> timed-out create calls (an inherently rare, agent-paced event),
	/// never as concurrent parallel bursts. Should clio ever move to a multiplexed or multi-client
	/// transport, this assumption no longer holds and a bounded <see cref="SemaphoreSlim"/>/work-queue
	/// must gate the detached background work.
	/// </para>
	/// </remarks>
	/// <typeparam name="TResult">The synchronous result type produced by <paramref name="work"/>.</typeparam>
	/// <param name="server">The active MCP server used to send progress notifications.</param>
	/// <param name="progressToken">The progress token from the current request, or <see langword="null"/>.</param>
	/// <param name="operationName">Human-readable operation label used in beats and the deadline exception.</param>
	/// <param name="work">The synchronous backend operation to execute; must be safe to keep running after the response returns.</param>
	/// <param name="deadline">Wall-clock budget for the response; defaults to <see cref="DefaultResponseDeadline"/>.</param>
	/// <param name="cancellationToken">Stops the heartbeat; also ends the wait (the work still continues).</param>
	/// <param name="interval">Beat cadence; defaults to <see cref="DefaultInterval"/>.</param>
	/// <returns>The value returned by <paramref name="work"/> when it completes within the deadline.</returns>
	/// <remarks>
	/// If the detached <paramref name="work"/> faults <em>after</em> the deadline was reported — the exact
	/// cold/large-stand failure mode this method exists for — the fault is written to <c>stderr</c> via
	/// <see cref="ObserveInBackground"/> so it is not silent. <c>stderr</c> is the stdio-MCP-safe diagnostic
	/// channel (it never corrupts the protocol stream on <c>stdout</c>), and an agent following the
	/// "poll until it appears" guidance otherwise has no signal that creation actually failed.
	/// </remarks>
	/// <exception cref="McpResponseDeadlineExceededException">The work did not complete within <paramref name="deadline"/> and the request was not cancelled.</exception>
	/// <exception cref="OperationCanceledException">The request (or server shutdown) cancelled <paramref name="cancellationToken"/> before the work completed — the detached work does not outlive the process, so this is reported distinctly from a deadline.</exception>
	internal static Task<TResult> RunWithProgressAndDeadlineAsync<TResult>(
		ModelContextProtocol.Server.McpServer server,
		ProgressToken? progressToken,
		string operationName,
		Func<TResult> work,
		TimeSpan? deadline = null,
		CancellationToken cancellationToken = default,
		TimeSpan? interval = null) {
		ArgumentNullException.ThrowIfNull(work);
		// The explicit `Action<string>` parameter type is load-bearing (see the non-deadline overload):
		// it routes to the reporter overload below. Simplifying it to `() => work()` would rebind to
		// THIS SAME Func<TResult> overload and recurse forever — a runtime stack overflow, NOT a compile
		// error. The ProgressChannel counter reproduces the old per-beat counter unchanged.
		return RunWithProgressAndDeadlineAsync(
			server, progressToken, operationName, (Action<string> _) => work(), deadline, cancellationToken, interval);
	}

	/// <summary>
	/// Reporter-aware overload of
	/// <see cref="RunWithProgressAndDeadlineAsync{TResult}(ModelContextProtocol.Server.McpServer, ProgressToken?, string, Func{TResult}, TimeSpan?, CancellationToken, TimeSpan?)"/>:
	/// same deadline semantics, plus a stage reporter passed to <paramref name="work"/>.
	/// </summary>
	internal static Task<TResult> RunWithProgressAndDeadlineAsync<TResult>(
		ModelContextProtocol.Server.McpServer server,
		ProgressToken? progressToken,
		string operationName,
		Func<Action<string>, TResult> work,
		TimeSpan? deadline = null,
		CancellationToken cancellationToken = default,
		TimeSpan? interval = null) {
		return RunWithProgressAndDeadlineAsync(
			CreateChannel(server, progressToken), operationName, work, deadline, cancellationToken, interval);
	}

	/// <summary>
	/// Deadline-bounded twin of <see cref="RunWithProgressAsync{TResult}(ProgressChannel, string, Func{Action{String}, TResult}, CancellationToken, TimeSpan?)"/>:
	/// <paramref name="work"/> is started detached on the thread pool so it outlives both the deadline and
	/// a client disconnect, and the response is bounded by <paramref name="deadline"/>. Exposed as
	/// <c>internal</c> so unit tests can drive it with a fake sink channel. A <see langword="null"/>
	/// <paramref name="channel"/> still detaches the work (only the heartbeat pump is skipped).
	/// </summary>
	/// <remarks>
	/// On the deadline path <paramref name="work"/> runs detached (<see cref="Task.Run{TResult}(Func{TResult}, CancellationToken)"/>),
	/// and the <c>finally</c> cancels/awaits only the heartbeat pump — outstanding fire-and-forget
	/// <c>reportStage</c> marker sends are NOT cancelled. So a late stage marker can fire
	/// <c>notifications/progress</c> after the tool has already returned the in-progress ("poll") envelope.
	/// This is harmless by design: the sends are swallowed via <see cref="SafeSendAsync"/>, and clients ignore
	/// progress for a token they consider finished. It is accepted rather than adding cross-cancellation
	/// between the detached work and the response.
	/// </remarks>
	internal static async Task<TResult> RunWithProgressAndDeadlineAsync<TResult>(
		ProgressChannel channel,
		string operationName,
		Func<Action<string>, TResult> work,
		TimeSpan? deadline = null,
		CancellationToken cancellationToken = default,
		TimeSpan? interval = null) {
		ArgumentNullException.ThrowIfNull(work);
		TimeSpan effectiveInterval = interval ?? DefaultInterval;
		TimeSpan effectiveDeadline = deadline ?? DefaultResponseDeadline;
		Action<string> reportStage = BuildStageReporter(channel);

		// Start the work detached from the request lifetime so it can outlive both the deadline and a
		// client disconnect (see the McpServer deadline overload's remarks).
		Task<TResult> workTask = Task.Run(() => work(reportStage), CancellationToken.None);

		using CancellationTokenSource heartbeatCts =
			CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		Task pump = channel is null
			? Task.CompletedTask
			: Task.Run(() => PumpChannelAsync(channel, operationName, effectiveInterval, heartbeatCts.Token), CancellationToken.None);
		try {
			// The delay shares heartbeatCts so the finally below cancels the surviving timer when
			// work wins the race — no 150 s timer lingers after a fast completion.
			Task completed = await Task
				.WhenAny(workTask, Task.Delay(effectiveDeadline, heartbeatCts.Token))
				.ConfigureAwait(false);
			if (completed == workTask) {
				return await workTask.ConfigureAwait(false);
			}

			// The wait ended without the work finishing. Observe the abandoned task's eventual
			// exception so it cannot crash the process, and surface it to stderr for diagnostics.
			ObserveInBackground(workTask, operationName);

			// Distinguish genuine cancellation/shutdown from a real deadline: if the request was
			// cancelled, Task.Delay won the race only because heartbeatCts is linked to
			// cancellationToken — the deadline never elapsed, and on server shutdown the detached
			// Task.Run dies with the process, so the "work continues, keep polling" guidance would be
			// false. Propagate cancellation distinctly instead of fabricating a 150 s deadline.
			cancellationToken.ThrowIfCancellationRequested();
			throw new McpResponseDeadlineExceededException(operationName, effectiveDeadline);
		}
		finally {
			await heartbeatCts.CancelAsync().ConfigureAwait(false);
			try {
				await pump.ConfigureAwait(false);
			}
			catch (OperationCanceledException) {
				// Defensive: PumpChannelAsync self-handles cancellation and SafeSendAsync swallows send faults, so
				// pump completes normally; this guards a future change that could let it fault.
			}
		}
	}

	private static void ObserveInBackground<TResult>(Task<TResult> task, string operationName) {
		_ = task.ContinueWith(
			t => {
				// Reading t.Exception observes the fault so it never surfaces as an
				// UnobservedTaskException; writing it to stderr turns the otherwise-silent
				// post-deadline background failure into a diagnostic trail.
				AggregateException exception = t.Exception;
				if (exception is null) {
					return;
				}

				try {
					Console.Error.WriteLine(
						$"[{operationName}] background operation faulted after the response deadline: {exception.GetBaseException()}");
				}
				catch {
					// stderr diagnostics are best-effort: a closed/redirected stream must never
					// resurface as an UnobservedTaskException from the very continuation that
					// exists to suppress one.
				}
			},
			CancellationToken.None,
			TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
			TaskScheduler.Default);
	}

	private static ProgressChannel CreateChannel(
		ModelContextProtocol.Server.McpServer server, ProgressToken? progressToken) {
		if (server is null || progressToken is null) {
			return null;
		}

		ProgressToken token = progressToken.Value;
		return new ProgressChannel(value => server.SendNotificationAsync(
			"notifications/progress",
			new ProgressNotificationParams {
				ProgressToken = token,
				Progress = value
			}));
	}

	/// <summary>
	/// Builds the stage-marker reporter handed to <paramref name="work"/>. A <see langword="null"/>
	/// <paramref name="channel"/> (no progress token) yields an inert no-op. Exposed as <c>internal</c> so
	/// unit tests can drive the reporter directly against a fake sink channel.
	/// </summary>
	internal static Action<string> BuildStageReporter(ProgressChannel channel) {
		if (channel is null) {
			return static _ => { };
		}

		return message => {
			if (!string.IsNullOrWhiteSpace(message)) {
				// Fire-and-forget: markers are not ordered or flushed against each other, so callers must
				// have real work between successive reportStage calls (all current callers do).
				_ = SafeSendAsync(channel, message);
			}
		};
	}

	private static async Task SafeSendAsync(ProgressChannel channel, string message, CancellationToken cancellationToken = default) {
		try {
			await channel.SendAsync(message, cancellationToken).ConfigureAwait(false);
		}
		catch {
			// Best-effort: a broken progress channel must never surface from the tool.
		}
	}

	private static async Task PumpChannelAsync(
		ProgressChannel channel, string operationName, TimeSpan interval, CancellationToken cancellationToken) {
		int intervalSeconds = Math.Max(1, (int)Math.Round(interval.TotalSeconds));
		string label = string.IsNullOrWhiteSpace(operationName) ? "operation" : operationName;
		int tick = 0;
		while (!cancellationToken.IsCancellationRequested) {
			try {
				await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException) {
				return;
			}

			tick++;
			// Reuse SafeSendAsync so the "broken channel must never surface" swallow policy lives in one place.
			// Passing the pump's cancellationToken makes the send-gate wait cancellable, so a pump blocked on the
			// gate (behind a wedged fire-and-forget marker send) unblocks when the finally cancels heartbeatCts.
			await SafeSendAsync(channel, $"{label} is still running… (~{tick * intervalSeconds}s elapsed)", cancellationToken).ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Serializes progress sends for one request so heartbeats and stage markers share a single
	/// monotonic <c>Progress</c> counter. Transport is injected so tests can drive it with a fake sink.
	/// </summary>
	/// <remarks>
	/// Intentionally NOT built on <see cref="Clio.Command.McpServer.Progress.StageEventProgressForwarder"/>,
	/// which also builds progress notifications: that forwarder emits a TYPED <c>ClioStageEvent</c> envelope into
	/// <c>_meta.clioStageEvent</c>, driven by a command's <c>IStageEventSource.StageChanged</c> event stream
	/// (deploy/uninstall). <see cref="ProgressChannel"/> instead emits plain human-readable text
	/// (<c>Message</c>) with a numeric <c>Progress</c>, pushed imperatively by tool-level stage markers plus a
	/// timer heartbeat sharing one monotonic counter. Different payload contract and push model, so the
	/// envelope-builder is deliberately not shared.
	/// </remarks>
	internal sealed class ProgressChannel {
		private readonly Func<ModelContextProtocol.ProgressNotificationValue, Task> _send;

		// Intentionally NOT disposed: AvailableWaitHandle is never accessed, so no OS handle is allocated,
		// and late fire-and-forget marker sends may run after the enclosing method returns — disposing
		// here would risk an ObjectDisposedException on those trailing sends.
		private readonly SemaphoreSlim _sendGate = new(1, 1);
		private int _sequence;

		/// <summary>Creates a channel that serializes sends through the injected transport (a fake sink in tests).</summary>
		internal ProgressChannel(Func<ModelContextProtocol.ProgressNotificationValue, Task> send) {
			_send = send ?? throw new ArgumentNullException(nameof(send));
		}

		/// <summary>Sends <paramref name="message"/> under the next gap-free monotonic <c>Progress</c> value; sends are serialized so the counter never regresses.</summary>
		internal async Task SendAsync(string message, CancellationToken cancellationToken = default) {
			await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
			try {
				int sequence = ++_sequence;
				await _send(new ModelContextProtocol.ProgressNotificationValue {
					Progress = sequence,
					Message = message
				}).ConfigureAwait(false);
			}
			finally {
				_sendGate.Release();
			}
		}
	}
}
