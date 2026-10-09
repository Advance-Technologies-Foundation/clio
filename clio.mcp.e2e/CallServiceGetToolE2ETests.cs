using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the <c>call-service-get</c> MCP tool.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(CallServiceGetTool.ToolName)]
[NonParallelizable]
public sealed class CallServiceGetToolE2ETests : McpContractFixtureBase {
	[Test]
	[Description("Exposes call-service-get as a discoverable read-only tool on the lazy MCP surface.")]
	[AllureTag(CallServiceGetTool.ToolName)]
	[AllureName("call-service-get MCP tool is discoverable on the lazy surface")]
	public async Task CallServiceGet_Should_Be_Discoverable_On_Lazy_Surface() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames = await arrangeContext.Session.ListReachableToolNamesAsync(
			arrangeContext.CancellationTokenSource.Token);
		IReadOnlyList<ToolContractIndexEntry> index = await arrangeContext.Session.GetToolContractIndexAsync(
			arrangeContext.CancellationTokenSource.Token);
		CallToolResult contractResult = await arrangeContext.Session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["tool-names"] = new[] { CallServiceGetTool.ToolName }
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		ToolContractGetResponse contractResponse =
			EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(contractResult);

		// Assert
		toolNames.Should().Contain(CallServiceGetTool.ToolName);
		ToolContractIndexEntry entry = index.Should()
			.ContainSingle(item => item.Name == CallServiceGetTool.ToolName)
			.Which;
		entry.Destructive.Should().NotBe(true,
			because: "endpoint verification is a read-only operation");
		ToolContractDefinition contract = contractResponse.Tools.Should().ContainSingle().Which;
		contract.InputSchema.Required.Should().BeEquivalentTo(["environment-name", "service-path"]);
		contract.InputSchema.Properties.Select(property => property.Name).Should().BeEquivalentTo(
			["environment-name", "service-path", "authenticated", "timeout"],
			because: "the tool must not expose headers, credentials, an origin or an HTTP method");
	}

	[Test]
	[Description("Binds call-service-get arguments through the real MCP server and refuses an absolute URL before environment resolution.")]
	[AllureTag(CallServiceGetTool.ToolName)]
	[AllureName("call-service-get MCP tool refuses an absolute URL")]
	public async Task CallServiceGet_Should_Bind_Arguments_And_Reject_Absolute_Url() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			CallServiceGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = "missing-call-service-get-env",
					["service-path"] = "https://other.example/rest/Test",
					["authenticated"] = false
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CallServiceGetResponse response = EntitySchemaStructuredResultParser.Extract<CallServiceGetResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a rejected route is a structured tool result");
		response.Success.Should().BeFalse();
		response.ErrorClass.Should().Be("invalid-service-path");
		response.Status.Should().BeNull();
	}
}
