using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Common.DataForge;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature("dataforge")]
[NonParallelizable]
// ENG-92457 resolved: the DataForge readiness gate no longer hangs on a freshly-deployed sandbox.
// The data-structure index now becomes Ready (status/context/get-table-columns/initialize/update pass
// in ~13s each) instead of burning the ~300s gate ceiling — confirmed by run 15643975 (whole fixture
// ~38s vs the former ~900s). The fixture-level [Ignore] is therefore removed so those reads run.
//
// ENG-92557: the three similarity-search reads (find-tables, find-lookups, get-relations) no longer
// carry a bare [Ignore] under the now-closed ENG-92147. Reproduced against a stand (d2, CrtDataForge
// present): the DataForge service is an external OAuth-gated microservice, so the reads only return
// Success=true on a stand wired to a DataForge tier (DataForgeServiceUrl + IdentityServer* settings,
// an OAuth client with the use_enrichment scope) AND with a seeded similarity index. Without that
// wiring the maintenance service reports Unavailable and every read returns a structured
// Success=false — an environment precondition, NOT a clio defect (the clio-side Success=false
// contract is covered by unit tests, ENG-92147). Table similarity search additionally returns a
// service-side 404 even on a fully wired stand (ENG-87092, open). The fixtures therefore (a) attempt
// a best-effort index warm-up in arrange when McpE2E:DataForge:InitializeAndWait is on, then (b) run
// SkipUnlessServiceServedRead: assert the happy path where the service actually serves the read, and
// skip deterministically (with the service's own error) where it cannot — never a bare [Ignore],
// never a false red.
public sealed class DataForgeToolE2ETests {
	private const string StatusToolName = DataForgeTool.DataForgeStatusToolName;
	private const string FindTablesToolName = DataForgeTool.DataForgeFindTablesToolName;
	private const string FindLookupsToolName = DataForgeTool.DataForgeFindLookupsToolName;
	private const string GetRelationsToolName = DataForgeTool.DataForgeGetRelationsToolName;
	private const string ColumnsToolName = DataForgeTool.DataForgeGetTableColumnsToolName;
	private const string ContextToolName = DataForgeTool.DataForgeContextToolName;
	private const string InitializeToolName = DataForgeTool.DataForgeInitializeToolName;
	private const string UpdateToolName = DataForgeTool.DataForgeUpdateToolName;

