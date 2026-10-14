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
					["processName"] = StockProcessCode,
					// The stock process is over the 18,000-byte reply threshold, so without a mode it comes back as a
					// summary, which carries no buildType (ENG-103023).
					["mode"] = "full"
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

	[Test]
	[AllureTag("create-business-process")]
	[AllureName("create-business-process refuses a nested descriptor next to other fields")]
	[AllureDescription("QA ENG-103025: descriptor{name, descriptor{...}} has two candidate processes; the call is refused and nothing is built.")]
	[Description("QA ENG-103025: create-business-process with a nested 'descriptor' next to other descriptor fields is refused as ambiguous before anything is built.")]
	public async Task CreateBusinessProcess_Should_Refuse_Ambiguous_Nested_Descriptor() {
		// Arrange
		string environmentName = await ResolveEnvironmentAsync();
		await using var arrange = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult result = await arrange.Session.CallDestructiveAsync(
			"create-business-process",
			new Dictionary<string, object?> {
				["environment-name"] = environmentName,
				["descriptor"] = new Dictionary<string, object?> {
					["name"] = "UsrClioE2eNestedOuter",
					["descriptor"] = new Dictionary<string, object?> {
						["name"] = "UsrClioE2eNestedInner", ["caption"] = "x", ["packageName"] = "Custom",
						["elements"] = Array.Empty<object>(), ["flows"] = Array.Empty<object>()
					}
				}
			},
			arrange.CancellationTokenSource.Token);
		string text = string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

		// Assert
		text.Should().Contain("Ambiguous descriptor", because: "two candidate process objects have no single reading");
		text.Should().Contain("Nothing was built", because: "the refusal happens before any write");
	}

	[Test]
	[AllureTag(ValidateProcessGraphTool.ToolName)]
	[AllureName("validate-process-graph says element configuration was not checked for a descriptor")]
	[AllureDescription("Review ENG-103025: a create-business-process descriptor sent as the graph is validated, and the response says only the graph shape was checked.")]
	[Description("validate-process-graph with descriptor{elements,flows} succeeds and warns that element configuration was not checked.")]
	public async Task ValidateProcessGraph_Should_Say_Element_Configuration_Was_Not_Checked() {
		// Arrange
		string environmentName = await ResolveEnvironmentAsync();
		await using var arrange = Arrange(TimeSpan.FromMinutes(5));

		// Act
		CallToolResult callResult = await arrange.Session.CallToolAsync(
			ValidateProcessGraphTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["descriptor"] = new Dictionary<string, object?> {
						["name"] = "UsrClioE2eGraphOnly", ["caption"] = "x",
						["elements"] = new object[] {
							new Dictionary<string, object?> { ["name"] = "s", ["type"] = "startEvent", ["caption"] = "Start" },
							new Dictionary<string, object?> { ["name"] = "e", ["type"] = "endEvent", ["caption"] = "End" }
						},
						["flows"] = new object[] { new Dictionary<string, object?> { ["source"] = "s", ["target"] = "e" } }
					}
				}
			},
			arrange.CancellationTokenSource.Token);
		ValidateProcessGraphResponse response = EntitySchemaStructuredResultParser.Extract<ValidateProcessGraphResponse>(callResult);

		// Assert
		response.Success.Should().BeTrue(because: $"the descriptor's graph is valid: {response.Error}");
		response.Warnings.Should().Contain(ProcessToolArgumentAliases.DescriptorOnlyGraphCheckedNote,
			because: "a passing graph must not read as a passing descriptor");
	}

	[Test]
	[AllureTag("modify-business-process")]
	[AllureName("modify-business-process refuses a synonym next to process-name")]
	[AllureDescription("Review ENG-103025: modify-business-process now reads processName as process-name; sent next to process-name it is ambiguous and refused before anything is written.")]
	[Description("modify-business-process with both process-name and processName is refused as an ambiguous identity and changes nothing.")]
	public async Task ModifyBusinessProcess_Should_Refuse_Ambiguous_Identity() {
		// Arrange
		string environmentName = await ResolveEnvironmentAsync();
		await using var arrange = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult result = await arrange.Session.CallDestructiveAsync(
			"modify-business-process",
			new Dictionary<string, object?> {
				["environment-name"] = environmentName,
				["process-name"] = StockProcessCode,
				["processName"] = "UsrClioE2eOther",
				["operations"] = "[{\"op\":\"addParameter\",\"parameter\":{\"name\":\"UsrE2e\",\"type\":\"Integer\"}}]"
			},
			arrange.CancellationTokenSource.Token);
		string text = string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

		// Assert
		text.Should().Contain("Ambiguous process identity", because: "two names for one process have no single reading");
		text.Should().Contain("Nothing was changed", because: "the refusal happens before any write");
	}

	[Test]
	[AllureTag("modify-business-process-as-new-version")]
	[AllureName("modify-business-process-as-new-version refuses a synonym next to process-name")]
	[AllureDescription("Review ENG-103025: the as-new-version tool follows the same identity rule as modify-business-process; an ambiguous identity is refused before any version is created.")]
	[Description("modify-business-process-as-new-version with both process-name and processName is refused as an ambiguous identity and creates nothing.")]
	public async Task ModifyProcessAsNewVersion_Should_Refuse_Ambiguous_Identity() {
		// Arrange
		string environmentName = await ResolveEnvironmentAsync();
		await using var arrange = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult result = await arrange.Session.CallDestructiveAsync(
			"modify-business-process-as-new-version",
			new Dictionary<string, object?> {
				["environment-name"] = environmentName,
				["process-name"] = StockProcessCode,
				["processName"] = "UsrClioE2eOther"
			},
			arrange.CancellationTokenSource.Token);
		string text = string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

		// Assert
		text.Should().Contain("Ambiguous process identity", because: "two names for one process have no single reading");
		text.Should().Contain("Nothing was changed", because: "an undeletable version must never be created from a guess");
	}

	[Test]
	[AllureTag("create-business-process")]
	[AllureName("create-business-process refuses a double wrapper around nothing")]
	[AllureDescription("QA ENG-103025: descriptor{descriptor{}} holds no process; the call is refused with the mini-contract and nothing is built.")]
	[Description("QA ENG-103025: create-business-process with descriptor{descriptor:{}} is refused as empty, with a minimal valid example.")]
	public async Task CreateBusinessProcess_Should_Refuse_Empty_Double_Wrapper() {
		// Arrange
		string environmentName = await ResolveEnvironmentAsync();
		await using var arrange = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult result = await arrange.Session.CallDestructiveAsync(
			"create-business-process",
			new Dictionary<string, object?> {
				["environment-name"] = environmentName,
				["descriptor"] = new Dictionary<string, object?> { ["descriptor"] = new Dictionary<string, object?>() }
			},
			arrange.CancellationTokenSource.Token);
		string text = string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

		// Assert
		text.Should().Contain("it is empty", because: "there is no process to build");
		text.Should().Contain("Minimal valid arguments:", because: "the caller gets a buildable example");
	}

	private static async Task<string> ResolveEnvironmentAsync() {
		McpE2ESettings settings = TestConfiguration.Load();
		return await ReachableSandboxEnvironment.ResolveConfiguredOrIgnoreAsync(settings,
			$"process tool alias MCP E2E requires the configured sandbox environment '{settings.Sandbox.EnvironmentName}' with CrtProcessBuilder to be set and reachable.");
	}
}
