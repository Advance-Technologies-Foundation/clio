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
[AllureNUnit]
[AllureFeature("dataforge")]
[NonParallelizable]
public sealed class DataForgeToolE2ETests {
	private const string HealthToolName = DataForgeTool.DataForgeHealthToolName;
	private const string StatusToolName = DataForgeTool.DataForgeStatusToolName;
	private const string FindTablesToolName = DataForgeTool.DataForgeFindTablesToolName;
	private const string FindLookupsToolName = DataForgeTool.DataForgeFindLookupsToolName;
	private const string GetRelationsToolName = DataForgeTool.DataForgeGetRelationsToolName;
	private const string ColumnsToolName = DataForgeTool.DataForgeGetTableColumnsToolName;
	private const string ContextToolName = DataForgeTool.DataForgeContextToolName;
	private const string InitializeToolName = DataForgeTool.DataForgeInitializeToolName;
	private const string UpdateToolName = DataForgeTool.DataForgeUpdateToolName;

	[Test]
	[Description("Starts the real clio MCP server, invokes dataforge-health against the configured sandbox environment, and verifies the structured health response succeeds.")]
	[AllureTag(HealthToolName)]
	[AllureName("DataForge health returns a structured successful health payload")]
	[AllureDescription("Uses the real clio MCP server to call dataforge-health against the configured reachable sandbox environment and verifies that the structured response reports successful liveness and readiness probes.")]
	public async Task DataForgeHealth_Should_Return_Structured_Health_Response() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3), requireReachableEnvironment: true);

		// Act
		CallToolResult callResult = await CallToolAsync(
			arrangeContext,
			HealthToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName
			});
		DataForgeHealthResponse response = DeserializeStructuredContent<DataForgeHealthResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "dataforge-health should return a structured success payload for a reachable configured environment");
		response.Success.Should().BeTrue(
			because: "the structured Data Forge health response should report success when the service probes are healthy");
		response.Health.Should().NotBeNull(
			because: "the health tool should return the detailed liveness/readiness payload");
		response.Health!.Liveness.Should().BeTrue(
			because: "the Data Forge liveness probe should succeed for the configured test environment");
		response.Health.Readiness.Should().BeTrue(
			because: "the Data Forge readiness probe should succeed for the configured test environment");
	}

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
	[Description("Starts the real clio MCP server, invokes dataforge-find-tables with a Contact-style query against the configured sandbox environment, and verifies the structured table search response succeeds.")]
	[AllureTag(FindTablesToolName)]
	[AllureName("DataForge find-tables returns table matches for Contact-style terms")]
	[AllureDescription("Uses the real clio MCP server to call dataforge-find-tables for a Contact-style query against the configured reachable sandbox environment and verifies that the structured response includes at least one named table match.")]
	public async Task DataForgeFindTables_Should_Return_Table_Matches() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3), requireReachableEnvironment: true);

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
			because: "dataforge-find-tables should return a structured success payload for a reachable configured environment");
		response.Success.Should().BeTrue(
			because: "table similarity searches should succeed for a valid environment and non-empty query");
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
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3), requireReachableEnvironment: true);

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
			because: "dataforge-find-lookups should return a structured success payload for a reachable configured environment");
		response.Success.Should().BeTrue(
			because: "lookup similarity searches should succeed for a valid environment and non-empty query");
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
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3), requireReachableEnvironment: true);

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
			because: "dataforge-get-relations should return a structured success payload for a reachable configured environment");
		response.Success.Should().BeTrue(
			because: "relation path reads should succeed for a valid environment and known table pair");
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
		string structuredContentJson = JsonSerializer.Serialize(callResult.StructuredContent);
		return JsonSerializer.Deserialize<TResponse>(structuredContentJson)
			?? throw new InvalidOperationException("MCP tool did not return a structured response payload.");
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
