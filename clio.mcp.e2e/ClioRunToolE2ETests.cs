using System.Text.Json;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the clio-run generic executor over the real MCP server (ENG-92653).
/// These exercise the wire path the unit tests deliberately bypass: with args declared as
/// <c>Dictionary&lt;string, JsonElement&gt;?</c> the SDK now binds a JSON object, so the args
/// payload must survive real by-name binding + re-serialization and reach the dispatched target
/// tool. Both the flat call shape <c>{"command":"X","args":{...}}</c> and the wrapped shape
/// <c>{"args":{"command":"X","args":{...}}}</c> are covered.
/// A read-only, environment-free target (<c>get-guidance</c>) is used so the assertion proves the
/// args reached the target without needing a live Creatio environment.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(ClioRunTool.ToolName)]
[NonParallelizable]
public sealed class ClioRunToolE2ETests : McpContractFixtureBase {

	// A stable, always-registered guidance name + a marker unique to its article body. If the args
	// object failed to reach get-guidance, the target would report a missing-name failure instead.
	private const string GuidanceName = "page-schema-handlers";
	private const string GuidanceMarker = "clio MCP page-schema handlers guide";

	[Test]
	[Category("E2E")]
	[Description("clio-run dispatches the flat call shape {command, args} through the real MCP server and the args object reaches the target tool (ENG-92653).")]
	[AllureTag(ClioRunTool.ToolName)]
	[AllureName("clio-run forwards flat-shape args to the target tool over the wire")]
	public async Task ClioRun_ShouldForwardArgsToTarget_WhenCalledWithFlatShape() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act — flat shape: top-level command + args, args being the object get-guidance expects.
		CallToolResult callResult = await context.Session.CallToolAsync(
			ClioRunTool.ToolName,
			new Dictionary<string, object?> {
				["command"] = "get-guidance",
				["args"] = new Dictionary<string, object?> {
					["name"] = GuidanceName
				}
			},
			context.CancellationTokenSource.Token);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a well-formed flat clio-run call must dispatch to get-guidance and succeed");
		SerializeResult(callResult).Should().Contain(GuidanceMarker,
			because: "the args object must reach get-guidance so it resolves the requested guidance article (ENG-92653)");
	}

	[Test]
	[Category("E2E")]
	[Description("clio-run dispatches the wrapped call shape {args:{command, args}} through the real MCP server and the args object reaches the target tool (ENG-92653).")]
	[AllureTag(ClioRunTool.ToolName)]
	[AllureName("clio-run forwards wrapped-shape args to the target tool over the wire")]
	public async Task ClioRun_ShouldForwardArgsToTarget_WhenCalledWithWrappedShape() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act — wrapped shape: everything nested under args, top-level command omitted. With the new
		// Dictionary binding the SDK binds this whole object under args; the executor must recover the
		// real command/args from it (the exact path most affected by the JsonElement?→Dictionary change).
		CallToolResult callResult = await context.Session.CallToolAsync(
			ClioRunTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["command"] = "get-guidance",
					["args"] = new Dictionary<string, object?> {
						["name"] = GuidanceName
					}
				}
			},
			context.CancellationTokenSource.Token);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "the wrapped clio-run shape must be recovered and dispatched to get-guidance over the wire");
		SerializeResult(callResult).Should().Contain(GuidanceMarker,
			because: "the wrapped args object must survive binding + recovery and reach get-guidance (ENG-92653)");
	}

	// Serializes the tool result (structured content preferred, content blocks as fallback) to a JSON
	// string so the assertion can look for the guidance marker without coupling to the response DTO shape.
	private static string SerializeResult(CallToolResult callResult) =>
		JsonSerializer.Serialize(callResult.StructuredContent) + JsonSerializer.Serialize(callResult.Content);
}
