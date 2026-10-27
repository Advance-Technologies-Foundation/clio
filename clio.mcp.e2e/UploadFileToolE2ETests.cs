using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Hermetic (NoEnvironment) end-to-end coverage for the upload-file MCP tool. The real clio MCP server
/// advertises upload-file on the lazy surface, binds the args wrapper, validates the required fields with
/// structured failures, and rejects a camelCase alias with a rename hint. None of these needs a live
/// Creatio environment. <see cref="UploadFileHappyPathE2ETests"/> covers the upload itself against a
/// loopback fake.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("upload-file")]
[NonParallelizable]
public sealed class UploadFileToolE2ETests : McpContractFixtureBase {
	private const string ParentId = "410006e1-ca4e-4502-a9ec-e54d922d2c00";

	[Test]
	[AllureTag(UploadFileTool.ToolName)]
	[AllureName("upload-file tool is discoverable on the lazy surface")]
	[AllureDescription("Starts the real clio MCP server and verifies upload-file is discoverable via the get-tool-contract compact index on the lazy tool surface.")]
	[Description("Starts the real clio MCP server and verifies upload-file is discoverable via the get-tool-contract compact index on the lazy tool surface.")]
	public async Task UploadFile_Should_Be_Discoverable_On_Lazy_Surface() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(UploadFileTool.ToolName,
			because: "the upload-file MCP tool must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list");
	}

	[Test]
	[AllureTag(UploadFileTool.ToolName)]
	[AllureName("upload-file binds the args wrapper and returns a structured validation failure")]
	[AllureDescription("Calls upload-file through the real clio MCP server with an empty args object and verifies the structured { success=false, error } result names environment-name. This proves the args wrapper binds without a live Creatio environment.")]
	[Description("Calls upload-file through the real clio MCP server with an empty args object and verifies the structured { success=false, error } result names environment-name.")]
	public async Task UploadFile_Should_Return_Structured_Validation_Failure_When_Args_Are_Empty() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await CallAsync(context, new Dictionary<string, object?>());
		UploadFileResult result = EntitySchemaStructuredResultParser.Extract<UploadFileResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an argument mistake must surface as a structured in-tool failure, not an MCP protocol error");
		result.Success.Should().BeFalse(
			because: "a missing environment name is an expected, caller-actionable validation error");
		result.Error.Should().Contain("environment-name is required",
			because: "the failure must name the exact kebab-case field the caller has to add");
		result.FileId.Should().BeNull(
			because: "nothing was sent, so there is no file record the caller has to check");
	}

	[Test]
	[AllureTag(UploadFileTool.ToolName)]
	[AllureName("upload-file requires the file argument before any environment work")]
	[AllureDescription("Calls upload-file with only environment-name and verifies the structured failure names the missing file field. The validation runs before environment resolution, so no live Creatio environment is needed.")]
	[Description("Calls upload-file with only environment-name and verifies the structured failure names the missing file field.")]
	public async Task UploadFile_Should_Return_Structured_Validation_Failure_When_File_Is_Missing() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await CallAsync(context, new Dictionary<string, object?> {
			["environment-name"] = "docker_fix2"
		});
		UploadFileResult result = EntitySchemaStructuredResultParser.Extract<UploadFileResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an argument mistake must surface as a structured in-tool failure, not an MCP protocol error");
		result.Success.Should().BeFalse(
			because: "a missing file path leaves nothing to upload");
		result.Error.Should().Contain("file is required",
			because: "the failure must name the exact field the caller has to add");
	}

	[Test]
	[AllureTag(UploadFileTool.ToolName)]
	[AllureName("upload-file requires the parent-id argument before any environment work")]
	[AllureDescription("Calls upload-file with every field except parent-id and verifies the structured failure names parent-id. The validation runs before environment resolution, so no live Creatio environment is needed.")]
	[Description("Calls upload-file without parent-id and verifies the structured failure names the missing parent-id field.")]
	public async Task UploadFile_Should_Return_Structured_Validation_Failure_When_ParentId_Is_Missing() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await CallAsync(context, new Dictionary<string, object?> {
			["environment-name"] = "docker_fix2",
			["file"] = "C:/docs/contract.pdf",
			["entity"] = "ContactFile",
			["parent-column"] = "Contact"
		});
		UploadFileResult result = EntitySchemaStructuredResultParser.Extract<UploadFileResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an argument mistake must surface as a structured in-tool failure, not an MCP protocol error");
		result.Success.Should().BeFalse(
			because: "without a parent record the file cannot be attached to anything");
		result.Error.Should().Contain("parent-id is required",
			because: "the failure must name the exact field the caller has to add");
	}

	[Test]
	[AllureTag(UploadFileTool.ToolName)]
	[AllureName("upload-file rejects a camelCase alias with a structured rename hint over the wire")]
	[AllureDescription("Calls upload-file through the real clio MCP server with a camelCase parentId field and verifies the structured rename hint. This proves unknown keys reach the ExtensionData bag through the real MCP serializer, without a live Creatio environment.")]
	[Description("Calls upload-file with a camelCase parentId field and verifies the structured rename hint names parent-id.")]
	public async Task UploadFile_Should_Return_RenameHint_When_CamelCase_Alias_Is_Passed_Over_The_Wire() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await CallAsync(context, new Dictionary<string, object?> {
			["environment-name"] = "docker_fix2",
			["file"] = "C:/docs/contract.pdf",
			["entity"] = "ContactFile",
			["parent-column"] = "Contact",
			["parentId"] = ParentId
		});
		UploadFileResult result = EntitySchemaStructuredResultParser.Extract<UploadFileResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an argument mistake must surface as a structured in-tool failure, not an MCP protocol error");
		result.Success.Should().BeFalse(
			because: "a camelCase alias must be rejected, not silently dropped");
		result.Error.Should().Contain("'parentId' -> 'parent-id'",
			because: "the failure must tell the caller the exact rename that fixes the call");
	}

	private static Task<CallToolResult> CallAsync(ArrangeContext context, Dictionary<string, object?> args) =>
		context.Session.CallToolAsync(
			UploadFileTool.ToolName,
			new Dictionary<string, object?> { ["args"] = args },
			context.CancellationTokenSource.Token);
}
