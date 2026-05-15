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

	[Test]
	[Description("Returns valid: false with a VendorPrefix error when a SCHEMA_HANDLERS entry's request value is missing the required dot.")]
	[AllureTag(ToolName)]
	[AllureName("validate-page rejects handler request value without dot")]
	[AllureDescription("Sends a page body with a SCHEMA_HANDLERS array entry whose request value has no dot separator through the real MCP server and verifies that validation fails with an actionable error.")]
	public async Task PageValidateTool_Should_Reject_Handler_Request_Without_Dot() {
		// Arrange
		string bodyWithBadHandler = ValidPageBody.Replace(
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
			"/**SCHEMA_HANDLERS*/[{ request: \"BadHandlerRequest\", " +
			"handler: async (request, next) => { await next?.handle(request); } }]/**SCHEMA_HANDLERS*/");
		await using ArrangeContext context = await ArrangeAsync();

		// Act
		PageValidateResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			bodyWithBadHandler);

		// Assert
		response.Valid.Should().BeFalse(
			because: "a handler request value without a dot causes a Creatio runtime error and must be rejected");
		response.Validation.Should().NotBeNull(
			because: "validation details are always included in the response");
		response.Validation!.ContentOk.Should().BeFalse(
			because: "handler request format failure is a content-level error");
		response.Validation.Errors.Should().NotBeNullOrEmpty(
			because: "the validation result must list the specific error to give the agent actionable feedback");
		response.Validation.Errors!.Should().Contain(
			e => e.Contains("BadHandlerRequest") && e.Contains("VendorPrefix") && e.Contains("page-schema-handlers"),
			because: "the error must name the offending request value and direct the agent at the handler guidance");
	}

	[Test]
	[Description("Returns valid: false with a VendorPrefix error when a validator key in SCHEMA_VALIDATORS is missing the required dot.")]
	[AllureTag(ToolName)]
	[AllureName("validate-page rejects validator key without dot")]
	[AllureDescription("Sends a page body with a SCHEMA_VALIDATORS entry whose key has no dot separator through the real MCP server and verifies that validation fails with an actionable error.")]
	public async Task PageValidateTool_Should_Reject_Validator_Key_Without_Dot() {
		// Arrange
		string bodyWithBadValidator = ValidPageBody.Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"/**SCHEMA_VALIDATORS*/{ \"BadValidator\": { params: [] } }/**SCHEMA_VALIDATORS*/");
		await using ArrangeContext context = await ArrangeAsync();

		// Act
		PageValidateResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			bodyWithBadValidator);

		// Assert
		response.Valid.Should().BeFalse(
			because: "a validator key without a dot causes a Creatio runtime error and must be rejected");
		response.Validation.Should().NotBeNull(
			because: "validation details are always included in the response");
		response.Validation!.ContentOk.Should().BeFalse(
			because: "validator key format failure is a content-level error");
		response.Validation.Errors.Should().NotBeNullOrEmpty(
			because: "the validation result must list the specific error to give the agent actionable feedback");
		response.Validation.Errors!.Should().Contain(
			e => e.Contains("BadValidator") && e.Contains("VendorPrefix") && e.Contains("page-schema-validators"),
			because: "the error must name the offending key and direct the agent at the validator guidance");
	}

	[Test]
	[Description("Returns valid: false when a custom validator uses 'validate' key instead of the runtime-recognised 'validator' factory key.")]
	[AllureTag(ToolName)]
	[AllureName("validate-page rejects custom validator with 'validate' key alias")]
	[AllureDescription("Sends a page body where SCHEMA_VALIDATORS uses the misleading 'validate' key alias instead of the canonical 'validator' factory key, and verifies that validate-page reports the validator never executes.")]
	public async Task PageValidateTool_Should_Reject_Validator_With_Validate_Key_Alias() {
		// Arrange
		string bodyWithValidateAlias = ValidPageBody.Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"/**SCHEMA_VALIDATORS*/{ \"usr.PhoneFormatValidator\": { params: [{ \"name\": \"message\" }], async: false, " +
			"validate: function(value, config) { return null; } } }/**SCHEMA_VALIDATORS*/");
		await using ArrangeContext context = await ArrangeAsync();

		// Act
		PageValidateResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			bodyWithValidateAlias);

		// Assert
		response.Valid.Should().BeFalse(
			because: "the runtime ignores any key other than 'validator', so a 'validate' alias means the validator never executes");
		response.Validation.Should().NotBeNull(
			because: "validation details are always included in the response");
		response.Validation!.ContentOk.Should().BeFalse(
			because: "validator factory shape failure is a content-level error");
		response.Validation.Errors.Should().NotBeNullOrEmpty(
			because: "the validation result must list the specific factory-shape error");
		response.Validation.Errors!.Should().Contain(
			e => e.Contains("usr.PhoneFormatValidator") && e.Contains("'validate'") && e.Contains("'validator'") &&
			     e.Contains("page-schema-validators"),
			because: "the error must name the offending validator and the wrong key, and direct the agent at the validator guidance");
	}

	[Test]
	[Description("Returns valid: false when a custom converter is declared as an object literal instead of a callable function expression.")]
	[AllureTag(ToolName)]
	[AllureName("validate-page rejects converter declared as object literal")]
	[AllureDescription("Sends a page body where SCHEMA_CONVERTERS contains an object literal in place of a function value and verifies that validate-page reports the converter is not callable.")]
	public async Task PageValidateTool_Should_Reject_Converter_With_Object_Literal_Value() {
		// Arrange
		string bodyWithObjectConverter = ValidPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"/**SCHEMA_CONVERTERS*/{ \"usr.WrongShape\": { transform: \"upper\" } }/**SCHEMA_CONVERTERS*/");
		await using ArrangeContext context = await ArrangeAsync();

		// Act
		PageValidateResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			bodyWithObjectConverter);

		// Assert
		response.Valid.Should().BeFalse(
			because: "converters must be callable function expressions; an object literal silently fails to apply at the binding site");
		response.Validation.Should().NotBeNull(
			because: "validation details are always included in the response");
		response.Validation!.ContentOk.Should().BeFalse(
			because: "converter function shape failure is a content-level error");
		response.Validation.Errors.Should().NotBeNullOrEmpty(
			because: "the validation result must list the specific function-shape error");
		response.Validation.Errors!.Should().Contain(
			e => e.Contains("usr.WrongShape") && e.Contains("not callable") && e.Contains("page-schema-converters"),
			because: "the error must name the offending converter and direct the agent at the converter guidance");
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

	[Test]
	[Description("Returns valid=true for a well-formed mobile JSON body with allowed sections only.")]
	[AllureTag(ToolName)]
	[AllureName("validate-page accepts a valid mobile JSON body")]
	[AllureDescription("Verifies that validate-page returns valid=true for a plain-JSON mobile body with no disallowed keys.")]
	public async Task PageValidateTool_Should_Return_Valid_For_Well_Formed_Mobile_Body() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();
		string mobileBody = """
			{
			  "viewConfigDiff": [],
			  "viewModelConfigDiff": [],
			  "modelConfigDiff": []
			}
			""";

		// Act
		PageValidateResponse response = await CallAsync(context.Session, context.CancellationTokenSource.Token, mobileBody);

		// Assert
		response.Valid.Should().BeTrue(
			because: "a well-formed mobile JSON body with no disallowed keys should pass validation");
		response.Validation.ContentOk.Should().BeTrue(
			because: "mobile body with allowed sections only should pass content validation");
	}

	[Test]
	[Description("Returns valid=false for a mobile JSON body that contains a 'validators' section.")]
	[AllureTag(ToolName)]
	[AllureName("validate-page rejects mobile body with 'validators' key")]
	[AllureDescription("Verifies that validate-page rejects a mobile body that contains the 'validators' key.")]
	public async Task PageValidateTool_Should_Reject_Mobile_Body_With_Validators() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();
		string mobileBodyWithValidators = """
			{
			  "viewConfigDiff": [],
			  "validators": {}
			}
			""";

		// Act
		PageValidateResponse response = await CallAsync(context.Session, context.CancellationTokenSource.Token, mobileBodyWithValidators);

		// Assert
		response.Valid.Should().BeFalse(
			because: "mobile pages do not support the 'validators' key");
		response.Validation.ContentOk.Should().BeFalse(
			because: "the 'validators' key is disallowed in mobile page bodies");
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
