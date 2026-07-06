using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the OData write MCP tools (create / update / delete).
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[NonParallelizable]
public sealed class ODataWriteToolsE2ETests : McpContractFixtureBase {
	[Test]
	[Description("Advertises odata-create as a non-read-only, non-destructive MCP tool.")]
	[AllureTag(ODataCreateTool.ToolName)]
	[AllureName("odata-create MCP tool is advertised")]
	public async Task ODataCreate_Should_Be_Advertised() {
		await using var arrange = Arrange(TimeSpan.FromMinutes(3));
		IList<McpClientTool> tools = await arrange.Session.ListToolsAsync(arrange.CancellationTokenSource.Token);
		McpClientTool tool = tools.Single(t => t.Name == ODataCreateTool.ToolName);
		tool.ProtocolTool.Annotations!.ReadOnlyHint.Should().BeFalse();
		tool.ProtocolTool.Annotations.DestructiveHint.Should().BeFalse();
	}

	[Test]
	[Description("Advertises odata-update as a destructive MCP tool.")]
	[AllureTag(ODataUpdateTool.ToolName)]
	[AllureName("odata-update MCP tool is advertised")]
	public async Task ODataUpdate_Should_Be_Advertised() {
		await using var arrange = Arrange(TimeSpan.FromMinutes(3));
		IList<McpClientTool> tools = await arrange.Session.ListToolsAsync(arrange.CancellationTokenSource.Token);
		McpClientTool tool = tools.Single(t => t.Name == ODataUpdateTool.ToolName);
		tool.ProtocolTool.Annotations!.ReadOnlyHint.Should().BeFalse();
		tool.ProtocolTool.Annotations.DestructiveHint.Should().BeTrue();
	}

	[Test]
	[Description("Advertises odata-delete as a destructive MCP tool.")]
	[AllureTag(ODataDeleteTool.ToolName)]
	[AllureName("odata-delete MCP tool is advertised")]
	public async Task ODataDelete_Should_Be_Advertised() {
		await using var arrange = Arrange(TimeSpan.FromMinutes(3));
		IList<McpClientTool> tools = await arrange.Session.ListToolsAsync(arrange.CancellationTokenSource.Token);
		McpClientTool tool = tools.Single(t => t.Name == ODataDeleteTool.ToolName);
		tool.ProtocolTool.Annotations!.ReadOnlyHint.Should().BeFalse();
		tool.ProtocolTool.Annotations.DestructiveHint.Should().BeTrue();
	}

	[Test]
	[Description("Binds odata-create arguments and reports a structured failure for an unknown environment.")]
	[AllureTag(ODataCreateTool.ToolName)]
	[AllureName("odata-create MCP tool binds arguments")]
	public async Task ODataCreate_Should_Bind_Arguments_And_Report_Invalid_Environment() {
		await using var arrange = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-odata-env-{Guid.NewGuid():N}";

		CallToolResult callResult = await arrange.Session.CallToolAsync(
			ODataCreateTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["entity"] = "Contact",
					["rows"] = new object[] { new Dictionary<string, object?> { ["Name"] = "e2e" }
 }
				}
			},
			arrange.CancellationTokenSource.Token);
		ODataWriteResponse response = EntitySchemaStructuredResultParser.Extract<ODataWriteResponse>(callResult);

		callResult.IsError.Should().NotBeTrue();
		response.Success.Should().BeFalse();
		response.Error.Should().Contain(invalidEnvironmentName);
	}

	[Test]
	[Description("Binds a multi-row odata-create batch through the real MCP server and reports a request-level invalid-environment failure.")]
	[AllureTag(ODataCreateTool.ToolName)]
	[AllureName("odata-create MCP tool binds a multi-row batch")]
	public async Task ODataCreate_Should_Bind_Multiple_Rows_Batch_And_Report_Invalid_Environment() {
		await using var arrange = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-odata-batch-env-{Guid.NewGuid():N}";

		CallToolResult callResult = await arrange.Session.CallToolAsync(
			ODataCreateTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["entity"] = "Contact",
					["rows"] = new object[] {
						new Dictionary<string, object?> { ["Name"] = "e2e batch 1" },
						new Dictionary<string, object?> { ["Name"] = "e2e batch 2" }
					}
				}
			},
			arrange.CancellationTokenSource.Token);
		ODataCreateBatchResponse response = EntitySchemaStructuredResultParser.Extract<ODataCreateBatchResponse>(callResult);

		callResult.IsError.Should().NotBeTrue(
			because: "a valid two-row batch payload should bind and return the structured batch response");
		response.Error.Should().Contain(invalidEnvironmentName,
			because: "the missing environment fails the whole batch before any row is attempted");
	}

	[Test]
	[Description("odata-update rejects a non-GUID id through the real MCP server without touching an environment.")]
	[AllureTag(ODataUpdateTool.ToolName)]
	[AllureName("odata-update MCP tool guards keyless updates")]
	public async Task ODataUpdate_Should_Reject_NonGuid_Id() {
		await using var arrange = Arrange(TimeSpan.FromMinutes(3));

		CallToolResult callResult = await arrange.Session.CallToolAsync(
			ODataUpdateTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = $"missing-{Guid.NewGuid():N}",
					["entity"] = "Contact",
					["id"] = "all",
					["rows"] = new object[] { new Dictionary<string, object?> { ["Name"] = "e2e" }
 }
				}
			},
			arrange.CancellationTokenSource.Token);
		ODataWriteResponse response = EntitySchemaStructuredResultParser.Extract<ODataWriteResponse>(callResult);

		callResult.IsError.Should().NotBeTrue();
		response.Success.Should().BeFalse();
		response.Error.Should().Contain("must be a record GUID");
	}

}
