using System.Collections.Generic;
using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public class SchemaValidationServiceTests {

	private const string ValidListPageBody =
		"define(\"TestPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
		"function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/{ return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	private const string ValidFormPageBody =
		"define(\"TestPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
		"function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/{ return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfig: /**SCHEMA_VIEW_MODEL_CONFIG*/{}/**SCHEMA_VIEW_MODEL_CONFIG*/, " +
		"modelConfig: /**SCHEMA_MODEL_CONFIG*/{}/**SCHEMA_MODEL_CONFIG*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	[Test]
	[Description("Valid list page body passes marker integrity validation")]
	public void ValidateMarkerIntegrity_ValidListPageBody_ReturnsValid() {
		var result = SchemaValidationService.ValidateMarkerIntegrity(ValidListPageBody);
		result.IsValid.Should().BeTrue("because all required markers are present in list page format");
		result.Errors.Should().BeEmpty("because no markers are missing");
	}

	[Test]
	[Description("Valid form page body passes marker integrity validation")]
	public void ValidateMarkerIntegrity_ValidFormPageBody_ReturnsValid() {
		var result = SchemaValidationService.ValidateMarkerIntegrity(ValidFormPageBody);
		result.IsValid.Should().BeTrue("because all required markers are present in form page format");
		result.Errors.Should().BeEmpty("because no markers are missing");
	}

	[Test]
	[Description("Empty body fails validation")]
	public void ValidateMarkerIntegrity_EmptyBody_ReturnsInvalid() {
		var result = SchemaValidationService.ValidateMarkerIntegrity("");
		result.IsValid.Should().BeFalse("because an empty body cannot contain any markers");
	}

	[Test]
	[Description("Null body fails validation")]
	public void ValidateMarkerIntegrity_NullBody_ReturnsInvalid() {
		var result = SchemaValidationService.ValidateMarkerIntegrity(null);
		result.IsValid.Should().BeFalse("because a null body cannot contain any markers");
	}

	[Test]
	[Description("Body missing SCHEMA_DEPS marker fails validation")]
	public void ValidateMarkerIntegrity_MissingRequiredMarker_ReportsError() {
		string body = ValidListPageBody.Replace("/**SCHEMA_DEPS*/", "");
		var result = SchemaValidationService.ValidateMarkerIntegrity(body);
		result.IsValid.Should().BeFalse("because SCHEMA_DEPS marker is missing");
		result.Errors.Should().Contain("SCHEMA_DEPS", "because the missing marker should be reported");
	}

	[Test]
	[Description("Body with neither DIFF nor non-DIFF alternate markers fails validation")]
	public void ValidateMarkerIntegrity_MissingBothAlternateMarkers_ReportsError() {
		string body = ValidListPageBody
			.Replace("/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/", "")
			.Replace("/**SCHEMA_MODEL_CONFIG_DIFF*/", "");
		var result = SchemaValidationService.ValidateMarkerIntegrity(body);
		result.IsValid.Should().BeFalse("because neither DIFF nor non-DIFF alternate markers are present");
	}

	[Test]
	[Description("Body with only one marker occurrence (unpaired) fails validation")]
	public void ValidateMarkerIntegrity_SingleMarkerOccurrence_ReportsError() {
		string body = "define(\"Test\", /**SCHEMA_DEPS*/[]);";
		var result = SchemaValidationService.ValidateMarkerIntegrity(body);
		result.IsValid.Should().BeFalse("because markers must appear in pairs");
	}

	[Test]
	[Description("BuildMarkerPattern generates correct regex for matching /**MARKER*/ pairs")]
	public void BuildMarkerPattern_GeneratesCorrectRegex() {
		string pattern = SchemaValidationService.BuildMarkerPattern("SCHEMA_DEPS");
		var regex = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.Singleline);
		regex.IsMatch("/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/").Should()
			.BeTrue("because the pattern should match the standard marker pair format");
		regex.IsMatch("/* Start:SCHEMA_DEPS */").Should()
			.BeFalse("because the old format should not match");
	}

	[Test]
	[Description("Valid list page body passes JavaScript syntax validation")]
	public void ValidateJsSyntax_ValidListPageBody_ReturnsValid() {
		var result = SchemaValidationService.ValidateJsSyntax(ValidListPageBody);
		result.IsValid.Should().BeTrue("because all brackets are balanced in a valid list page body");
		result.Errors.Should().BeEmpty("because there are no syntax errors");
	}

	[Test]
	[Description("Valid form page body passes JavaScript syntax validation")]
	public void ValidateJsSyntax_ValidFormPageBody_ReturnsValid() {
		var result = SchemaValidationService.ValidateJsSyntax(ValidFormPageBody);
		result.IsValid.Should().BeTrue("because all brackets are balanced in a valid form page body");
		result.Errors.Should().BeEmpty("because there are no syntax errors");
	}

	[Test]
	[Description("Empty body fails JavaScript syntax validation")]
	public void ValidateJsSyntax_EmptyBody_ReturnsInvalid() {
		var result = SchemaValidationService.ValidateJsSyntax("");
		result.IsValid.Should().BeFalse("because an empty body is not valid JavaScript");
	}

	[Test]
	[Description("Body with unclosed curly brace fails validation")]
	public void ValidateJsSyntax_UnclosedBrace_ReturnsInvalid() {
		var result = SchemaValidationService.ValidateJsSyntax("function test() {");
		result.IsValid.Should().BeFalse("because the curly brace is never closed");
		result.Errors.Should().ContainMatch("*Unclosed*", "because the error should mention the unclosed bracket");
	}

	[Test]
	[Description("Body with mismatched brackets fails validation")]
	public void ValidateJsSyntax_MismatchedBrackets_ReturnsInvalid() {
		var result = SchemaValidationService.ValidateJsSyntax("function test() { return [1, 2}; }");
		result.IsValid.Should().BeFalse("because '[' is closed by '}' which is a mismatch");
		result.Errors.Should().ContainMatch("*Mismatched*", "because the error should describe the mismatch");
	}

	[Test]
	[Description("Body with unterminated string literal fails validation")]
	public void ValidateJsSyntax_UnterminatedString_ReturnsInvalid() {
		var result = SchemaValidationService.ValidateJsSyntax("var x = \"hello\nvar y = 1;");
		result.IsValid.Should().BeFalse("because the string literal is not properly closed before newline");
		result.Errors.Should().ContainMatch("*Unterminated string*", "because the error should mention the unterminated string");
	}

	[Test]
	[Description("Body with unterminated block comment fails validation")]
	public void ValidateJsSyntax_UnterminatedBlockComment_ReturnsInvalid() {
		var result = SchemaValidationService.ValidateJsSyntax("var x = 1; /* this is never closed");
		result.IsValid.Should().BeFalse("because the block comment is not properly closed");
		result.Errors.Should().ContainMatch("*Unterminated block comment*", "because the error should mention the comment");
	}

	[Test]
	[Description("Brackets inside strings are not counted")]
	public void ValidateJsSyntax_BracketsInsideStrings_AreIgnored() {
		var result = SchemaValidationService.ValidateJsSyntax("var x = '{[('; var y = 1;");
		result.IsValid.Should().BeTrue("because brackets inside string literals should be ignored");
	}

	[Test]
	[Description("Brackets inside block comments are not counted")]
	public void ValidateJsSyntax_BracketsInsideComments_AreIgnored() {
		var result = SchemaValidationService.ValidateJsSyntax("var x = 1; /* {[( */ var y = 2;");
		result.IsValid.Should().BeTrue("because brackets inside comments should be ignored");
	}

	[Test]
	[Description("Extra closing bracket with no opener fails validation")]
	public void ValidateJsSyntax_ExtraClosingBracket_ReturnsInvalid() {
		var result = SchemaValidationService.ValidateJsSyntax("var x = 1; }");
		result.IsValid.Should().BeFalse("because there is a closing brace with no matching opening brace");
		result.Errors.Should().ContainMatch("*Unexpected closing*", "because the error should mention the unexpected bracket");
	}

	[Test]
	[Description("Valid list page body passes marker content validation")]
	public void ValidateMarkerContent_ValidListPageBody_ReturnsValid() {
		var result = SchemaValidationService.ValidateMarkerContent(ValidListPageBody);
		result.IsValid.Should().BeTrue("because all marker sections contain valid structured content");
		result.Errors.Should().BeEmpty("because no marker section contains structural or syntax errors");
	}

	[Test]
	[Description("Valid form page body passes marker content validation")]
	public void ValidateMarkerContent_ValidFormPageBody_ReturnsValid() {
		var result = SchemaValidationService.ValidateMarkerContent(ValidFormPageBody);
		result.IsValid.Should().BeTrue("because all marker sections contain valid structured content");
		result.Errors.Should().BeEmpty("because no marker section contains structural or syntax errors");
	}

	[Test]
	[Description("Body with double comma inside viewConfigDiff fails content validation")]
	public void ValidateMarkerContent_DoubleComma_ReturnsInvalid() {
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/",
			"/**SCHEMA_VIEW_CONFIG_DIFF*/[{\"a\":1},,{\"b\":2}]/**SCHEMA_VIEW_CONFIG_DIFF*/");
		var result = SchemaValidationService.ValidateMarkerContent(body);
		result.IsValid.Should().BeFalse("because double comma is invalid JSON");
		result.Errors.Should().ContainMatch("*SCHEMA_VIEW_CONFIG_DIFF*",
			"because the error should identify the broken marker section");
	}

	[Test]
	[Description("Body with trailing comma passes content validation (Hjson tolerates trailing commas)")]
	public void ValidateMarkerContent_TrailingComma_ReturnsValid() {
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/",
			"/**SCHEMA_VIEW_CONFIG_DIFF*/[{\"a\":1},{\"b\":2},]/**SCHEMA_VIEW_CONFIG_DIFF*/");
		var result = SchemaValidationService.ValidateMarkerContent(body);
		result.IsValid.Should().BeTrue("because Hjson tolerates trailing commas");
		result.Errors.Should().BeEmpty("because Hjson parser does not treat trailing commas as errors");
	}

	[Test]
	[Description("Body with JavaScript handler functions passes content validation")]
	public void ValidateMarkerContent_JavaScriptHandlers_ReturnsValid() {
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
			"/**SCHEMA_HANDLERS*/[{ request: \"crt.HandleViewModelInitRequest\", handler: async (request, next) => { await next?.handle(request); } }]/**SCHEMA_HANDLERS*/");
		var result = SchemaValidationService.ValidateMarkerContent(body);
		result.IsValid.Should().BeTrue("because handlers can contain JavaScript and should not be parsed as JSON content");
		result.Errors.Should().BeEmpty("because the JSON-backed markers remain valid");
	}

	[Test]
	[Description("Body with JavaScript converter functions passes content validation")]
	public void ValidateMarkerContent_JavaScriptConverters_ReturnsValid() {
		string body = ValidFormPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"/**SCHEMA_CONVERTERS*/{ \"usr.ToUpperCase\": function(value) { return value?.toUpperCase() ?? \"\"; } }/**SCHEMA_CONVERTERS*/");
		var result = SchemaValidationService.ValidateMarkerContent(body);

		result.IsValid.Should().BeTrue("because converters are authored as JavaScript object sections and may contain functions");
		result.Errors.Should().BeEmpty("because function-based converter sections should not be rejected as non-JSON");
	}

	[Test]
	[Description("Body with JavaScript validator functions passes content validation")]
	public void ValidateMarkerContent_JavaScriptValidators_ReturnsValid() {
		string body = ValidFormPageBody.Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"/**SCHEMA_VALIDATORS*/{ \"usr.ValidateFieldValue\": { \"validator\": function(config) { return function(control) { return control.value !== config.invalidName ? null : { \"usr.ValidateFieldValue\": { message: config.message } }; }; }, \"params\": [{ \"name\": \"invalidName\" }, { \"name\": \"message\" }], \"async\": false } }/**SCHEMA_VALIDATORS*/");
		var result = SchemaValidationService.ValidateMarkerContent(body);

		result.IsValid.Should().BeTrue("because validators are authored as JavaScript object sections and may contain functions");
		result.Errors.Should().BeEmpty("because function-based validator sections should not be rejected as non-JSON");
	}

	[Test]
	[Description("Body with non-object converters section fails content validation")]
	public void ValidateMarkerContent_NonObjectConverters_ReturnsInvalid() {
		string body = ValidFormPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"/**SCHEMA_CONVERTERS*/[\"usr.ToUpperCase\"]/**SCHEMA_CONVERTERS*/");
		var result = SchemaValidationService.ValidateMarkerContent(body);
		result.IsValid.Should().BeFalse("because converters must remain an object-literal section");
		result.Errors.Should().ContainMatch("*SCHEMA_CONVERTERS*",
			because: "the error should identify the broken converter marker section");
	}

	[Test]
	[Description("Body with non-object validators section fails content validation")]
	public void ValidateMarkerContent_NonObjectValidators_ReturnsInvalid() {
		string body = ValidFormPageBody.Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"/**SCHEMA_VALIDATORS*/[\"usr.SomeValidator\"]/**SCHEMA_VALIDATORS*/");
		var result = SchemaValidationService.ValidateMarkerContent(body);
		result.IsValid.Should().BeFalse("because validators must remain an object-literal section");
		result.Errors.Should().ContainMatch("*SCHEMA_VALIDATORS*",
			because: "the error should identify the broken validator marker section");
	}

	[Test]
	[Description("Converter section with invalid JavaScript syntax fails content validation")]
	public void ValidateMarkerContent_InvalidJsSyntaxInConverters_ReturnsInvalid() {
		string body = ValidFormPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"/**SCHEMA_CONVERTERS*/{ \"usr.Bad\": function(value { return value; } }/**SCHEMA_CONVERTERS*/");
		var result = SchemaValidationService.ValidateMarkerContent(body);
		result.IsValid.Should().BeFalse("because a syntax error inside a JavaScript object section must be caught");
		result.Errors.Should().ContainMatch("*SCHEMA_CONVERTERS*",
			because: "the error should identify which marker section contains the syntax problem");
	}

	[Test]
	[Description("Validator section with invalid JavaScript syntax fails content validation")]
	public void ValidateMarkerContent_InvalidJsSyntaxInValidators_ReturnsInvalid() {
		string body = ValidFormPageBody.Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"/**SCHEMA_VALIDATORS*/{ \"usr.Bad\": function(config { return null; } }/**SCHEMA_VALIDATORS*/");
		var result = SchemaValidationService.ValidateMarkerContent(body);
		result.IsValid.Should().BeFalse("because a syntax error inside a JavaScript object section must be caught");
		result.Errors.Should().ContainMatch("*SCHEMA_VALIDATORS*",
			because: "the error should identify which marker section contains the syntax problem");
	}

	[Test]
	[Description("Invalid converter and validator JavaScript sections are both reported during marker content validation")]
	public void ValidateMarkerContent_InvalidJsSyntaxInConvertersAndValidators_ReturnsBothErrors() {
		// Arrange
		string bodyWithInvalidConverters = ValidFormPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"/**SCHEMA_CONVERTERS*/{ \"usr.Bad\": function(value { return value; } }/**SCHEMA_CONVERTERS*/");
		string body = bodyWithInvalidConverters.Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"/**SCHEMA_VALIDATORS*/{ \"usr.Bad\": function(config { return null; } }/**SCHEMA_VALIDATORS*/");

		// Act
		var result = SchemaValidationService.ValidateMarkerContent(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "syntax errors in peer JavaScript object markers must make the body invalid");
		result.Errors.Should().ContainMatch("*SCHEMA_CONVERTERS*",
			because: "the converters syntax error should still be reported");
		result.Errors.Should().ContainMatch("*SCHEMA_VALIDATORS*",
			because: "the validators syntax error should also be reported instead of being skipped");
	}

	[Test]
	[Description("Empty body fails marker content validation")]
	public void ValidateMarkerContent_EmptyBody_ReturnsInvalid() {
		var result = SchemaValidationService.ValidateMarkerContent("");
		result.IsValid.Should().BeFalse("because an empty body is not valid");
	}

	[Test]
	[Description("ListPage with matching column bindings passes validation")]
	public void ValidateColumnBindings_MatchingBindings_ReturnsValid() {
		string body =
			"define(\"Test\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
			"function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/{ return { " +
			"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{\"name\":\"DataTable\",\"values\":{\"columns\":[" +
			"{\"code\":\"PDS_Name\"},{\"code\":\"PDS_UsrStatus\"}]}}]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[{\"operation\":\"merge\",\"values\":{" +
			"\"PDS_Name\":{\"modelConfig\":{\"path\":\"PDS.Name\"}}," +
			"\"PDS_UsrStatus\":{\"modelConfig\":{\"path\":\"PDS.UsrStatus\"}}" +
			"}}]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
			"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
			"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
			"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
			"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";
		var result = SchemaValidationService.ValidateColumnBindings(body);
		result.IsValid.Should().BeTrue("because all DataTable columns have matching bindings");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("ListPage with missing column bindings reports errors")]
	public void ValidateColumnBindings_MissingBindings_ReturnsInvalid() {
		string body =
			"define(\"Test\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
			"function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/{ return { " +
			"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{\"name\":\"DataTable\",\"values\":{\"columns\":[" +
			"{\"code\":\"PDS_Name\"},{\"code\":\"PDS_UsrStatus\"},{\"code\":\"PDS_UsrDueDate\"}]}}]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[{\"operation\":\"merge\",\"values\":{" +
			"\"PDS_Name\":{\"modelConfig\":{\"path\":\"PDS.Name\"}}" +
			"}}]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
			"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
			"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
			"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
			"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";
		var result = SchemaValidationService.ValidateColumnBindings(body);
		result.IsValid.Should().BeFalse("because PDS_UsrStatus and PDS_UsrDueDate have no matching bindings");
		result.Errors.Should().HaveCount(2);
		result.Errors.Should().ContainMatch("*PDS_UsrStatus*");
		result.Errors.Should().ContainMatch("*PDS_UsrDueDate*");
	}

	[Test]
	[Description("Page without DataTable passes column binding validation")]
	public void ValidateColumnBindings_NoDataTable_ReturnsValid() {
		var result = SchemaValidationService.ValidateColumnBindings(ValidFormPageBody);
		result.IsValid.Should().BeTrue("because FormPage without DataTable should pass");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Standard field proxy bindings to direct PDS paths are rejected")]
	public void ValidateStandardFieldBindings_ProxyBindingToPdsPath_ReturnsInvalid() {
		string body = BuildDiffBackedPageBody(
			"[{\"operation\":\"insert\",\"name\":\"UsrStatus\",\"values\":{\"type\":\"crt.ComboBox\",\"label\":\"$Resources.Strings.PDS_UsrStatus\",\"control\":\"$UsrStatus\"}}]",
			"[{\"operation\":\"merge\",\"values\":{\"UsrStatus\":{\"modelConfig\":{\"path\":\"PDS.UsrStatus\"}}}}]");

		var result = SchemaValidationService.ValidateStandardFieldBindings(body);

		result.IsValid.Should().BeFalse("because data-bound fields must bind directly to datasource-backed attributes");
		result.Errors.Should().ContainSingle(error => error.Contains("$UsrStatus") && error.Contains("$PDS_UsrStatus"),
			"because the validation should explain the rejected proxy binding and the expected datasource binding");
	}

	[Test]
	[Description("Standard field Usr label shortcuts without explicit resources are rejected")]
	public void ValidateStandardFieldBindings_UsrLabelShortcutWithoutResources_ReturnsInvalid() {
		string body = BuildDiffBackedPageBody(
			"[{\"operation\":\"insert\",\"name\":\"UsrStatus\",\"values\":{\"type\":\"crt.ComboBox\",\"label\":\"#ResourceString(UsrStatus_label)#\",\"control\":\"$PDS_UsrStatus\"}}]",
			"[]");

		var result = SchemaValidationService.ValidateStandardFieldBindings(body);

		result.IsValid.Should().BeFalse("because data-bound field captions should not rely on implicit Usr label resources");
		result.Errors.Should().ContainSingle(error => error.Contains("UsrStatus_label"),
			"because the missing explicit resource entry should be called out");
	}

	[Test]
	[Description("Standard field direct PDS binding with datasource caption passes semantic validation")]
	public void ValidateStandardFieldBindings_DirectPdsBindingWithDatasourceCaption_ReturnsValid() {
		string body = BuildDiffBackedPageBody(
			"[{\"operation\":\"insert\",\"name\":\"UsrStatus\",\"values\":{\"type\":\"crt.ComboBox\",\"label\":\"$Resources.Strings.PDS_UsrStatus\",\"control\":\"$PDS_UsrStatus\"}}]",
			"[]");

		var result = SchemaValidationService.ValidateStandardFieldBindings(body);

		result.IsValid.Should().BeTrue("because the field uses direct datasource binding and datasource captioning");
		result.Errors.Should().BeEmpty();
		result.Warnings.Should().BeEmpty();
	}

	[Test]
	[Description("Explicit custom resources on standard field shortcuts surface warnings instead of hard failures")]
	public void ValidateStandardFieldBindings_UsrLabelShortcutWithExplicitResources_ReturnsWarning() {
		string body = BuildDiffBackedPageBody(
			"[{\"operation\":\"insert\",\"name\":\"UsrStatus\",\"values\":{\"type\":\"crt.ComboBox\",\"label\":\"#ResourceString(UsrStatus_caption)#\",\"control\":\"$PDS_UsrStatus\"}}]",
			"[]");

		var result = SchemaValidationService.ValidateStandardFieldBindings(
			body,
			new Dictionary<string, string> { ["UsrStatus_caption"] = "Status" });

		result.IsValid.Should().BeTrue("because the explicit resource makes the pattern suspicious but not conclusively broken");
		result.Errors.Should().BeEmpty();
		result.Warnings.Should().ContainSingle(warning => warning.Contains("UsrStatus_caption"),
			"because the validator should steer callers toward datasource captions");
	}

	[Test]
	[Description("Custom non-field UI elements may use explicit Usr caption resources")]
	public void ValidateStandardFieldBindings_CustomStandaloneCaptionWithExplicitResources_ReturnsValid() {
		string body = BuildDiffBackedPageBody(
			"[{\"operation\":\"insert\",\"name\":\"UsrStandaloneLabel\",\"values\":{\"type\":\"crt.Label\",\"caption\":\"#ResourceString(UsrStatus_caption)#\"}}]",
			"[]");

		var result = SchemaValidationService.ValidateStandardFieldBindings(
			body,
			new Dictionary<string, string> { ["UsrStatus_caption"] = "Status" });

		result.IsValid.Should().BeTrue("because non-field standalone UI captions are outside the standard field guardrail");
		result.Errors.Should().BeEmpty();
		result.Warnings.Should().BeEmpty();
	}

	[Test]
	[Description("Label referencing $Resources.Strings.KEY warns when KEY is absent from explicit resources")]
	public void ValidateStandardFieldBindings_LabelResourceKeyMissingFromExplicitResources_ReturnsWarning() {
		string body = BuildDiffBackedPageBody(
			"[{\"operation\":\"insert\",\"name\":\"PDS_UsrName\",\"values\":{\"type\":\"crt.Input\",\"label\":\"$Resources.Strings.PDS_UsrName\",\"control\":\"$PDS_UsrName\"}}]",
			"[{\"operation\":\"merge\",\"values\":{\"PDS_UsrName\":{\"modelConfig\":{\"path\":\"PDS.UsrName\"}}}}]");

		var result = SchemaValidationService.ValidateStandardFieldBindings(
			body,
			new Dictionary<string, string> { ["PDS_UsrRequesterName"] = "Requester Name" });

		result.IsValid.Should().BeTrue("because a missing label resource is a recoverable issue, not a hard failure");
		result.Errors.Should().BeEmpty();
		result.Warnings.Should().ContainSingle(w => w.Contains("PDS_UsrName") && w.Contains("render blank"),
			"because the validator should surface that the label key is absent from the provided resources");
	}

	[Test]
	[Description("Label referencing $Resources.Strings.KEY passes when KEY is present in explicit resources")]
	public void ValidateStandardFieldBindings_LabelResourceKeyPresentInExplicitResources_ReturnsValid() {
		string body = BuildDiffBackedPageBody(
			"[{\"operation\":\"insert\",\"name\":\"PDS_UsrName\",\"values\":{\"type\":\"crt.Input\",\"label\":\"$Resources.Strings.PDS_UsrName\",\"control\":\"$PDS_UsrName\"}}]",
			"[{\"operation\":\"merge\",\"values\":{\"PDS_UsrName\":{\"modelConfig\":{\"path\":\"PDS.UsrName\"}}}}]");

		var result = SchemaValidationService.ValidateStandardFieldBindings(
			body,
			new Dictionary<string, string> { ["PDS_UsrName"] = "Request Subject" });

		result.IsValid.Should().BeTrue("because the label resource key is explicitly registered");
		result.Errors.Should().BeEmpty();
		result.Warnings.Should().BeEmpty();
	}

	[Test]
	[Description("Label referencing $Resources.Strings.KEY is not warned when no explicit resources are provided")]
	public void ValidateStandardFieldBindings_LabelResourceKeyWithoutExplicitResources_ReturnsValid() {
		string body = BuildDiffBackedPageBody(
			"[{\"operation\":\"insert\",\"name\":\"PDS_UsrName\",\"values\":{\"type\":\"crt.Input\",\"label\":\"$Resources.Strings.PDS_UsrName\",\"control\":\"$PDS_UsrName\"}}]",
			"[{\"operation\":\"merge\",\"values\":{\"PDS_UsrName\":{\"modelConfig\":{\"path\":\"PDS.UsrName\"}}}}]");

		var result = SchemaValidationService.ValidateStandardFieldBindings(body);

		result.IsValid.Should().BeTrue("because without explicit resources the validator cannot determine if the key is registered on the site");
		result.Errors.Should().BeEmpty();
		result.Warnings.Should().BeEmpty();
	}

	[Test]
	[Description("Standard field with validators uses view-model attribute binding $AttrName — must NOT be flagged as proxy-binding error")]
	public void ValidateStandardFieldBindings_AttributeWithValidators_ViewModelBindingIsAllowed() {
		// Arrange — UsrName has a validator in viewModelConfig; control binds to $UsrName (view-model attribute)
		string viewConfigDiff = "[{\"operation\":\"insert\",\"name\":\"UsrName\",\"values\":{\"type\":\"crt.Input\",\"label\":\"$Resources.Strings.UsrName\",\"control\":\"$UsrName\"}}]";
		string viewModelConfig = "{\"attributes\":{\"UsrName\":{\"modelConfig\":{\"path\":\"PDS.UsrName\"},\"validators\":{\"UpperCase\":{\"type\":\"usr.UpperCase\",\"params\":{\"message\":\"$Resources.Strings.UsrUpperCaseValidator_Message\"}}}}}}";
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig);

		// Act
		var result = SchemaValidationService.ValidateStandardFieldBindings(body);

		// Assert
		result.IsValid.Should().BeTrue("because when an attribute carries validators the correct control binding is $AttrName, not $PDS_AttrName");
		result.Errors.Should().NotContain(error => error.Contains("$UsrName"),
			"because the view-model attribute binding is required for validators to fire and must not be rejected as a proxy error");
	}

	[Test]
	[Description("Standard field without validators still requires $PDS_AttrName proxy binding to the datasource")]
	public void ValidateStandardFieldBindings_AttributeWithoutValidators_ViewModelBindingIsRejected() {
		// Arrange — UsrStatus has no validators; control binds to $UsrStatus (should use $PDS_UsrStatus)
		string viewConfigDiff = "[{\"operation\":\"insert\",\"name\":\"UsrStatus\",\"values\":{\"type\":\"crt.ComboBox\",\"label\":\"$Resources.Strings.PDS_UsrStatus\",\"control\":\"$UsrStatus\"}}]";
		string viewModelConfig = "{\"attributes\":{\"UsrStatus\":{\"modelConfig\":{\"path\":\"PDS.UsrStatus\"}}}}";
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig);

		// Act
		var result = SchemaValidationService.ValidateStandardFieldBindings(body);

		// Assert
		result.IsValid.Should().BeFalse("because attributes without validators must bind to the datasource proxy $PDS_UsrStatus");
		result.Errors.Should().ContainSingle(error => error.Contains("$PDS_UsrStatus"),
			"because the validation should reject the view-model binding and suggest the correct PDS proxy binding");
	}

	private static string BuildDiffBackedPageBody(string viewConfigDiff, string viewModelConfigDiff) {
		return
			"define(\"TestPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
			"function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/{ return { " +
			$"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/{viewConfigDiff}/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			$"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/{viewModelConfigDiff}/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
			"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
			"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
			"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
			"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";
	}

	private static string BuildStaticViewModelConfigPageBody(string viewConfigDiff, string viewModelConfig) {
		return
			"define(\"TestPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
			"function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/{ return { " +
			$"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/{viewConfigDiff}/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			$"viewModelConfig: /**SCHEMA_VIEW_MODEL_CONFIG*/{viewModelConfig}/**SCHEMA_VIEW_MODEL_CONFIG*/, " +
			"modelConfig: /**SCHEMA_MODEL_CONFIG*/{}/**SCHEMA_MODEL_CONFIG*/, " +
			"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
			"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
			"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";
	}

	[Test]
	[Description("Static viewModelConfig with $PDS_AttrName control where attribute has validators is rejected")]
	public void ValidateValidatorControlBindings_StaticViewModelConfig_PdsBinding_WithValidators_ReturnsInvalid() {
		// Arrange — exact shape of the bug: validator on 'UsrName' but control = "$PDS_UsrName"
		string viewConfigDiff = "[{\"operation\":\"insert\",\"name\":\"UsrName\",\"values\":{" +
		                        "\"type\":\"crt.Input\",\"label\":\"$Resources.Strings.UsrName\"," +
		                        "\"control\":\"$PDS_UsrName\"}}]";
		string viewModelConfig = "{\"attributes\":{\"UsrName\":{" +
		                         "\"modelConfig\":{\"path\":\"PDS.UsrName\"}," +
		                         "\"validators\":{\"UpperCase\":{\"type\":\"usr.UpperCaseValidator\"," +
		                         "\"params\":{\"message\":\"Must be uppercase\"}}}}}}";
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig);

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateValidatorControlBindings(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "a control bound to '$PDS_UsrName' will never trigger the validator registered on attribute 'UsrName'");
		result.Errors.Should().ContainSingle(
			error => error.Contains("UsrName") && error.Contains("$PDS_UsrName") && error.Contains("$UsrName"),
			because: "the error should identify the control, the wrong binding, and the correct alternative");
	}

	[Test]
	[Description("Static viewModelConfig with $AttrName control where attribute has validators passes validation")]
	public void ValidateValidatorControlBindings_StaticViewModelConfig_AttrBinding_WithValidators_ReturnsValid() {
		// Arrange — correct shape: validator on 'UsrName' and control = "$UsrName"
		string viewConfigDiff = "[{\"operation\":\"insert\",\"name\":\"UsrName\",\"values\":{" +
		                        "\"type\":\"crt.Input\",\"label\":\"$Resources.Strings.UsrName\"," +
		                        "\"control\":\"$UsrName\"}}]";
		string viewModelConfig = "{\"attributes\":{\"UsrName\":{" +
		                         "\"modelConfig\":{\"path\":\"PDS.UsrName\"}," +
		                         "\"validators\":{\"UpperCase\":{\"type\":\"usr.UpperCaseValidator\"," +
		                         "\"params\":{\"message\":\"Must be uppercase\"}}}}}}";
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig);

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateValidatorControlBindings(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "control '$UsrName' correctly binds to the view-model attribute that carries the validators");
		result.Errors.Should().BeEmpty(
			because: "no validator binding violations are present");
	}

	[Test]
	[Description("viewModelConfigDiff with $PDS_AttrName control where attribute has validators is rejected")]
	public void ValidateValidatorControlBindings_DiffViewModelConfig_PdsBinding_WithValidators_ReturnsInvalid() {
		// Arrange
		string viewConfigDiff = "[{\"operation\":\"insert\",\"name\":\"UsrEmail\",\"values\":{" +
		                        "\"type\":\"crt.EmailInput\",\"control\":\"$PDS_UsrEmail\"}}]";
		string viewModelConfigDiff = "[{\"operation\":\"merge\",\"path\":[\"attributes\"]," +
		                             "\"values\":{\"UsrEmail\":{\"modelConfig\":{\"path\":\"PDS.UsrEmail\"}," +
		                             "\"validators\":{\"EmailValidator\":{\"type\":\"usr.EmailValidator\"}}}}}]";
		string body = BuildDiffBackedPageBody(viewConfigDiff, viewModelConfigDiff);

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateValidatorControlBindings(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "a control bound to '$PDS_UsrEmail' will never trigger the validator on attribute 'UsrEmail' in viewModelConfigDiff");
		result.Errors.Should().ContainSingle(
			error => error.Contains("UsrEmail") && error.Contains("$PDS_UsrEmail"),
			because: "the error should identify the control with the wrong PDS binding");
	}

	[Test]
	[Description("viewModelConfigDiff with $AttrName control where attribute has validators passes validation")]
	public void ValidateValidatorControlBindings_DiffViewModelConfig_AttrBinding_WithValidators_ReturnsValid() {
		// Arrange
		string viewConfigDiff = "[{\"operation\":\"insert\",\"name\":\"UsrEmail\",\"values\":{" +
		                        "\"type\":\"crt.EmailInput\",\"control\":\"$UsrEmail\"}}]";
		string viewModelConfigDiff = "[{\"operation\":\"merge\",\"path\":[\"attributes\"]," +
		                             "\"values\":{\"UsrEmail\":{\"modelConfig\":{\"path\":\"PDS.UsrEmail\"}," +
		                             "\"validators\":{\"EmailValidator\":{\"type\":\"usr.EmailValidator\"}}}}}]";
		string body = BuildDiffBackedPageBody(viewConfigDiff, viewModelConfigDiff);

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateValidatorControlBindings(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "control '$UsrEmail' correctly binds to the view-model attribute carrying validators");
		result.Errors.Should().BeEmpty(
			because: "no validator binding violations are present");
	}

	[Test]
	[Description("$PDS_AttrName binding on attribute without validators is allowed")]
	public void ValidateValidatorControlBindings_PdsBinding_AttributeHasNoValidators_ReturnsValid() {
		// Arrange
		string viewConfigDiff = "[{\"operation\":\"insert\",\"name\":\"UsrName\",\"values\":{" +
		                        "\"type\":\"crt.Input\",\"control\":\"$PDS_UsrName\"}}]";
		string viewModelConfig = "{\"attributes\":{\"UsrName\":{\"modelConfig\":{\"path\":\"PDS.UsrName\"}}}}";
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig);

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateValidatorControlBindings(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "'$PDS_UsrName' is acceptable when the attribute has no validators registered");
		result.Errors.Should().BeEmpty(
			because: "no validator binding constraints apply to attributes without validators");
	}

	[Test]
	[Description("Attribute with empty validators object is not treated as having validators")]
	public void ValidateValidatorControlBindings_PdsBinding_AttributeHasEmptyValidators_ReturnsValid() {
		// Arrange
		string viewConfigDiff = "[{\"operation\":\"insert\",\"name\":\"UsrName\",\"values\":{" +
		                        "\"type\":\"crt.Input\",\"control\":\"$PDS_UsrName\"}}]";
		string viewModelConfig = "{\"attributes\":{\"UsrName\":{\"modelConfig\":{\"path\":\"PDS.UsrName\"}," +
		                         "\"validators\":{}}}}";
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig);

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateValidatorControlBindings(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "an empty validators object means no validators are registered, so the PDS binding is acceptable");
		result.Errors.Should().BeEmpty(
			because: "empty validators do not impose control binding constraints");
	}

	[Test]
	[Description("Empty body returns valid result without errors")]
	public void ValidateValidatorControlBindings_EmptyBody_ReturnsValid() {
		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateValidatorControlBindings("");

		// Assert
		result.IsValid.Should().BeTrue(
			because: "an empty body has nothing to validate and should not throw");
		result.Errors.Should().BeEmpty(
			because: "empty input produces no errors");
	}

	[Test]
	[Description("$Resources.Strings. binding in validator params is rejected — use #ResourceString()# instead")]
	public void ValidateValidatorParamResourceBindings_ReactiveBinding_InValidatorParam_ReturnsInvalid() {
		// Arrange
		string viewModelConfig = "{\"attributes\":{\"UsrName\":{\"modelConfig\":{\"path\":\"PDS.UsrName\"}," +
		                         "\"validators\":{\"AllUpperCase\":{\"type\":\"usr.AllUpperCase\"," +
		                         "\"params\":{\"message\":\"$Resources.Strings.UsrUpperCaseValidator_Message\"}}}}}}";
		string body = BuildStaticViewModelConfigPageBody("[]", viewModelConfig);

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateValidatorParamResourceBindings(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "$Resources.Strings. is a reactive binding syntax not evaluated in validator params");
		result.Errors.Should().ContainSingle(error =>
				error.Contains("$Resources.Strings.UsrUpperCaseValidator_Message") &&
				error.Contains("#ResourceString(UsrUpperCaseValidator_Message)#"),
			because: "the error should identify the wrong value and suggest the correct #ResourceString()# macro");
	}

	[Test]
	[Description("#ResourceString()# binding in validator params is accepted")]
	public void ValidateValidatorParamResourceBindings_ResourceStringMacro_InValidatorParam_ReturnsValid() {
		// Arrange
		string viewModelConfig = "{\"attributes\":{\"UsrName\":{\"modelConfig\":{\"path\":\"PDS.UsrName\"}," +
		                         "\"validators\":{\"AllUpperCase\":{\"type\":\"usr.AllUpperCase\"," +
		                         "\"params\":{\"message\":\"#ResourceString(UsrUpperCaseValidator_Message)#\"}}}}}}";
		string body = BuildStaticViewModelConfigPageBody("[]", viewModelConfig);

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateValidatorParamResourceBindings(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "#ResourceString()# is the correct server-side substitution format for validator params");
		result.Errors.Should().BeEmpty(
			because: "the correct macro format produces no errors");
	}

	[Test]
	[Description("$Resources.Strings. in viewModelConfigDiff validator params is also rejected")]
	public void ValidateValidatorParamResourceBindings_ReactiveBinding_InDiffFormat_ReturnsInvalid() {
		// Arrange — diff-backed format (viewModelConfigDiff)
		string viewModelConfigDiff = "[{\"operation\":\"merge\",\"path\":[\"attributes\"],\"values\":{" +
		                             "\"UsrName\":{\"modelConfig\":{\"path\":\"PDS.UsrName\"}," +
		                             "\"validators\":{\"Upper\":{\"type\":\"usr.Upper\"," +
		                             "\"params\":{\"message\":\"$Resources.Strings.UsrMsg\"}}}}}}]";
		string body = BuildDiffBackedPageBody("[]", viewModelConfigDiff);

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateValidatorParamResourceBindings(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "the reactive binding restriction applies in both viewModelConfig and viewModelConfigDiff formats");
		result.Errors.Should().ContainSingle(error => error.Contains("$Resources.Strings.UsrMsg"),
			because: "the error should identify the reactive binding in the diff-format validator params");
	}

	[Test]
	[Description("viewModelConfigDiff operations outside the attributes path are ignored by validator param validation")]
	public void ValidateValidatorParamResourceBindings_NonAttributeDiffOperationWithValidatorsLikeShape_ReturnsValid() {
		// Arrange
		string viewModelConfigDiff = "[{\"operation\":\"merge\",\"path\":[\"handlers\"],\"values\":{" +
		                             "\"UsrPseudoHandler\":{\"validators\":{\"Upper\":{\"type\":\"usr.Upper\"," +
		                             "\"params\":{\"message\":\"$Resources.Strings.UsrMsg\"}}}}}}]";
		string body = BuildDiffBackedPageBody("[]", viewModelConfigDiff);

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateValidatorParamResourceBindings(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "only viewModelConfigDiff operations targeting attributes should participate in validator binding scans");
		result.Errors.Should().BeEmpty(
			because: "a handlers diff operation must not be misread as an attribute container just because its values object contains a validators-like shape");
	}

	[Test]
	[Description("Nested viewModelConfigDiff paths that merely contain attributes are ignored by validator param validation")]
	public void ValidateValidatorParamResourceBindings_NestedAttributesPathOperation_ReturnsValid() {
		// Arrange
		string viewModelConfigDiff = "[{\"operation\":\"merge\",\"path\":[\"handlers\",\"attributes\"],\"values\":{" +
		                             "\"UsrPseudoHandler\":{\"validators\":{\"Upper\":{\"type\":\"usr.Upper\"," +
		                             "\"params\":{\"message\":\"$Resources.Strings.UsrMsg\"}}}}}}]";
		string body = BuildDiffBackedPageBody("[]", viewModelConfigDiff);

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateValidatorParamResourceBindings(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "only operations whose top-level target is attributes should participate in validator binding scans");
		result.Errors.Should().BeEmpty(
			because: "a nested path that merely contains attributes must not be treated as an attribute container");
	}

	[Test]
	[Description("Custom validators with any logic are allowed — guidance steers AI toward standard validators; runtime validation does not second-guess custom implementations.")]
	public void ValidateStandardValidatorUsage_CustomMaxLengthStyleValidator_ReturnsValid() {
		// Arrange
		string viewConfigDiff = "[{\"operation\":\"insert\",\"name\":\"UsrName\",\"values\":{\"type\":\"crt.Input\",\"control\":\"$UsrName\"}}]";
		string viewModelConfig = "{\"attributes\":{\"UsrName\":{\"modelConfig\":{\"path\":\"PDS.UsrName\"},\"validators\":{\"NameMaxLength\":{\"type\":\"usr.NameMaxLength\",\"params\":{\"message\":\"#ResourceString(UsrNameMaxLength_Message)#\"}}}}}}";
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig).Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"/**SCHEMA_VALIDATORS*/{\"usr.NameMaxLength\":{\"validator\":function(config){return function(control){if (control.value && control.value.length >= 5) { return {\"usr.NameMaxLength\": { message: config.message }}; } return null;};},\"params\":[{\"name\":\"message\"}],\"async\":false}}/**SCHEMA_VALIDATORS*/");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateStandardValidatorUsage(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "runtime validation no longer second-guesses whether a custom validator duplicates a built-in; guidance steers the AI at authoring time");
		result.Errors.Should().BeEmpty(
			because: "custom validators with .length checks are valid — only structural schema errors are rejected at runtime");
	}

	[Test]
	[Description("Non-standard custom validators remain allowed when no built-in validator obviously matches the rule.")]
	public void ValidateStandardValidatorUsage_CustomDomainValidator_ReturnsValid() {
		// Arrange
		string viewConfigDiff = "[{\"operation\":\"insert\",\"name\":\"UsrName\",\"values\":{\"type\":\"crt.Input\",\"control\":\"$UsrName\"}}]";
		string viewModelConfig = "{\"attributes\":{\"UsrName\":{\"modelConfig\":{\"path\":\"PDS.UsrName\"},\"validators\":{\"UpperCase\":{\"type\":\"usr.UpperCaseValidator\",\"params\":{\"message\":\"#ResourceString(UsrUpperCaseValidator_Message)#\"}}}}}}";
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig).Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"/**SCHEMA_VALIDATORS*/{\"usr.UpperCaseValidator\":{\"validator\":function(config){return function(control){const value = control.value; if (!value || value === value.toUpperCase()) { return null; } return {\"usr.UpperCaseValidator\": { message: config.message }};};},\"params\":[{\"name\":\"message\"}],\"async\":false}}/**SCHEMA_VALIDATORS*/");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateStandardValidatorUsage(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "domain-specific custom validators such as uppercase checks do not have an obvious built-in replacement");
		result.Errors.Should().BeEmpty(
			because: "no built-in validator misuse should be reported for non-standard validation logic");
	}

	[Test]
	[Description("Built-in crt.MaxLength validator must use maxLength instead of max in params.")]
	public void ValidateStandardValidatorUsage_BuiltInMaxLengthWithWrongParamName_ReturnsInvalid() {
		// Arrange
		string viewConfigDiff = "[{\"operation\":\"insert\",\"name\":\"UsrName\",\"values\":{\"type\":\"crt.Input\",\"control\":\"$UsrName\"}}]";
		string viewModelConfig = "{\"attributes\":{\"UsrName\":{\"modelConfig\":{\"path\":\"PDS.UsrName\"},\"validators\":{\"NameMaxLength\":{\"type\":\"crt.MaxLength\",\"params\":{\"max\":4}}}}}}";
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig);

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateStandardValidatorUsage(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "crt.MaxLength expects the maxLength param name, not max");
		result.Errors.Should().ContainSingle(error =>
				error.Contains("crt.MaxLength") &&
				error.Contains("max") &&
				error.Contains("maxLength"),
			because: "the validation error should identify both the wrong param and the required one");
	}

	[Test]
	[Description("Built-in crt.MaxLength validator with maxLength param passes validation.")]
	public void ValidateStandardValidatorUsage_BuiltInMaxLengthWithCorrectParamName_ReturnsValid() {
		// Arrange
		string viewConfigDiff = "[{\"operation\":\"insert\",\"name\":\"UsrName\",\"values\":{\"type\":\"crt.Input\",\"control\":\"$UsrName\"}}]";
		string viewModelConfig = "{\"attributes\":{\"UsrName\":{\"modelConfig\":{\"path\":\"PDS.UsrName\"},\"validators\":{\"NameMaxLength\":{\"type\":\"crt.MaxLength\",\"params\":{\"maxLength\":4}}}}}}";
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig);

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateStandardValidatorUsage(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "crt.MaxLength with the canonical maxLength param should not be rejected");
		result.Errors.Should().BeEmpty(
			because: "the standard validator binding is structurally correct");
	}

	[Test]
	[Description("Built-in crt.MaxLength with an optional message param passes validation because message is universally allowed via ValidatorParametersValues.")]
	public void ValidateStandardValidatorUsage_BuiltInMaxLengthWithMessageParam_ReturnsValid() {
		// Arrange
		string viewConfigDiff = "[{\"operation\":\"insert\",\"name\":\"UsrName\",\"values\":{\"type\":\"crt.Input\",\"control\":\"$UsrName\"}}]";
		string viewModelConfig = "{\"attributes\":{\"UsrName\":{\"modelConfig\":{\"path\":\"PDS.UsrName\"},\"validators\":{\"NameMaxLength\":{\"type\":\"crt.MaxLength\",\"params\":{\"maxLength\":4,\"message\":\"#ResourceString(UsrNameTooLong)#\"}}}}}}";
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig);

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateStandardValidatorUsage(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "message is universally optional on all validators via ValidatorParametersValues and must not be rejected");
		result.Errors.Should().BeEmpty(
			because: "crt.MaxLength with maxLength and message params is structurally valid");
	}

	[Test]
	[Description("Built-in crt.Required with only a message param passes validation because message is universally allowed.")]
	public void ValidateStandardValidatorUsage_BuiltInRequiredWithOnlyMessageParam_ReturnsValid() {
		// Arrange
		string viewConfigDiff = "[{\"operation\":\"insert\",\"name\":\"UsrName\",\"values\":{\"type\":\"crt.Input\",\"control\":\"$UsrName\"}}]";
		string viewModelConfig = "{\"attributes\":{\"UsrName\":{\"modelConfig\":{\"path\":\"PDS.UsrName\"},\"validators\":{\"Required\":{\"type\":\"crt.Required\",\"params\":{\"message\":\"#ResourceString(UsrRequired)#\"}}}}}}";
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig);

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateStandardValidatorUsage(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "message is universally optional — crt.Required with a message override must pass");
		result.Errors.Should().BeEmpty(
			because: "message is the only universally-optional param and must never be reported as unsupported");
	}


	public void ValidateStandardValidatorUsage_CustomValidatorWithEmptyParamsBindingParams_ReturnsInvalid() {
		// Arrange
		string viewConfigDiff = "[{\"operation\":\"insert\",\"name\":\"UsrName\",\"values\":{\"type\":\"crt.Input\",\"control\":\"$UsrName\"}}]";
		string viewModelConfig = "{\"attributes\":{\"UsrName\":{\"modelConfig\":{\"path\":\"PDS.UsrName\"},\"validators\":{\"NoParams\":{\"type\":\"usr.NoParamsValidator\",\"params\":{\"message\":\"#ResourceString(UsrMsg)#\"}}}}}}";
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig).Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"/**SCHEMA_VALIDATORS*/{\"usr.NoParamsValidator\":{\"validator\":function(){return function(){return null;};},\"params\":[],\"async\":false}}/**SCHEMA_VALIDATORS*/");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateStandardValidatorUsage(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "a custom validator that declares no params must not accept bound params on the attribute");
		result.Errors.Should().ContainSingle(error =>
				error.Contains("usr.NoParamsValidator") &&
				error.Contains("message"),
			because: "the validation error should identify the custom validator and the unsupported bound param");
	}

	[Test]
	[Description("Custom validators without a params array still reject bound params instead of overriding built-in contracts.")]
	public void ValidateStandardValidatorUsage_CustomValidatorWithoutParamsDeclarationBindingParams_ReturnsInvalid() {
		// Arrange
		string viewConfigDiff = "[{\"operation\":\"insert\",\"name\":\"UsrName\",\"values\":{\"type\":\"crt.Input\",\"control\":\"$UsrName\"}}]";
		string viewModelConfig = "{\"attributes\":{\"UsrName\":{\"modelConfig\":{\"path\":\"PDS.UsrName\"},\"validators\":{\"NoParams\":{\"type\":\"usr.NoParamsValidator\",\"params\":{\"message\":\"#ResourceString(UsrMsg)#\"}}}}}}";
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig).Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"/**SCHEMA_VALIDATORS*/{\"usr.NoParamsValidator\":{\"validator\":function(){return function(){return null;};},\"async\":false}}/**SCHEMA_VALIDATORS*/");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateStandardValidatorUsage(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "a custom validator without a declared params array must still reject bound params");
		result.Errors.Should().ContainSingle(error =>
				error.Contains("usr.NoParamsValidator") &&
				error.Contains("message"),
			because: "the validation error should identify the unsupported param bound to a zero-param custom validator");
	}

	[Test]
	[Description("SCHEMA_VALIDATORS cannot override built-in crt.MaxLength param contracts.")]
	public void ValidateStandardValidatorUsage_BuiltInContractCannotBeOverriddenFromSchemaValidators_ReturnsInvalid() {
		// Arrange
		string viewConfigDiff = "[{\"operation\":\"insert\",\"name\":\"UsrName\",\"values\":{\"type\":\"crt.Input\",\"control\":\"$UsrName\"}}]";
		string viewModelConfig = "{\"attributes\":{\"UsrName\":{\"modelConfig\":{\"path\":\"PDS.UsrName\"},\"validators\":{\"NameMaxLength\":{\"type\":\"crt.MaxLength\",\"params\":{\"max\":4}}}}}}";
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig).Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"/**SCHEMA_VALIDATORS*/{\"crt.MaxLength\":{\"validator\":function(){return function(){return null;};},\"params\":[{\"name\":\"max\"}],\"async\":false}}/**SCHEMA_VALIDATORS*/");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateStandardValidatorUsage(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "built-in validator contracts must stay canonical even if SCHEMA_VALIDATORS tries to redefine crt.MaxLength");
		result.Errors.Should().ContainSingle(error =>
				error.Contains("crt.MaxLength") &&
				error.Contains("max") &&
				error.Contains("maxLength"),
			because: "the validation error should still enforce the canonical built-in maxLength param name");
	}

	[Test]
	[Description("Validator with unquoted params key (valid JS) must be parsed correctly — no false 'missing message' error.")]
	public void ValidateCustomValidatorParamCompleteness_UnquotedParamsKey_ReturnsValid() {
		// Arrange
		string viewConfigDiff = "[{\"operation\":\"insert\",\"name\":\"UsrName\",\"values\":{\"type\":\"crt.Input\",\"control\":\"$UsrName\"}}]";
		string viewModelConfig = "{\"attributes\":{\"UsrName\":{\"modelConfig\":{\"path\":\"PDS.UsrName\"},\"validators\":{\"Upper\":{\"type\":\"usr.UpperCaseValidator\",\"params\":{\"message\":\"#ResourceString(UsrMsg)#\"}}}}}}";
		// SCHEMA_VALIDATORS uses unquoted JS property: params: [{ "name": "message" }]
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig).Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"/**SCHEMA_VALIDATORS*/{\"usr.UpperCaseValidator\":{validator:function(config){return function(control){return null;};},params:[{\"name\":\"message\"}],async:false}}/**SCHEMA_VALIDATORS*/");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateCustomValidatorParamCompleteness(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "unquoted JS property names in SCHEMA_VALIDATORS are valid and must be recognised by the parser");
		result.Errors.Should().BeEmpty(
			because: "the params array declares 'message' — no structural error should be reported");
	}

	[Test]
	[Description("Validator with both unquoted params key and unquoted name key (pure JS style) must be parsed correctly.")]
	public void ValidateCustomValidatorParamCompleteness_FullyUnquotedParamEntry_ReturnsValid() {
		// Arrange
		string viewConfigDiff = "[{\"operation\":\"insert\",\"name\":\"UsrName\",\"values\":{\"type\":\"crt.Input\",\"control\":\"$UsrName\"}}]";
		string viewModelConfig = "{\"attributes\":{\"UsrName\":{\"modelConfig\":{\"path\":\"PDS.UsrName\"},\"validators\":{\"Upper\":{\"type\":\"usr.UpperCaseValidator\",\"params\":{\"message\":\"#ResourceString(UsrMsg)#\"}}}}}}";
		// SCHEMA_VALIDATORS uses fully unquoted JS properties: params: [{ name: "message" }]
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig).Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"/**SCHEMA_VALIDATORS*/{\"usr.UpperCaseValidator\":{validator:function(config){return function(control){return null;};},params:[{name:\"message\"}],async:false}}/**SCHEMA_VALIDATORS*/");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateCustomValidatorParamCompleteness(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "fully unquoted JS-style param entries are valid JavaScript and must not trigger a false-positive missing-param error");
		result.Errors.Should().BeEmpty(
			because: "params declares 'message' via unquoted name key — the parser must recognise both quoted and unquoted forms");
	}

	[Test]
	[Description("Validator with unquoted params key that truly has no message param is still rejected.")]
	public void ValidateCustomValidatorParamCompleteness_UnquotedParamsKeyMissingMessage_ReturnsInvalid() {
		// Arrange
		string viewConfigDiff = "[{\"operation\":\"insert\",\"name\":\"UsrName\",\"values\":{\"type\":\"crt.Input\",\"control\":\"$UsrName\"}}]";
		string viewModelConfig = "{\"attributes\":{\"UsrName\":{\"modelConfig\":{\"path\":\"PDS.UsrName\"},\"validators\":{\"Upper\":{\"type\":\"usr.UpperCaseValidator\",\"params\":{\"message\":\"#ResourceString(UsrMsg)#\"}}}}}}";
		// params array exists but has no message param
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig).Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"/**SCHEMA_VALIDATORS*/{\"usr.UpperCaseValidator\":{validator:function(config){return function(control){return null;};},params:[{name:\"settingCode\"}],async:false}}/**SCHEMA_VALIDATORS*/");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateCustomValidatorParamCompleteness(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "a validator without 'message' in params must still be rejected regardless of whether param keys are quoted or unquoted");
		result.Errors.Should().ContainSingle(error => error.Contains("usr.UpperCaseValidator") && error.Contains("message"),
			because: "the missing message param error should identify the validator type");
	}

	[Test]
	[Description("Attribute without validators passes param resource binding check without errors")]
	public void ValidateValidatorParamResourceBindings_NoValidators_ReturnsValid() {
		// Arrange
		string viewModelConfig = "{\"attributes\":{\"UsrName\":{\"modelConfig\":{\"path\":\"PDS.UsrName\"}}}}";
		string body = BuildStaticViewModelConfigPageBody("[]", viewModelConfig);

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateValidatorParamResourceBindings(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "attributes without validators have no params to validate");
		result.Errors.Should().BeEmpty(
			because: "no validator params are present");
	}

	[Test]
	[Description("Custom validator that returns a proper error object with declared params passes completeness check")]
	public void ValidateCustomValidatorParamCompleteness_ValidatorWithDeclaredMessageParam_ReturnsValid() {
		// Arrange
		string body = BuildStaticViewModelConfigPageBody("[]", "{}").Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"/**SCHEMA_VALIDATORS*/{\"usr.OnlyDigits\":{\"validator\":function(config){return function(control){" +
			"var v=control.value;if(v&&!/^\\d+$/.test(v)){return{\"usr.OnlyDigits\":{message:config.message}};}" +
			"return null;};},\"params\":[{\"name\":\"message\"}],\"async\":false}}/**SCHEMA_VALIDATORS*/");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateCustomValidatorParamCompleteness(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "the validator declares 'message' in params and the error object only uses 'message'");
		result.Errors.Should().BeEmpty(
			because: "all returned error properties are declared in params");
	}

	[Test]
	[Description("Custom validator that returns boolean true instead of error object fails completeness check")]
	public void ValidateCustomValidatorParamCompleteness_ValidatorReturnsPrimitiveTrue_ReturnsInvalid() {
		// Arrange
		string body = BuildStaticViewModelConfigPageBody("[]", "{}").Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"/**SCHEMA_VALIDATORS*/{\"usr.OnlyDigits\":{\"validator\":function(config){return function(control){" +
			"var v=control.value;if(v&&!/^\\d+$/.test(v)){return{\"usr.OnlyDigits\":true};}" +
			"return null;};},\"params\":[],\"async\":false}}/**SCHEMA_VALIDATORS*/");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateCustomValidatorParamCompleteness(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "returning a boolean true instead of an error object causes a Creatio runtime error");
		result.Errors.Should().ContainSingle(error =>
				error.Contains("usr.OnlyDigits") &&
				error.Contains("primitive"),
			because: "the error message should identify the validator and explain the primitive return issue");
	}

	[Test]
	[Description("Custom validator with empty params that returns undeclared message property fails completeness check")]
	public void ValidateCustomValidatorParamCompleteness_ValidatorReturnsUndeclaredMessageProperty_ReturnsInvalid() {
		// Arrange
		string body = BuildStaticViewModelConfigPageBody("[]", "{}").Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"/**SCHEMA_VALIDATORS*/{\"usr.OnlyDigits\":{\"validator\":function(config){return function(control){" +
			"var v=control.value;if(v&&!/^\\d+$/.test(v)){return{\"usr.OnlyDigits\":{message:\"Only digits allowed\"}};}" +
			"return null;};},\"params\":[],\"async\":false}}/**SCHEMA_VALIDATORS*/");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateCustomValidatorParamCompleteness(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "a validator without a declared 'message' param cannot show a user-visible error message");
		result.Errors.Should().ContainSingle(error =>
				error.Contains("usr.OnlyDigits") &&
				error.Contains("message"),
			because: "the error should identify the validator and explain the missing 'message' param");
	}

	[Test]
	[Description("Custom validator with nested error object still reports undeclared top-level params")]
	public void ValidateCustomValidatorParamCompleteness_ValidatorReturnsNestedErrorObject_ReturnsInvalid() {
		// Arrange
		string body = BuildStaticViewModelConfigPageBody("[]", "{}").Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"/**SCHEMA_VALIDATORS*/{\"usr.OnlyDigits\":{\"validator\":function(config){return function(control){" +
			"var v=control.value;if(v&&!/^\\d+$/.test(v)){return{\"usr.OnlyDigits\":{details:{field:\"UsrName\"},message:config.message}};}" +
			"return null;};},\"params\":[{\"name\":\"message\"}],\"async\":false}}/**SCHEMA_VALIDATORS*/");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateCustomValidatorParamCompleteness(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "top-level error properties such as details must still be declared even when their values are nested objects");
		result.Errors.Should().ContainSingle(error =>
				error.Contains("usr.OnlyDigits") &&
				error.Contains("details") &&
				!error.Contains("field"),
			because: "the validation should report the undeclared top-level property without treating nested object keys as separate validator params");
	}

	[Test]
	[Description("Custom validator that returns an empty error object fails completeness check because message param is missing")]
	public void ValidateCustomValidatorParamCompleteness_ValidatorReturnsEmptyErrorObject_ReturnsInvalid() {
		// Arrange
		string body = BuildStaticViewModelConfigPageBody("[]", "{}").Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"/**SCHEMA_VALIDATORS*/{\"usr.OnlyDigits\":{\"validator\":function(config){return function(control){" +
			"var v=control.value;if(v&&!/^\\d+$/.test(v)){return{\"usr.OnlyDigits\":{}};}" +
			"return null;};},\"params\":[],\"async\":false}}/**SCHEMA_VALIDATORS*/");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateCustomValidatorParamCompleteness(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "an empty error object with no message param means the user never sees an error message");
		result.Errors.Should().ContainSingle(error =>
				error.Contains("usr.OnlyDigits") &&
				error.Contains("message"),
			because: "the error should identify that 'message' param is missing from the validator declaration");
	}

	[Test]
	[Description("Custom validator with params array beyond the old 1200-character cutoff is parsed correctly and passes completeness check")]
	public void ValidateCustomValidatorParamCompleteness_LongValidatorBodyWithParamsBeyond1200Chars_ReturnsValid() {
		// Arrange — pad the validator function body so that "params" appears well past character 1200
		string padding = new string(' ', 1300); // blank space inside the function comment
		string validatorsBlock =
			"{\"usr.LongValidator\":{\"validator\":function(config){" +
			$"/* {padding} */" +
			"return function(control){var v=control.value;" +
			"if(!v||v.length>0){return{\"usr.LongValidator\":{message:config.message}};}" +
			"return null;};}" +
			",\"params\":[{\"name\":\"message\"}],\"async\":false}}";
		string body = BuildStaticViewModelConfigPageBody("[]", "{}").Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			$"/**SCHEMA_VALIDATORS*/{validatorsBlock}/**SCHEMA_VALIDATORS*/");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateCustomValidatorParamCompleteness(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "brace-balanced extraction must find params even when they appear past the old 1200-char cutoff");
		result.Errors.Should().BeEmpty(
			because: "the validator declares 'message' and returns it correctly — it is structurally valid");
	}

	[Test]
	[Description("Custom validator whose SCHEMA_VALIDATORS block contains balanced braces inside regex character class literals is extracted correctly")]
	public void ValidateCustomValidatorParamCompleteness_ValidatorWithBracesInStrings_ReturnsValid() {
		// Arrange — the validator body contains a regex literal /^[{a-z}]+$/ with balanced braces.
		// These braces are balanced so brace-depth tracking works correctly.
		// NOTE: regex literals with unbalanced braces (e.g. /{[a-z]+/) are a known ExtractValidatorBody limitation.
		string validatorsBlock =
			"{\"usr.PatternValidator\":{\"validator\":function(config){" +
			"return function(control){var v=control.value;" +
			"if(v&&!/^[{a-z}]+$/.test(v)){return{\"usr.PatternValidator\":{message:config.message}};}" +
			"return null;};}" +
			",\"params\":[{\"name\":\"message\"}],\"async\":false}}";
		string body = BuildStaticViewModelConfigPageBody("[]", "{}").Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			$"/**SCHEMA_VALIDATORS*/{validatorsBlock}/**SCHEMA_VALIDATORS*/");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateCustomValidatorParamCompleteness(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "brace-balanced extraction handles balanced braces inside regex character class literals without misreading depth");
		result.Errors.Should().BeEmpty(
			because: "the validator is structurally valid with a declared message param");
	}
}

