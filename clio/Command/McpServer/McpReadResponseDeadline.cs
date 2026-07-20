using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;

namespace Clio.Command.McpServer;

/// <summary>
/// Bounds a read-only / retry-safe MCP tool call by a wall-clock <em>response deadline</em> so it can
/// never block indefinitely (ENG-93373). When the wrapped work completes within the deadline its
/// <see cref="CallToolResult"/> is returned unchanged; when the deadline elapses first the work is
/// abandoned and a structured <c>error-class: creatio-timeout</c> result is returned instead, telling
/// the agent the call is non-destructive and safe to retry or poll.
/// </summary>
/// <remarks>
/// This is the read-path counterpart of <see cref="Tools.McpProgressHeartbeat"/> (the write-path
/// deadline). It lives at the call-tool pipeline layer (shape-agnostic: it produces a
/// <see cref="CallToolResult"/> directly), so a single mechanism covers every retry-safe tool
/// regardless of its typed return shape. Only tools classified retry-safe by
/// <see cref="McpReadDeadlineGate"/> are wrapped; destructive tools keep their own timeout contract.
/// <para>
/// The abandoned work is left to run (its linked token is cancelled to nudge cooperative tools; its
/// eventual result/exception is observed so it can never surface as an
/// <see cref="System.Threading.Tasks.TaskScheduler.UnobservedTaskException"/>). Abandoning a read is
/// safe — the caller simply retries. The single-session, sequential-client execution model that makes
/// this safe is the same one documented for the write path in
/// <c>spec/adr/adr-create-app-section-response-deadline.md</c>.
/// </para>
/// <para>
/// <strong>Accepted trade-off (single-session).</strong> The shipped read services are synchronous and
/// do not observe the cancellation token, so an abandoned read keeps its thread-pool thread — and any
/// per-tenant execution lock the tool holds (<c>BaseTool.ExecuteUnderTenantLock</c>) — until the backend
/// finally responds. Under clio's single-session, sequential-client model the agent waits for each
/// bounded response before issuing the next call, so abandoned reads are added at most one-per-timeout
/// (agent-paced), never as a concurrent burst; the same-tenant lock also serialises them, which
/// incidentally prevents a concurrent double-execution. Against a slow-but-eventually-responsive backend
/// each abandoned read drains as the stand catches up, so the steady-state count stays near one. The
/// pathological case is a <em>permanently</em>-hung stand plus an auto-retrying agent: the first read
/// holds the tenant monitor forever, each retry blocks another pool thread on that same monitor, times
/// out and retries — so N threads accumulate (1 holding + N-1 blocked), one per deadline, until the
/// stand responds. This still self-limits (it is agent-paced and bounded by the single-session model,
/// and unwinds the moment the stand replies), so it is an accepted trade-off, not a leak; a per-tenant
/// in-flight guard that fast-fails a new bounded read while an abandoned one still holds the lock is the
/// fix if a future multiplexed/multi-client transport makes the pile-up matter. This mirrors the write
/// path's documented detach trade-off.
/// </para>
/// </remarks>
internal static class McpReadResponseDeadline {

	/// <summary>
	/// Environment variable that overrides <see cref="DefaultReadDeadline"/>, expressed in seconds
	/// (invariant culture, accepted range 0 &lt; n ≤ 600). Kept SEPARATE from the write path's
	/// <c>CLIO_MCP_RESPONSE_DEADLINE_SECONDS</c> so an operator can tune read latency independently of
	/// the write ceiling. Invalid or out-of-range values fall back to the 120 s default.
	/// </summary>
	internal const string ReadDeadlineOverrideEnvVar = "CLIO_MCP_READ_DEADLINE_SECONDS";

	/// <summary>
	/// Machine-readable error-class emitted on a read timeout. Deliberately the SAME wire token used by
	/// the write path (<see cref="Command.ApplicationSectionCreateFailureClass.CreatioTimeout"/>) so any
	/// existing "on error-class=creatio-timeout …" client guidance applies unchanged. The read envelope
	/// is distinguished by <c>read-response-timed-out: true</c> and the absence of <c>section-created</c>.
	/// </summary>
	internal const string ReadTimeoutErrorClass = "creatio-timeout";

