using System.Collections.Generic;
using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
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
		result.IsValid.Should().BeTrue("because all marker sections contain valid JSON");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Valid form page body passes marker content validation")]
	public void ValidateMarkerContent_ValidFormPageBody_ReturnsValid() {
		var result = SchemaValidationService.ValidateMarkerContent(ValidFormPageBody);
		result.IsValid.Should().BeTrue("because all marker sections contain valid JSON");
		result.Errors.Should().BeEmpty();
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
		result.Errors.Should().BeEmpty();
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
	[Description("Body with malformed JSON in converters fails content validation")]
	public void ValidateMarkerContent_MalformedConverters_ReturnsInvalid() {
		string body = ValidFormPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"/**SCHEMA_CONVERTERS*/{\"key\": }/**SCHEMA_CONVERTERS*/");
		var result = SchemaValidationService.ValidateMarkerContent(body);
		result.IsValid.Should().BeFalse("because the converters section contains malformed JSON");
		result.Errors.Should().ContainMatch("*SCHEMA_CONVERTERS*");
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
}
