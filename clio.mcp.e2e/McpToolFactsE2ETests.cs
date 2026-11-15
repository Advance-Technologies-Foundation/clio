using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for the tool facts a host reads to decide how to treat a call: the real clio MCP server
/// publishes them in each resident tool's <c>_meta</c> in <c>tools/list</c>, and answers them for every reachable
/// tool through <c>get-tool-contract</c> with <c>detail=facts</c>. Needs no Creatio environment.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("mcp-tool-facts")]
[NonParallelizable]
public sealed class McpToolFactsE2ETests : McpContractFixtureBase {

	[Test]
	[AllureTag(ToolContractGetTool.ToolName)]
	[AllureName("tools/list carries each resident tool's facts in _meta")]
	[Description("Starts the real clio MCP server, lists its tools and verifies every one carries clio/facts in _meta with an effect, and that get-tool-contract reads as a read on the local scope.")]
	public async Task ToolsList_Should_Carry_Facts_In_Meta_For_Every_Resident_Tool() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);

		// Assert
		tools.Should().NotBeEmpty(because: "the server lists its resident tools");
		foreach (McpClientTool tool in tools) {
			JsonNode? facts = tool.ProtocolTool.Meta?[McpToolFacts.MetaKey];
			facts.Should().NotBeNull(because: $"'{tool.Name}' must say what it does in _meta for a host to read");
			((string?)facts!["effect"]).Should().BeOneOf(["read", "change", "destroy"],
				because: $"'{tool.Name}' must publish a declared effect");
		}
		JsonNode contractFacts = tools.Single(tool => tool.Name == ToolContractGetTool.ToolName).ProtocolTool.Meta![McpToolFacts.MetaKey]!;
		((string?)contractFacts["effect"]).Should().Be("read", because: "get-tool-contract only reads clio's own catalog");
		((string?)contractFacts["scope"]).Should().Be("local", because: "get-tool-contract never reaches an environment");
	}

	[Test]
	[AllureTag(ToolContractGetTool.ToolName)]
	[AllureName("get-tool-contract detail=facts answers every reachable tool, and manage-role list is a read")]
	[Description("Asks the real clio MCP server for detail=facts and verifies it answers every tool the index lists, and that manage-role carries a by-argument entry saying action=list only reads.")]
	public async Task GetToolContract_Should_Answer_Facts_For_Every_Reachable_Tool() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));
		IReadOnlyCollection<string> reachable =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> { ["detail"] = ToolContractCatalog.FactsDetail }
			},
			context.CancellationTokenSource.Token);
		ToolContractGetResponse response = EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(because: "a facts request is an ordinary successful lookup");
		response.Success.Should().BeTrue(because: "every served tool declares its facts");
		response.Facts.Should().NotBeNull(because: "detail=facts answers under facts");
		response.Facts!.Keys.Should().Contain(reachable,
			because: "a host must find the facts of every tool it can call, resident or through clio-run");
		JsonArray byArgument = response.Facts["manage-role"]["by-argument"]!.AsArray();
		byArgument.Should().Contain(entry => (string?)entry!["argument"] == "action"
				&& (string?)entry["value"] == "list" && (string?)entry["effect"] == "read",
			because: "listing roles only reads, so a host must not ask for a change approval to do it");
	}
}
