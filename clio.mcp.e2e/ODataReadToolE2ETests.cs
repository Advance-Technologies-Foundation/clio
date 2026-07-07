using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the OData read MCP tool.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(ODataReadTool.ToolName)]
[NonParallelizable]
public sealed class ODataReadToolE2ETests : McpContractFixtureBase {
	[Test]
	[Description("Exposes odata-read via the get-tool-contract compact index with a non-destructive safety flag on the lazy tool surface.")]
	[AllureTag(ODataReadTool.ToolName)]
	[AllureName("odata-read MCP tool is discoverable on the lazy surface")]
	public async Task ODataRead_Should_Be_Advertised() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyList<ToolContractIndexEntry> index = await arrangeContext.Session.GetToolContractIndexAsync(
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		// The lazy surface exposes hidden tools only through the compact discovery index, which carries the
		// destructive flag; the read-only hint is no longer observable for non-resident tools.
		ToolContractIndexEntry entry = index.Should().ContainSingle(entry => entry.Name == ODataReadTool.ToolName,
			because: "odata-read must be discoverable via the get-tool-contract compact index so callers can find the query tool")
			.Which;
		entry.Destructive.Should().NotBe(true,
			because: "odata-read is a read-only query tool and must not be flagged destructive");
	}

	[Test]
	[Description("Binds odata-read arguments through the real MCP server and returns a structured failure for an unknown environment.")]
	[AllureTag(ODataReadTool.ToolName)]
	[AllureName("odata-read MCP tool binds arguments")]
	public async Task ODataRead_Should_Bind_Arguments_And_Report_Invalid_Environment() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-odata-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ODataReadTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["entity"] = "Contact",
					["select"] = new[] { "Id" },
					["top"] = 1
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		ODataReadResponse response = EntitySchemaStructuredResultParser.Extract<ODataReadResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "valid odata-read payloads should bind and return a structured tool response");
		response.Success.Should().BeFalse(
			because: "an unknown registered environment should fail inside tool execution");
		response.Error.Should().Contain(invalidEnvironmentName,
			because: "the structured failure should identify the missing environment name");
	}

}
