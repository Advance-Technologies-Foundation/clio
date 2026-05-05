using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the validate-page MCP tool.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(PageValidateTool.ToolName)]
[NonParallelizable]
public sealed class PageValidateToolE2ETests {

	private const string ToolName = PageValidateTool.ToolName;

	private const string ValidPageBody =
		"define(\"TestPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
		"function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	[Test]
	[Description("Advertises validate-page MCP tool in the server tool list so callers can discover and invoke it.")]
	[AllureTag(ToolName)]
	[AllureName("validate-page tool is advertised by the MCP server")]
	[AllureDescription("Verifies that validate-page appears in the MCP server tool manifest.")]
	public async Task PageValidateTool_Should_Be_Listed_By_MCP_Server() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();

		// Act
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);

		// Assert
		tools.Select(t => t.Name).Should().Contain(ToolName,
			because: "validate-page must be advertised so MCP clients can discover the client-side validation tool");
	}

	[Test]
	[Description("Returns valid: true when the page body is structurally correct.")]
	[AllureTag(ToolName)]
	[AllureName("validate-page accepts a well-formed page body")]
	[AllureDescription("Sends a valid Freedom UI page body through the real MCP server and verifies that validation passes.")]
	public async Task PageValidateTool_Should_Accept_Valid_Page_Body() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();

		// Act
		PageValidateResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			ValidPageBody);

		// Assert
		response.Valid.Should().BeTrue(
			because: "a well-formed page body with all required markers should pass client-side validation");
		response.Validation.Should().NotBeNull(
			because: "validation details are always included in the response");
		response.Validation!.MarkersOk.Should().BeTrue(
			because: "all required schema markers are present in the valid body");
		response.Validation.ContentOk.Should().BeTrue(
			because: "all marker sections contain valid structured content");
		response.Validation.Errors.Should().BeNullOrEmpty(
			because: "a valid body should produce no validation errors");
	}

	[Test]
	[Description("Returns valid: false with a VendorPrefix error when a converter key in SCHEMA_CONVERTERS is missing the required dot.")]
	[AllureTag(ToolName)]
	[AllureName("validate-page rejects converter key without dot")]
	[AllureDescription("Sends a page body with a SCHEMA_CONVERTERS entry whose key has no dot separator through the real MCP server and verifies that validation fails with an actionable error.")]
	public async Task PageValidateTool_Should_Reject_Converter_Key_Without_Dot() {
		// Arrange
		string bodyWithBadConverter = ValidPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"/**SCHEMA_CONVERTERS*/{ \"UsrBadConverter\": function(value) { return value; } }/**SCHEMA_CONVERTERS*/");
		await using ArrangeContext context = await ArrangeAsync();

		// Act
		PageValidateResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			bodyWithBadConverter);

		// Assert
		response.Valid.Should().BeFalse(
			because: "a converter key without a dot causes a Creatio runtime error and must be rejected");
		response.Validation.Should().NotBeNull(
			because: "validation details are always included in the response");
		response.Validation!.ContentOk.Should().BeFalse(
			because: "converter key format failure is a content-level error");
		response.Validation.Errors.Should().NotBeNullOrEmpty(
			because: "the validation result must list the specific error to give the agent actionable feedback");
		response.Validation.Errors!.Should().Contain(
			e => e.Contains("UsrBadConverter") && e.Contains("VendorPrefix"),
			because: "the error must name the offending key and reference the VendorPrefix.Name format requirement");
	}

	private static async Task<PageValidateResponse> CallAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		string body) {
		CallToolResult callResult = await session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["body"] = body
				}
			},
			cancellationToken);
		callResult.IsError.Should().NotBeTrue(
			because: "validate-page should return a structured tool result, not a protocol-level error");
		return EntitySchemaStructuredResultParser.Extract<PageValidateResponse>(callResult);
	}

	private static async Task<ArrangeContext> ArrangeAsync() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
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
