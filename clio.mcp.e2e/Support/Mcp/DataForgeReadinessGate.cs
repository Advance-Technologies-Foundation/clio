using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Tools;
using Clio.Common.DataForge;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Mcp;

/// <summary>
/// Makes the DataForge similarity-search E2E fixtures deterministic on a freshly-deployed stand
/// (ENG-92147, spec <c>spec/dataforge-e2e-readiness</c>, Step 2A).
/// </summary>
/// <remarks>
/// On a fresh Studio + PostgreSQL stand <c>CrtDataForge</c> can be present and reachable
/// (the status / context / initialize / update fixtures pass) while the similarity INDEX has
/// never been built — so <c>dataforge-find-tables</c>, <c>dataforge-find-lookups</c>, and
/// <c>dataforge-get-relations</c> return <c>Success=false</c>. The fix is an E2E arrange step,
/// not a clio code change: invoke <c>dataforge-initialize</c> (which is fire-and-forget and
/// returns <c>Scheduled</c>, not <c>Ready</c>), then poll <c>dataforge-status</c> until the
/// index-ready signal flips. The ready signal is the maintenance status <c>Ready</c> plus both
/// health readiness flags (<c>DataStructureReadiness</c> for tables/relations, <c>LookupsReadiness</c>
/// for lookups) — a generic online/liveness flag is NOT enough to prove the index is queryable.
/// The <see cref="IsIndexReady"/> predicate is pure so it can be unit-tested without a stand.
/// <para>
/// Every wait in the gate is bounded by wall-clock so it can NEVER hang the suite (ENG-90640): the
/// initialize and each status poll run under their own per-call timeout (<see cref="PerCallTimeout"/>)
/// linked to the incoming token, so a single hung MCP call is aborted rather than blocking forever;
/// the whole gate is additionally bounded by an overall deadline (<see cref="OverallDeadline"/>) on top
/// of the attempt cap. A timed-out or faulted individual poll is treated as "not ready yet" and the loop
/// proceeds to the next attempt — only the overall deadline (or the incoming token) ends the gate, by
/// failing the DataForge tests with the descriptive timeout instead of hanging.
/// </para>
/// </remarks>
internal static class DataForgeReadinessGate {
	private const string InitializeToolName = DataForgeTool.DataForgeInitializeToolName;
	private const string StatusToolName = DataForgeTool.DataForgeStatusToolName;

	/// <summary>
	/// Decides whether a <see cref="DataForgeStatusResponse"/> proves the DataForge similarity
	/// index is built and queryable. Ready requires the structured status call to succeed, the
	/// maintenance status to be <c>Ready</c>, and BOTH the data-structure and lookups readiness
	/// health flags to be true; any of <c>Offline</c>/<c>NotReady</c>/<c>Unavailable</c> or a
	/// false readiness flag means the index is not yet queryable for similarity search.
	/// </summary>
	/// <param name="response">The structured status response returned by <c>dataforge-status</c>, or <c>null</c>.</param>
	/// <returns><c>true</c> when the similarity index is ready for reads; otherwise <c>false</c>.</returns>
	public static bool IsIndexReady(DataForgeStatusResponse? response) {
		if (response is null || !response.Success) {
			return false;
		}

		DataForgeMaintenanceStatusResult? status = response.Status;
		DataForgeHealthResult? health = response.Health;
		if (status is null || health is null) {
			return false;
		}

		return status.Success
			&& string.Equals(status.Status, "Ready", System.StringComparison.OrdinalIgnoreCase)
			&& health.DataStructureReadiness
			&& health.LookupsReadiness;
	}

