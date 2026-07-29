using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
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
	[Description("Exposes odata-create via the get-tool-contract compact index with a destructive safety flag on the lazy tool surface.")]
	[AllureTag(ODataCreateTool.ToolName)]
	[AllureName("odata-create MCP tool is discoverable on the lazy surface")]
	public async Task ODataCreate_Should_Be_Advertised() {
		await using var arrange = Arrange(TimeSpan.FromMinutes(3));
		// The lazy surface exposes hidden tools only through the compact discovery index, which carries the
		// destructive flag; the read-only hint is no longer observable for non-resident tools.
		IReadOnlyList<ToolContractIndexEntry> index =
			await arrange.Session.GetToolContractIndexAsync(arrange.CancellationTokenSource.Token);
		ToolContractIndexEntry entry = index.Should().ContainSingle(entry => entry.Name == ODataCreateTool.ToolName,
			because: "odata-create must be discoverable via the get-tool-contract compact index on the lazy surface")
			.Which;
		entry.Destructive.Should().BeTrue(
			because: "odata-create writes durable Creatio records; a data mutation MCP hosts must gate for approval and audit (GH-953)");
	}

	[Test]
	[Description("Exposes odata-update via the get-tool-contract compact index with a destructive safety flag on the lazy tool surface.")]
	[AllureTag(ODataUpdateTool.ToolName)]
	[AllureName("odata-update MCP tool is discoverable on the lazy surface")]
	public async Task ODataUpdate_Should_Be_Advertised() {
		await using var arrange = Arrange(TimeSpan.FromMinutes(3));
		IReadOnlyList<ToolContractIndexEntry> index =
			await arrange.Session.GetToolContractIndexAsync(arrange.CancellationTokenSource.Token);
		ToolContractIndexEntry entry = index.Should().ContainSingle(entry => entry.Name == ODataUpdateTool.ToolName,
			because: "odata-update must be discoverable via the get-tool-contract compact index on the lazy surface")
			.Which;
		entry.Destructive.Should().BeTrue(
			because: "odata-update overwrites existing record data and must be flagged destructive");
	}

	[Test]
	[Description("Exposes odata-delete via the get-tool-contract compact index with a destructive safety flag on the lazy tool surface.")]
	[AllureTag(ODataDeleteTool.ToolName)]
	[AllureName("odata-delete MCP tool is discoverable on the lazy surface")]
	public async Task ODataDelete_Should_Be_Advertised() {
		await using var arrange = Arrange(TimeSpan.FromMinutes(3));
		IReadOnlyList<ToolContractIndexEntry> index =
			await arrange.Session.GetToolContractIndexAsync(arrange.CancellationTokenSource.Token);
		ToolContractIndexEntry entry = index.Should().ContainSingle(entry => entry.Name == ODataDeleteTool.ToolName,
			because: "odata-delete must be discoverable via the get-tool-contract compact index on the lazy surface")
			.Which;
		entry.Destructive.Should().BeTrue(
			because: "odata-delete removes records and must be flagged destructive");
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
