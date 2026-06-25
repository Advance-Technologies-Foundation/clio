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
		TimeSpan effectiveInterval = interval ?? DefaultInterval;
		Func<int, Task> beat = server is null || progressToken is null
			? null
			: BuildServerBeat(server, progressToken.Value, operationName, effectiveInterval);
		return RunWithBeatAsync(beat, work, cancellationToken, effectiveInterval);
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
	internal static async Task<TResult> RunWithProgressAndDeadlineAsync<TResult>(
		ModelContextProtocol.Server.McpServer server,
		ProgressToken? progressToken,
		string operationName,
		Func<TResult> work,
		TimeSpan? deadline = null,
		CancellationToken cancellationToken = default,
		TimeSpan? interval = null) {
		ArgumentNullException.ThrowIfNull(work);
		TimeSpan effectiveInterval = interval ?? DefaultInterval;
		TimeSpan effectiveDeadline = deadline ?? DefaultResponseDeadline;
		Func<int, Task> beat = server is null || progressToken is null
			? null
			: BuildServerBeat(server, progressToken.Value, operationName, effectiveInterval);

		// Start the work detached from the request lifetime: it must outlive both the response
		// deadline and a client disconnect so the backend operation can still complete server-side.
		Task<TResult> workTask = Task.Run(work, CancellationToken.None);

		using CancellationTokenSource heartbeatCts =
			CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		Task pump = beat is null
			? Task.CompletedTask
			: Task.Run(() => PumpAsync(beat, effectiveInterval, heartbeatCts.Token), CancellationToken.None);
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
				// Expected when the pump observes cancellation on stop.
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

	/// <summary>
	/// Core, transport-agnostic heartbeat loop. Runs <paramref name="work"/> on the calling
	/// thread while invoking <paramref name="beat"/> every <paramref name="interval"/> on a
	/// background task. A <see langword="null"/> <paramref name="beat"/> means "no heartbeat":
	/// <paramref name="work"/> runs inline. Exceptions from <paramref name="work"/> propagate
	/// unchanged; the heartbeat is always stopped (success, throw, or cancellation). Beat
	/// failures are swallowed so keep-alive never breaks tool execution. Exposed as
	/// <c>internal</c> so unit tests can drive the cadence with a fake sink.
	/// </summary>
	internal static async Task<TResult> RunWithBeatAsync<TResult>(
		Func<int, Task> beat,
		Func<TResult> work,
		CancellationToken cancellationToken = default,
		TimeSpan? interval = null) {
		ArgumentNullException.ThrowIfNull(work);
		if (beat is null) {
			return work();
		}

		TimeSpan effectiveInterval = interval ?? DefaultInterval;
		using CancellationTokenSource heartbeatCts =
			CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		// Pump on a background task so its timer continuations never depend on the calling
		// thread, which blocks inside the synchronous work() below.
		Task pump = Task.Run(
			() => PumpAsync(beat, effectiveInterval, heartbeatCts.Token), CancellationToken.None);
		try {
			return work();
		}
		finally {
			await heartbeatCts.CancelAsync().ConfigureAwait(false);
			try {
				await pump.ConfigureAwait(false);
			}
			catch (OperationCanceledException) {
				// Expected when the pump observes cancellation on stop.
			}
		}
	}

	private static async Task PumpAsync(Func<int, Task> beat, TimeSpan interval, CancellationToken cancellationToken) {
		int beatNumber = 0;
		while (!cancellationToken.IsCancellationRequested) {
			try {
				await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException) {
				return;
			}

			beatNumber++;
			try {
				await beat(beatNumber).ConfigureAwait(false);
			}
			catch {
				// Keep-alive is best-effort: a disconnected client or serialization error
				// must never surface from the tool. Mirrors McpLogNotifier's policy.
			}
		}
	}

	private static Func<int, Task> BuildServerBeat(
		ModelContextProtocol.Server.McpServer server,
		ProgressToken progressToken,
		string operationName,
		TimeSpan interval) {
		int intervalSeconds = Math.Max(1, (int)Math.Round(interval.TotalSeconds));
		string label = string.IsNullOrWhiteSpace(operationName) ? "operation" : operationName;
		return beatNumber => server.SendNotificationAsync(
			"notifications/progress",
			new ProgressNotificationParams {
				ProgressToken = progressToken,
				Progress = new ModelContextProtocol.ProgressNotificationValue {
					Progress = beatNumber,
					Message = $"{label} is still running… (~{beatNumber * intervalSeconds}s elapsed)"
				}
			});
	}
}