	/// <summary>
	/// Best-effort warm-up: invokes <c>dataforge-initialize</c> once, then polls
	/// <c>dataforge-status</c> until <see cref="IsIndexReady"/> reports the index is built, using
	/// bounded retries with a fixed inter-attempt delay and a hard overall cap. Returns <c>true</c>
	/// once the index is ready, and <c>false</c> if it could not be made ready within the budget
	/// (or initialization was not accepted) — writing the last full status payload to
	/// <see cref="TestContext.Out"/> for diagnostics. It deliberately does NOT fail the test: on a
	/// stand that is not wired to a DataForge tier (no <c>DataForgeServiceUrl</c>/IdentityServer
	/// settings, so the maintenance service reports <c>Unavailable</c>) the index can never become
	/// ready, and that is an environment precondition — the per-read service-state guard in the
	/// fixtures decides skip-vs-assert from the actual read response (ENG-92557). Returning a bool
	/// keeps this a warm-up nudge, not a gate.
	/// </summary>
	/// <param name="session">Active MCP server session.</param>
	/// <param name="environmentName">Registered clio sandbox environment name.</param>
	/// <param name="cancellationToken">Cancellation token bounding the arrange phase.</param>
	/// <returns><c>true</c> when the similarity index became ready within the budget; otherwise <c>false</c>.</returns>
	public static async Task<bool> EnsureIndexReadyAsync(
		McpServerSession session,
		string environmentName,
		CancellationToken cancellationToken) {
		// Bound the entire gate by wall-clock so a hung stand can never freeze the suite (ENG-90640).
		Stopwatch elapsedTimer = Stopwatch.StartNew();

		// 1. Schedule initialization. Initialize is fire-and-forget: it returns Scheduled, never
		//    Ready, so it only means "accepted". The readiness wait below is what gates the reads.
		//    The call is per-call-timeout-wrapped: a hung initialize is aborted, not blocking forever.
		CallToolResult? initializeResult = await TryCallDataForgeToolAsync(
			session, InitializeToolName, environmentName, cancellationToken);
		DataForgeMaintenanceResponse? initializeResponse = initializeResult is null
			? null
			: TryDeserialize<DataForgeMaintenanceResponse>(initializeResult);
		if (!WasInitializationAccepted(initializeResponse)) {
			if (initializeResult is not null) {
				WriteToolDiagnostics(InitializeToolName, initializeResult);
			}

			// Initialization was not accepted — typically because the stand is not wired to a
			// DataForge tier (the maintenance service is Unavailable). This is an environment
			// precondition, not a clio failure: report it as not-ready so the per-read guard can
			// skip deterministically rather than failing the suite here (ENG-92557).
			TestContext.Out.WriteLine(
				$"[dataforge-readiness] could not schedule initialization on '{environmentName}': " +
				$"dataforge-initialize did not return a successful structured payload within {PerCallTimeout.TotalSeconds:0}s. " +
				$"Treating the similarity index as not-ready (see output for the full response).");
			return false;
		}

		// 2. Poll dataforge-status until the index-ready signal flips, or the budget is exhausted.
		//    The index build is asynchronous, so a Scheduled initialize is followed by a window in
		//    which reads still fail. Each poll is per-call-timeout-wrapped and a timed-out/faulted poll
		//    is treated as "not ready yet" so one slow call cannot kill the gate — only the overall
		//    deadline (or the incoming token) does. The polling budget is ReadinessAttempts(20) x
		//    ReadinessDelay(15s) ~= 5 min, hard-capped by the OverallDeadline(6 min); both sit under the
		//    fixtures' 8-min arrange timeout so the gate always ends before the arrange token fires.
		DataForgeStatusResponse? lastStatus = null;
		CallToolResult? lastStatusResult = null;
		for (int attempt = 0; attempt < ReadinessAttempts; attempt++) {
			cancellationToken.ThrowIfCancellationRequested();
			if (OverallDeadlineReached(elapsedTimer.Elapsed, OverallDeadline)) {
				break;
			}

			CallToolResult? statusResult = await TryCallDataForgeToolAsync(
				session, StatusToolName, environmentName, cancellationToken);
			if (statusResult is not null) {
				lastStatusResult = statusResult;
				lastStatus = TryDeserialize<DataForgeStatusResponse>(statusResult);
				if (IsIndexReady(lastStatus)) {
					return true;
				}
			}

			if (attempt < ReadinessAttempts - 1 &&
				!OverallDeadlineReached(elapsedTimer.Elapsed + ReadinessDelay, OverallDeadline)) {
				await Task.Delay(ReadinessDelay, cancellationToken);
			}
		}

		// Emit the COMPLETE last status payload before failing: FluentAssertions truncates long
		// assertion messages, so the full readiness state is written to TestContext.Out (not truncated).
		if (lastStatusResult is not null) {
			WriteToolDiagnostics(StatusToolName, lastStatusResult);
		}

		int budgetSeconds = (int)System.Math.Min(
			ReadinessAttempts * ReadinessDelay.TotalSeconds, OverallDeadline.TotalSeconds);
		TestContext.Out.WriteLine(
			$"[dataforge-readiness] similarity index on '{environmentName}' did not become Ready within {budgetSeconds}s " +
			$"(up to {ReadinessAttempts} dataforge-status polls every {ReadinessDelay.TotalSeconds:0}s after " +
			$"dataforge-initialize, capped by a {OverallDeadline.TotalSeconds:0}s overall deadline and a " +
			$"{PerCallTimeout.TotalSeconds:0}s per-call timeout). Last status: success={lastStatus?.Success}, " +
			$"status={lastStatus?.Status?.Status ?? "<none>"}, " +
			$"data-structure-readiness={lastStatus?.Health?.DataStructureReadiness}, " +
			$"lookups-readiness={lastStatus?.Health?.LookupsReadiness}. " +
			$"Treating the similarity index as not-ready (see output for the full status payload).");
		return false;
	}

