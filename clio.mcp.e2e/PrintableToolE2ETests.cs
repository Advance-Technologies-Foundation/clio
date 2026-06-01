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
/// End-to-end tests for the MS Word printable MCP tools (list / get / create / update / delete /
/// upload-report-template). Advertisement and argument-binding/guard behavior is verified through
/// the real MCP server without requiring a reachable Creatio environment.
/// </summary>
[TestFixture]
[AllureNUnit]
[NonParallelizable]
public sealed class PrintableToolE2ETests {

	[TestCase(PrintableListTool.ToolName, true, false,
		TestName = "list-printables MCP tool is advertised read-only and non-destructive")]
	[TestCase(PrintableGetTool.ToolName, true, false,
		TestName = "get-printable MCP tool is advertised read-only and non-destructive")]
	[TestCase(PrintableCreateTool.ToolName, false, false,
		TestName = "create-printable MCP tool is advertised non-read-only and non-destructive")]
	[TestCase(PrintableUpdateTool.ToolName, false, true,
		TestName = "update-printable MCP tool is advertised as destructive")]
	[TestCase(PrintableDeleteTool.ToolName, false, true,
		TestName = "delete-printable MCP tool is advertised as destructive")]
	[TestCase(PrintableTemplateUploadTool.ToolName, false, true,
		TestName = "upload-report-template MCP tool is advertised as destructive")]
	[Description("Verifies that each printable MCP tool is advertised with the expected read-only and destructive annotations.")]
	public async Task PrintableTool_Should_Be_Advertised(string toolName, bool expectedReadOnly, bool expectedDestructive) {
		await using McpSessionArrangeContext arrange = await McpSessionArrangeContext.ArrangeAsync(TimeSpan.FromMinutes(3));
		IList<McpClientTool> tools = await arrange.Session.ListToolsAsync(arrange.CancellationTokenSource.Token);
		McpClientTool tool = tools.Single(t => t.Name == toolName);
		tool.ProtocolTool.Annotations!.ReadOnlyHint.Should().Be(expectedReadOnly);
		tool.ProtocolTool.Annotations.DestructiveHint.Should().Be(expectedDestructive);
	}

	[Test]
	[Description("Binds list-printables arguments and reports a structured failure for an unknown environment.")]
	[AllureTag(PrintableListTool.ToolName)]
	[AllureName("list-printables MCP tool binds arguments")]
	public async Task ListPrintables_Should_Bind_Arguments_And_Report_Invalid_Environment() {
		await using McpSessionArrangeContext arrange = await McpSessionArrangeContext.ArrangeAsync(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-printable-env-{Guid.NewGuid():N}";

		CallToolResult callResult = await arrange.Session.CallToolAsync(
			PrintableListTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["top"] = 1
				}
			},
			arrange.CancellationTokenSource.Token);
		ODataReadResponse response = EntitySchemaStructuredResultParser.Extract<ODataReadResponse>(callResult);

