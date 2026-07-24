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
/// Hermetic (NoEnvironment) end-to-end coverage for the upload-image MCP tool: the real clio MCP server
/// advertises upload-image on the lazy surface, binds the args wrapper, validates the required fields
/// with structured failures, and rejects a camelCase alias with a rename hint — none of which needs a
/// live Creatio environment. The live upload (which requires forms-auth credentials and the image API)
/// is a sandbox-environment concern, mirroring how ThemingSandboxE2ETests covers the live theming writes.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("upload-image")]
[NonParallelizable]
public sealed class UploadImageToolE2ETests : McpContractFixtureBase {
	[Test]
	[AllureTag(UploadImageTool.ToolName)]
	[AllureName("upload-image tool is discoverable on the lazy surface")]
	[AllureDescription("Starts the real clio MCP server and verifies upload-image is discoverable via the get-tool-contract compact index on the lazy tool surface.")]
	[Description("Starts the real clio MCP server and verifies upload-image is discoverable via the get-tool-contract compact index on the lazy tool surface.")]
	public async Task UploadImage_Should_Be_Discoverable_On_Lazy_Surface() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(UploadImageTool.ToolName,
			because: "the upload-image MCP tool must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list");
	}

	[Test]
	[AllureTag(UploadImageTool.ToolName)]
	[AllureName("upload-image binds the args wrapper and returns a structured validation failure")]
	[AllureDescription("Calls upload-image through the real clio MCP server with an empty args object and verifies the structured { success=false, error } result names environment-name — proving the args wrapper binds without a live Creatio environment.")]
	[Description("Calls upload-image through the real clio MCP server with an empty args object and verifies the structured { success=false, error } result names environment-name — proving the args wrapper binds without a live Creatio environment.")]
	public async Task UploadImage_Should_Return_Structured_Validation_Failure_When_Args_Are_Empty() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			UploadImageTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?>()
			},
			context.CancellationTokenSource.Token);
		UploadImageResult result = EntitySchemaStructuredResultParser.Extract<UploadImageResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an argument mistake must surface as a structured in-tool failure, not an MCP protocol error");
		result.Success.Should().BeFalse(
			because: "a missing environment name is an expected, caller-actionable validation error");
		result.Error.Should().Contain("environment-name is required",
			because: "the failure must name the exact kebab-case field the caller has to add");
	}

	[Test]
	[AllureTag(UploadImageTool.ToolName)]
	[AllureName("upload-image requires the file argument before any environment work")]
	[AllureDescription("Calls upload-image with only environment-name and verifies the structured failure names the missing file field — the validation runs before environment resolution, so no live Creatio environment is needed.")]
	[Description("Calls upload-image with only environment-name and verifies the structured failure names the missing file field — the validation runs before environment resolution, so no live Creatio environment is needed.")]
	public async Task UploadImage_Should_Return_Structured_Validation_Failure_When_File_Is_Missing() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			UploadImageTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = "docker_fix2"
				}
			},
			context.CancellationTokenSource.Token);
		UploadImageResult result = EntitySchemaStructuredResultParser.Extract<UploadImageResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an argument mistake must surface as a structured in-tool failure, not an MCP protocol error");
		result.Success.Should().BeFalse(
			because: "a missing file path leaves nothing to upload");
		result.Error.Should().Contain("file is required",
			because: "the failure must name the exact field the caller has to add");
	}

	[Test]
	[AllureTag(UploadImageTool.ToolName)]
	[AllureName("upload-image rejects a camelCase alias with a structured rename hint over the wire")]
	[AllureDescription("Calls upload-image through the real clio MCP server with a camelCase environmentName field and verifies the structured rename hint — proving the args wrapper binds and unknown keys reach the ExtensionData bag through the real MCP serializer, without a live Creatio environment.")]
	[Description("Calls upload-image through the real clio MCP server with a camelCase environmentName field and verifies the structured rename hint — proving the args wrapper binds and unknown keys reach the ExtensionData bag through the real MCP serializer, without a live Creatio environment.")]
	public async Task UploadImage_Should_Return_RenameHint_When_CamelCase_Alias_Is_Passed_Over_The_Wire() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			UploadImageTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environmentName"] = "docker_fix2",
					["file"] = "C:/brand/background.png"
				}
			},
			context.CancellationTokenSource.Token);
		UploadImageResult result = EntitySchemaStructuredResultParser.Extract<UploadImageResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an argument mistake must surface as a structured in-tool failure, not an MCP protocol error");
		result.Success.Should().BeFalse(
			because: "a camelCase alias must be rejected, not silently dropped");
		result.Error.Should().Contain("'environmentName' -> 'environment-name'",
			because: "the failure must tell the caller the exact rename that fixes the call");
	}
}
