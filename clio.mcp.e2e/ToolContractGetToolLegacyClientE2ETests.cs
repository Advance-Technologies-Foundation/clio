using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// ENG-93885: end-to-end coverage for the CAADT 1.4.0 legacy stdio client identity
/// (<c>clientInfo = {"name": "mcp_client", "version": "1.0"}</c>, the exact pair sent by
/// <c>runtime/scripts/mcp_client.py</c> line 181 at that tag). That client hard-crashes on the compact
/// "index" shape from a no-args <c>get-tool-contract</c> call and cannot be patched, so
/// <see cref="ToolContractGetTool.IsLegacyStdioClient"/> flips it back onto the legacy full "tools" shape.
/// This fixture starts its own shared MCP server session with that exact clientInfo (via
/// <see cref="ConfigureMcpServerSettings"/>) so the real "initialize" handshake carries it end-to-end,
/// instead of asserting the detection helper in isolation.
/// </summary>
/// <remarks>
/// The real CAADT 1.4.0 Python client deliberately never sends <c>notifications/initialized</c> after
/// <c>initialize</c>. The C# SDK's <c>McpClient.CreateAsync</c> used by this harness always completes the
/// handshake with that notification as part of well-behaved client startup, and there is no supported hook
/// to suppress it. The fix under test does not depend on its absence — it only inspects <c>clientInfo</c>
/// captured during "initialize" — so this is a harmless divergence from the real client, not a gap in this
/// coverage. Reproducing that byte-for-byte quirk is out of scope for this SDK-based harness; it is covered
/// separately by a manual run of the real Python client against a real build.
/// </remarks>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(ToolContractGetTool.ToolName)]
[NonParallelizable]
public sealed class ToolContractGetToolLegacyClientE2ETests : McpContractFixtureBase {

	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		settings.ClientInfo = new Implementation {
			Name = "mcp_client",
			Version = "1.0"
		};
	}

	[Test]
	[Description("The CAADT 1.4.0 legacy stdio clientInfo gets the full tools array (not the compact index) from a no-args get-tool-contract call, with real per-tool schemas attached.")]
	[AllureTag(ToolContractGetTool.ToolName)]
	[AllureName("get-tool-contract returns the full tools shape for the legacy CAADT stdio client identity")]
	public async Task ToolContractGet_Should_Return_Full_Tools_For_Legacy_Stdio_Client() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		ToolContractGetResponse response = await CallContractAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?>());

		// Assert
		response.Success.Should().BeTrue(
			because: "a no-args get-tool-contract call must still succeed for the legacy stdio client identity");
		response.Tools.Should().NotBeNullOrEmpty(
			because: "the legacy CAADT 1.4.0 client hard-crashes on the compact index and must receive the full tools array instead");
		response.Index.Should().BeNull(
			because: "the legacy shape is the full tools array, not the compact discovery index");
		response.Tools!.Should().Contain(tool =>
				tool.InputSchema != null && tool.InputSchema.Properties != null && tool.InputSchema.Properties.Count > 0,
			because: "the legacy client must receive real per-tool input schemas, not just names, to avoid the historical crash");
	}

	[Test]
	[Description("A normal follow-up tool call still succeeds through the same legacy-client session, proving the fix does not otherwise disturb this client.")]
	[AllureTag(ToolContractGetTool.ToolName)]
	[AllureName("legacy stdio client identity can still make an ordinary tool call after get-tool-contract")]
	public async Task ToolContractGet_Should_Not_Break_Ordinary_Tool_Calls_For_Legacy_Stdio_Client() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse guidanceResponse = await CallGuidanceAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			"page-schema-handlers");

		// Assert
		guidanceResponse.Success.Should().BeTrue(
			because: "an ordinary no-environment tool call should still succeed normally for the legacy stdio client identity");
		guidanceResponse.Article.Should().NotBeNull(
			because: "a successful guidance lookup should still return the resolved article payload for this client");
	}

	private static async Task<ToolContractGetResponse> CallContractAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		IReadOnlyDictionary<string, object?> arguments) {
		CallToolResult callResult = await session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?> { ["args"] = arguments },
			cancellationToken);
		callResult.IsError.Should().NotBeTrue(
			because: "get-tool-contract should return a normal MCP tool result envelope for the legacy stdio client identity");
		return EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(callResult);
	}

	private static async Task<GuidanceGetResponse> CallGuidanceAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		string guidanceName) {
		CallToolResult callResult = await session.CallToolAsync(
			GuidanceGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> { ["name"] = guidanceName }
			},
			cancellationToken);
		callResult.IsError.Should().NotBeTrue(
			because: "get-guidance should return a normal MCP tool result envelope");
		return EntitySchemaStructuredResultParser.Extract<GuidanceGetResponse>(callResult);
	}
}