	[Test]
	[Description("Starts the real clio MCP server, invokes dataforge-status against the configured sandbox environment, and verifies the structured status response succeeds.")]
	[AllureTag(StatusToolName)]
	[AllureName("DataForge status returns structured health and maintenance state")]
	[AllureDescription("Uses the real clio MCP server to call dataforge-status against the configured reachable sandbox environment and verifies that the response includes both health and maintenance-status payloads.")]
	public async Task DataForgeStatus_Should_Return_Structured_Status_Response() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3), requireReachableEnvironment: true);

		// Act
		CallToolResult callResult = await CallToolAsync(
			arrangeContext,
			StatusToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName
			});
		DataForgeStatusResponse response = DeserializeStructuredContent<DataForgeStatusResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "dataforge-status should return a structured success payload for a reachable configured environment");
		response.Success.Should().BeTrue(
			because: "the status tool should succeed when the underlying service health and maintenance reads are reachable");
		response.Health.Should().NotBeNull(
			because: "the status tool should include the detailed Data Forge health payload");
		response.Status.Should().NotBeNull(
			because: "the status tool should include the Creatio maintenance status payload");
		response.Status!.Status.Should().NotBeNullOrWhiteSpace(
			because: "the maintenance status payload should expose a human-readable status value");
	}

	[Test]
	[Description("Starts the real clio MCP server with poisoned proxy env vars, invokes dataforge-status against the configured sandbox environment, and verifies the Data Forge call still reaches the real service instead of failing with a masked syssetting error.")]
	[AllureTag(StatusToolName)]
	[AllureName("DataForge status ignores poisoned proxy env vars")]
	[AllureDescription("Uses the real clio MCP server to call dataforge-status while HTTP_PROXY, HTTPS_PROXY, and ALL_PROXY point to 127.0.0.1:9, verifying that clio's MCP-mode proxy neutralization (HttpClient.DefaultProxy) still lets the call return the real structured response for a reachable environment.")]
	public async Task DataForgeStatus_Should_Ignore_Poisoned_Proxy_Environment_Variables() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();

		// Resolve and reachability-probe the environment with a clean process environment FIRST.
		// The poisoned proxy variables below would otherwise also reach the ping-app readiness probe
		// (it spawns a normal clio child that honours HTTP(S)_PROXY), so probing after poisoning would
		// always route through the dead 127.0.0.1:9 proxy and self-skip the test as "not reachable".
		string environmentName = await ResolveReachableEnvironmentAsync(settings);

		string? originalHttpProxy = Environment.GetEnvironmentVariable("HTTP_PROXY");
		string? originalHttpsProxy = Environment.GetEnvironmentVariable("HTTPS_PROXY");
		string? originalAllProxy = Environment.GetEnvironmentVariable("ALL_PROXY");
		string? originalNoProxy = Environment.GetEnvironmentVariable("NO_PROXY");

		try {
			// Poison the process proxy vars inside the try (after capturing the originals above) so the
			// finally below always restores them even if a Set were to throw — poison must never leak to
			// sibling tests. The MCP server is started next with these vars in place: it is the SUT, and
			// clio's MCP-mode proxy neutralization (Program.cs, HttpClient.DefaultProxy) must still let the
			// Data Forge call reach the service. Reachability was already verified above with a clean
			// environment, so do not re-probe here (that probe is not proxy-safe and would self-skip).
			Environment.SetEnvironmentVariable("HTTP_PROXY", "http://127.0.0.1:9");
			Environment.SetEnvironmentVariable("HTTPS_PROXY", "http://127.0.0.1:9");
			Environment.SetEnvironmentVariable("ALL_PROXY", "http://127.0.0.1:9");
			Environment.SetEnvironmentVariable("NO_PROXY", null);

			await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3), requireReachableEnvironment: false);

			// Act
			CallToolResult callResult = await CallToolAsync(
				arrangeContext,
				StatusToolName,
				new Dictionary<string, object?> {
					["environment-name"] = environmentName
				});
			DataForgeStatusResponse response = DeserializeStructuredContent<DataForgeStatusResponse>(callResult);

			// Assert
			callResult.IsError.Should().NotBeTrue(
				because: "clio's MCP-mode proxy neutralization should bypass poisoned process proxy variables for the real service call");
			response.Success.Should().BeTrue(
				because: "the real Data Forge status call should still succeed when proxy env vars are temporarily neutralized");
			response.Health.Should().NotBeNull(
				because: "the status tool should still return the health payload under poisoned proxy conditions");
			response.Status.Should().NotBeNull(
				because: "the status tool should still return the maintenance payload under poisoned proxy conditions");
		} finally {
			Environment.SetEnvironmentVariable("HTTP_PROXY", originalHttpProxy);
			Environment.SetEnvironmentVariable("HTTPS_PROXY", originalHttpsProxy);
			Environment.SetEnvironmentVariable("ALL_PROXY", originalAllProxy);
			Environment.SetEnvironmentVariable("NO_PROXY", originalNoProxy);
		}
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes dataforge-find-tables with a Contact-style query against the configured sandbox environment, and verifies the structured table search response succeeds.")]
	[AllureTag(FindTablesToolName)]
	[AllureName("DataForge find-tables returns table matches for Contact-style terms")]
	[AllureDescription("Uses the real clio MCP server to call dataforge-find-tables for a Contact-style query against the configured reachable sandbox environment and verifies that the structured response includes at least one named table match.")]
	public async Task DataForgeFindTables_Should_Return_Table_Matches() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(8), requireReachableEnvironment: true);
		await EnsureSimilarityIndexReadyAsync(settings, arrangeContext);

		// Act
		CallToolResult callResult = await CallToolAsync(
			arrangeContext,
			FindTablesToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName,
				["query"] = "contact"
			});
		DataForgeFindTablesResponse response = DeserializeStructuredContent<DataForgeFindTablesResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "dataforge-find-tables should return a structured payload, not an MCP protocol error, for a reachable configured environment");
		SkipUnlessServiceServedRead(response.Success, response.Error, FindTablesToolName, arrangeContext.EnvironmentName);
		response.Success.Should().BeTrue(
			because: "table similarity searches should succeed for a valid environment and non-empty query once the service served the read");
		response.SimilarTables.Should().Contain(table => !string.IsNullOrWhiteSpace(table.Name),
			because: "the response should contain at least one named table match for a Contact-style query");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes dataforge-find-lookups against the configured sandbox environment, and verifies the structured lookup search response succeeds.")]
	[AllureTag(FindLookupsToolName)]
	[AllureName("DataForge find-lookups returns a structured lookup response")]
	[AllureDescription("Uses the real clio MCP server to call dataforge-find-lookups against the configured reachable sandbox environment and verifies that the tool returns a structured successful payload instead of an MCP invocation error.")]
	public async Task DataForgeFindLookups_Should_Return_Structured_Response() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(8), requireReachableEnvironment: true);
		await EnsureSimilarityIndexReadyAsync(settings, arrangeContext);

		// Act
		CallToolResult callResult = await CallToolAsync(
			arrangeContext,
			FindLookupsToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName,
				["query"] = "industry"
			});
		DataForgeFindLookupsResponse response = DeserializeStructuredContent<DataForgeFindLookupsResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "dataforge-find-lookups should return a structured payload, not an MCP protocol error, for a reachable configured environment");
		SkipUnlessServiceServedRead(response.Success, response.Error, FindLookupsToolName, arrangeContext.EnvironmentName);
		response.Success.Should().BeTrue(
			because: "lookup similarity searches should succeed for a valid environment and non-empty query once the service served the read");
		response.SimilarLookups.Should().NotBeNull(
			because: "the tool should always return a structured lookup collection even when the environment has no matches");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes dataforge-get-relations for Contact to Account against the configured sandbox environment, and verifies the structured relations response succeeds.")]
	[AllureTag(GetRelationsToolName)]
	[AllureName("DataForge get-relations returns relation paths between Contact and Account")]
	[AllureDescription("Uses the real clio MCP server to call dataforge-get-relations for Contact and Account against the configured reachable sandbox environment and verifies that the structured response contains at least one relation path.")]
	public async Task DataForgeGetRelations_Should_Return_Relation_Paths() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(8), requireReachableEnvironment: true);
		await EnsureSimilarityIndexReadyAsync(settings, arrangeContext);

		// Act
		CallToolResult callResult = await CallToolAsync(
			arrangeContext,
			GetRelationsToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName,
				["source-table"] = "Contact",
				["target-table"] = "Account"
			});
		DataForgeRelationsResponse response = DeserializeStructuredContent<DataForgeRelationsResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "dataforge-get-relations should return a structured payload, not an MCP protocol error, for a reachable configured environment");
		SkipUnlessServiceServedRead(response.Success, response.Error, GetRelationsToolName, arrangeContext.EnvironmentName);
		response.Success.Should().BeTrue(
			because: "relation path reads should succeed for a valid environment and known table pair once the service served the read");
		response.Relations.Should().NotBeEmpty(
			because: "Contact and Account should expose at least one relation path in the sandbox model");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes dataforge-get-table-columns for Contact against the configured sandbox environment, and verifies the structured columns response succeeds.")]
	[AllureTag(ColumnsToolName)]
	[AllureName("DataForge get-table-columns returns Contact columns")]
	[AllureDescription("Uses the real clio MCP server to call dataforge-get-table-columns for Contact against the configured reachable sandbox environment and verifies that the structured response contains common Contact columns.")]
	public async Task DataForgeGetTableColumns_Should_Return_Contact_Columns() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3), requireReachableEnvironment: true);

		// Act
		CallToolResult callResult = await CallToolAsync(
			arrangeContext,
			ColumnsToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName,
				["table-name"] = "Contact"
			});
		DataForgeColumnsResponse response = DeserializeStructuredContent<DataForgeColumnsResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "dataforge-get-table-columns should return a structured success payload for a reachable configured environment");
		response.Success.Should().BeTrue(
			because: "Contact runtime schema reads should succeed through the shared by-name runtime reader");
		response.Columns.Should().Contain(column => column.Name == "Name",
			because: "Contact should expose its primary display column through the Data Forge columns tool");
		response.Columns.Should().Contain(column => column.Name == "Account",
			because: "Contact should expose its Account lookup through the Data Forge columns tool");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes dataforge-context against the configured sandbox environment, and verifies that aggregation succeeds with table-column coverage enabled.")]
	[AllureTag(ContextToolName)]
	[AllureName("DataForge context aggregates with table-column coverage")]
	[AllureDescription("Uses the real clio MCP server to call dataforge-context against the configured reachable sandbox environment and verifies that the structured response succeeds without aggregation failure while reporting table-column coverage.")]
	public async Task DataForgeContext_Should_Return_TableColumn_Coverage() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3), requireReachableEnvironment: true);

		// Act
		CallToolResult callResult = await CallToolAsync(
			arrangeContext,
			ContextToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName,
				["requirement-summary"] = "find customer-related model context",
				["candidate-terms"] = new[] { "contact" }
			});
		DataForgeContextResponse response = DeserializeStructuredContent<DataForgeContextResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "dataforge-context should return a structured response for a reachable configured environment");
		response.Success.Should().BeTrue(
			because: "the context tool should aggregate health, status, and columns without throwing");
		response.Coverage.Columns.Should().BeTrue(
			because: "the aggregated context payload should report successful table-column enrichment for Contact");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes dataforge-initialize against the configured sandbox environment, and verifies the structured maintenance response reports a scheduled mutation.")]
	[AllureTag(InitializeToolName)]
	[AllureName("DataForge initialize schedules maintenance work")]
	[AllureDescription("Uses the real clio MCP server to call dataforge-initialize against the configured sandbox environment, behind the standard destructive-test opt-in, and verifies that the response reports scheduled maintenance work.")]
	public async Task DataForgeInitialize_Should_Return_Scheduled_Response() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to run destructive Data Forge MCP end-to-end tests.");
		}

		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3), requireReachableEnvironment: true);

		// Act
		CallToolResult callResult = await CallToolAsync(
			arrangeContext,
			InitializeToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName
			});
		DataForgeMaintenanceResponse response = DeserializeStructuredContent<DataForgeMaintenanceResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "dataforge-initialize should return a structured maintenance payload instead of an MCP invocation error");
		response.Success.Should().BeTrue(
			because: "the initialize tool should report success when the maintenance request is accepted by Creatio");
		response.Status.Status.Should().Be("Scheduled",
			because: "the initialize tool should report that maintenance work was scheduled");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes dataforge-update against the configured sandbox environment, and verifies the structured maintenance response reports a scheduled mutation.")]
	[AllureTag(UpdateToolName)]
	[AllureName("DataForge update schedules maintenance work")]
	[AllureDescription("Uses the real clio MCP server to call dataforge-update against the configured sandbox environment, behind the standard destructive-test opt-in, and verifies that the response reports scheduled maintenance work.")]
	public async Task DataForgeUpdate_Should_Return_Scheduled_Response() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to run destructive Data Forge MCP end-to-end tests.");
		}

		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3), requireReachableEnvironment: true);

		// Act
		CallToolResult callResult = await CallToolAsync(
			arrangeContext,
			UpdateToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName
			});
		DataForgeMaintenanceResponse response = DeserializeStructuredContent<DataForgeMaintenanceResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "dataforge-update should return a structured maintenance payload instead of an MCP invocation error");
		response.Success.Should().BeTrue(
			because: "the update tool should report success when the maintenance request is accepted by Creatio");
		response.Status.Status.Should().Be("Scheduled",
			because: "the update tool should report that maintenance work was scheduled");
	}

	private static TResponse DeserializeStructuredContent<TResponse>(CallToolResult callResult) {
		if (TrySerializeToJsonElement(callResult.StructuredContent, out JsonElement structuredElement) &&
			TryParseDataForgeResponse<TResponse>(structuredElement, out TResponse? structuredResult)) {
			return structuredResult!;
		}

		if (TrySerializeToJsonElement(callResult.Content, out JsonElement contentElement) &&
			TryParseDataForgeResponse<TResponse>(contentElement, out TResponse? contentResult)) {
			return contentResult!;
		}

		if (callResult.IsError == true) {
			string errorText = string.Join(
				Environment.NewLine,
				(callResult.Content ?? []).Select(c => c.ToString()));
			throw new InvalidOperationException(
				$"MCP tool returned an invocation error: {errorText}");
		}

		throw new InvalidOperationException("MCP tool did not return a structured response payload.");
	}

	private static bool TrySerializeToJsonElement(object? value, out JsonElement element) {
		if (value is null) {
			element = default;
			return false;
		}
		element = JsonSerializer.SerializeToElement(value);
		return true;
	}

	private static bool TryParseDataForgeResponse<TResponse>(JsonElement element, out TResponse? result) {
		if (element.ValueKind == JsonValueKind.Array) {
			foreach (JsonElement item in element.EnumerateArray()) {
				if (TryGetTextPayload(item, out string? text) &&
					!string.IsNullOrWhiteSpace(text) &&
					TryDeserializeJson<TResponse>(text, out result)) {
					return true;
				}
			}
		}

		if (element.ValueKind == JsonValueKind.String) {
			string? textPayload = element.GetString();
			if (!string.IsNullOrWhiteSpace(textPayload) &&
				TryDeserializeJson<TResponse>(textPayload, out result)) {
				return true;
			}
		}

		if (TryDeserializeJson<TResponse>(element.GetRawText(), out result)) {
			return true;
		}

		result = default;
		return false;
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

	private static bool TryDeserializeJson<TResponse>(string json, out TResponse? result) {
		try {
			result = JsonSerializer.Deserialize<TResponse>(json);
			return result is not null;
		} catch (JsonException) {
			result = default;
			return false;
		}
	}

	private static async Task<CallToolResult> CallToolAsync(
		ArrangeContext arrangeContext,
		string toolName,
		Dictionary<string, object?> args) {
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(toolName,
			because: "the production Data Forge tool must be advertised before the end-to-end call can be executed");
		return await arrangeContext.Session.CallToolAsync(
			toolName,
			new Dictionary<string, object?> {
				["args"] = args
			},
			arrangeContext.CancellationTokenSource.Token);
	}

	/// <summary>
	/// Best-effort arrange warm-up for the similarity-search reads (find-tables, find-lookups,
	/// get-relations): on a freshly-deployed stand the similarity index is not built, so these reads
	/// return <c>Success=false</c> until <c>dataforge-initialize</c> has run and the index is ready
	/// (ENG-92147, Step 2A). When <c>McpE2E:DataForge:InitializeAndWait</c> is off this is a no-op,
	/// keeping non-DataForge runs and already-warm stands unaffected and the destructive initialize
	/// opt-in. The gate is best-effort: it never fails the test on a stand that cannot become ready
	/// (e.g. one not wired to a DataForge tier). The skip-vs-assert decision is taken after the read
	/// by <see cref="SkipUnlessServiceServedRead"/> from the service's actual response (ENG-92557).
	/// </summary>
	private static async Task EnsureSimilarityIndexReadyAsync(McpE2ESettings settings, ArrangeContext arrangeContext) {
		if (!settings.DataForge.InitializeAndWait) {
			return;
		}

		arrangeContext.EnvironmentName.Should().NotBeNullOrWhiteSpace(
			because: "the DataForge readiness arrange needs a reachable sandbox environment to initialize the similarity index against");
		bool becameReady = await DataForgeReadinessGate.EnsureIndexReadyAsync(
			arrangeContext.Session,
			arrangeContext.EnvironmentName!,
			arrangeContext.CancellationTokenSource.Token);
		if (!becameReady) {
			TestContext.Out.WriteLine(
				$"[dataforge] similarity index did not warm up to Ready on '{arrangeContext.EnvironmentName}'; " +
				"the per-read service-state guard will decide skip-vs-assert from the read response.");
		}
	}

	/// <summary>
	/// Deterministic service-state skip-guard for the DataForge similarity-search reads
	/// (find-tables, find-lookups, get-relations). The DataForge service is an external OAuth-gated
	/// microservice, so a read only returns <c>Success=true</c> on a stand wired to a DataForge tier
	/// (<c>DataForgeServiceUrl</c> + <c>IdentityServer*</c> settings, the OAuth client carrying the
	/// <c>use_enrichment</c> scope) whose similarity index has been seeded. Without that wiring the
	/// maintenance service reports <c>Unavailable</c> and every read returns a structured
	/// <c>Success=false</c> — an environment precondition, not a clio defect (the clio-side
	/// <c>Success=false</c> contract is covered by unit tests under ENG-92147). Table similarity
	/// search additionally returns a service-side 404 even on a fully wired stand (ENG-87092, open).
	/// So when the service itself reports it could not serve the read, skip deterministically with the
	/// service's own error — mirroring the existing reachability guard
	/// (<see cref="ResolveReachableEnvironmentAsync"/> → <see cref="Assert.Ignore(string)"/>) — rather
	/// than failing on an environment the test cannot control. A protocol-level error is still a
	/// failure and is asserted separately by the caller (ENG-92557).
	/// </summary>
	/// <param name="success">The <c>success</c> flag from the structured DataForge read response.</param>
	/// <param name="error">The structured error payload from the read response, when present.</param>
	/// <param name="toolName">The DataForge tool that produced the response, for the skip diagnostic.</param>
	/// <param name="environmentName">The sandbox environment the read targeted, for the skip diagnostic.</param>
	private static void SkipUnlessServiceServedRead(
		bool success, DataForgeErrorResult? error, string toolName, string? environmentName) {
		if (success) {
			return;
		}

		Assert.Ignore(
			$"DataForge '{toolName}' could not be served on '{environmentName}': the service returned a structured " +
			$"Success=false (error: {error?.Code ?? "<none>"} — {error?.Message ?? "<none>"}). The similarity index is " +
			"not queryable here — this needs a DataForge-wired stand (DataForgeServiceUrl + IdentityServer* settings + " +
			"use_enrichment scope + a seeded index); table similarity search additionally has an open service-side 404 " +
			"(ENG-87092). Skipping deterministically rather than asserting against an unavailable service.");
	}

	private static async Task<ArrangeContext> ArrangeAsync(
		McpE2ESettings settings,
		TimeSpan timeout,
		bool requireReachableEnvironment) {
		CancellationTokenSource cancellationTokenSource = new(timeout);
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		string? environmentName = requireReachableEnvironment
			? await ResolveReachableEnvironmentAsync(settings)
			: settings.Sandbox.EnvironmentName;
		return new ArrangeContext(session, cancellationTokenSource, environmentName);
	}

	private static async Task<string> ResolveReachableEnvironmentAsync(McpE2ESettings settings) {
		string? configuredEnvironmentName = settings.Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(configuredEnvironmentName)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName to run Data Forge MCP E2E tests.");
		}

		if (!await CanReachEnvironmentAsync(settings, configuredEnvironmentName!)) {
			Assert.Ignore($"Data Forge MCP E2E requires a reachable sandbox environment. '{configuredEnvironmentName}' was not reachable.");
		}

		return configuredEnvironmentName!;
	}

	private static async Task<bool> CanReachEnvironmentAsync(McpE2ESettings settings, string environmentName) {
		ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
			settings,
			["ping-app", "-e", environmentName]);
		return result.ExitCode == 0;
	}

	private sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource,
		string? EnvironmentName) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}
}