	/// <summary>
	/// Default wall-clock budget for a read-only MCP response. Chosen inside the ticket's 120–150 s band
	/// (and below common client request ceilings) so a stalled read returns an actionable timeout rather
	/// than hanging. Overridable via <see cref="ReadDeadlineOverrideEnvVar"/>.
	/// </summary>
	internal static readonly TimeSpan DefaultReadDeadline =
		ResolveDeadline(Environment.GetEnvironmentVariable(ReadDeadlineOverrideEnvVar));

	/// <summary>
	/// Parses a raw seconds override into a deadline, falling back to 120 s for null / empty / non-numeric /
	/// out-of-range (<c>0 &lt; n ≤ 600</c>) values. Pure (takes the raw string, reads no environment) so the
	/// parse rules are unit-testable without mutating process-wide state.
	/// </summary>
	/// <param name="rawValue">The raw override value (typically from <see cref="ReadDeadlineOverrideEnvVar"/>).</param>
	/// <returns>The resolved deadline.</returns>
	internal static TimeSpan ResolveDeadline(string rawValue) {
		if (!string.IsNullOrWhiteSpace(rawValue)
			&& double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
			&& seconds > 0 && seconds <= 600) {
			return TimeSpan.FromSeconds(seconds);
		}

		return TimeSpan.FromSeconds(120);
	}

	/// <summary>
	/// Runs <paramref name="work"/> under a wall-clock <paramref name="deadline"/>. Returns the work's
	/// result when it completes in time; on expiry abandons the work and returns a structured
	/// <c>creatio-timeout</c> <see cref="CallToolResult"/>.
	/// </summary>
	/// <param name="toolName">MCP tool name, surfaced in the timeout result and diagnostics.</param>
	/// <param name="work">The call-tool invocation to bound; receives a token cancelled on deadline.</param>
	/// <param name="cancellationToken">The request token; genuine cancellation propagates (not a timeout).</param>
	/// <param name="deadline">Wall-clock budget; defaults to <see cref="DefaultReadDeadline"/>.</param>
	/// <returns>The work's <see cref="CallToolResult"/>, or a structured timeout result.</returns>
	/// <exception cref="OperationCanceledException">The request was cancelled before the work completed.</exception>
	internal static async ValueTask<CallToolResult> RunAsync(
		string toolName,
		Func<CancellationToken, ValueTask<CallToolResult>> work,
		CancellationToken cancellationToken,
		TimeSpan? deadline = null) {
		ArgumentNullException.ThrowIfNull(work);
		TimeSpan effectiveDeadline = deadline ?? DefaultReadDeadline;

		// The work runs detached on its OWN cancellation source so it can outlive the response (a stalled
		// read must not block the process) yet still be nudged to stop cooperatively on deadline. The source
		// is NOT disposed synchronously here: on the abandon path the work may still touch the token, so it
		// is disposed only after the work observably completes (ObserveAndDispose) — avoiding an
		// ObjectDisposedException on a cooperative tool.
		CancellationTokenSource workCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		// Start the work off the calling thread so the deadline timer below is never starved by a tool
		// that executes its backend round-trip synchronously.
		Task<CallToolResult> workTask = Task.Run(() => work(workCts.Token).AsTask(), CancellationToken.None);

		// Separate timer source so the losing Task.Delay is cancelled the instant the work wins the race —
		// no deadline-long timer lingers after a fast read.
		using CancellationTokenSource delayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		Task completed = await Task
			.WhenAny(workTask, Task.Delay(effectiveDeadline, delayCts.Token))
			.ConfigureAwait(false);
		if (completed == workTask) {
			await delayCts.CancelAsync().ConfigureAwait(false);  // stop the losing timer
			workCts.Dispose();        // work is done; it will not touch the token again
			return await workTask.ConfigureAwait(false);
		}

		// The delay won. Nudge cooperative tools to stop, THEN hand ownership of workCts to the observer.
		// Cancel BEFORE ObserveAndDispose: the observer's continuation disposes workCts once the work
		// completes, so cancelling afterwards could race a dispose and throw ObjectDisposedException.
		await workCts.CancelAsync().ConfigureAwait(false);
		// Observe (and eventually dispose) the abandoned work on EVERY non-work-wins path, including
		// cancellation, so a later fault can never surface as an UnobservedTaskException.
		ObserveAndDispose(workTask, workCts, toolName);

		// Distinguish genuine request cancellation from a real deadline: if the request was cancelled,
		// propagate cancellation (the outer error filter honours it) rather than fabricating a timeout.
		cancellationToken.ThrowIfCancellationRequested();

		return CreateTimeoutResult(toolName, effectiveDeadline);
	}

