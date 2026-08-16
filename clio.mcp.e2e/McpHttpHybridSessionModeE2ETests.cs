using Allure.Net.Commons;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Process-level compatibility coverage for the hybrid MCP HTTP transport introduced with the
/// ModelContextProtocol 2.2 upgrade. The tests need no Creatio environment: they start the real
/// <c>clio mcp-http</c> executable and exercise protocol negotiation plus tool discovery.
/// </summary>
[TestFixture]
[Category("E2E")]
[Category("McpE2E.NoEnvironment")]
// [AllureNUnit] is intentionally omitted: its async synchronization context deadlocks disposal of
// the real child HTTP process. The metadata and explicit Allure steps below remain available to CI.
[AllureFeature("MCP HTTP hybrid transport")]
[NonParallelizable]
public sealed class McpHttpHybridSessionModeE2ETests {
	private const string ModernProtocolVersion = "2026-07-28";
	private const string LegacyProtocolVersion = "2025-11-25";
	private McpE2ESettings _settings = null!;

	[SetUp]
	public void SetUp() {
		_settings = TestConfiguration.Load();
	}

	[Test]
	[AllureTag("mcp-http")]
	[AllureName("Hybrid HTTP keeps modern calls stateless while isolating a live legacy session")]
	[AllureDescription("Starts the real clio mcp-http process, keeps a CAADT-compatible legacy initialize session alive, then proves a modern discovery-first request remains stateless and receives the compact contract index rather than inheriting legacy client identity.")]
	[Description("A single real mcp-http endpoint keeps default 2026-07-28 clients stateless and compact-index aware while preserving a usable stateful legacy 2025-11-25 session with its compatibility response shape.")]
	public async Task HttpEndpoint_ShouldServeModernAndLegacyClients_WhenHybridSessionModeIsEnabled() {
		// Arrange
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(2));
		await using McpHttpServerSession server =
			await StartServerAsync(_settings, cts.Token);
		await using McpClient legacyClient = await server.ConnectAsync(
			platformApiKey: null,
			integrationCredentialsBase64: null,
			cancellationToken: cts.Token,
			protocolVersion: LegacyProtocolVersion,
			clientInfo: new Implementation { Name = "mcp_client", Version = "1.0" });
		await using McpClient modernClient = await server.ConnectAsync(
			platformApiKey: null,
			integrationCredentialsBase64: null,
			cancellationToken: cts.Token);

		// Act
		IList<McpClientTool> legacyTools = await ListToolsAsync(legacyClient, "legacy", cts.Token);
		ToolContractGetResponse legacyContractsBefore = await GetDefaultContractsAsync(
			legacyClient, "legacy before modern request", cts.Token);
		IList<McpClientTool> modernTools = await ListToolsAsync(modernClient, "modern", cts.Token);
		ToolContractGetResponse modernContracts = await GetDefaultContractsAsync(
			modernClient, "modern", cts.Token);
		ToolContractGetResponse legacyContractsAfter = await GetDefaultContractsAsync(
			legacyClient, "legacy after modern request", cts.Token);

		// Assert
		AllureApi.Step("Assert modern protocol negotiation", () =>
			modernClient.NegotiatedProtocolVersion.Should().Be(ModernProtocolVersion,
				because: "the default SDK 2.2 client must remain on discovery-first 2026-07-28 instead of being forced to downgrade"));
		AllureApi.Step("Assert modern transport is stateless", () =>
			modernClient.SessionId.Should().BeNull(
				because: "the 2026-07-28 protocol removed MCP HTTP sessions and must not receive an Mcp-Session-Id"));
		AllureApi.Step("Assert modern tool discovery remains usable", () =>
			modernTools.Should().NotBeEmpty(
				because: "the stateless modern half of the hybrid endpoint must remain fully usable"));
		AllureApi.Step("Assert modern contract call succeeds during legacy session", () =>
			modernContracts.Success.Should().BeTrue(
				because: "the modern request must complete while the legacy stateful session remains alive"));
		AllureApi.Step("Assert modern contract call returns compact index", () =>
			modernContracts.Index.Should().NotBeNullOrEmpty(
				because: "a modern client with no legacy identity must receive the compact discovery index"));
		AllureApi.Step("Assert modern contract call excludes legacy full tools", () =>
			modernContracts.Tools.Should().BeNullOrEmpty(
				because: "the modern stateless request must not inherit the live legacy client's full-contract response shape"));

		AllureApi.Step("Assert legacy protocol negotiation", () =>
			legacyClient.NegotiatedProtocolVersion.Should().Be(LegacyProtocolVersion,
				because: "an explicitly pinned initialize client must retain its requested supported protocol version"));
		AllureApi.Step("Assert legacy transport retains a session", () =>
			legacyClient.SessionId.Should().NotBeNullOrWhiteSpace(
				because: "legacy initialize clients need a stateful Mcp-Session-Id for server-to-client compatibility"));
		AllureApi.Step("Assert legacy tool discovery remains usable", () =>
			legacyTools.Should().NotBeEmpty(
				because: "the stateful legacy half of the same endpoint must remain fully usable"));
		foreach ((string phase, ToolContractGetResponse contracts) in new[] {
			("before modern request", legacyContractsBefore),
			("after modern request", legacyContractsAfter)
		}) {
			AllureApi.Step($"Assert legacy contract call succeeds {phase}", () =>
				contracts.Success.Should().BeTrue(
					because: "the legacy compatibility call must succeed on both sides of the interleaved modern request"));
			AllureApi.Step($"Assert legacy full tools remain available {phase}", () =>
				contracts.Tools.Should().NotBeNullOrEmpty(
					because: "the CAADT-compatible mcp_client identity must retain its full legacy no-names response shape"));
			AllureApi.Step($"Assert legacy compact index remains suppressed {phase}", () =>
				contracts.Index.Should().BeNullOrEmpty(
					because: "the legacy session must not inherit the modern client's compact-index response shape"));
		}
	}

	private static async Task<McpHttpServerSession> StartServerAsync(
		McpE2ESettings settings, CancellationToken cancellationToken) =>
		await AllureApi.Step("Arrange a real clio mcp-http process", async () =>
			await McpHttpServerSession.StartAsync(settings, platformApiKey: null, cancellationToken));

	private static async Task<IList<McpClientTool>> ListToolsAsync(
		McpClient client, string clientKind, CancellationToken cancellationToken) =>
		await AllureApi.Step($"List tools through the {clientKind} hybrid client", async () =>
			await client.ListToolsAsync(cancellationToken: cancellationToken));

	private static async Task<ToolContractGetResponse> GetDefaultContractsAsync(
		McpClient client, string clientKind, CancellationToken cancellationToken) {
		CallToolResult callResult = await AllureApi.Step(
			$"Call get-tool-contract with no names through the {clientKind} hybrid client",
			async () => await client.CallToolAsync(
				ToolContractGetTool.ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?>()
				},
				cancellationToken: cancellationToken));
		AllureApi.Step($"Assert the {clientKind} contract call has no protocol error", () =>
			callResult.IsError.Should().NotBeTrue(
				because: $"the {clientKind} get-tool-contract call must return a normal MCP result"));
		return EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(callResult);
	}
}
