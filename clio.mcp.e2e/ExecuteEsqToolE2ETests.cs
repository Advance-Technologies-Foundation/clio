using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the execute-esq MCP tool.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(ExecuteEsqTool.ToolName)]
[NonParallelizable]
public sealed class ExecuteEsqToolE2ETests : McpContractFixtureBase {
	[Test]
	[Description("Exposes execute-esq as a discoverable, non-destructive tool via the get-tool-contract compact index on the lazy MCP surface.")]
	[AllureTag(ExecuteEsqTool.ToolName)]
	[AllureName("execute-esq MCP tool is discoverable on the lazy surface")]
	public async Task ExecuteEsq_Should_Be_Discoverable_On_Lazy_Surface() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames = await arrangeContext.Session.ListReachableToolNamesAsync(
			arrangeContext.CancellationTokenSource.Token);
		IReadOnlyList<ToolContractIndexEntry> index = await arrangeContext.Session.GetToolContractIndexAsync(
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ExecuteEsqTool.ToolName,
			because: $"the {ExecuteEsqTool.ToolName} MCP tool must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list");
		ToolContractIndexEntry entry = index.Should()
			.ContainSingle(entry => entry.Name == ExecuteEsqTool.ToolName,
				because: "the compact discovery index must carry exactly one entry for execute-esq")
			.Which;
		entry.Destructive.Should().NotBe(true,
			because: "execute-esq is a read-only query tool and must not be flagged destructive in the discovery index");
	}

	[Test]
	[Description("Binds execute-esq arguments through the real MCP server and returns a structured failure for an unknown environment.")]
	[AllureTag(ExecuteEsqTool.ToolName)]
	[AllureName("execute-esq MCP tool binds arguments")]
	public async Task ExecuteEsq_Should_Bind_Arguments_And_Report_Invalid_Environment() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-esq-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ExecuteEsqTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["query"] = new Dictionary<string, object?> {
						["rootSchemaName"] = "Contact",
						["operationType"] = 0,
						["allColumns"] = false,
						["columns"] = new Dictionary<string, object?> {
							["items"] = new Dictionary<string, object?> {
								["Id"] = new Dictionary<string, object?> {
									["expression"] = new Dictionary<string, object?> {
										["expressionType"] = 0,
										["columnPath"] = "Id"
									}
								}
							}
						}
					}
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		ExecuteEsqResponse response = EntitySchemaStructuredResultParser.Extract<ExecuteEsqResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "valid execute-esq payloads should bind and return a structured tool response");
		response.Success.Should().BeFalse(
			because: "an unknown registered environment should fail inside tool execution");
		response.Error.Should().Contain(invalidEnvironmentName,
			because: "the structured failure should identify the missing environment name");
	}

}