	/// <summary>
	/// Builds the structured read-timeout <see cref="CallToolResult"/>: a machine-readable
	/// <c>StructuredContent</c> envelope (<c>error-class</c>, <c>read-response-timed-out</c>,
	/// <c>retry-guidance</c>) plus a concise text mirror for clients that only read text content.
	/// </summary>
	/// <param name="toolName">MCP tool name.</param>
	/// <param name="deadline">The elapsed wall-clock budget.</param>
	/// <returns>The structured timeout result.</returns>
	internal static CallToolResult CreateTimeoutResult(string toolName, TimeSpan deadline) {
		int seconds = Math.Max(1, (int)Math.Round(deadline.TotalSeconds));
		string retryGuidance =
			$"This read-only / retry-safe call did not complete within the {seconds}s response deadline, so the "
			+ "clio MCP server bounded the response rather than blocking indefinitely. The underlying read may still "
			+ "be completing on a busy Creatio stand — this is NOT a failure of the operation. Wait briefly, then "
			+ "retry the same call (or narrow it with a filter / smaller limit and retry). If the stand is "
			+ $"legitimately slow, raise the budget via the {ReadDeadlineOverrideEnvVar} environment variable.";
		string text =
			$"MCP tool '{toolName}' timed out after {seconds}s (error-class={ReadTimeoutErrorClass}). "
			+ "It is read-only / non-destructive and safe to retry.";
		JsonObject payload = new() {
			["success"] = false,
			["error-class"] = ReadTimeoutErrorClass,
			["read-response-timed-out"] = true,
			["tool"] = toolName,
			["deadline-seconds"] = seconds,
			["error"] = text,
			["retry-guidance"] = retryGuidance
		};
		return new CallToolResult {
			IsError = true,
			Content = [new TextContentBlock { Text = text }],
			StructuredContent = JsonSerializer.SerializeToElement(payload)
		};
	}

	// Observes the abandoned work's eventual completion — reading t.Exception so a fault never surfaces as
	// an UnobservedTaskException, logging it to stderr for diagnostics — and disposes the work's
	// cancellation source once the work has finished touching its token. The continuation runs on EVERY
	// completion (success/fault/cancel), never OnlyOnFaulted, so the source is always disposed.
	private static void ObserveAndDispose(Task<CallToolResult> task, CancellationTokenSource workCts, string toolName) {
		_ = task.ContinueWith(
			t => {
				try {
					// Reading t.Exception observes a fault (null for success/cancel — those need no observing
					// and never raise UnobservedTaskException). stderr is the stdio-MCP-safe channel.
					AggregateException exception = t.Exception;
					if (exception is not null) {
						try {
							// Redact the fault text before logging: a backend read failure routinely carries
							// target URIs, absolute paths, or connection-string hosts, and this stderr line is
							// frequently captured into MCP server logs. Log a safe summary (exception type +
							// redacted message) rather than the full base exception (mirrors the other MCP
							// error paths in this area that scrub via SensitiveErrorTextRedactor).
							Exception baseException = exception.GetBaseException();
							Console.Error.WriteLine(
								$"[{toolName}] read operation faulted after the response deadline: "
								+ $"{baseException.GetType().Name}: {SensitiveErrorTextRedactor.Redact(baseException.Message)}");
						}
						catch {
							// Best-effort diagnostics: a closed/redirected stream must never resurface as an
							// UnobservedTaskException from the very continuation that exists to suppress one.
						}
					}
				}
				finally {
					workCts.Dispose();
				}
			},
			CancellationToken.None,
			TaskContinuationOptions.ExecuteSynchronously,
			TaskScheduler.Default);
	}
}
