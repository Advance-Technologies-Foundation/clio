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
	/// Invokes <c>dataforge-initialize</c> once, then polls <c>dataforge-status</c> until
	/// <see cref="IsIndexReady"/> reports the index is built, using bounded retries with a fixed
	/// inter-attempt delay and a hard overall cap. On timeout it writes the last full status
	/// payload to <see cref="TestContext.Out"/> (mirroring the arrange diagnostics in
	/// <c>ClioCliCommandRunner</c>) and fails the test with a descriptive message.
	/// </summary>
	/// <param name="session">Active MCP server session.</param>
	/// <param name="environmentName">Registered clio sandbox environment name.</param>
	/// <param name="cancellationToken">Cancellation token bounding the arrange phase.</param>
	public static async Task EnsureIndexReadyAsync(
		McpServerSession session,
		string environmentName,
		CancellationToken cancellationToken) {
		// 1. Schedule initialization. Initialize is fire-and-forget: it returns Scheduled, never
		//    Ready, so it only means "accepted". The readiness wait below is what gates the reads.
		CallToolResult initializeResult = await CallDataForgeToolAsync(
			session, InitializeToolName, environmentName, cancellationToken);
		DataForgeMaintenanceResponse? initializeResponse =
			TryDeserialize<DataForgeMaintenanceResponse>(initializeResult);
		if (initializeResponse is null || !initializeResponse.Success) {
			WriteToolDiagnostics(InitializeToolName, initializeResult);
			Assert.Fail(
				$"DataForge arrange could not schedule initialization on '{environmentName}': " +
				$"dataforge-initialize did not return a successful structured payload. See test output for the full response.");
		}

		// 2. Poll dataforge-status until the index-ready signal flips, or the budget is exhausted.
		//    The index build is asynchronous, so a Scheduled initialize is followed by a window in
		//    which reads still fail; 5 min / 15 s mirrors the spec's recommended budget.
		DataForgeStatusResponse? lastStatus = null;
		CallToolResult? lastStatusResult = null;
		for (int attempt = 0; attempt < ReadinessAttempts; attempt++) {
			cancellationToken.ThrowIfCancellationRequested();
			lastStatusResult = await CallDataForgeToolAsync(
				session, StatusToolName, environmentName, cancellationToken);
			lastStatus = TryDeserialize<DataForgeStatusResponse>(lastStatusResult);
			if (IsIndexReady(lastStatus)) {
				return;
			}

			if (attempt < ReadinessAttempts - 1) {
				await Task.Delay(ReadinessDelay, cancellationToken);
			}
		}

		// Emit the COMPLETE last status payload before failing: FluentAssertions truncates long
		// assertion messages, so the full readiness state is written to TestContext.Out (not truncated).
		if (lastStatusResult is not null) {
			WriteToolDiagnostics(StatusToolName, lastStatusResult);
		}

		int totalSeconds = (int)(ReadinessAttempts * ReadinessDelay.TotalSeconds);
		Assert.Fail(
			$"DataForge similarity index on '{environmentName}' did not become Ready within {totalSeconds}s " +
			$"({ReadinessAttempts} dataforge-status polls every {ReadinessDelay.TotalSeconds:0}s after " +
			$"dataforge-initialize). Last status: success={lastStatus?.Success}, " +
			$"status={lastStatus?.Status?.Status ?? "<none>"}, " +
			$"data-structure-readiness={lastStatus?.Health?.DataStructureReadiness}, " +
			$"lookups-readiness={lastStatus?.Health?.LookupsReadiness}. " +
			$"See test output for the full status payload.");
	}

	private const int ReadinessAttempts = 20;
	private static readonly System.TimeSpan ReadinessDelay = System.TimeSpan.FromSeconds(15);

	private static async Task<CallToolResult> CallDataForgeToolAsync(
		McpServerSession session,
		string toolName,
		string environmentName,
		CancellationToken cancellationToken) {
		return await session.CallToolAsync(
			toolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName
				}
			},
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
