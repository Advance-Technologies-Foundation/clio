using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
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
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(PageValidateTool.ToolName)]
[NonParallelizable]
public sealed class PageValidateToolE2ETests : McpContractFixtureBase {

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
		await using var context = Arrange(TimeSpan.FromMinutes(3));

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
		await using var context = Arrange(TimeSpan.FromMinutes(3));

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
	[Description("Accepts an explicit version argument and still validates a well-formed body — proves the version arg flows end-to-end through the real MCP transport into the registry-driven chart-widget validation path.")]
	[AllureTag(ToolName)]
	[AllureName("validate-page accepts an explicit version argument")]
	[AllureDescription("Sends a valid Freedom UI page body with an explicit version through the real MCP server and verifies the call is accepted (not a protocol error) and validation still passes.")]
	public async Task PageValidateTool_Should_Accept_Explicit_Version_Argument() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["body"] = ValidPageBody,
					["version"] = "8.3.3"
				}
			},
			context.CancellationTokenSource.Token);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an explicit version must be accepted and bound by the tool, not rejected as a protocol-level error");
		PageValidateResponse response = EntitySchemaStructuredResultParser.Extract<PageValidateResponse>(callResult);
		response.Valid.Should().BeTrue(
			because: "a well-formed body validates whether the chart-widget catalog is scoped to the version or falls back to latest");
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
		await using var context = Arrange(TimeSpan.FromMinutes(3));

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
		await using var context = Arrange(TimeSpan.FromMinutes(3));

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
		await using var context = Arrange(TimeSpan.FromMinutes(3));

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
		await using var context = Arrange(TimeSpan.FromMinutes(3));

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
		await using var context = Arrange(TimeSpan.FromMinutes(3));

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

	[Test]
	[Description("Returns valid: false when viewConfigDiff inserts a standard field control whose binding attribute is not declared in viewModelConfigDiff and whose label resource is neither registered nor auto-provided.")]
	[AllureTag(ToolName)]
	[AllureName("validate-page rejects inserted field without binding or resource")]
	[AllureDescription("Sends a page body that inserts a crt.Checkbox bound to an undeclared PDS_<column> attribute with no resources payload, and verifies that validate-page surfaces an actionable error naming the offending field and the missing section.")]
	public async Task PageValidateTool_Should_Reject_Inserted_Field_Without_Binding_Or_Resource() {
		// Arrange
		string bodyWithBareInsert = ValidPageBody.Replace(
			"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/",
			"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[" +
				"{\"operation\":\"insert\",\"name\":\"UsrCompleted\",\"values\":{\"type\":\"crt.Checkbox\"," +
				"\"label\":\"$Resources.Strings.PDS_UsrCompleted\",\"control\":\"$PDS_UsrCompleted\"}}" +
				"]/**SCHEMA_VIEW_CONFIG_DIFF*/");
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		PageValidateResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			bodyWithBareInsert);

		// Assert
		response.Valid.Should().BeFalse(
			because: "an inserted field without a matching viewModelConfigDiff attribute or a resolvable label resource would render with no data source and a blank caption");
		response.Validation.Should().NotBeNull(
			because: "validation details are always included in the response");
		response.Validation!.ContentOk.Should().BeFalse(
			because: "the inserted-field self-consistency check is a content-level validator");
		response.Validation.Errors.Should().NotBeNullOrEmpty(
			because: "the validation result must list at least one error for the broken insert");
		response.Validation.Errors!.Should().Contain(
			e => e.Contains("UsrCompleted") && e.Contains("PDS_UsrCompleted") && e.Contains("viewModelConfigDiff"),
			because: "the missing-binding diagnostic must name the field, the binding attribute, and the section that needs the declaration");
		response.Validation.Errors!.Should().Contain(
			e => e.Contains("UsrCompleted") && e.Contains("PDS_UsrCompleted") && e.Contains("render blank"),
			because: "the unregistered-label diagnostic must name the field, the resource key, and what will go wrong at runtime");
	}

	[Test]
	[Description("Returns valid: true when viewConfigDiff inserts a standard field whose binding attribute is declared in viewModelConfigDiff with a DS-bound modelConfig.path and whose label key equals that binding attribute — auto-provided by the platform under the attribute name, even when it differs from the entity column code.")]
	[AllureTag(ToolName)]
	[AllureName("validate-page accepts inserted field with declared binding and attribute-name label")]
	[AllureDescription("Sends a page body that inserts a crt.Checkbox bound to a declared DS attribute (PDS_UsrCompleted, whose name differs from the entity column code UsrCompleted) with the label set to $Resources.Strings.PDS_UsrCompleted, and verifies that validate-page accepts the payload without resources because the platform auto-provides the caption under the attribute name.")]
	public async Task PageValidateTool_Should_Accept_Inserted_Field_With_AutoProvided_Label() {
		// Arrange
		string bodyWithFullPayload = ValidPageBody
			.Replace(
				"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/",
				"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[" +
					"{\"operation\":\"insert\",\"name\":\"UsrCompleted\",\"values\":{\"type\":\"crt.Checkbox\"," +
					"\"label\":\"$Resources.Strings.PDS_UsrCompleted\",\"control\":\"$PDS_UsrCompleted\"}}" +
					"]/**SCHEMA_VIEW_CONFIG_DIFF*/")
			.Replace(
				"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/",
				"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[" +
					"{\"operation\":\"merge\",\"path\":[],\"values\":{\"attributes\":{\"PDS_UsrCompleted\":{\"modelConfig\":{\"path\":\"PDS.UsrCompleted\"}}}}}" +
					"]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/");
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		PageValidateResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			bodyWithFullPayload);

		// Assert
		response.Valid.Should().BeTrue(
			because: "a self-consistent insert with the matching viewModelConfigDiff entry and a label keyed by the DS-bound binding attribute (auto-provided) is the canonical happy path");
		response.Validation.Should().NotBeNull(
			because: "validation details are always included in the response");
		response.Validation!.ContentOk.Should().BeTrue(
			because: "every content-level validator should accept the self-consistent payload");
		response.Validation.Errors.Should().BeNullOrEmpty(
			because: "no error should be reported for the canonical happy path");
	}

	[Test]
	[Description("Returns valid: false when viewConfigDiff sets a user-visible text property (placeholder) to an inline string literal instead of a localizable-string binding — proves the localizable-text hard reject fires through the real MCP transport.")]
	[AllureTag(ToolName)]
	[AllureName("validate-page rejects inline placeholder literal")]
	[AllureDescription("Sends a page body whose inserted crt.Input carries a hardcoded placeholder string and verifies that validate-page surfaces an actionable error naming the node and the placeholder property and pointing to the page-schema-resources guide.")]
	public async Task PageValidateTool_Should_Reject_Inline_Placeholder_Literal() {
		// Arrange
		string bodyWithInlinePlaceholder = ValidPageBody.Replace(
			"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/",
			"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[" +
				"{\"operation\":\"insert\",\"name\":\"EmailField\",\"values\":{\"type\":\"crt.Input\"," +
				"\"control\":\"$Email\",\"placeholder\":\"name@firm.com\"}}" +
				"]/**SCHEMA_VIEW_CONFIG_DIFF*/");
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		PageValidateResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			bodyWithInlinePlaceholder);

		// Assert
		response.Valid.Should().BeFalse(
			because: "a hardcoded placeholder string is not localizable and must be rejected by the localizable-text check");
		response.Validation.Should().NotBeNull(
			because: "validation details are always included in the response");
		response.Validation!.ContentOk.Should().BeFalse(
			because: "the localizable-text rule is a content-level validator");
		response.Validation.Errors.Should().NotBeNullOrEmpty(
			because: "the validation result must list at least one error for the inline placeholder literal");
		response.Validation.Errors!.Should().Contain(
			e => e.Contains("EmailField") && e.Contains("placeholder") && e.Contains("page-schema-resources"),
			because: "the diagnostic must name the node, the offending property, and point to the localization guide");
	}

	[Test]
	[Description("validate-page rejects a body whose JavaScript syntax is invalid (the production incident shape `await X = Y`) — proves the new Acornima syntax pre-flight gate fires through the real MCP transport, not just the regex brace-counter that previously passed this body as syntax-OK.")]
	[AllureTag(ToolName)]
	[AllureName("validate-page rejects body with await-as-assignment-target syntax error")]
	[AllureDescription("Sends the canonical incident body (`await request.$context.X = \"value\"`) through validate-page and verifies that the response carries valid=false with a `JavaScript syntax error at line N, column M` message — proving the pre-flight tool now matches the write-path tools' Acornima-based gate.")]
	public async Task PageValidateTool_Should_Reject_Body_With_JavaScript_Syntax_Error() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));
		string incidentBody =
			"define(\"Bad_FormPage\", [], function() {\n" +
			"    return {\n" +
			"        handlers: [{\n" +
			"            request: 'crt.HandleViewModelInitRequest',\n" +
			"            handler: async function(request, next) {\n" +
			"                await request.$context.FieldX = \"value\";\n" +
			"                return next?.handle(request);\n" +
			"            }\n" +
			"        }]\n" +
			"    };\n" +
			"});";

		// Act
		PageValidateResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			incidentBody);

		// Assert
		response.Valid.Should().BeFalse(
			because: "the body's `await X = Y` shape is a JavaScript syntax error; the pre-flight tool must reject it before any write tool sees it");
		response.Validation.JsSyntaxOk.Should().BeFalse(
			because: "the syntax gate must surface its dedicated JsSyntaxOk=false signal so callers can route to the right fix");
		response.Validation.Errors.Should().NotBeNullOrEmpty(
			because: "an actionable failure response per the AC requires the error list to carry the syntax diagnostic");
		response.Validation.Errors!.Should().Contain(
			e => e.Contains("JavaScript syntax error", System.StringComparison.OrdinalIgnoreCase),
			because: "the canonical syntax-gate prefix is what existing tooling and operator habits key on");
	}

	[Test]
	[Description("validate-page rejects a body whose custom converter uses the reserved `crt.*` namespace — proves the new AST lint pass surfaces through the pre-flight tool end-to-end. The regex layer treats `crt.*` as a valid vendor prefix, so this body is the canonical proof that the lint pass adds detection beyond regex under default validation.")]
	[AllureTag(ToolName)]
	[AllureName("validate-page rejects converter using reserved crt.* prefix")]
	[AllureDescription("Sends a body whose `converters` section registers a custom converter under the reserved `crt.*` namespace. The regex validators accept the body (their checks explicitly skip `crt.*` keys); verifying the response carries `converter-crt-prefix-reserved` proves the AST lint pass surfaces through the validate-page MCP wire.")]
	public async Task PageValidateTool_Should_Reject_Custom_Converter_With_Crt_Prefix() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));
		string crtPrefixConverterBody = ValidPageBody.Replace(
			"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"converters: /**SCHEMA_CONVERTERS*/{ \"crt.MyConverter\": function(v) { return v; } }/**SCHEMA_CONVERTERS*/");

		// Act
		PageValidateResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			crtPrefixConverterBody);

		// Assert
		response.Valid.Should().BeFalse(
			because: "the reserved `crt.*` namespace is for Creatio built-in converters; the AST lint pass must catch the custom-name usage end-to-end via the pre-flight tool");
		response.Validation.Errors.Should().NotBeNullOrEmpty(
			because: "the lint failure must contribute to the validation error list");
		response.Validation.Errors!.Should().Contain(
			e => e.Contains("converter-crt-prefix-reserved", System.StringComparison.OrdinalIgnoreCase),
			because: "the rule id must be visible in the wire response so the agent can map the failure back to the guidance");
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
		await using var context = Arrange(TimeSpan.FromMinutes(3));
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
		await using var context = Arrange(TimeSpan.FromMinutes(3));
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

}