	private const int ReadinessAttempts = 20;
	private static readonly TimeSpan ReadinessDelay = TimeSpan.FromSeconds(15);

	/// <summary>Per-call wall-clock budget for a single <c>dataforge-initialize</c>/<c>dataforge-status</c> MCP call.</summary>
	private static readonly TimeSpan PerCallTimeout = TimeSpan.FromSeconds(60);

	/// <summary>Hard upper bound for the whole gate, regardless of attempt count or how individual calls behave.</summary>
	private static readonly TimeSpan OverallDeadline = TimeSpan.FromMinutes(6);

	/// <summary>
	/// Pure "stop polling" decision: returns whether the gate's overall wall-clock deadline has been
	/// reached, given the time already spent and the configured deadline. Extracted so the bounding
	/// logic can be unit-tested without an MCP stand (the per-call MCP timeout itself needs a stand).
	/// </summary>
	/// <param name="elapsed">Wall-clock time already spent inside the gate.</param>
	/// <param name="overallDeadline">Configured hard upper bound for the whole gate.</param>
	/// <returns><c>true</c> when polling must stop because the deadline is reached; otherwise <c>false</c>.</returns>
	public static bool OverallDeadlineReached(TimeSpan elapsed, TimeSpan overallDeadline) =>
		elapsed >= overallDeadline;

	/// <summary>
	/// Pure "was the maintenance work accepted" decision for <c>dataforge-initialize</c>: a warm-up
	/// can only proceed to the readiness poll when the initialize call returned a structured, successful
	/// maintenance payload. A <c>null</c> (unreadable / timed-out) or <c>Success=false</c> response means
	/// initialization was not accepted — typically because the stand is not wired to a DataForge tier and
	/// the maintenance service is <c>Unavailable</c>. Extracted so the accept/reject branch can be
	/// unit-tested without an MCP stand (the surrounding poll needs a live session).
	/// </summary>
	/// <param name="initializeResponse">The structured <c>dataforge-initialize</c> response, or <c>null</c>.</param>
	/// <returns><c>true</c> when initialization was accepted and the readiness poll should run; otherwise <c>false</c>.</returns>
	public static bool WasInitializationAccepted(DataForgeMaintenanceResponse? initializeResponse) =>
		initializeResponse is not null && initializeResponse.Success;