		callResult.IsError.Should().NotBeTrue();
		response.Success.Should().BeFalse();
		response.Error.Should().Contain(invalidEnvironmentName);
	}

	[Test]
	[Description("Binds create-printable arguments and reports a structured failure for an unknown environment.")]
	[AllureTag(PrintableCreateTool.ToolName)]
	[AllureName("create-printable MCP tool binds arguments")]
	public async Task CreatePrintable_Should_Bind_Arguments_And_Report_Invalid_Environment() {
		await using McpSessionArrangeContext arrange = await McpSessionArrangeContext.ArrangeAsync(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-printable-env-{Guid.NewGuid():N}";

		CallToolResult callResult = await arrange.Session.CallToolAsync(
			PrintableCreateTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["caption"] = "E2E report",
					["entity-schema-id"] = Guid.NewGuid().ToString()
				}
			},
			arrange.CancellationTokenSource.Token);
		ODataWriteResponse response = EntitySchemaStructuredResultParser.Extract<ODataWriteResponse>(callResult);

		callResult.IsError.Should().NotBeTrue();
		response.Success.Should().BeFalse();
		response.Error.Should().Contain(invalidEnvironmentName);
	}

	[Test]
	[Description("create-printable rejects a non-GUID entity-schema-id without touching an environment.")]
	[AllureTag(PrintableCreateTool.ToolName)]
	[AllureName("create-printable MCP tool validates entity-schema-id")]
	public async Task CreatePrintable_Should_Reject_NonGuid_EntitySchemaId() {
		await using McpSessionArrangeContext arrange = await McpSessionArrangeContext.ArrangeAsync(TimeSpan.FromMinutes(3));

		CallToolResult callResult = await arrange.Session.CallToolAsync(
			PrintableCreateTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = $"missing-{Guid.NewGuid():N}",
					["caption"] = "E2E report",
					["entity-schema-id"] = "not-a-guid"
				}
			},
			arrange.CancellationTokenSource.Token);
		ODataWriteResponse response = EntitySchemaStructuredResultParser.Extract<ODataWriteResponse>(callResult);

		callResult.IsError.Should().NotBeTrue();
		response.Success.Should().BeFalse();
		response.Error.Should().Contain("entity-schema-id");
	}

	[TestCase(PrintableUpdateTool.ToolName,
		TestName = "update-printable MCP tool guards keyless updates")]
	[TestCase(PrintableDeleteTool.ToolName,
		TestName = "delete-printable MCP tool guards keyless deletes")]
	[Description("Rejects a non-GUID id through the real MCP server without touching an environment.")]
	public async Task PrintableTool_Should_Reject_NonGuid_Id(string toolName) {
		await using McpSessionArrangeContext arrange = await McpSessionArrangeContext.ArrangeAsync(TimeSpan.FromMinutes(3));

		CallToolResult callResult = await arrange.Session.CallToolAsync(
			toolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = $"missing-{Guid.NewGuid():N}",
					["id"] = "all",
					["confirm"] = true
				}
			},
			arrange.CancellationTokenSource.Token);
		ODataWriteResponse response = EntitySchemaStructuredResultParser.Extract<ODataWriteResponse>(callResult);

		callResult.IsError.Should().NotBeTrue();
		response.Success.Should().BeFalse();
		response.Error.Should().Contain("must be a record GUID");
	}

	[Test]
	[Description("delete-printable refuses without confirmation, before any remote call.")]
	[AllureTag(PrintableDeleteTool.ToolName)]
	[AllureName("delete-printable MCP tool requires confirmation")]
	public async Task DeletePrintable_Should_Require_Confirmation() {
		await using McpSessionArrangeContext arrange = await McpSessionArrangeContext.ArrangeAsync(TimeSpan.FromMinutes(3));

		CallToolResult callResult = await arrange.Session.CallToolAsync(
			PrintableDeleteTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = $"missing-{Guid.NewGuid():N}",
					["id"] = Guid.NewGuid().ToString()
				}
			},
			arrange.CancellationTokenSource.Token);
		ODataWriteResponse response = EntitySchemaStructuredResultParser.Extract<ODataWriteResponse>(callResult);

		callResult.IsError.Should().NotBeTrue();
		response.Success.Should().BeFalse();
		response.Error.Should().Contain("without confirmation");
	}

	[Test]
	[Description("upload-report-template rejects a non-.docx file before any remote call.")]
	[AllureTag(PrintableTemplateUploadTool.ToolName)]
	[AllureName("upload-report-template MCP tool validates file extension")]
	public async Task UploadReportTemplate_Should_Reject_Non_Docx() {
		await using McpSessionArrangeContext arrange = await McpSessionArrangeContext.ArrangeAsync(TimeSpan.FromMinutes(3));

		CallToolResult callResult = await arrange.Session.CallToolAsync(
			PrintableTemplateUploadTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = $"missing-{Guid.NewGuid():N}",
					["id"] = Guid.NewGuid().ToString(),
					["file-path"] = "/tmp/not-a-template.txt",
					["confirm"] = true
				}
			},
			arrange.CancellationTokenSource.Token);
		PrintableTemplateUploadResponse response =
			EntitySchemaStructuredResultParser.Extract<PrintableTemplateUploadResponse>(callResult);

		callResult.IsError.Should().NotBeTrue();
		response.Success.Should().BeFalse();
		response.Error.Should().Contain(".docx");
	}

	[Test]
	[Description("upload-report-template refuses without confirmation, before any file or remote access.")]
	[AllureTag(PrintableTemplateUploadTool.ToolName)]
	[AllureName("upload-report-template MCP tool requires confirmation")]
	public async Task UploadReportTemplate_Should_Require_Confirmation() {
		await using McpSessionArrangeContext arrange = await McpSessionArrangeContext.ArrangeAsync(TimeSpan.FromMinutes(3));

		CallToolResult callResult = await arrange.Session.CallToolAsync(
			PrintableTemplateUploadTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = $"missing-{Guid.NewGuid():N}",
					["id"] = Guid.NewGuid().ToString(),
					["file-path"] = "/tmp/missing-template.docx"
				}
			},
			arrange.CancellationTokenSource.Token);
		PrintableTemplateUploadResponse response =
			EntitySchemaStructuredResultParser.Extract<PrintableTemplateUploadResponse>(callResult);

		callResult.IsError.Should().NotBeTrue();
		response.Success.Should().BeFalse();
		response.Error.Should().Contain("without confirmation");
	}
}
