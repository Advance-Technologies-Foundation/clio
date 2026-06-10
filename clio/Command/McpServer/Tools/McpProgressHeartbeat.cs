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
			heartbeatCts.Cancel();
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