	/// <summary>
	/// Invokes a DataForge tool under a per-call timeout linked to <paramref name="cancellationToken"/>.
	/// A call that exceeds <see cref="PerCallTimeout"/> (or otherwise faults) is contained: <c>null</c>
	/// is returned so the poll loop treats it as "not ready yet" and continues. The incoming token is
	/// honoured: if it is cancelled the cancellation propagates so the gate ends rather than spinning.
	/// </summary>
	private static async Task<CallToolResult?> TryCallDataForgeToolAsync(
		McpServerSession session,
		string toolName,
		string environmentName,
		CancellationToken cancellationToken) {
		using CancellationTokenSource perCallCts =
			CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		perCallCts.CancelAfter(PerCallTimeout);
		try {
			return await CallDataForgeToolAsync(session, toolName, environmentName, perCallCts.Token);
		} catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
			// The caller's arrange token won — surface it so the gate ends instead of looping.
			throw;
		} catch (OperationCanceledException) {
			// Only the per-call timeout fired: contain it as "not ready yet" and let the loop retry.
			TestContext.Out.WriteLine(
				$"[dataforge-readiness] tool '{toolName}' timed out after {PerCallTimeout.TotalSeconds:0}s; treating as not-ready and retrying.");
			return null;
		} catch (Exception ex) {
			// A single faulted call must not kill the gate; the overall deadline still bounds total time.
			TestContext.Out.WriteLine(
				$"[dataforge-readiness] tool '{toolName}' faulted ({ex.GetType().Name}: {ex.Message}); treating as not-ready and retrying.");
			return null;
		}
	}

	private static async Task<CallToolResult> CallDataForgeToolAsync(
		McpServerSession session,
		string toolName,
		string environmentName,
		CancellationToken cancellationToken) {
		Dictionary<string, object?> args = new() {
			["environment-name"] = environmentName
		};
		// dataforge-initialize is destructive and now long-tail (ENG-92761): drive it through the
		// host-gated clio-run-destructive executor. The auto-routing CallToolAsync would send this
		// unadvertised tool through the SAFE clio-run executor, which refuses destructive commands.
		// dataforge-status stays read-only and rides the normal long-tail routing.
		if (string.Equals(toolName, InitializeToolName, System.StringComparison.Ordinal)) {
			return await session.CallDestructiveAsync(toolName, args, cancellationToken);
		}
		return await session.CallToolAsync(
			toolName,
			new Dictionary<string, object?> { ["args"] = args },
			cancellationToken);
	}

	private static void WriteToolDiagnostics(string toolName, CallToolResult callResult) {
		TestContext.Out.WriteLine($"[dataforge-readiness] tool: {toolName}");
		TestContext.Out.WriteLine($"[dataforge-readiness] is-error: {callResult.IsError}");
		string structured = callResult.StructuredContent is null
			? "<none>"
			: JsonSerializer.Serialize(callResult.StructuredContent);
		TestContext.Out.WriteLine($"[dataforge-readiness] structured content: {structured}");
		string content = JsonSerializer.Serialize(callResult.Content ?? []);
		TestContext.Out.WriteLine($"[dataforge-readiness] content: {content}");
	}

	private static TResponse? TryDeserialize<TResponse>(CallToolResult callResult) where TResponse : class {
		if (TrySerializeToJsonElement(callResult.StructuredContent, out JsonElement structuredElement) &&
			TryParse<TResponse>(structuredElement, out TResponse? structuredResult)) {
			return structuredResult;
		}

		if (TrySerializeToJsonElement(callResult.Content, out JsonElement contentElement) &&
			TryParse<TResponse>(contentElement, out TResponse? contentResult)) {
			return contentResult;
		}

		return null;
	}

	private static bool TrySerializeToJsonElement(object? value, out JsonElement element) {
		if (value is null) {
			element = default;
			return false;
		}
		element = JsonSerializer.SerializeToElement(value);
		return true;
	}

	private static bool TryParse<TResponse>(JsonElement element, out TResponse? result) where TResponse : class {
		if (element.ValueKind == JsonValueKind.Array) {
			foreach (JsonElement item in element.EnumerateArray()) {
				if (TryGetTextPayload(item, out string? text) &&
					!string.IsNullOrWhiteSpace(text) &&
					TryDeserializeJson(text, out result)) {
					return true;
				}
			}
		}

		if (element.ValueKind == JsonValueKind.String) {
			string? textPayload = element.GetString();
			if (!string.IsNullOrWhiteSpace(textPayload) &&
				TryDeserializeJson(textPayload, out result)) {
				return true;
			}
		}

		return TryDeserializeJson(element.GetRawText(), out result);
	}

	private static bool TryGetTextPayload(JsonElement element, out string? textPayload) {
		textPayload = null;
		if (element.ValueKind != JsonValueKind.Object) {
			return false;
		}
		if (element.TryGetProperty("text", out JsonElement textElement) &&
			textElement.ValueKind == JsonValueKind.String) {
			textPayload = textElement.GetString();
			return true;
		}
		return false;
	}

	private static bool TryDeserializeJson<TResponse>(string json, out TResponse? result) where TResponse : class {
		try {
			result = JsonSerializer.Deserialize<TResponse>(json);
			return result is not null;
		} catch (JsonException) {
			result = default;
			return false;
		}
	}
}
