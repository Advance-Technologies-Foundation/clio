using System.Text.Json;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// ENG-103025: validate-process-graph and describe-business-process accept the unambiguous argument shapes agents
/// send, and say which canonical form to use, measured against a stand that carries CrtProcessBuilder.
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[Category(McpE2ECategories.ProcessDesigner)]
[AllureNUnit]
[AllureFeature(ValidateProcessGraphTool.ToolName)]
[NonParallelizable]
public sealed class ProcessToolArgumentAliasesE2ETests : McpContractFixtureBase {

	/// <summary>Stock process of the product; the same one DescribeProcessToolE2ETests reads.</summary>
	private const string StockProcessCode = "InvoiceVisaProcess";

	[Test]
	[AllureTag(ValidateProcessGraphTool.ToolName)]
	[AllureName("validate-process-graph accepts a graph wrapper and warns")]
	[AllureDescription("Sends the nodes and edges under graph and verifies the graph is validated and the response names the canonical form.")]
	[Description("validate-process-graph with graph{nodes,edges} succeeds with no findings and a warning naming nodes/edges.")]
	public async Task ValidateProcessGraph_Should_Accept_Graph_Wrapper() {
		// Arrange
		string environmentName = await ResolveEnvironmentAsync();
		await using var arrange = Arrange(TimeSpan.FromMinutes(5));

		// Act
		CallToolResult callResult = await arrange.Session.CallToolAsync(
			ValidateProcessGraphTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["graph"] = new Dictionary<string, object?> {
						["nodes"] = new object[] {
							new Dictionary<string, object?> { ["name"] = "s", ["type"] = "startEvent" },
							new Dictionary<string, object?> { ["name"] = "e", ["type"] = "endEvent" }
						},
						["edges"] = new object[] {
							new Dictionary<string, object?> { ["source"] = "s", ["target"] = "e", ["flow-kind"] = "sequence" }
						}
					}
				}
			},
			arrange.CancellationTokenSource.Token);
		ValidateProcessGraphResponse response = EntitySchemaStructuredResultParser.Extract<ValidateProcessGraphResponse>(callResult);

		// Assert
		response.Success.Should().BeTrue(because: $"the wrapped graph is the same graph: {response.Error}");
		response.HasErrors.Should().BeFalse(because: "start -> end is a valid graph");
		response.Warnings.Should().ContainSingle(warning => warning.Contains("'graph'") && warning.Contains("'nodes'"),
			because: "the caller learns the canonical argument names");
	}

	[Test]
	[AllureTag(ValidateProcessGraphTool.ToolName)]
	[AllureName("validate-process-graph refuses a graph wrapper together with nodes")]
	[AllureDescription("Sends graph and nodes together and verifies the ambiguity refusal.")]
	[Description("validate-process-graph with both graph and nodes fails as ambiguous and validates nothing.")]
	public async Task ValidateProcessGraph_Should_Refuse_Graph_With_Nodes() {
		// Arrange
		string environmentName = await ResolveEnvironmentAsync();
		await using var arrange = Arrange(TimeSpan.FromMinutes(5));
		Dictionary<string, object?> node = new() { ["name"] = "s", ["type"] = "startEvent" };

		// Act
		CallToolResult callResult = await arrange.Session.CallToolAsync(
			ValidateProcessGraphTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["nodes"] = new object[] { node },
					["graph"] = new Dictionary<string, object?> { ["nodes"] = new object[] { node } }
				}
			},
			arrange.CancellationTokenSource.Token);
		ValidateProcessGraphResponse response = EntitySchemaStructuredResultParser.Extract<ValidateProcessGraphResponse>(callResult);

		// Assert
		response.Success.Should().BeFalse(because: "two graphs for one call is ambiguous");
		response.Findings.Should().BeNullOrEmpty(because: "nothing was validated");
	}

	[Test]
	[AllureTag(DescribeProcessTool.ToolName)]
	[AllureName("describe-business-process accepts processName and warns")]
	[AllureDescription("Describes the stock InvoiceVisaProcess through the processName alias and verifies the real graph comes back with a warning naming process-name.")]
	[Description("describe-business-process with processName returns the structured process graph and a warning naming the canonical process-name.")]
	public async Task DescribeProcess_Should_Accept_ProcessName_Alias() {
		// Arrange
		string environmentName = await ResolveEnvironmentAsync();
		await using var arrange = Arrange(TimeSpan.FromMinutes(5));

		// Act
		CallToolResult callResult = await arrange.Session.CallToolAsync(
			DescribeProcessTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["processName"] = StockProcessCode
				}
			},
			arrange.CancellationTokenSource.Token);
		string text = string.Concat(callResult.Content.OfType<TextContentBlock>().Select(block => block.Text));

		// Assert
		callResult.IsError.Should().NotBeTrue(because: "an accepted alias is a normal result");
		text.Should().Contain("Argument 'processName' was accepted as 'process-name'",
			because: "the response names the canonical argument");
		text.Should().Contain(StockProcessCode, because: "the real process was read");
		text.Should().Contain("buildType", because: "the structured graph, not an error, came back");
	}

	private static async Task<string> ResolveEnvironmentAsync() {
		McpE2ESettings settings = TestConfiguration.Load();
		return await ReachableSandboxEnvironment.ResolveConfiguredOrIgnoreAsync(settings,
			$"process tool alias MCP E2E requires the configured sandbox environment '{settings.Sandbox.EnvironmentName}' with CrtProcessBuilder to be set and reachable.");
	}
}
