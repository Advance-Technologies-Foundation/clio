using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace Clio.Mcp.E2E;

/// <summary>
///     End-to-end coverage for the <c>reload-workplaces</c> MCP tool (ENG-88474).
/// </summary>
/// <remarks>
///     The tool publishes a navigation change to sessions that are already signed in. Its live effect is a platform
///     cache reload behind cliogate, which needs a stand with cliogate installed — so these tests cover the parts that
///     run everywhere and are the ones an agent depends on: the tool is a long-tail write reachable through
///     <c>clio-run</c>, its contract is discoverable, and an unresolvable environment fails cleanly instead of
///     reporting a publish that never happened. Option forwarding is pinned by
///     <c>ReloadWorkplacesToolTests</c> and the failure/success reporting by <c>ReloadWorkplacesCommandTests</c>.
/// </remarks>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(ReloadWorkplacesTool.ToolName)]
[NonParallelizable]
public sealed class ReloadWorkplacesToolE2ETests : McpContractFixtureBase {

	[Test]
	[Description("reload-workplaces is dispatchable through clio-run against the real MCP server, which is how a long-tail write tool must be invoked.")]
	[AllureTag(ReloadWorkplacesTool.ToolName)]
	[AllureName("reload-workplaces is reachable through clio-run")]
	public async Task ReloadWorkplaces_ShouldBeReachable_ThroughClioRun(){
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));
		string unknownEnvironment = $"{nameof(ReloadWorkplaces_ShouldBeReachable_ThroughClioRun)}-missing";

		// Act — an unknown environment is deliberate: it exercises dispatch without touching a live stand.
		CallToolResult callResult = await context.Session.CallToolAsync(
			ClioRunTool.ToolName,
			new Dictionary<string, object?> {
				["command"] = ReloadWorkplacesTool.ToolName,
				["args"] = new Dictionary<string, object?> {
					["environmentName"] = unknownEnvironment
				}
			},
			context.CancellationTokenSource.Token);

		// Assert
		string payload = SerializeResult(callResult);
		payload.Should().NotContain("Unknown command",
			because: "clio-run must recognise reload-workplaces, otherwise the guide sends agents at a tool that cannot be dispatched");
		payload.Should().NotContain("no re-login is required",
			because: "an unresolvable environment must never report a successful publish");
	}

	[Test]
	[Description("The reload-workplaces contract is discoverable and marked as a non-destructive idempotent write, so an agent can tell it apart from the destructive cache flush.")]
	[AllureTag(ReloadWorkplacesTool.ToolName)]
	[AllureName("reload-workplaces is advertised as a non-destructive idempotent write")]
	public async Task ReloadWorkplaces_ShouldBeAdvertised_AsNonDestructiveIdempotentWrite(){
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?>(),
			context.CancellationTokenSource.Token);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "the compact contract index is the discovery entry point and must answer over the wire");
		SerializeResult(callResult).Should().Contain(ReloadWorkplacesTool.ToolName,
			because: "an agent that cannot discover the tool falls back to prescribing a re-login it no longer needs");
	}

	// Serializes the tool result (structured content plus content blocks) to a JSON string so assertions can inspect
	// the payload without coupling to the response DTO shape.
	private static string SerializeResult(CallToolResult callResult) =>
		JsonSerializer.Serialize(callResult.StructuredContent) + JsonSerializer.Serialize(callResult.Content);

}
