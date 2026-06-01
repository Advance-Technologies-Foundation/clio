using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
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
	[Test]
	[Description("Advertises list-printables as a read-only MCP tool.")]
	[AllureTag(PrintableListTool.ToolName)]
	[AllureName("list-printables MCP tool is advertised")]
	public async Task ListPrintables_Should_Be_Advertised() {
		await using ArrangeContext arrange = await ArrangeAsync(TimeSpan.FromMinutes(3));
		IList<McpClientTool> tools = await arrange.Session.ListToolsAsync(arrange.CancellationTokenSource.Token);
		McpClientTool tool = tools.Single(t => t.Name == PrintableListTool.ToolName);
		tool.ProtocolTool.Annotations!.ReadOnlyHint.Should().BeTrue();
		tool.ProtocolTool.Annotations.DestructiveHint.Should().BeFalse();
	}

	[Test]
	[Description("Advertises get-printable as a read-only MCP tool.")]
	[AllureTag(PrintableGetTool.ToolName)]
	[AllureName("get-printable MCP tool is advertised")]
	public async Task GetPrintable_Should_Be_Advertised() {
		await using ArrangeContext arrange = await ArrangeAsync(TimeSpan.FromMinutes(3));
		IList<McpClientTool> tools = await arrange.Session.ListToolsAsync(arrange.CancellationTokenSource.Token);
		McpClientTool tool = tools.Single(t => t.Name == PrintableGetTool.ToolName);
		tool.ProtocolTool.Annotations!.ReadOnlyHint.Should().BeTrue();
		tool.ProtocolTool.Annotations.DestructiveHint.Should().BeFalse();
	}

	[Test]
	[Description("Advertises create-printable as a non-read-only, non-destructive MCP tool.")]
	[AllureTag(PrintableCreateTool.ToolName)]
	[AllureName("create-printable MCP tool is advertised")]
	public async Task CreatePrintable_Should_Be_Advertised() {
		await using ArrangeContext arrange = await ArrangeAsync(TimeSpan.FromMinutes(3));
		IList<McpClientTool> tools = await arrange.Session.ListToolsAsync(arrange.CancellationTokenSource.Token);
		McpClientTool tool = tools.Single(t => t.Name == PrintableCreateTool.ToolName);
		tool.ProtocolTool.Annotations!.ReadOnlyHint.Should().BeFalse();
		tool.ProtocolTool.Annotations.DestructiveHint.Should().BeFalse();
	}

	[Test]
	[Description("Advertises update-printable as a destructive MCP tool.")]
	[AllureTag(PrintableUpdateTool.ToolName)]
	[AllureName("update-printable MCP tool is advertised")]
	public async Task UpdatePrintable_Should_Be_Advertised() {
		await using ArrangeContext arrange = await ArrangeAsync(TimeSpan.FromMinutes(3));
		IList<McpClientTool> tools = await arrange.Session.ListToolsAsync(arrange.CancellationTokenSource.Token);
		McpClientTool tool = tools.Single(t => t.Name == PrintableUpdateTool.ToolName);
		tool.ProtocolTool.Annotations!.ReadOnlyHint.Should().BeFalse();
		tool.ProtocolTool.Annotations.DestructiveHint.Should().BeTrue();
	}

	[Test]
	[Description("Advertises delete-printable as a destructive MCP tool.")]
	[AllureTag(PrintableDeleteTool.ToolName)]
	[AllureName("delete-printable MCP tool is advertised")]
	public async Task DeletePrintable_Should_Be_Advertised() {
		await using ArrangeContext arrange = await ArrangeAsync(TimeSpan.FromMinutes(3));
		IList<McpClientTool> tools = await arrange.Session.ListToolsAsync(arrange.CancellationTokenSource.Token);
		McpClientTool tool = tools.Single(t => t.Name == PrintableDeleteTool.ToolName);
		tool.ProtocolTool.Annotations!.ReadOnlyHint.Should().BeFalse();
		tool.ProtocolTool.Annotations.DestructiveHint.Should().BeTrue();
	}

	[Test]
	[Description("Advertises upload-report-template as a destructive MCP tool.")]
	[AllureTag(PrintableTemplateUploadTool.ToolName)]
	[AllureName("upload-report-template MCP tool is advertised")]
	public async Task UploadReportTemplate_Should_Be_Advertised() {
		await using ArrangeContext arrange = await ArrangeAsync(TimeSpan.FromMinutes(3));
		IList<McpClientTool> tools = await arrange.Session.ListToolsAsync(arrange.CancellationTokenSource.Token);
		McpClientTool tool = tools.Single(t => t.Name == PrintableTemplateUploadTool.ToolName);
		tool.ProtocolTool.Annotations!.ReadOnlyHint.Should().BeFalse();
		tool.ProtocolTool.Annotations.DestructiveHint.Should().BeTrue();
	}

	[Test]
	[Description("Binds list-printables arguments and reports a structured failure for an unknown environment.")]
	[AllureTag(PrintableListTool.ToolName)]
	[AllureName("list-printables MCP tool binds arguments")]
	public async Task ListPrintables_Should_Bind_Arguments_And_Report_Invalid_Environment() {
		await using ArrangeContext arrange = await ArrangeAsync(TimeSpan.FromMinutes(3));
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
		await using ArrangeContext arrange = await ArrangeAsync(TimeSpan.FromMinutes(3));
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
		await using ArrangeContext arrange = await ArrangeAsync(TimeSpan.FromMinutes(3));

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

	[Test]
	[Description("update-printable rejects a non-GUID id through the real MCP server without touching an environment.")]
	[AllureTag(PrintableUpdateTool.ToolName)]
	[AllureName("update-printable MCP tool guards keyless updates")]
	public async Task UpdatePrintable_Should_Reject_NonGuid_Id() {
		await using ArrangeContext arrange = await ArrangeAsync(TimeSpan.FromMinutes(3));

		CallToolResult callResult = await arrange.Session.CallToolAsync(
			PrintableUpdateTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = $"missing-{Guid.NewGuid():N}",
					["id"] = "all",
					["caption"] = "E2E",
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
	[Description("delete-printable rejects a non-GUID id through the real MCP server without touching an environment.")]
	[AllureTag(PrintableDeleteTool.ToolName)]
	[AllureName("delete-printable MCP tool guards keyless deletes")]
	public async Task DeletePrintable_Should_Reject_NonGuid_Id() {
		await using ArrangeContext arrange = await ArrangeAsync(TimeSpan.FromMinutes(3));

		CallToolResult callResult = await arrange.Session.CallToolAsync(
			PrintableDeleteTool.ToolName,
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
		await using ArrangeContext arrange = await ArrangeAsync(TimeSpan.FromMinutes(3));

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
		await using ArrangeContext arrange = await ArrangeAsync(TimeSpan.FromMinutes(3));

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
		await using ArrangeContext arrange = await ArrangeAsync(TimeSpan.FromMinutes(3));

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

	private static async Task<ArrangeContext> ArrangeAsync(TimeSpan timeout) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		CancellationTokenSource cancellationTokenSource = new(timeout);
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource);
	}

	private sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}
}
