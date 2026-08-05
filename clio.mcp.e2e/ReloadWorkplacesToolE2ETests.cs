using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace Clio.Mcp.E2E;

/// <summary>
///     End-to-end coverage for the <c>reload-workplaces</c> MCP tool. The live cache reload needs a stand with
///     cliogate, so these cover the environment-free surface: dispatch through <c>clio-run</c> and contract discovery.
/// </summary>
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
		callResult.IsError.Should().NotBeTrue(
			because: "clio-run must dispatch the tool and let it report its own outcome, not fail at the executor");
		string payload = SerializeResult(callResult);
		payload.Should().NotContain("Unknown command",
			because: "clio-run must recognise reload-workplaces, otherwise the guide sends agents at a tool that cannot be dispatched");
		payload.Should().Contain(unknownEnvironment,
			because: "the failure must name the environment it could not resolve, which also proves the args reached the tool");
		payload.Should().NotContain("Navigation caches reloaded",
			because: "an unresolvable environment must never report a successful publish");
	}

	[Test]
	[Description("The reload-workplaces contract index entry is discoverable and advertises the tool as non-destructive, so an agent can tell it apart from the destructive cache flush it might otherwise reach for.")]
	[AllureTag(ReloadWorkplacesTool.ToolName)]
	[AllureName("reload-workplaces is advertised as non-destructive in the contract index")]
	public async Task ReloadWorkplaces_ShouldBeAdvertised_AsNonDestructive(){
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?> {["args"] = new Dictionary<string, object?>()},
			context.CancellationTokenSource.Token);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "the compact contract index is the discovery entry point and must answer over the wire");
		ToolContractGetResponse response = EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(callResult);
		ToolContractIndexEntry entry = response.Index!.Single(item => item.Name == ReloadWorkplacesTool.ToolName);
		entry.Destructive.Should().NotBeTrue(
			because: "publishing a navigation change destroys nothing, and advertising it as destructive would push agents to prescribe a re-login instead");
		entry.Resident.Should().BeFalse(
			because: "it is a long-tail write dispatched through clio-run, like the other navigation writes");
	}


	// Serializes the tool result (structured content plus content blocks) to a JSON string so assertions can inspect
	// the payload without coupling to the response DTO shape.
	private static string SerializeResult(CallToolResult callResult) =>
		JsonSerializer.Serialize(callResult.StructuredContent) + JsonSerializer.Serialize(callResult.Content);

}
