using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class SchemaValidationServiceTests
{

	private const string ValidListPageBody =
		"""
			define(
				"TestPage",
				/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/,
				function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/{
					return {
						viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/,
						viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
						modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
						handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,
						converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
						validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
					};
				}
			);
		""";

	private const string ValidFormPageBody =
		"""
			define(
				"TestPage",
				/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/,
				function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/{
					return {
						viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/,
						viewModelConfig: /**SCHEMA_VIEW_MODEL_CONFIG*/{}/**SCHEMA_VIEW_MODEL_CONFIG*/,
						modelConfig: /**SCHEMA_MODEL_CONFIG*/{}/**SCHEMA_MODEL_CONFIG*/,
						handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,
						converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
						validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
					};
				}
			);
		""";

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
		string body = """
			define("Test", /**SCHEMA_DEPS*/[]);
		""";
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
			"""
				/**SCHEMA_VIEW_CONFIG_DIFF*/[{"a":1},,{"b":2}]/**SCHEMA_VIEW_CONFIG_DIFF*/
			""");
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
			"""
				/**SCHEMA_VIEW_CONFIG_DIFF*/[{"a":1},{"b":2},]/**SCHEMA_VIEW_CONFIG_DIFF*/
			""");
		var result = SchemaValidationService.ValidateMarkerContent(body);
		result.IsValid.Should().BeTrue("because Hjson tolerates trailing commas");
		result.Errors.Should().BeEmpty("because Hjson parser does not treat trailing commas as errors");
	}

	[Test]
	[Description("Body with JavaScript handler functions passes content validation")]
	public void ValidateMarkerContent_JavaScriptHandlers_ReturnsValid() {
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
			"""
				/**SCHEMA_HANDLERS*/
					[
						{
							request: "crt.HandleViewModelInitRequest",
							handler: async (request, next) => { await next?.handle(request); }
						}
					]
				/**SCHEMA_HANDLERS*/
			""");
		var result = SchemaValidationService.ValidateMarkerContent(body);
		result.IsValid.Should().BeTrue("because handlers can contain JavaScript and should not be parsed as JSON content");
		result.Errors.Should().BeEmpty("because the JSON-backed markers remain valid");
	}

	[Test]
	[Description("Handlers section must remain an array literal")]
	public void ValidateHandlerStructure_NonArrayHandlersSection_ReturnsInvalid() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
			"""
				/**SCHEMA_HANDLERS*/
					{
						request: "crt.HandleViewModelInitRequest",
						handler: async (request, next) => { await next?.handle(request); }
					}
				/**SCHEMA_HANDLERS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateHandlerStructure(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "SCHEMA_HANDLERS must remain an array literal even when it contains only one handler");
		result.Errors.Should().ContainSingle(error => error.Contains("array literal"),
			because: "the validation error should explain that handlers must stay wrapped in an array");
	}

	[Test]
	[Description("Each handler entry must declare a string request property")]
	public void ValidateHandlerStructure_HandlerEntryWithoutRequest_ReturnsInvalid() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
			"""
				/**SCHEMA_HANDLERS*/
					[
						{
							handler: async (request, next) => {
								await next?.handle(request);
							}
						}
					]
				/**SCHEMA_HANDLERS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateHandlerStructure(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "each handler entry must declare which request type it handles");
		result.Errors.Should().ContainSingle(error => error.Contains("'request'"),
			because: "the validation error should identify the missing request property");
	}

	[Test]
	[Description("Each handler entry must declare a handler property")]
	public void ValidateHandlerStructure_HandlerEntryWithoutHandler_ReturnsInvalid() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
			"""
				/**SCHEMA_HANDLERS*/[{ request: "crt.HandleViewModelInitRequest" }]/**SCHEMA_HANDLERS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateHandlerStructure(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "each handler entry must declare a handler property with the implementation");
		result.Errors.Should().ContainSingle(error => error.Contains("'handler'"),
			because: "the validation error should identify the missing handler property");
	}

	[Test]
	[Description("Each handler entry must keep request as a string literal")]
	public void ValidateHandlerStructure_HandlerEntryWithNonStringRequest_ReturnsInvalid() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
			"""
				/**SCHEMA_HANDLERS*/
					[
						{
							request: true,
							handler:
								async (request, next) => {
									await next?.handle(request);
								}
						}
					]
				/**SCHEMA_HANDLERS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateHandlerStructure(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "handler request must stay a string literal so tooling can identify the handled request type");
		result.Errors.Should().ContainSingle(error => error.Contains("'request'"),
			because: "the validation error should explain that request must be a string property");
	}

	[Test]
	[Description("Nested request string literals inside handler bodies must not satisfy the top-level request contract")]
	public void ValidateHandlerStructure_NestedRequestStringLiteral_DoesNotSatisfyTopLevelRequestContract() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
			"""
				/**SCHEMA_HANDLERS*/
					[
						{
							request: someExpression,
							handler: async (request, next) => { return { request: "crt.NestedRequest" }; }
						}
					]
				/**SCHEMA_HANDLERS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateHandlerStructure(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "only the top-level request property should satisfy the handler contract");
		result.Errors.Should().ContainSingle(error => error.Contains("'request'"),
			because: "a nested request string literal inside the handler body must not make the top-level request valid");
	}

	[Test]
	[Description("Interpolated template literals are rejected for handler request names")]
	public void ValidateHandlerStructure_HandlerEntryWithInterpolatedTemplateLiteralRequest_ReturnsInvalid() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
			"""
				/**SCHEMA_HANDLERS*/
					[
						{
							request: `crt.${suffix}`,
							handler:
								async (request, next) => {
									await next?.handle(request);
								}
						}
					]
				/**SCHEMA_HANDLERS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateHandlerStructure(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "handler request names must stay statically readable and interpolated template literals are dynamic");
		result.Errors.Should().ContainSingle(error => error.Contains("'request'"),
			because: "the validation error should explain that request must remain a stable string property");
	}

	[Test]
	[Description("Plain template literals without interpolation are accepted for handler request names")]
	public void ValidateHandlerStructure_HandlerEntryWithPlainTemplateLiteralRequest_ReturnsValid() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
			"""
				/**SCHEMA_HANDLERS*/
					[
						{
							request: `crt.HandleViewModelInitRequest`,
							handler:
								async (request, next) => {
									await next?.handle(request);
								}
						}
					]
				/**SCHEMA_HANDLERS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateHandlerStructure(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "plain template literals without interpolation are still static JavaScript string literals");
		result.Errors.Should().BeEmpty(
			because: "the request type stays statically readable even when backticks are used");
	}

	[Test]
	[Description("Non-callable handler values are rejected")]
	public void ValidateHandlerStructure_HandlerEntryWithNonCallableHandler_ReturnsInvalid() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
			"""
				/**SCHEMA_HANDLERS*/
					[
						{
							request: "crt.HandleViewModelInitRequest",
							handler: true
						}
					]
				/**SCHEMA_HANDLERS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateHandlerStructure(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "handler entries must expose executable handler code rather than plain scalar values");
		result.Errors.Should().ContainSingle(error => error.Contains("'handler'"),
			because: "the validation error should explain that handler must remain callable");
	}

	[Test]
	[Description("Quoted strings containing arrow syntax are rejected as handler values")]
	public void ValidateHandlerStructure_HandlerEntryWithQuotedArrowStringHandler_ReturnsInvalid() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
			"""
				/**SCHEMA_HANDLERS*/
					[
						{
							request: "crt.HandleViewModelInitRequest",
							handler: "not a function =>"
						}
					]
				/**SCHEMA_HANDLERS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateHandlerStructure(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "quoted text that merely contains arrow syntax is still not executable handler code");
		result.Errors.Should().ContainSingle(error => error.Contains("'handler'"),
			because: "the validation error should explain that string literals are not callable handlers");
	}

	[Test]
	[Description("Object literals containing nested arrow functions are rejected as handler values")]
	public void ValidateHandlerStructure_HandlerEntryWithObjectLiteralContainingNestedArrow_ReturnsInvalid() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
			"""
				/**SCHEMA_HANDLERS*/
					[
						{
							request: "crt.HandleViewModelInitRequest",
							handler: { nested: () => {} }
						}
					]
				/**SCHEMA_HANDLERS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateHandlerStructure(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "a handler object that merely contains a nested arrow function is still not itself callable");
		result.Errors.Should().ContainSingle(error => error.Contains("'handler'"),
			because: "the validation error should explain that the top-level handler value must remain callable");
	}

	[Test]
	[Description("Regex literals containing arrow syntax are rejected as handler values")]
	public void ValidateHandlerStructure_HandlerEntryWithRegexLiteralContainingArrow_ReturnsInvalid() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
			"""
				/**SCHEMA_HANDLERS*/
					[
						{
							request: "crt.HandleViewModelInitRequest",
							handler: /=>/
						}
					]
				/**SCHEMA_HANDLERS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateHandlerStructure(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "a regex literal that merely contains arrow syntax is still not callable handler code");
		result.Errors.Should().ContainSingle(error => error.Contains("'handler'"),
			because: "the validation error should explain that regex literals are not callable handlers");
	}

	[Test]
	[Description("Object-literal method shorthand handlers are accepted as callable values")]
	public void ValidateHandlerStructure_HandlerEntryWithMethodShorthandHandler_ReturnsValid() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
			"""
				/**SCHEMA_HANDLERS*/
					[
						{
							request: "crt.HandleViewModelInitRequest",
							handler(request, next) { return next?.handle(request); }
						}
					]
				/**SCHEMA_HANDLERS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateHandlerStructure(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "object-literal method shorthand is still a callable handler form in JavaScript");
		result.Errors.Should().BeEmpty(
			because: "valid method shorthand handlers should not be rejected as missing or non-callable");
	}

	[Test]
	[Description("Handler entries that use request.viewModel APIs are rejected with a recovery hint to read handler guidance")]
	public void ValidateHandlerStructure_HandlerEntryWithRequestViewModelApi_ReturnsInvalid_WithRecoveryHint() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
			"""
				/**SCHEMA_HANDLERS*/
					[
						{
							request: "crt.HandleViewModelAttributeChangeRequest",
							handler:
								async (request, next) => {
									const current = await request.viewModel.get("UsrParkingRequired");
									await request.viewModel.set("UsrVehicleNumber", current ? "A-01" : null);
									return next?.handle(request);
								}
						}
					]
				/**SCHEMA_HANDLERS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateHandlerStructure(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "invented request.viewModel accessors are not part of the canonical page-body handler API");
		result.Errors.Should().ContainSingle(error =>
				error.Contains("request.viewModel", StringComparison.Ordinal) &&
				error.Contains("page-schema-handlers", StringComparison.Ordinal) &&
				error.Contains("canonical clio handler examples", StringComparison.Ordinal),
			because: "the validation error should both reject the API and tell the caller to reread the handler guidance and examples");
	}

	[Test]
	[Description("Handler entries that use request.$context.get are rejected with a recovery hint to read handler guidance")]
	public void ValidateHandlerStructure_HandlerEntryWithContextGetApi_ReturnsInvalid_WithRecoveryHint() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
			"""
				/**SCHEMA_HANDLERS*/
					[
						{
							request: "crt.HandleViewModelInitRequest",
							handler:
								async (request, next) => {
									const current = await request.$context.get("UsrParkingRequired");
									return next?.handle(request);
								}
						}
					]
				/**SCHEMA_HANDLERS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateHandlerStructure(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "request.$context.get is not part of the canonical page-body handler API");
		result.Errors.Should().ContainSingle(error =>
				error.Contains("request.$context.get", StringComparison.Ordinal) &&
				error.Contains("page-schema-handlers", StringComparison.Ordinal) &&
				error.Contains("canonical clio handler examples", StringComparison.Ordinal),
			because: "the validation error should reject the unsupported getter and point the caller back to handler guidance and examples");
	}

	[Test]
	[Description("Handler entries that use request.sender are rejected with a recovery hint to read handler guidance")]
	public void ValidateHandlerStructure_HandlerEntryWithSenderApi_ReturnsInvalid_WithRecoveryHint() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
			"""
				/**SCHEMA_HANDLERS*/
					[
						{
							request: "crt.HandleViewModelInitRequest",
							handler:
								async (request, next) => {
									const sender = request.sender;
									return next?.handle(request);
								}
						}
					]
				/**SCHEMA_HANDLERS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateHandlerStructure(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "request.sender is not part of the supported deployed page-body handler contract");
		result.Errors.Should().ContainSingle(error =>
				error.Contains("request.sender", StringComparison.Ordinal) &&
				error.Contains("page-schema-handlers", StringComparison.Ordinal) &&
				error.Contains("canonical clio handler examples", StringComparison.Ordinal),
			because: "the validation error should reject request.sender and point the caller back to handler guidance and examples");
	}

	[Test]
	[Description("Handler entries that use .$get are rejected with a recovery hint to read handler guidance")]
	public void ValidateHandlerStructure_HandlerEntryWithDollarGetApi_ReturnsInvalid_WithRecoveryHint() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
			"""
				/**SCHEMA_HANDLERS*/
					[
						{
							request: "crt.HandleViewModelInitRequest",
							handler:
								async (request, next) => {
									const current = await request.$context.$get("UsrParkingRequired");
									return next?.handle(request);
								}
						}
					]
				/**SCHEMA_HANDLERS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateHandlerStructure(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: ".$get is not part of the canonical deployed page-body handler API");
		result.Errors.Should().ContainSingle(error =>
				error.Contains(".$get", StringComparison.Ordinal) &&
				error.Contains("page-schema-handlers", StringComparison.Ordinal) &&
				error.Contains("canonical clio handler examples", StringComparison.Ordinal),
			because: "the validation error should reject .$get and point the caller back to handler guidance and examples");
	}

	[Test]
	[Description("Handler entries that use .$set are rejected with a recovery hint to read handler guidance")]
	public void ValidateHandlerStructure_HandlerEntryWithDollarSetApi_ReturnsInvalid_WithRecoveryHint() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
			"""
				/**SCHEMA_HANDLERS*/
					[
						{
							request: "crt.HandleViewModelInitRequest",
							handler:
								async (request, next) => {
									await request.$context.$set("UsrParkingRequired", true);
									return next?.handle(request);
								}
						}
					]
				/**SCHEMA_HANDLERS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateHandlerStructure(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: ".$set is not part of the canonical deployed page-body handler API");
		result.Errors.Should().ContainSingle(error =>
				error.Contains(".$set", StringComparison.Ordinal) &&
				error.Contains("page-schema-handlers", StringComparison.Ordinal) &&
				error.Contains("canonical clio handler examples", StringComparison.Ordinal),
			because: "the validation error should reject .$set and point the caller back to handler guidance and examples");
	}

	[Test]
	[Description("Multiple handler entries where both lack a callable handler expression each produce their own error")]
	public void ValidateHandlerStructure_MultipleInvalidHandlerEntries_ReturnsAllErrors() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
			"""
				/**SCHEMA_HANDLERS*/
					[
						{ request: "crt.HandleViewModelInitRequest", handler: "not-callable" },
						{ request: "crt.HandleViewModelDestroyRequest", handler: "also-not-callable" }
					]
				/**SCHEMA_HANDLERS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateHandlerStructure(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "both handler entries have non-callable handler expressions");
		result.Errors.Should().HaveCount(2,
			because: "each invalid handler entry must produce its own error instead of stopping after the first one");
	}

	[Test]
	[Description("Multiple valid handler entries in one array pass handler structure validation")]
	public void ValidateHandlerStructure_MultipleValidHandlerEntries_ReturnsValid() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
			"""
				/**SCHEMA_HANDLERS*/
					[
						{
							request: "crt.HandleViewModelInitRequest",
							handler: async (request, next) => { return next?.handle(request); }
						},
						{
							request: "crt.HandleViewModelDestroyRequest",
							handler: async (request, next) => { return next?.handle(request); }
						}
					]
				/**SCHEMA_HANDLERS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateHandlerStructure(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "the handler validator should accept more than one correctly formed handler entry in the same array");
		result.Errors.Should().BeEmpty(
			because: "comma-separated valid handler entries should pass the array traversal and entry parsing logic");
	}

	[Test]
	[Description("Body with JavaScript converter functions passes content validation")]
	public void ValidateMarkerContent_JavaScriptConverters_ReturnsValid() {
		string body = ValidFormPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"""
				/**SCHEMA_CONVERTERS*/
					{
						"usr.ToUpperCase": function(value) {
							return value?.toUpperCase() ?? "";
						}
					}
				/**SCHEMA_CONVERTERS*/
			""");
		var result = SchemaValidationService.ValidateMarkerContent(body);

		result.IsValid.Should().BeTrue("because converters are authored as JavaScript object sections and may contain functions");
		result.Errors.Should().BeEmpty("because function-based converter sections should not be rejected as non-JSON");
	}

	[Test]
	[Description("Body with async arrow function converter passes content validation — runtime supports Promise-returning converters")]
	public void ValidateMarkerContent_AsyncArrowFunctionConverter_ReturnsValid() {
		string body = ValidFormPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"""
				/**SCHEMA_CONVERTERS*/
					{
						"usr.FormatPhoneNumber": async (value) => {
							if (!value) return "";
							const svc = new sdk.SysSettingsService();
							const setting = await svc.getByCode("UsrEnablePhoneFormatting");
							if (!Boolean(setting?.value)) return value;
							const digits = String(value).replace(/\D/g, "");
							if (digits.length !== 11) return value;
							return `+${digits.slice(0, 1)} (${digits.slice(1, 4)}) ${digits.slice(4, 7)}-${digits.slice(7, 9)}-${digits.slice(9, 11)}`;
						}
					}
				/**SCHEMA_CONVERTERS*/
			""");
		var result = SchemaValidationService.ValidateMarkerContent(body);

		result.IsValid.Should().BeTrue(
			because: "async arrow function converters with await, template literals, and regex are valid per the guidance resource");
		result.Errors.Should().BeEmpty(
			because: "no syntax error should be reported for a well-formed async converter body");
	}

	[Test]
	[Description("Body with converter containing multiple nested brace pairs passes content validation")]
	public void ValidateMarkerContent_ConverterWithNestedBraces_ReturnsValid() {
		string body = ValidFormPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"""
				/**SCHEMA_CONVERTERS*/
					{
						"usr.FormatScore": function(value) {
							if (!value) {
								return "";
							}  if (value >= 90) { return "Excellent"; }  if (value >= 70) { return "Good"; }  return "Poor";
						}
					}
				/**SCHEMA_CONVERTERS*/
			""");
		var result = SchemaValidationService.ValidateMarkerContent(body);

		result.IsValid.Should().BeTrue(
			because: "converter function bodies with multiple nested brace pairs must be accepted");
		result.Errors.Should().BeEmpty(
			because: "bracket-depth tracking should correctly handle peer-level if-blocks inside a converter");
	}

	[Test]
	[Description("Body with JavaScript validator functions passes content validation")]
	public void ValidateMarkerContent_JavaScriptValidators_ReturnsValid() {
		string body = ValidFormPageBody.Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"""
				/**SCHEMA_VALIDATORS*/
					{
						"usr.ValidateFieldValue": {
							"validator":
								function(config) {
									return function(control) {
										return control.value !== config.invalidName ? null : { "usr.ValidateFieldValue": { message: config.message } };
									};
								},
							"params": [{ "name": "invalidName" }, { "name": "message" }],
							"async": false
						}
					}
				/**SCHEMA_VALIDATORS*/
			""");
		var result = SchemaValidationService.ValidateMarkerContent(body);

		result.IsValid.Should().BeTrue("because validators are authored as JavaScript object sections and may contain functions");
		result.Errors.Should().BeEmpty("because function-based validator sections should not be rejected as non-JSON");
	}

	[Test]
	[Description("Body with non-object converters section fails content validation")]
	public void ValidateMarkerContent_NonObjectConverters_ReturnsInvalid() {
		string body = ValidFormPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"""
				/**SCHEMA_CONVERTERS*/["usr.ToUpperCase"]/**SCHEMA_CONVERTERS*/
			""");
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
			"""
				/**SCHEMA_VALIDATORS*/["usr.SomeValidator"]/**SCHEMA_VALIDATORS*/
			""");
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
			"""
				/**SCHEMA_CONVERTERS*/{ "usr.Bad": function(value { return value; } }/**SCHEMA_CONVERTERS*/
			""");
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
			"""
				/**SCHEMA_VALIDATORS*/{ "usr.Bad": function(config { return null; } }/**SCHEMA_VALIDATORS*/
			""");
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
			"""
				/**SCHEMA_CONVERTERS*/{ "usr.Bad": function(value { return value; } }/**SCHEMA_CONVERTERS*/
			""");
		string body = bodyWithInvalidConverters.Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"""
				/**SCHEMA_VALIDATORS*/{ "usr.Bad": function(config { return null; } }/**SCHEMA_VALIDATORS*/
			""");

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
			"""
				define(
					"Test",
					/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/,
					function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/{
						return {
							viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{"name":"DataTable","values":{"columns":[{"code":"PDS_Name"},{"code":"PDS_UsrStatus"}]}}]/**SCHEMA_VIEW_CONFIG_DIFF*/,
							viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[{"operation":"merge","values":{"PDS_Name":{"modelConfig":{"path":"PDS.Name"}},"PDS_UsrStatus":{"modelConfig":{"path":"PDS.UsrStatus"}}}}]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
							modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
							handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,
							converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
							validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
						};
					}
				);
			""";
		var result = SchemaValidationService.ValidateColumnBindings(body);
		result.IsValid.Should().BeTrue("because all DataTable columns have matching bindings");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("ListPage with missing column bindings reports errors")]
	public void ValidateColumnBindings_MissingBindings_ReturnsInvalid() {
		string body =
			"""
				define(
					"Test",
					/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/,
					function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/{
						return {
							viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{"name":"DataTable","values":{"columns":[{"code":"PDS_Name"},{"code":"PDS_UsrStatus"},{"code":"PDS_UsrDueDate"}]}}]/**SCHEMA_VIEW_CONFIG_DIFF*/,
							viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[{"operation":"merge","values":{"PDS_Name":{"modelConfig":{"path":"PDS.Name"}}}}]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
							modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
							handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,
							converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
							validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
						};
					}
				);
			""";
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
	[Description("Standard field binding to attribute not in current schema is valid — attribute may be declared in a parent schema")]
	public void ValidateStandardFieldBindings_BindingToAttributeNotInCurrentSchema_ReturnsValid() {
		string body = BuildDiffBackedPageBody(
			"""
				[
					{
						"operation":"insert",
						"name":"UsrStatus",
						"values":
							{
								"type":"crt.ComboBox",
								"label":"$Resources.Strings.PDS_UsrStatus",
								"control":"$UsrStatusField"
							}
					}
				]
			""",
			"""
				[
					{
						"operation":"merge",
						"values":{"UsrStatus":{"modelConfig":{"path":"PDS.UsrStatus"}}}
					}
				]
			""");

		var result = SchemaValidationService.ValidateStandardFieldBindings(body);

		result.IsValid.Should().BeTrue("because the control may bind to an attribute declared in a parent schema");
		result.Errors.Should().BeEmpty("because missing declaration in the current schema is not an error");
	}

	[Test]
	[Description("Standard field Usr label shortcuts without explicit resources are rejected")]
	public void ValidateStandardFieldBindings_UsrLabelShortcutWithoutResources_ReturnsInvalid() {
		string body = BuildDiffBackedPageBody(
			"""
				[
					{
						"operation":"insert",
						"name":"UsrStatus",
						"values":
							{
								"type":"crt.ComboBox",
								"label":"#ResourceString(UsrStatus_label)#",
								"control":"$UsrStatus"
							}
					}
				]
			""",
			"""
				[
					{
						"operation":"merge",
						"values":{"UsrStatus":{"modelConfig":{"path":"PDS.UsrStatus"}}}
					}
				]
			""");

		var result = SchemaValidationService.ValidateStandardFieldBindings(body);

		result.IsValid.Should().BeFalse("because data-bound field captions should not rely on implicit Usr label resources");
		result.Errors.Should().ContainSingle(error => error.Contains("UsrStatus_label"),
			"because the missing explicit resource entry should be called out");
	}

	[Test]
	[Description("Standard field declared view-model binding with auto-provided label passes semantic validation")]
	public void ValidateStandardFieldBindings_DeclaredAttributeBindingWithDatasourceCaption_ReturnsValid() {
		string body = BuildDiffBackedPageBody(
			"""
				[
					{
						"operation":"insert",
						"name":"UsrStatus",
						"values":
							{
								"type":"crt.ComboBox",
								"label":"$Resources.Strings.PDS_UsrStatus",
								"control":"$UsrStatus"
							}
					}
				]
			""",
			"""
				[
					{
						"operation":"merge",
						"values":{"UsrStatus":{"modelConfig":{"path":"PDS.UsrStatus"}}}
					}
				]
			""");

		var result = SchemaValidationService.ValidateStandardFieldBindings(body);

		result.IsValid.Should().BeTrue("because the field binds to a declared view-model attribute with an auto-provided label");
		result.Errors.Should().BeEmpty();
		result.Warnings.Should().BeEmpty();
	}

	[Test]
	[Description("Explicit custom resources on standard field shortcuts surface warnings instead of hard failures")]
	public void ValidateStandardFieldBindings_UsrLabelShortcutWithExplicitResources_ReturnsWarning() {
		string body = BuildDiffBackedPageBody(
			"""
				[
					{
						"operation":"insert",
						"name":"UsrStatus",
						"values":
							{
								"type":"crt.ComboBox",
								"label":"#ResourceString(UsrStatus_caption)#",
								"control":"$UsrStatus"
							}
					}
				]
			""",
			"""
				[
					{
						"operation":"merge",
						"values":{"UsrStatus":{"modelConfig":{"path":"PDS.UsrStatus"}}}
					}
				]
			""");

		var result = SchemaValidationService.ValidateStandardFieldBindings(
			body,
			new Dictionary<string, string> { ["UsrStatus_caption"] = "Status" });

		result.IsValid.Should().BeTrue("because the explicit resource makes the pattern suspicious but not conclusively broken");
		result.Errors.Should().BeEmpty();
		result.Warnings.Should().ContainSingle(warning => warning.Contains("UsrStatus_caption"),
			"because the validator should steer callers toward auto-provided labels");
	}

	[Test]
	[Description("Custom non-field UI elements may use explicit Usr caption resources")]
	public void ValidateStandardFieldBindings_CustomStandaloneCaptionWithExplicitResources_ReturnsValid() {
		string body = BuildDiffBackedPageBody(
			"""
				[
					{
						"operation":"insert",
						"name":"UsrStandaloneLabel",
						"values":{"type":"crt.Label","caption":"#ResourceString(UsrStatus_caption)#"}
					}
				]
			""",
			"[]");

		var result = SchemaValidationService.ValidateStandardFieldBindings(
			body,
			new Dictionary<string, string> { ["UsrStatus_caption"] = "Status" });

		result.IsValid.Should().BeTrue("because non-field standalone UI captions are outside the standard field guardrail");
		result.Errors.Should().BeEmpty();
		result.Warnings.Should().BeEmpty();
	}

	[Test]
	[Description("Label using attribute-name resource key for a DS-bound attribute does not warn when the key is absent from resources — the platform auto-provides captions under the attribute name")]
	public void ValidateStandardFieldBindings_LabelResourceKeyMissingButDsBound_ReturnsNoWarning() {
		string body = BuildDiffBackedPageBody(
			"""
				[
					{
						"operation":"insert",
						"name":"UsrName",
						"values":
							{
								"type":"crt.Input",
								"label":"$Resources.Strings.UsrName",
								"control":"$UsrName"
							}
					}
				]
			""",
			"""
				[
					{
						"operation":"merge",
						"values":{"UsrName":{"modelConfig":{"path":"PDS.UsrName"}}}
					}
				]
			""");

		var result = SchemaValidationService.ValidateStandardFieldBindings(
			body,
			new Dictionary<string, string> { ["PDS_UsrRequesterName"] = "Requester Name" });

		result.IsValid.Should().BeTrue("because DS-bound caption keys are auto-provided by the platform under the view-model attribute name");
		result.Errors.Should().BeEmpty();
		result.Warnings.Should().BeEmpty("because the label key equals the DS-bound attribute name and the platform auto-provides the caption");
	}

	[Test]
	[Description("Label using path-with-underscores resource key warns when the key is missing from resources and is not the auto-provided attribute-name form")]
	public void ValidateStandardFieldBindings_LabelResourceKeyIsPathWithUnderscoresAndMissing_ReturnsWarning() {
		string body = BuildDiffBackedPageBody(
			"""
				[
					{
						"operation":"insert",
						"name":"UsrName",
						"values":
							{
								"type":"crt.Input",
								"label":"$Resources.Strings.PDS_UsrName",
								"control":"$UsrName"
							}
					}
				]
			""",
			"""
				[
					{
						"operation":"merge",
						"values":{"UsrName":{"modelConfig":{"path":"PDS.UsrName"}}}
					}
				]
			""");

		var result = SchemaValidationService.ValidateStandardFieldBindings(
			body,
			new Dictionary<string, string> { ["PDS_UsrRequesterName"] = "Requester Name" });

		result.IsValid.Should().BeTrue("because a missing label resource is a recoverable warning, not a hard failure");
		result.Errors.Should().BeEmpty();
		result.Warnings.Should().ContainSingle(w => w.Contains("PDS_UsrName") && w.Contains("render blank"),
			"because the platform auto-provides captions under the attribute name 'UsrName', not under the path-with-underscores form 'PDS_UsrName'");
	}

	[Test]
	[Description("Label using a DS-bound SIBLING attribute name that is not the field's own binding attribute warns — auto-provide is keyed by the control's binding attribute, and 'UsrNameAlias' is a different attribute than the bound 'UsrName' even though both share the same DS path.")]
	public void ValidateStandardFieldBindings_LabelResourceKeyIsSiblingAttributeOnSameDsPath_ReturnsWarning() {
		string body = BuildDiffBackedPageBody(
			"""
				[
					{
						"operation":"insert",
						"name":"UsrName",
						"values":
							{
								"type":"crt.Input",
								"label":"$Resources.Strings.UsrNameAlias",
								"control":"$UsrName"
							}
					}
				]
			""",
			"""
				[
					{
						"operation":"merge",
						"values":
							{
								"UsrName":{"modelConfig":{"path":"PDS.UsrName"}},
								"UsrNameAlias":{"modelConfig":{"path":"PDS.UsrName"}}
							}
					}
				]
			""");

		var result = SchemaValidationService.ValidateStandardFieldBindings(
			body,
			new Dictionary<string, string> { ["SomeUnrelatedKey"] = "value" });

		result.IsValid.Should().BeTrue("because the missing label resource is a recoverable warning, not a hard failure");
		result.Errors.Should().BeEmpty();
		result.Warnings.Should().ContainSingle(w => w.Contains("UsrNameAlias") && w.Contains("render blank"),
			"because the label key is a different attribute than the control's binding attribute 'UsrName', so the platform does not auto-provide the caption under it");
	}

	[Test]
	[Description("Label keyed by a DS-bound attribute name whose column code differs is auto-provided — NO warning. Production evidence: in SsoSamlBase_FormPage the attribute 'PartnerIdentityName' is bound to 'SsoSamlProviderDS.EntityID' (column code 'EntityID') and ships with label '$Resources.Strings.PartnerIdentityName' and no explicit resource; the platform auto-provides the caption. Auto-provide is keyed by the attribute name, not the column code.")]
	public void ValidateStandardFieldBindings_LabelResourceKeyIsDsBoundAttributeName_NoWarning() {
		string body = BuildDiffBackedPageBody(
			"""
				[
					{
						"operation":"insert",
						"name":"UsrLabel",
						"values":
							{
								"type":"crt.Input",
								"label":"$Resources.Strings.UsrLabel",
								"control":"$UsrLabel"
							}
					}
				]
			""",
			"""
				[
					{
						"operation":"merge",
						"path":[],
						"values":{"attributes":{"UsrLabel":{"modelConfig":{"path":"PDS.UsrFullName"}}}}
					}
				]
			""");

		var result = SchemaValidationService.ValidateStandardFieldBindings(
			body,
			new Dictionary<string, string> { ["SomeOtherKey"] = "Other" });

		result.IsValid.Should().BeTrue("because the field binding is valid");
		result.Errors.Should().BeEmpty();
		result.Warnings.Should().NotContain(w => w.Contains("UsrLabel") && w.Contains("render blank"),
			"because the label key 'UsrLabel' equals the DS-bound binding attribute, so the platform auto-provides the caption regardless of the column code 'UsrFullName'");
	}

	[Test]
	[Description("Label referencing $Resources.Strings.KEY warns when KEY is absent and does not match any DS-bound attribute")]
	public void ValidateStandardFieldBindings_LabelResourceKeyMissingNotDsBound_ReturnsWarning() {
		string body = BuildDiffBackedPageBody(
			"""
				[
					{
						"operation":"insert",
						"name":"UsrName",
						"values":
							{
								"type":"crt.Input",
								"label":"$Resources.Strings.UsrCustomLabel",
								"control":"$UsrName"
							}
					}
				]
			""",
			"""
				[
					{
						"operation":"merge",
						"values":{"UsrName":{"modelConfig":{"path":"PDS.UsrName"}}}
					}
				]
			""");

		var result = SchemaValidationService.ValidateStandardFieldBindings(
			body,
			new Dictionary<string, string> { ["PDS_UsrName"] = "Name" });

		result.IsValid.Should().BeTrue("because a missing label resource is a recoverable issue, not a hard failure");
		result.Errors.Should().BeEmpty();
		result.Warnings.Should().ContainSingle(w => w.Contains("UsrCustomLabel") && w.Contains("render blank"),
			"because the label key does not match any DS-bound attribute and is missing from resources");
	}

	[Test]
	[Description("Label referencing $Resources.Strings.KEY passes when KEY is present in explicit resources")]
	public void ValidateStandardFieldBindings_LabelResourceKeyPresentInExplicitResources_ReturnsValid() {
		string body = BuildDiffBackedPageBody(
			"""
				[
					{
						"operation":"insert",
						"name":"UsrName",
						"values":
							{
								"type":"crt.Input",
								"label":"$Resources.Strings.PDS_UsrName",
								"control":"$UsrName"
							}
					}
				]
			""",
			"""
				[
					{
						"operation":"merge",
						"values":{"UsrName":{"modelConfig":{"path":"PDS.UsrName"}}}
					}
				]
			""");

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
			"""
				[
					{
						"operation":"insert",
						"name":"UsrName",
						"values":
							{
								"type":"crt.Input",
								"label":"$Resources.Strings.PDS_UsrName",
								"control":"$UsrName"
							}
					}
				]
			""",
			"""
				[
					{
						"operation":"merge",
						"values":{"UsrName":{"modelConfig":{"path":"PDS.UsrName"}}}
					}
				]
			""");

		var result = SchemaValidationService.ValidateStandardFieldBindings(body);

		result.IsValid.Should().BeTrue("because without explicit resources the validator cannot determine if the key is registered on the site");
		result.Errors.Should().BeEmpty();
		result.Warnings.Should().BeEmpty();
	}

	[Test]
	[Description("Insert of a new field control without a matching viewModelConfigDiff attribute fails — without the attribute declaration the control has no data source at runtime.")]
	public void ValidateInsertedFieldSelfConsistency_InsertWithoutViewModelAttribute_ReturnsInvalid() {
		string body = BuildDiffBackedPageBody(
			"""
				[
					{
						"operation":"insert",
						"name":"UsrEstimatedMinutes",
						"values":
							{
								"type":"crt.NumberInput",
								"label":"$Resources.Strings.PDS_UsrEstimatedMinutes",
								"control":"$PDS_UsrEstimatedMinutes"
							}
					}
				]
			""",
			"[]");

		var result = SchemaValidationService.ValidateInsertedFieldSelfConsistency(body);

		result.IsValid.Should().BeFalse("because the inserted control binds to an attribute that the body never declares — the field would have no data source at runtime");
		result.Errors.Should().Contain(error =>
			error.Contains("UsrEstimatedMinutes") &&
			error.Contains("PDS_UsrEstimatedMinutes") &&
			error.Contains("viewModelConfigDiff"),
			"because the diagnostic should name the broken field, the missing attribute, and the section that needs to be updated");
	}

	[Test]
	[Description("Insert of a new field control with a label resource that is neither auto-provided nor passed in 'resources' fails — without a resolvable resource the field renders with a blank caption.")]
	public void ValidateInsertedFieldSelfConsistency_InsertWithUnregisteredLabelResource_ReturnsInvalid() {
		string body = BuildDiffBackedPageBody(
			"""
				[
					{
						"operation":"insert",
						"name":"UsrContactPhone",
						"values":
							{
								"type":"crt.PhoneInput",
								"label":"$Resources.Strings.PDS_UsrContactPhone",
								"control":"$UsrContactPhone"
							}
					}
				]
			""",
			"""
				[
					{
						"operation":"merge",
						"values":{"UsrContactPhone":{"modelConfig":{"path":"PDS.UsrContactPhone"}}}
					}
				]
			""");

		var result = SchemaValidationService.ValidateInsertedFieldSelfConsistency(body);

		result.IsValid.Should().BeFalse("because the label points to a resource key that is neither auto-provided (the binding attribute name differs) nor passed in 'resources'");
		result.Errors.Should().Contain(error =>
			error.Contains("UsrContactPhone") &&
			error.Contains("PDS_UsrContactPhone") &&
			error.Contains("render blank"),
			"because the diagnostic should name the broken field, the missing resource key, and what will go wrong at runtime");
	}

	[Test]
	[Description("Insert of a new field using the Designer format (path:[], values.attributes nesting) with the label resource keyed by the BINDING ATTRIBUTE NAME is accepted — the platform auto-provides the caption from the DS-bound entity column. This is the form the Designer emits (label key == control attribute name).")]
	public void ValidateInsertedFieldSelfConsistency_InsertWithAutoProvidedLabel_ReturnsValid() {
		string body = BuildDiffBackedPageBody(
			"""
				[
					{
						"operation":"insert",
						"name":"UsrEstimatedMinutes",
						"values":
							{
								"type":"crt.NumberInput",
								"label":"$Resources.Strings.PDS_UsrEstimatedMinutes",
								"control":"$PDS_UsrEstimatedMinutes"
							}
					}
				]
			""",
			"""
				[
					{
						"operation":"merge",
						"path":[],
						"values":{"attributes":{"PDS_UsrEstimatedMinutes":{"modelConfig":{"path":"PDS.UsrEstimatedMinutes"}}}}
					}
				]
			""");

		var result = SchemaValidationService.ValidateInsertedFieldSelfConsistency(body);

		result.IsValid.Should().BeTrue("because the label key equals the DS-bound binding attribute 'PDS_UsrEstimatedMinutes', so the platform auto-provides the caption from the entity column");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("VERIFY: real Designer output uses the ATTRIBUTE-NAME label form ($Resources.Strings.AccountDS_Name_ud92nhf), not the column code, with NO explicit resource — the platform auto-provides it from the DS-bound column caption. This must be accepted.")]
	public void ValidateInsertedFieldSelfConsistency_AttributeNameLabelForm_AutoProvided_ReturnsValid() {
		string body = BuildDiffBackedPageBody(
			"""
				[
					{
						"operation":"insert",
						"name":"Input_zl5k81v",
						"values":{"type":"crt.Input","label":"$Resources.Strings.AccountDS_Name_ud92nhf","control":"$AccountDS_Name_ud92nhf"}
					}
				]
			""",
			"""
				[
					{
						"operation":"merge",
						"path":[],
						"values":{"attributes":{"AccountDS_Name_ud92nhf":{"modelConfig":{"path":"AccountDS.Name"}}}}
					}
				]
			""");

		var result = SchemaValidationService.ValidateInsertedFieldSelfConsistency(body);

		result.IsValid.Should().BeTrue("because the real Designer emits the attribute-name label form which the platform auto-provides from the DS-bound column caption");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Custom 'Title on page': when the user overrides a field's title, the Designer emits a #ResourceString({component}_label)# macro label and registers the resource explicitly. The macro form is not a reactive $Resources.Strings.* binding, so the auto-provide check does not apply — the inserted field with a properly-nested binding must be accepted.")]
	public void ValidateInsertedFieldSelfConsistency_CustomTitleMacroLabelForm_ReturnsValid() {
		string body = BuildDiffBackedPageBody(
			"""
				[
					{
						"operation":"insert",
						"name":"Input_zl5k81v",
						"values":{"type":"crt.Input","label":"#ResourceString(Input_zl5k81v_label)#","control":"$AccountDS_Name_ud92nhf"}
					}
				]
			""",
			"""
				[
					{
						"operation":"merge",
						"path":[],
						"values":{"attributes":{"AccountDS_Name_ud92nhf":{"modelConfig":{"path":"AccountDS.Name"}}}}
					}
				]
			""");

		var result = SchemaValidationService.ValidateInsertedFieldSelfConsistency(body);

		result.IsValid.Should().BeTrue("because the custom-title macro label form is not a reactive binding subject to the auto-provide check, and the field binding is properly nested");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Insert whose label key does NOT match the binding attribute and is not registered is rejected — only '$Resources.Strings.{bindingAttribute}' is auto-provided for a DS-bound attribute, so a mismatched key would render blank.")]
	public void ValidateInsertedFieldSelfConsistency_LabelKeyNotMatchingBindingAndNoResource_ReturnsInvalid() {
		string body = BuildDiffBackedPageBody(
			"""
				[
					{
						"operation":"insert",
						"name":"UsrCompleted",
						"values":
							{
								"type":"crt.Checkbox",
								"label":"$Resources.Strings.SomeUnrelatedCaption",
								"control":"$PDS_UsrCompleted"
							}
					}
				]
			""",
			"""
				[
					{
						"operation":"merge",
						"path":[],
						"values":{"attributes":{"PDS_UsrCompleted":{"modelConfig":{"path":"PDS.UsrCompleted"}}}}
					}
				]
			""");

		var result = SchemaValidationService.ValidateInsertedFieldSelfConsistency(body);

		result.IsValid.Should().BeFalse("because the label key 'SomeUnrelatedCaption' is neither the binding attribute 'PDS_UsrCompleted' (which would be auto-provided) nor registered in resources");
		result.Errors.Should().ContainSingle(error =>
			error.Contains("SomeUnrelatedCaption") &&
			error.Contains("render blank"),
			"because the diagnostic should name the unresolved resource key and what will go wrong at runtime");
	}

	[Test]
	[Description("Insert of a new field control using Designer format with the label resource passed in 'resources' is accepted.")]
	public void ValidateInsertedFieldSelfConsistency_InsertWithExplicitLabelResource_ReturnsValid() {
		string body = BuildDiffBackedPageBody(
			"""
				[
					{
						"operation":"insert",
						"name":"UsrContactPhone",
						"values":
							{
								"type":"crt.PhoneInput",
								"label":"$Resources.Strings.PDS_UsrContactPhone",
								"control":"$PDS_UsrContactPhone"
							}
					}
				]
			""",
			"""
				[
					{
						"operation":"merge",
						"path":[],
						"values":{"attributes":{"PDS_UsrContactPhone":{"modelConfig":{"path":"PDS.UsrContactPhone"}}}}
					}
				]
			""");

		var result = SchemaValidationService.ValidateInsertedFieldSelfConsistency(
			body,
			new Dictionary<string, string> { ["PDS_UsrContactPhone"] = "Contact phone" });

		result.IsValid.Should().BeTrue("because the resource key is explicitly registered through the 'resources' parameter");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Merge (not insert) operations are tolerated — the validator only enforces self-consistency for newly inserted controls, because parent schemas may legitimately provide the attribute and resource for merge operations.")]
	public void ValidateInsertedFieldSelfConsistency_MergeOperationWithoutDeclaration_ReturnsValid() {
		string body = BuildDiffBackedPageBody(
			"""
				[
					{
						"operation":"merge",
						"name":"UsrName",
						"values":
							{
								"type":"crt.Input",
								"label":"$Resources.Strings.SomeUnrelatedResource",
								"control":"$UsrInheritedField"
							}
					}
				]
			""",
			"[]");

		var result = SchemaValidationService.ValidateInsertedFieldSelfConsistency(body);

		result.IsValid.Should().BeTrue("because the merge operation may target an existing field whose attribute and resource are provided by the parent schema");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Multiple insert entries with distinct violations each surface their own error instead of stopping after the first — keeps the agent off the one-error-per-attempt remediation treadmill.")]
	public void ValidateInsertedFieldSelfConsistency_MultipleInvalidInserts_ReturnsAllErrors() {
		string body = BuildDiffBackedPageBody(
			"""
				[
					{
						"operation":"insert",
						"name":"UsrCompleted",
						"values":
							{
								"type":"crt.Checkbox",
								"label":"$Resources.Strings.PDS_UsrCompleted",
								"control":"$PDS_UsrCompleted"
							}
					},
					{
						"operation":"insert",
						"name":"UsrCompletionComment",
						"values":
							{
								"type":"crt.Input",
								"label":"$Resources.Strings.PDS_UsrCompletionComment",
								"control":"$PDS_UsrCompletionComment"
							}
					}
				]
			""",
			"[]");

		var result = SchemaValidationService.ValidateInsertedFieldSelfConsistency(body);

		result.IsValid.Should().BeFalse(
			"because each insert is missing its own viewModelConfigDiff declaration and label resource");
		result.Errors.Should().Contain(e => e.Contains("UsrCompleted") && e.Contains("PDS_UsrCompleted"),
			"because the first insert must surface its own diagnostic naming the offending field and binding attribute");
		result.Errors.Should().Contain(e => e.Contains("UsrCompletionComment") && e.Contains("PDS_UsrCompletionComment"),
			"because the second insert must surface its own diagnostic instead of being masked by the first error");
	}

	[Test]
	[Description("Insert of a non-field component (crt.Button) does not invoke the inserted-field contract — only standard field component types are gated.")]
	public void ValidateInsertedFieldSelfConsistency_NonFieldComponentInsert_ReturnsValid() {
		string body = BuildDiffBackedPageBody(
			"""
				[
					{
						"operation":"insert",
						"name":"UsrSaveButton",
						"values":
							{
								"type":"crt.Button",
								"caption":"$Resources.Strings.UsrSaveButton_caption",
								"clicked":{"request":"usr.SaveRequest"}
							}
					}
				]
			""",
			"[]");

		var result = SchemaValidationService.ValidateInsertedFieldSelfConsistency(body);

		result.IsValid.Should().BeTrue(
			"because the inserted-field contract only applies to data-source-bound standard field component types listed in StandardFieldComponentTypes");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Insert of a standard field component with no control binding is skipped silently — there is no binding to cross-check against viewModelConfigDiff.")]
	public void ValidateInsertedFieldSelfConsistency_FieldInsertWithoutControlBinding_ReturnsValid() {
		string body = BuildDiffBackedPageBody(
			"""
				[
					{
						"operation":"insert",
						"name":"UsrDecorative",
						"values":
							{
								"type":"crt.Input"
							}
					}
				]
			""",
			"[]");

		var result = SchemaValidationService.ValidateInsertedFieldSelfConsistency(body);

		result.IsValid.Should().BeTrue(
			"because the validator only checks bindings and labels when they are actually present on the inserted control");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Real Designer output for an unbound input: control is an empty string and the label is a #ResourceString macro with an explicitly registered resource. The empty control means there is no binding to cross-check, so the field must be skipped (no false-positive) — even with no viewModelConfigDiff entry.")]
	public void ValidateInsertedFieldSelfConsistency_UnboundInputEmptyControl_ReturnsValid() {
		string body = BuildDiffBackedPageBody(
			"""
				[
					{
						"operation":"insert",
						"name":"Input_y4u48sv",
						"values":
							{
								"type":"crt.Input",
								"label":"#ResourceString(Input_y4u48sv_label)#",
								"control":"",
								"multiline":false
							}
					}
				]
			""",
			"[]");

		var result = SchemaValidationService.ValidateInsertedFieldSelfConsistency(body);

		result.IsValid.Should().BeTrue("because an unbound input (empty control) has no data binding to validate and its macro label is not a reactive auto-provide candidate");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Legacy path:[\"attributes\"] DS-bound field with the attribute-name label form, mirroring real Designer output (Contact.Full name -> ContactDS_Name_xxx). Both the legacy nesting and the attribute-name auto-provide must be accepted.")]
	public void ValidateInsertedFieldSelfConsistency_LegacyPathAttributes_DsBoundInput_ReturnsValid() {
		string body = BuildDiffBackedPageBody(
			"""
				[
					{
						"operation":"insert",
						"name":"Input_8zo0uzp",
						"values":
							{
								"type":"crt.Input",
								"label":"$Resources.Strings.ContactDS_Name_dtjv2lx",
								"control":"$ContactDS_Name_dtjv2lx"
							}
					}
				]
			""",
			"""
				[
					{
						"operation":"merge",
						"path":["attributes"],
						"values":{"ContactDS_Name_dtjv2lx":{"modelConfig":{"path":"ContactDS.Name"}}}
					}
				]
			""");

		var result = SchemaValidationService.ValidateInsertedFieldSelfConsistency(body);

		result.IsValid.Should().BeTrue("because the legacy path:[\"attributes\"] form is properly nested and the label key equals the DS-bound binding attribute (auto-provided)");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Empty body or whitespace-only body is tolerated — the validator returns valid without throwing so it can be chained behind earlier syntactic checks.")]
	public void ValidateInsertedFieldSelfConsistency_EmptyBody_ReturnsValid() {
		var emptyResult = SchemaValidationService.ValidateInsertedFieldSelfConsistency(string.Empty);
		var whitespaceResult = SchemaValidationService.ValidateInsertedFieldSelfConsistency("   ");

		emptyResult.IsValid.Should().BeTrue("because an empty body has no inserts to validate");
		emptyResult.Errors.Should().BeEmpty();
		whitespaceResult.IsValid.Should().BeTrue("because a whitespace body has no inserts to validate");
		whitespaceResult.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Flat viewModelConfigDiff (no path property, attribute directly in values) is rejected because the attribute lands at viewModelConfig root instead of viewModelConfig.attributes and the Freedom UI runtime ignores it — controls render but bind no data.")]
	public void ValidateInsertedFieldSelfConsistency_FlatViewModelConfigDiff_ReturnsInvalid() {
		string body = BuildDiffBackedPageBody(
			"""
				[
					{
						"operation":"insert",
						"name":"UsrEstimatedMinutes",
						"values":
							{
								"type":"crt.NumberInput",
								"label":"$Resources.Strings.PDS_UsrEstimatedMinutes",
								"control":"$PDS_UsrEstimatedMinutes"
							}
					}
				]
			""",
			"""
				[
					{
						"operation":"merge",
						"values":{"PDS_UsrEstimatedMinutes":{"modelConfig":{"path":"PDS.UsrEstimatedMinutes"}}}
					}
				]
			""");

		var result = SchemaValidationService.ValidateInsertedFieldSelfConsistency(body);

		result.IsValid.Should().BeFalse("because the flat form (no 'path':[], attribute directly in values) passes the platform save but fails at runtime — the attribute lands at viewModelConfig root instead of viewModelConfig.attributes");
		result.Errors.Should().ContainSingle(error =>
			error.Contains("PDS_UsrEstimatedMinutes") &&
			error.Contains("required nesting") &&
			error.Contains("read and write no data"),
			"because the diagnostic should explain that the flat declaration form will not bind data at runtime");
	}

	[Test]
	[Description("Designer format: viewModelConfigDiff entry uses path:[] with values.attributes nesting. The attribute must be recognised so validation passes for a correctly-declared field with an auto-provided label.")]
	public void ValidateInsertedFieldSelfConsistency_DesignerFormat_PathEmptyAttributesNested_ReturnsValid() {
		string body = BuildDiffBackedPageBody(
			"""
				[
					{
						"operation":"insert",
						"name":"UsrEstimatedMinutes",
						"values":
							{
								"type":"crt.NumberInput",
								"label":"$Resources.Strings.PDS_UsrEstimatedMinutes",
								"control":"$PDS_UsrEstimatedMinutes"
							}
					}
				]
			""",
			"""
				[
					{
						"operation":"merge",
						"path":[],
						"values":{
							"attributes":{
								"PDS_UsrEstimatedMinutes":{
									"modelConfig":{"path":"PDS.UsrEstimatedMinutes"}
								}
							}
						}
					}
				]
			""");

		var result = SchemaValidationService.ValidateInsertedFieldSelfConsistency(body);

		result.IsValid.Should().BeTrue(
			"because the attribute is declared in the Designer format (path:[], values.attributes nesting) and the label key 'UsrEstimatedMinutes' matches the column code so the platform auto-provides the caption");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Designer format with full attribute name as resource key — must pass when the explicit resources parameter covers the key.")]
	public void ValidateInsertedFieldSelfConsistency_DesignerFormat_ExplicitResourceForFullAttributeName_ReturnsValid() {
		string body = BuildDiffBackedPageBody(
			"""
				[
					{
						"operation":"insert",
						"name":"UsrEstimatedMinutes",
						"values":
							{
								"type":"crt.NumberInput",
								"label":"$Resources.Strings.PDS_UsrEstimatedMinutes",
								"control":"$PDS_UsrEstimatedMinutes"
							}
					}
				]
			""",
			"""
				[
					{
						"operation":"merge",
						"path":[],
						"values":{
							"attributes":{
								"PDS_UsrEstimatedMinutes":{
									"modelConfig":{"path":"PDS.UsrEstimatedMinutes"}
								}
							}
						}
					}
				]
			""");

		var result = SchemaValidationService.ValidateInsertedFieldSelfConsistency(
			body,
			new Dictionary<string, string> { ["PDS_UsrEstimatedMinutes"] = "Estimated minutes" });

		result.IsValid.Should().BeTrue("because 'PDS_UsrEstimatedMinutes' is explicitly registered in the resources parameter");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Designer format with the label keyed by the full PDS_-prefixed attribute name and no explicit resource is accepted — the label key equals the DS-bound binding attribute, so the platform auto-provides the caption. (Auto-provide is keyed by attribute name, not column code.)")]
	public void ValidateInsertedFieldSelfConsistency_DesignerFormat_PdsUnderscoreLabelMatchingBinding_ReturnsValid() {
		string body = BuildDiffBackedPageBody(
			"""
				[
					{
						"operation":"insert",
						"name":"UsrEstimatedMinutes",
						"values":
							{
								"type":"crt.NumberInput",
								"label":"$Resources.Strings.PDS_UsrEstimatedMinutes",
								"control":"$PDS_UsrEstimatedMinutes"
							}
					}
				]
			""",
			"""
				[
					{
						"operation":"merge",
						"path":[],
						"values":{
							"attributes":{
								"PDS_UsrEstimatedMinutes":{
									"modelConfig":{"path":"PDS.UsrEstimatedMinutes"}
								}
							}
						}
					}
				]
			""");

		var result = SchemaValidationService.ValidateInsertedFieldSelfConsistency(body);

		result.IsValid.Should().BeTrue("because the label key 'PDS_UsrEstimatedMinutes' equals the DS-bound binding attribute, so the platform auto-provides the caption");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Static viewModelConfig form (FormPage created by create-app, SCHEMA_VIEW_MODEL_CONFIG marker): attribute declared under viewModelConfig.attributes is accepted — this is the replace-mode path for such pages.")]
	public void ValidateInsertedFieldSelfConsistency_StaticViewModelConfig_AttributeUnderAttributes_ReturnsValid() {
		// Static form uses viewModelConfig (not viewModelConfigDiff). Attribute must be under .attributes.
		string viewConfigDiff = """
			[
				{
					"operation":"insert",
					"name":"UsrEstimatedMinutes",
					"values":
						{
							"type":"crt.NumberInput",
							"label":"$Resources.Strings.UsrEstimatedMinutes",
							"control":"$UsrEstimatedMinutes"
						}
				}
			]
		""";
		string viewModelConfig = """
			{
				"attributes": {
					"UsrEstimatedMinutes": {
						"modelConfig": { "path": "PDS.UsrEstimatedMinutes" }
					}
				}
			}
		""";
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig);

		var result = SchemaValidationService.ValidateInsertedFieldSelfConsistency(body);

		result.IsValid.Should().BeTrue(
			"because the attribute is declared in static viewModelConfig.attributes with a valid modelConfig.path, and the label key 'UsrEstimatedMinutes' matches the column code for auto-provide");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Static viewModelConfig form: a binding attribute placed at the viewModelConfig ROOT (a sibling of .attributes) instead of under .attributes is NOT recognised as declared, so the inserted field is rejected. This is the static-body analogue of the ENG-90846 runtime symptom where new attributes ended up at the viewModelConfig root (sibling of attributes) and the Freedom UI runtime ignored them — the controls rendered with no data.")]
	public void ValidateInsertedFieldSelfConsistency_StaticViewModelConfig_AttributeAtRootNotUnderAttributes_ReturnsInvalid() {
		string viewConfigDiff = """
			[
				{
					"operation":"insert",
					"name":"UsrEstimatedMinutes",
					"values":{"type":"crt.NumberInput","control":"$UsrEstimatedMinutes"}
				}
			]
		""";
		// Attribute sits at the viewModelConfig root (sibling of "attributes"), NOT under .attributes —
		// the form the runtime ignores.
		string viewModelConfig = """
			{
				"attributes": {},
				"UsrEstimatedMinutes": { "modelConfig": { "path": "PDS.UsrEstimatedMinutes" } }
			}
		""";
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig);

		var result = SchemaValidationService.ValidateInsertedFieldSelfConsistency(body);

		result.IsValid.Should().BeFalse(
			"because an attribute at the viewModelConfig root (not under .attributes) is ignored by the runtime, so the control would render with no data");
		result.Errors.Should().ContainSingle(error =>
			error.Contains("UsrEstimatedMinutes") &&
			error.Contains("does not declare attribute"));
	}

	[Test]
	[Description("Static viewModelConfig form: an inserted field whose binding attribute is declared nowhere (empty attributes map) is rejected with the binding-declaration diagnostic — the static-form counterpart of the diff-form InsertWithoutViewModelAttribute case.")]
	public void ValidateInsertedFieldSelfConsistency_StaticViewModelConfig_AttributeMissing_ReturnsInvalid() {
		string viewConfigDiff = """
			[
				{
					"operation":"insert",
					"name":"UsrEstimatedMinutes",
					"values":{"type":"crt.NumberInput","control":"$UsrEstimatedMinutes"}
				}
			]
		""";
		string viewModelConfig = """
			{
				"attributes": {}
			}
		""";
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig);

		var result = SchemaValidationService.ValidateInsertedFieldSelfConsistency(body);

		result.IsValid.Should().BeFalse(
			"because the static viewModelConfig.attributes map does not declare the bound attribute, so the control would have no data source");
		result.Errors.Should().ContainSingle(error =>
			error.Contains("UsrEstimatedMinutes") &&
			error.Contains("does not declare attribute"));
	}

	[Test]
	[Description("Legacy diff format path:[\"attributes\"] (older platform form, values IS the attributes container) is recognised as properly-nested and accepted by ValidateInsertedFieldSelfConsistency.")]
	public void ValidateInsertedFieldSelfConsistency_LegacyPathAttributesFormat_ReturnsValid() {
		string body = BuildDiffBackedPageBody(
			"""
				[
					{
						"operation":"insert",
						"name":"UsrEstimatedMinutes",
						"values":
							{
								"type":"crt.NumberInput",
								"label":"$Resources.Strings.PDS_UsrEstimatedMinutes",
								"control":"$PDS_UsrEstimatedMinutes"
							}
					}
				]
			""",
			"""
				[
					{
						"operation":"merge",
						"path":["attributes"],
						"values":{"PDS_UsrEstimatedMinutes":{"modelConfig":{"path":"PDS.UsrEstimatedMinutes"}}}
					}
				]
			""");

		var result = SchemaValidationService.ValidateInsertedFieldSelfConsistency(body);

		result.IsValid.Should().BeTrue(
			"because path:[\"attributes\"] is the older properly-nested form (attributes reach viewModelConfig.attributes) and must be accepted");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Designer format (path:[], values.attributes) with multiple attributes in a single entry — both controls must be recognised and validation must pass.")]
	public void ValidateInsertedFieldSelfConsistency_DesignerFormat_MultipleAttributesInOneEntry_ReturnsValid() {
		string body = BuildDiffBackedPageBody(
			"""
				[
					{
						"operation":"insert",
						"name":"UsrA",
						"values":{"type":"crt.Input","label":"$Resources.Strings.PDS_UsrA","control":"$PDS_UsrA"}
					},
					{
						"operation":"insert",
						"name":"UsrB",
						"values":{"type":"crt.NumberInput","label":"$Resources.Strings.PDS_UsrB","control":"$PDS_UsrB"}
					}
				]
			""",
			"""
				[
					{
						"operation":"merge",
						"path":[],
						"values":{
							"attributes":{
								"PDS_UsrA":{"modelConfig":{"path":"PDS.UsrA"}},
								"PDS_UsrB":{"modelConfig":{"path":"PDS.UsrB"}}
							}
						}
					}
				]
			""");

		var result = SchemaValidationService.ValidateInsertedFieldSelfConsistency(body);

		result.IsValid.Should().BeTrue("because both attributes are declared in a single Designer-format path:[] entry and labels use auto-provided column codes");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Case-collision regression guard: a properly-nested attribute and a flat attribute differing ONLY in case must NOT collapse. properlyNestedAttributes is Ordinal (runtime keys case-exact), so the flat-form 'pds_usrx' is still rejected even though a nested 'PDS_USRX' exists.")]
	public void ValidateInsertedFieldSelfConsistency_CaseCollidingNestedAndFlat_RejectsFlat() {
		string body = BuildDiffBackedPageBody(
			"""
				[
					{
						"operation":"insert",
						"name":"FieldLower",
						"values":{"type":"crt.Input","label":"$Resources.Strings.UsrX","control":"$pds_usrx"}
					}
				]
			""",
			"""
				[
					{
						"operation":"merge",
						"path":[],
						"values":{"attributes":{"PDS_USRX":{"modelConfig":{"path":"PDS.UsrX"}}}}
					},
					{
						"operation":"merge",
						"values":{"pds_usrx":{"modelConfig":{"path":"PDS.UsrX"}}}
					}
				]
			""");

		var result = SchemaValidationService.ValidateInsertedFieldSelfConsistency(body);

		result.IsValid.Should().BeFalse(
			"because the flat 'pds_usrx' lands at viewModelConfig root and must not be masked by the case-different nested 'PDS_USRX'");
		result.Errors.Should().ContainSingle(e => e.Contains("pds_usrx") && e.Contains("without the required nesting"));
	}

	[Test]
	[Description("Multi-segment path:[\"attributes\",\"X\"] must NOT be treated as an attributes container — for that path `values` is the attribute body, not an attribute map. The classifier rejects it, so a control binding the targeted attribute is reported as not declared rather than silently accepting body keys (e.g. 'modelConfig') as attribute names.")]
	public void ValidateInsertedFieldSelfConsistency_MultiSegmentAttributesPath_NotTreatedAsContainer() {
		string body = BuildDiffBackedPageBody(
			"""
				[
					{
						"operation":"insert",
						"name":"FieldX",
						"values":{"type":"crt.Input","label":"$Resources.Strings.UsrX","control":"$PDS_UsrX"}
					}
				]
			""",
			"""
				[
					{
						"operation":"merge",
						"path":["attributes","PDS_UsrX"],
						"values":{"modelConfig":{"path":"PDS.UsrX"}}
					}
				]
			""");

		var result = SchemaValidationService.ValidateInsertedFieldSelfConsistency(body);

		result.IsValid.Should().BeFalse("because a multi-segment attributes path is not a valid attribute-map container");
		result.Errors.Should().ContainSingle(e => e.Contains("PDS_UsrX") && e.Contains("does not declare attribute"),
			"because PDS_UsrX is not collected (the body keys under a 2-segment path are not attribute names) — and the spurious 'modelConfig' must not be accepted as an attribute either");
	}

	[Test]
	[Description("A remove operation must not contribute declared attribute names. An insert binding an attribute that is only 'declared' by a remove entry is reported as not declared.")]
	public void ValidateInsertedFieldSelfConsistency_RemoveOperationDoesNotDeclareAttribute_ReturnsInvalid() {
		string body = BuildDiffBackedPageBody(
			"""
				[
					{
						"operation":"insert",
						"name":"FieldFoo",
						"values":{"type":"crt.Input","label":"$Resources.Strings.UsrFoo","control":"$PDS_UsrFoo"}
					}
				]
			""",
			"""
				[
					{
						"operation":"remove",
						"path":["attributes"],
						"values":{"PDS_UsrFoo":{"modelConfig":{"path":"PDS.UsrFoo"}}}
					}
				]
			""");

		var result = SchemaValidationService.ValidateInsertedFieldSelfConsistency(body);

		result.IsValid.Should().BeFalse("because a remove operation deletes the attribute and must not make a binding to it appear valid");
		result.Errors.Should().Contain(e => e.Contains("PDS_UsrFoo") && e.Contains("does not declare attribute"));
	}

	[Test]
	[Description("Crash guard: a SCHEMA_VIEW_MODEL_CONFIG block that parses as a non-object (e.g. an array) must not throw — System.Text.Json TryGetProperty throws on non-objects, so EnumerateAttributesContainers guards ValueKind first. Validation returns a result instead of crashing.")]
	public void ValidateInsertedFieldSelfConsistency_NonObjectStaticViewModelConfig_DoesNotThrow() {
		// Static viewModelConfig marker holds an array instead of an object — malformed but must not crash.
		string body = BuildStaticViewModelConfigPageBody(
			"""
				[
					{
						"operation":"insert",
						"name":"FieldX",
						"values":{"type":"crt.Input","label":"$Resources.Strings.UsrX","control":"$UsrX"}
					}
				]
			""",
			"[]");

		Action act = () => SchemaValidationService.ValidateInsertedFieldSelfConsistency(body);

		act.Should().NotThrow("because EnumerateAttributesContainers guards ValueKind before TryGetProperty");
	}

	[Test]
	[Description("Designer format (path:[], values.attributes) with a validator using reactive binding in a param — the param binding rule must apply regardless of the viewModelConfigDiff format.")]
	public void ValidateValidatorParamResourceBindings_DesignerFormat_ReactiveBindingInValidatorParam_ReturnsInvalid() {
		string viewModelConfigDiff = """
			[
				{
					"operation":"merge",
					"path":[],
					"values":{
						"attributes":{
							"UsrName":{
								"modelConfig":{"path":"PDS.UsrName"},
								"validators":{
									"Upper":{
										"type":"usr.Upper",
										"params":{"message":"$Resources.Strings.UsrMsg"}
									}
								}
							}
						}
					}
				}
			]
		""";
		string body = BuildDiffBackedPageBody("[]", viewModelConfigDiff);

		var result = SchemaValidationService.ValidateValidatorParamResourceBindings(body);

		result.IsValid.Should().BeFalse("because the reactive binding $Resources.Strings.* is not valid in validator params — must use #ResourceString()# — and the path:[] Designer format does not exempt this rule");
		result.Errors.Should().ContainSingle(e => e.Contains("#ResourceString(UsrMsg)#"));
	}

	[Test]
	[Description("path:[] entry where attribute is directly in values (no 'attributes' sub-object) must produce an error — " +
	             "the attribute falls through all collection passes and is reported as undeclared. " +
	             "Known approximation: the message says 'not declared' rather than 'wrong nesting', but the save is correctly blocked.")]
	public void ValidateInsertedFieldSelfConsistency_RootPathWithAttributeDirectlyInValues_IsRejected() {
		string body = BuildDiffBackedPageBody(
			"""
				[
					{
						"operation":"insert",
						"name":"UsrX",
						"values":{"type":"crt.Input","label":"$Resources.Strings.UsrX","control":"$PDS_UsrX"}
					}
				]
			""",
			"""
				[
					{
						"operation":"merge",
						"path":[],
						"values":{"PDS_UsrX":{"modelConfig":{"path":"PDS.UsrX"}}}
					}
				]
			""");

		var result = SchemaValidationService.ValidateInsertedFieldSelfConsistency(body);

		result.IsValid.Should().BeFalse(
			"because path:[] with attribute directly in values (no 'attributes' sub-object) is not recognised " +
			"by any collection path and the save is blocked — the diagnostic says 'not declared' rather than 'wrong nesting' " +
			"(known approximation; the fix is correct nesting under values.attributes)");
		result.Errors.Should().ContainSingle(e => e.Contains("PDS_UsrX") && e.Contains("does not declare attribute"),
			"because the undeclared-attribute message is produced since the attribute is not found in any valid collection pass");
	}

	[Test]
	[Description("Standard field binding to the declared validator attribute is accepted")]
	public void ValidateStandardFieldBindings_AttributeWithValidators_ViewModelBindingIsAllowed() {
		// Arrange — UsrName has a validator in viewModelConfig; control binds to the same declared attribute.
		string viewConfigDiff = """
			[
				{
					"operation":"insert",
					"name":"UsrName",
					"values":
						{
							"type":"crt.Input",
							"label":"$Resources.Strings.UsrName",
							"control":"$UsrName"
						}
				}
			]
		""";
		string viewModelConfig = """
			{
				"attributes": {
					"UsrName": {
						"modelConfig":{"path":"PDS.UsrName"},
						"validators":
							{
								"UpperCase": {
									"type":"usr.UpperCase",
									"params":{"message":"$Resources.Strings.UsrUpperCaseValidator_Message"}
								}
							}
					}
				}
			}
		""";
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig);

		// Act
		var result = SchemaValidationService.ValidateStandardFieldBindings(body);

		// Assert
		result.IsValid.Should().BeTrue("because the control uses the same declared attribute that carries the validators");
		result.Errors.Should().NotContain(error => error.Contains("$UsrName"),
			"because the declared validator attribute binding is the expected configuration");
	}

	[Test]
	[Description("Standard field binding to attribute not in current schema is valid even without validators — attribute may be declared in a parent schema")]
	public void ValidateStandardFieldBindings_AttributeWithoutValidators_BindingToParentAttributeIsAllowed() {
		// Arrange — UsrStatus declared in viewModelConfig; control binds to UsrStatusField which may be in parent schema.
		string viewConfigDiff = """
			[
				{
					"operation":"insert",
					"name":"UsrStatus",
					"values":
						{
							"type":"crt.ComboBox",
							"label":"$Resources.Strings.PDS_UsrStatus",
							"control":"$UsrStatusField"
						}
				}
			]
		""";
		string viewModelConfig = """
			{"attributes":{"UsrStatus":{"modelConfig":{"path":"PDS.UsrStatus"}}}}
		""";
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig);

		// Act
		var result = SchemaValidationService.ValidateStandardFieldBindings(body);

		// Assert
		result.IsValid.Should().BeTrue("because the attribute UsrStatusField may be declared in a parent schema");
		result.Errors.Should().BeEmpty("because binding to a parent-schema attribute is not an error");
	}

	[Test]
	[Description("Standard field populated by handlers may use view-model attribute binding $AttrName")]
	public void ValidateStandardFieldBindings_AttributeWrittenByHandlers_ViewModelBindingIsAllowed() {
		// Arrange — UsrName is populated from an init handler through $context.set("UsrName", ...).
		string viewConfigDiff = """
			[
				{
					"operation":"insert",
					"name":"UsrName",
					"values":
						{
							"type":"crt.Input",
							"label":"$Resources.Strings.UsrName",
							"control":"$UsrName"
						}
				}
			]
		""";
		string viewModelConfig = """
			{"attributes":{"UsrName":{"modelConfig":{"path":"PDS.UsrName"}}}}
		""";
		string handlers = """
			[
				{
					request: "crt.HandleViewModelInitRequest",
					handler:
						async (request, next) => {
							const result = await next?.handle(request);
							await request.$context.set("UsrName", "Primary currency");
							return result;
						}
				}
			]
		""";
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig, handlers);

		// Act
		var result = SchemaValidationService.ValidateStandardFieldBindings(body);

		// Assert
		result.IsValid.Should().BeTrue("because handler-driven writes target the same declared attribute that the control uses");
		result.Errors.Should().NotContain(error => error.Contains("$UsrName"),
			"because the handler-aware rule should allow the matching declared attribute binding");
	}

	[Test]
	[Description("Standard field populated by handlers must bind to the same declared attribute they update")]
	public void ValidateStandardFieldBindings_AttributeWrittenByHandlers_DifferentDeclaredAttributeIsRejected() {
		// Arrange — handlers update UsrName but the control stays on UsrNameField for the same model path.
		string viewConfigDiff = """
			[
				{
					"operation":"insert",
					"name":"UsrName",
					"values":
						{
							"type":"crt.Input",
							"label":"$Resources.Strings.UsrName",
							"control":"$UsrNameField"
						}
				}
			]
		""";
		string viewModelConfig = """
			{
				"attributes": {
					"UsrName":{"modelConfig":{"path":"PDS.UsrName"}},
					"UsrNameField":{"modelConfig":{"path":"PDS.UsrName"}}
				}
			}
		""";
		string handlers = """
			[
				{
					request: "crt.HandleViewModelInitRequest",
					handler:
						async (request, next) => {
							const { $context } = request;
							const result = await next?.handle(request);
							await $context.set("UsrName", "Primary currency");
							return result;
						}
				}
			]
		""";
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig, handlers);

		// Act
		var result = SchemaValidationService.ValidateStandardFieldBindings(body);

		// Assert
		result.IsValid.Should().BeFalse("because handler-driven writes should bind the UI control to the same declared attribute they update");
		result.Errors.Should().ContainSingle(error => error.Contains("$UsrNameField") && error.Contains("$UsrName") && error.Contains("$context.set"),
			"because the validation should explain that the control and the handler must use the same declared attribute");
	}

	[Test]
	[Description("Standard field mismatch reports all handler-written candidates that share the same model path")]
	public void ValidateStandardFieldBindings_AttributeWrittenByHandlers_MultipleDeclaredAlternatives_ReportsAllCandidates() {
		// Arrange
		string viewConfigDiff = """
			[
				{
					"operation":"insert",
					"name":"UsrName",
					"values":
						{
							"type":"crt.Input",
							"label":"$Resources.Strings.UsrName",
							"control":"$UsrNameField"
						}
				}
			]
		""";
		string viewModelConfig = """
			{
				"attributes": {
					"UsrName":{"modelConfig":{"path":"PDS.UsrName"}},
					"UsrNameField":{"modelConfig":{"path":"PDS.UsrName"}},
					"UsrNameSecondary":{"modelConfig":{"path":"PDS.UsrName"}}
				}
			}
		""";
		string handlers = """
			[
				{
					request: "crt.HandleViewModelInitRequest",
					handler:
						async (request, next) => {
							const result = await next?.handle(request);
							await request.$context.set("UsrName", "Primary currency");
							await request.$context.set("UsrNameSecondary", "Secondary currency");
							return result;
						}
				}
			]
		""";
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig, handlers);

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateStandardFieldBindings(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "the control still binds to a different declared attribute than the handler-updated attributes");
		result.Errors.Should().ContainSingle(error =>
				error.Contains("one of: UsrName, UsrNameSecondary") &&
				error.Contains("$UsrNameField"),
			because: "the diagnostic should list every matching declared attribute instead of picking one alphabetically");
	}

	[Test]
	[Description("An input placeholder set as an inline literal is rejected — user-visible text must be a localizable-string binding, not a plain string.")]
	public void ValidateLocalizableTextLiterals_PlaceholderInlineLiteral_ReturnsInvalid() {
		// Arrange
		string body = BuildDiffBackedPageBody(
			"""[{"operation":"insert","name":"EmailField","values":{"type":"crt.Input","control":"$Email","placeholder":"name@firm.com"}}]""",
			"[]");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateLocalizableTextLiterals(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "an inline placeholder literal is not localizable and must be rejected");
		result.Errors.Should().ContainSingle(error =>
				error.Contains("EmailField") &&
				error.Contains("placeholder") &&
				error.Contains("name@firm.com") &&
				error.Contains("page-schema-resources"),
			because: "the diagnostic must name the node, the offending property, the literal value, and point to the guide");
	}

	[Test]
	[Description("A panel/tab title set as an inline literal is rejected — titles are user-visible text and must be localizable.")]
	public void ValidateLocalizableTextLiterals_TitleInlineLiteral_ReturnsInvalid() {
		// Arrange
		string body = BuildDiffBackedPageBody(
			"""[{"operation":"insert","name":"DetailsPanel","values":{"type":"crt.ExpansionPanel","title":"Contact details"}}]""",
			"[]");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateLocalizableTextLiterals(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "an inline title literal is not localizable and must be rejected");
		result.Errors.Should().ContainSingle(error => error.Contains("DetailsPanel") && error.Contains("title"),
			because: "the diagnostic must name the node and the title property");
	}

	[Test]
	[Description("An inline literal on a child node nested under the inserted container is rejected — the scan recurses through the whole values subtree.")]
	public void ValidateLocalizableTextLiterals_NestedChildCaptionLiteral_ReturnsInvalid() {
		// Arrange
		string body = BuildDiffBackedPageBody(
			"""[{"operation":"insert","name":"Toolbar","values":{"type":"crt.Container","items":[{"name":"SaveButton","type":"crt.Button","caption":"Save record"}]}}]""",
			"[]");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateLocalizableTextLiterals(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "a caption literal on a nested child must be caught regardless of nesting depth");
		result.Errors.Should().ContainSingle(error => error.Contains("SaveButton") && error.Contains("caption"),
			because: "the diagnostic must attribute the literal to the nearest named node, not the container");
	}

	[Test]
	[Description("A label authored as a $Resources.Strings.* binding is accepted — only inline literals are rejected by the localizable-text check.")]
	public void ValidateLocalizableTextLiterals_ResourceBinding_ReturnsValid() {
		// Arrange
		string body = BuildDiffBackedPageBody(
			"""[{"operation":"insert","name":"EmailField","values":{"type":"crt.Input","control":"$Email","label":"$Resources.Strings.Email","placeholder":"$Resources.Strings.EmailPlaceholder"}}]""",
			"[]");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateLocalizableTextLiterals(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "values authored as $Resources.Strings.* bindings are already localizable");
		result.Errors.Should().BeEmpty(because: "no inline literal is present");
	}

	[Test]
	[Description("A caption authored with the #ResourceString()# macro is accepted — the macro form is the data-grid/validator localization convention.")]
	public void ValidateLocalizableTextLiterals_ResourceStringMacro_ReturnsValid() {
		// Arrange
		string body = BuildDiffBackedPageBody(
			"""[{"operation":"insert","name":"DataTable","values":{"type":"crt.DataGrid","columns":[{"caption":"#ResourceString(PDS_Name)#"}]}}]""",
			"[]");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateLocalizableTextLiterals(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "the #ResourceString()# macro is an accepted localizable form for grid column captions");
		result.Errors.Should().BeEmpty(because: "the macro form is not an inline literal");
	}

	[Test]
	[Description("A caption authored with the platform #MacrosTemplateString(#ResourceString(Key)#)# wrapper is accepted — it references a resource string, so it is localized even though the macro is wrapped. This is the dominant OOTB caption shape and must not be hard-rejected.")]
	public void ValidateLocalizableTextLiterals_MacrosTemplateStringWrappedResourceString_ReturnsValid() {
		// Arrange
		string body = BuildDiffBackedPageBody(
			"""[{"operation":"insert","name":"PageTitle","values":{"type":"crt.Label","caption":"#MacrosTemplateString(#ResourceString(PageTitle_caption)#)#"}}]""",
			"[]");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateLocalizableTextLiterals(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "a #ResourceString() macro wrapped in #MacrosTemplateString() still references a localizable resource");
		result.Errors.Should().BeEmpty(
			because: "the wrapped macro form is localized and must not be flagged as an inline literal");
	}

	[Test]
	[Description("A caption that concatenates a #ResourceString() macro with surrounding text is accepted — any value that references a resource string is treated as localized, not as a hardcoded literal.")]
	public void ValidateLocalizableTextLiterals_ResourceStringConcatenatedWithText_ReturnsValid() {
		// Arrange
		string body = BuildDiffBackedPageBody(
			"""[{"operation":"insert","name":"Header","values":{"type":"crt.Label","caption":"#MacrosTemplateString(#ResourceString(A_caption)# — #ResourceString(B_caption)#)#"}}]""",
			"[]");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateLocalizableTextLiterals(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "a value embedding one or more #ResourceString() macros references localizable resources");
		result.Errors.Should().BeEmpty(
			because: "an embedded resource reference is not a hardcoded inline literal");
	}

	[Test]
	[Description("A #MacrosTemplateString() wrapper that contains NO #ResourceString() macro is still rejected — without a resource reference the wrapped value is effectively hardcoded user-visible text.")]
	public void ValidateLocalizableTextLiterals_MacrosTemplateStringWithoutResourceString_ReturnsInvalid() {
		// Arrange
		string body = BuildDiffBackedPageBody(
			"""[{"operation":"insert","name":"Header","values":{"type":"crt.Label","caption":"#MacrosTemplateString(Hardcoded title)#"}}]""",
			"[]");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateLocalizableTextLiterals(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "a macro wrapper with no resource reference does not localize the text");
		result.Errors.Should().ContainSingle(error => error.Contains("Header") && error.Contains("caption"),
			because: "the hardcoded wrapped caption must still be reported as an inline literal");
	}

	[Test]
	[Description("The mobile variant also accepts the #MacrosTemplateString(#ResourceString(Key)#)# wrapper, mirroring the web rule so real mobile page bodies are not falsely rejected.")]
	public void ValidateMobileLocalizableTextLiterals_MacrosTemplateStringWrappedResourceString_ReturnsValid() {
		// Arrange
		const string body = """{"viewConfigDiff":[{"operation":"insert","name":"PageTitle","values":{"type":"crt.Label","caption":"#MacrosTemplateString(#ResourceString(PageTitle_caption)#)#"}}]}""";

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileLocalizableTextLiterals(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "the mobile localizable-text rule recognises the same wrapped macro form as the web rule");
		result.Errors.Should().BeEmpty(
			because: "the wrapped macro references a localizable resource and is not an inline literal");
	}

	[Test]
	[Description("A non-string placeholder (the boolean toggle some components expose) is ignored — only string text values are subject to the literal rule.")]
	public void ValidateLocalizableTextLiterals_NonStringPlaceholder_ReturnsValid() {
		// Arrange
		string body = BuildDiffBackedPageBody(
			"""[{"operation":"insert","name":"Combo","values":{"type":"crt.ComboBox","placeholder":false}}]""",
			"[]");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateLocalizableTextLiterals(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "a boolean placeholder is not user-visible text and must not be flagged");
		result.Errors.Should().BeEmpty(because: "non-string values are out of scope for the localizable-text check");
	}

	[Test]
	[Description("A placeholder bound to an attribute ($-prefixed expression) is accepted — binding expressions are not inline literals.")]
	public void ValidateLocalizableTextLiterals_AttributeBoundPlaceholder_ReturnsValid() {
		// Arrange
		string body = BuildDiffBackedPageBody(
			"""[{"operation":"insert","name":"Field","values":{"type":"crt.Input","placeholder":"$UsrPlaceholderText"}}]""",
			"[]");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateLocalizableTextLiterals(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "a $-prefixed binding expression resolves through the view model, not a hardcoded literal");
		result.Errors.Should().BeEmpty(because: "binding expressions are out of scope for the literal check");
	}

	[Test]
	[Description("The 'description' property is intentionally outside the hard-reject set (it also names non-display metadata) so a description literal does not fail validation.")]
	public void ValidateLocalizableTextLiterals_DescriptionLiteral_ReturnsValid() {
		// Arrange
		string body = BuildDiffBackedPageBody(
			"""[{"operation":"insert","name":"Widget","values":{"type":"crt.Container","description":"internal note"}}]""",
			"[]");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateLocalizableTextLiterals(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "'description' is excluded from the hard reject and covered by guidance only");
		result.Errors.Should().BeEmpty(because: "the overloaded description key must not produce false positives");
	}

	[Test]
	[Description("Multiple inline literals across separate nodes each produce their own diagnostic so the agent can fix them in one pass.")]
	public void ValidateLocalizableTextLiterals_MultipleLiterals_ReturnsErrorPerOccurrence() {
		// Arrange
		string body = BuildDiffBackedPageBody(
			"""[{"operation":"insert","name":"EmailField","values":{"type":"crt.Input","placeholder":"name@firm.com","tooltip":"Work email"}}]""",
			"[]");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateLocalizableTextLiterals(body);

		// Assert
		result.IsValid.Should().BeFalse(because: "both the placeholder and the tooltip are inline literals");
		result.Errors.Should().HaveCount(2, because: "each offending property must be reported separately");
		result.Errors.Should().Contain(e => e.Contains("placeholder"), because: "the placeholder literal must be reported");
		result.Errors.Should().Contain(e => e.Contains("tooltip"), because: "the tooltip literal must be reported");
	}

	[Test]
	[Description("The mobile variant reads viewConfigDiff from the plain-JSON root and rejects an inline placeholder literal the same way the web variant does.")]
	public void ValidateMobileLocalizableTextLiterals_PlaceholderInlineLiteral_ReturnsInvalid() {
		// Arrange
		const string body = """{"viewConfigDiff":[{"operation":"insert","name":"EmailField","values":{"type":"crt.Input","placeholder":"name@firm.com"}}]}""";

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileLocalizableTextLiterals(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "the mobile localizable-text rule mirrors the web rule");
		result.Errors.Should().ContainSingle(error => error.Contains("EmailField") && error.Contains("placeholder"),
			because: "the mobile diagnostic must name the node and the placeholder property");
	}

	private const string MetricInsertWithMacroTitle = """
		[
			{
				"operation":"insert",
				"name":"IndicatorWidget_CriticalRequests",
				"parentName":"Main",
				"values":{
					"type":"crt.IndicatorWidget",
					"config":{
						"title":"#ResourceString(IndicatorWidget_CriticalRequests_title)#",
						"text":{"template":"{0}","metricMacros":"{0}"}
					}
				}
			}
		]
	""";

	[Test]
	[Description("Inserted metric widget whose #ResourceString title key is neither registered, DS-bound, nor Usr-derivable and is not passed in resources is rejected (ENG-93098) — the binding would render raw $Resources.Strings.<key>.")]
	public void ValidateInsertedWidgetCaptionResources_MetricTitleMacroUnregistered_ReturnsInvalid() {
		// Arrange
		string body = BuildDiffBackedPageBody(MetricInsertWithMacroTitle, "[]");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateInsertedWidgetCaptionResources(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "the widget title key IndicatorWidget_CriticalRequests_title is not registered and cannot be auto-provided, so the binding renders raw");
		result.Errors.Should().ContainSingle(error =>
				error.Contains("IndicatorWidget_CriticalRequests_title") && error.Contains("render raw"),
			because: "the diagnostic must name the unresolved key and explain the raw-render failure");
	}

	[Test]
	[Description("Inserted metric widget title macro whose key IS passed in the resources parameter is accepted — clio registers it, so the binding resolves.")]
	public void ValidateInsertedWidgetCaptionResources_MetricTitleMacroInResources_ReturnsValid() {
		// Arrange
		string body = BuildDiffBackedPageBody(MetricInsertWithMacroTitle, "[]");
		var resources = new Dictionary<string, string> { ["IndicatorWidget_CriticalRequests_title"] = "Critical Requests" };

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateInsertedWidgetCaptionResources(body, resources);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "the title key is explicitly registered through the resources parameter");
		result.Errors.Should().BeEmpty(because: "no caption binding is unresolvable");
	}

	[Test]
	[Description("Inserted widget title in the $Resources.Strings binding form (not the macro form) with an unregistered key is rejected — both reference forms are checked.")]
	public void ValidateInsertedWidgetCaptionResources_DollarBindingTitleUnregistered_ReturnsInvalid() {
		// Arrange
		string viewConfigDiff = """
			[
				{
					"operation":"insert",
					"name":"IndicatorWidget_CriticalRequests",
					"values":{
						"type":"crt.IndicatorWidget",
						"config":{"title":"$Resources.Strings.IndicatorWidget_CriticalRequests_title"}
					}
				}
			]
		""";
		string body = BuildDiffBackedPageBody(viewConfigDiff, "[]");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateInsertedWidgetCaptionResources(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "the $Resources.Strings binding form is subject to the same resolvability rule as the macro form");
		result.Errors.Should().ContainSingle(error => error.Contains("IndicatorWidget_CriticalRequests_title"),
			because: "the diagnostic must name the unresolved key regardless of reference form");
	}

	[Test]
	[Description("Inserted widget title bound to a Usr-prefixed key is accepted without resources — clio auto-derives a caption for Usr* keys.")]
	public void ValidateInsertedWidgetCaptionResources_UsrTitleKey_ReturnsValid() {
		// Arrange
		string viewConfigDiff = """
			[
				{
					"operation":"insert",
					"name":"IndicatorWidget_Usr",
					"values":{
						"type":"crt.IndicatorWidget",
						"config":{"title":"#ResourceString(UsrCriticalRequests_title)#"}
					}
				}
			]
		""";
		string body = BuildDiffBackedPageBody(viewConfigDiff, "[]");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateInsertedWidgetCaptionResources(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "a Usr-prefixed key is auto-derived by clio and therefore resolves at runtime");
		result.Errors.Should().BeEmpty(because: "the Usr* title key needs no explicit resource");
	}

	[Test]
	[Description("A merge (not insert) operation carrying an unresolvable title binding is tolerated — a merge may target a widget whose caption resource the parent schema already provides.")]
	public void ValidateInsertedWidgetCaptionResources_MergeOperation_ReturnsValid() {
		// Arrange
		string viewConfigDiff = """
			[
				{
					"operation":"merge",
					"name":"IndicatorWidget_CriticalRequests",
					"values":{
						"config":{"title":"#ResourceString(IndicatorWidget_CriticalRequests_title)#"}
					}
				}
			]
		""";
		string body = BuildDiffBackedPageBody(viewConfigDiff, "[]");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateInsertedWidgetCaptionResources(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "the validator only enforces self-containment for inserts, not merges");
		result.Errors.Should().BeEmpty(because: "a merge may reference a key already registered on the schema");
	}

	[Test]
	[Description("Authoritative save-gate check ValidateInsertedWidgetCaptionsRegistered rejects a metric title whose key is absent from the final registered localizableStrings set (first creation without resources).")]
	public void ValidateInsertedWidgetCaptionsRegistered_TitleKeyNotRegistered_ReturnsInvalid() {
		// Arrange
		string body = BuildDiffBackedPageBody(MetricInsertWithMacroTitle, "[]");
		var registered = new HashSet<string>(StringComparer.Ordinal);
		var dsBound = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateInsertedWidgetCaptionsRegistered(body, registered, dsBound);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "the title key is not in the final localizableStrings, so the saved binding would render raw");
		result.Errors.Should().ContainSingle(e =>
				e.Contains("IndicatorWidget_CriticalRequests_title") && e.Contains("render raw"),
			because: "the diagnostic must name the unresolved key and the raw-render failure");
	}

	[Test]
	[Description("Authoritative save-gate check accepts a re-inserted metric title whose key is ALREADY present in the schema's registered localizableStrings even when resources is omitted — the re-save flow the body-only heuristic would false-positive (ENG-93098 review fix).")]
	public void ValidateInsertedWidgetCaptionsRegistered_TitleKeyAlreadyRegistered_ReturnsValid() {
		// Arrange
		string body = BuildDiffBackedPageBody(MetricInsertWithMacroTitle, "[]");
		var registered = new HashSet<string>(StringComparer.Ordinal) { "IndicatorWidget_CriticalRequests_title" };
		var dsBound = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateInsertedWidgetCaptionsRegistered(body, registered, dsBound);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "CleanAndMerge preserves already-registered localizableStrings, so a re-inserted title whose key is registered resolves and must not be rejected");
		result.Errors.Should().BeEmpty(because: "the pre-existing registration makes the binding resolve");
	}

	[Test]
	[Description("Authoritative save-gate check treats a DS-bound caption key as resolvable (the platform auto-provides the caption), so it is not rejected.")]
	public void ValidateInsertedWidgetCaptionsRegistered_DsBoundKey_ReturnsValid() {
		// Arrange
		string body = BuildDiffBackedPageBody(MetricInsertWithMacroTitle, "[]");
		var registered = new HashSet<string>(StringComparer.Ordinal);
		var dsBound = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "IndicatorWidget_CriticalRequests_title" };

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateInsertedWidgetCaptionsRegistered(body, registered, dsBound);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "a DS-bound attribute caption is auto-provided by the platform and therefore resolves");
		result.Errors.Should().BeEmpty(because: "DS-bound captions need no localizableStrings entry");
	}

	private static string BuildDiffBackedPageBody(string viewConfigDiff, string viewModelConfigDiff) {
		return $$"""
			define(
				"TestPage",
				/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/,
				function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/{
					return {
						viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/{{viewConfigDiff}}/**SCHEMA_VIEW_CONFIG_DIFF*/,
						viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/{{viewModelConfigDiff}}/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
						modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
						handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,
						converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
						validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
					};
				}
			);
			""";
	}

	private static string BuildStaticViewModelConfigPageBody(string viewConfigDiff, string viewModelConfig, string? handlers = null) {
		return $$"""
			define(
				"TestPage",
				/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/,
				function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/{
					return {
						viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/{{viewConfigDiff}}/**SCHEMA_VIEW_CONFIG_DIFF*/,
						viewModelConfig: /**SCHEMA_VIEW_MODEL_CONFIG*/{{viewModelConfig}}/**SCHEMA_VIEW_MODEL_CONFIG*/,
						modelConfig: /**SCHEMA_MODEL_CONFIG*/{}/**SCHEMA_MODEL_CONFIG*/,
						handlers: /**SCHEMA_HANDLERS*/{{handlers ?? "[]"}}/**SCHEMA_HANDLERS*/,
						converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
						validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
					};
				}
			);
			""";
	}

	[Test]
	[Description("Static viewModelConfig with validators on a different declared attribute is rejected")]
	public void ValidateValidatorControlBindings_StaticViewModelConfig_DifferentDeclaredAttribute_WithValidators_ReturnsInvalid() {
		// Arrange — validators live on UsrNameForValidation but the control stays on UsrName for the same field path.
		string viewConfigDiff = """
			[
				{
					"operation":"insert",
					"name":"UsrName",
					"values":
						{
							"type":"crt.Input",
							"label":"$Resources.Strings.UsrName",
							"control":"$UsrName"
						}
				}
			]
		""";
		string viewModelConfig = """
			{
				"attributes": {
					"UsrName":{"modelConfig":{"path":"PDS.UsrName"}},
					"UsrNameForValidation":
						{
							"modelConfig":{"path":"PDS.UsrName"},
							"validators":
								{
									"UpperCase":
										{
											"type":"usr.UpperCaseValidator",
											"params":{"message":"Must be uppercase"}
										}
								}
						}
				}
			}
		""";
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig);

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateValidatorControlBindings(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "validators must live on the same declared attribute that the control uses");
		result.Errors.Should().ContainSingle(
			error => error.Contains("$UsrName") && error.Contains("$UsrNameForValidation"),
			because: "the error should identify both the current control binding and the declared attribute that owns the validators");
	}

	[Test]
	[Description("Validator binding mismatch reports all declared validator attributes on the same model path")]
	public void ValidateValidatorControlBindings_StaticViewModelConfig_MultipleDeclaredAlternatives_ReportsAllCandidates() {
		// Arrange
		string viewConfigDiff = """
			[
				{
					"operation":"insert",
					"name":"UsrName",
					"values":{"type":"crt.Input","control":"$UsrName"}
				}
			]
		""";
		string viewModelConfig = """
			{
				"attributes": {
					"UsrName":{"modelConfig":{"path":"PDS.UsrName"}},
					"UsrNameForValidation":
						{
							"modelConfig":{"path":"PDS.UsrName"},
							"validators":{"UpperCase":{"type":"usr.UpperCaseValidator"}}
						},
					"UsrNameForValidationSecondary":
						{
							"modelConfig":{"path":"PDS.UsrName"},
							"validators":{"MaxLength":{"type":"crt.MaxLength","params":{"maxLength":5}}}
						}
				}
			}
		""";
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig).Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"""
				/**SCHEMA_VALIDATORS*/
					{
						"usr.UpperCaseValidator": {
							"validator":function(){return function(){return null;};},
							"params":[],
							"async":false
						}
					}
				/**SCHEMA_VALIDATORS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateValidatorControlBindings(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "validators still live on different declared attributes for the same model path");
		result.Errors.Should().ContainSingle(error =>
				error.Contains("one of: UsrNameForValidation, UsrNameForValidationSecondary") &&
				error.Contains("$UsrName"),
			because: "the diagnostic should list every declared validator attribute sharing the same path");
	}

	[Test]
	[Description("MergeResult preserves warnings from successful child validation results")]
	public void ValidateMarkerContent_MergeResult_PreservesWarningsFromSuccessfulChildResult() {
		// Arrange
		MethodInfo mergeResult = typeof(SchemaValidationService).GetMethod("MergeResult", BindingFlags.NonPublic | BindingFlags.Static)!;
		var target = new SchemaValidationResult { IsValid = true };
		var source = new SchemaValidationResult { IsValid = true };
		source.Warnings.Add("warning from child");

		// Act
		mergeResult.Invoke(null, new object[] { target, source });

		// Assert
		target.IsValid.Should().BeTrue(
			because: "successful child validation should not flip the aggregate result to invalid");
		target.Errors.Should().BeEmpty(
			because: "successful child validation contributes no errors");
		target.Warnings.Should().ContainSingle(warning => warning == "warning from child",
			because: "warnings from successful child validation must still propagate to the aggregate result");
	}

	[Test]
	[Description("Static viewModelConfig with $AttrName control where attribute has validators passes validation")]
	public void ValidateValidatorControlBindings_StaticViewModelConfig_AttrBinding_WithValidators_ReturnsValid() {
		// Arrange — correct shape: validator on 'UsrName' and control = "$UsrName"
		string viewConfigDiff = """
			[
				{
					"operation":"insert",
					"name":"UsrName",
					"values":
						{
							"type":"crt.Input",
							"label":"$Resources.Strings.UsrName",
							"control":"$UsrName"
						}
				}
			]
		""";
		string viewModelConfig = """
			{
				"attributes": {
					"UsrName": {
						"modelConfig":{"path":"PDS.UsrName"},
						"validators":
							{
								"UpperCase":
									{
										"type":"usr.UpperCaseValidator",
										"params":{"message":"Must be uppercase"}
									}
							}
					}
				}
			}
		""";
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
	[Description("viewModelConfigDiff with validators on a different declared attribute is rejected")]
	public void ValidateValidatorControlBindings_DiffViewModelConfig_DifferentDeclaredAttribute_WithValidators_ReturnsInvalid() {
		// Arrange
		string viewConfigDiff = """
			[
				{
					"operation":"insert",
					"name":"UsrEmail",
					"values":{"type":"crt.EmailInput","control":"$UsrEmail"}
				}
			]
		""";
		string viewModelConfigDiff = """
			[
				{
					"operation":"merge",
					"path":["attributes"],
					"values":
						{
							"UsrEmail":{"modelConfig":{"path":"PDS.UsrEmail"}},
							"UsrEmailForValidation":
								{
									"modelConfig":{"path":"PDS.UsrEmail"},
									"validators":{"EmailValidator":{"type":"usr.EmailValidator"}}
								}
						}
				}
			]
		""";
		string body = BuildDiffBackedPageBody(viewConfigDiff, viewModelConfigDiff);

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateValidatorControlBindings(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "validators must live on the same declared attribute that the control uses in viewModelConfigDiff");
		result.Errors.Should().ContainSingle(
			error => error.Contains("$UsrEmail") && error.Contains("$UsrEmailForValidation"),
			because: "the error should identify the control binding and the declared attribute that owns the validators");
	}

	[Test]
	[Description("viewModelConfigDiff with $AttrName control where attribute has validators passes validation")]
	public void ValidateValidatorControlBindings_DiffViewModelConfig_AttrBinding_WithValidators_ReturnsValid() {
		// Arrange
		string viewConfigDiff = """
			[
				{
					"operation":"insert",
					"name":"UsrEmail",
					"values":{"type":"crt.EmailInput","control":"$UsrEmail"}
				}
			]
		""";
		string viewModelConfigDiff = """
			[
				{
					"operation":"merge",
					"path":["attributes"],
					"values":
						{
							"UsrEmail": {
								"modelConfig":{"path":"PDS.UsrEmail"},
								"validators":{"EmailValidator":{"type":"usr.EmailValidator"}}
							}
						}
				}
			]
		""";
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
	[Description("Declared attribute binding on attribute without validators is allowed")]
	public void ValidateValidatorControlBindings_DeclaredBinding_AttributeHasNoValidators_ReturnsValid() {
		// Arrange
		string viewConfigDiff = """
			[
				{
					"operation":"insert",
					"name":"UsrName",
					"values":{"type":"crt.Input","control":"$UsrName"}
				}
			]
		""";
		string viewModelConfig = """
			{"attributes":{"UsrName":{"modelConfig":{"path":"PDS.UsrName"}}}}
		""";
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig);

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateValidatorControlBindings(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "no validator binding constraints apply when the control already uses its declared attribute and no validators are registered");
		result.Errors.Should().BeEmpty(
			because: "no validator binding constraints apply to attributes without validators");
	}

	[Test]
	[Description("Attribute with empty validators object is not treated as having validators")]
	public void ValidateValidatorControlBindings_DeclaredBinding_AttributeHasEmptyValidators_ReturnsValid() {
		// Arrange
		string viewConfigDiff = """
			[
				{
					"operation":"insert",
					"name":"UsrName",
					"values":{"type":"crt.Input","control":"$UsrName"}
				}
			]
		""";
		string viewModelConfig = """
			{
				"attributes": {
					"UsrName": {
						"modelConfig":{"path":"PDS.UsrName"},
						"validators":{}
					}
				}
			}
		""";
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig);

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateValidatorControlBindings(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "an empty validators object means no validators are registered, so the declared attribute binding is acceptable");
		result.Errors.Should().BeEmpty(
			because: "empty validators do not impose control binding constraints");
	}

	[Test]
	[Description("Validators declared directly on a viewConfigDiff control are rejected")]
	public void ValidateValidatorBindingPlacement_ViewConfigDiffControlValidators_ReturnsInvalid() {
		// Arrange
		string viewConfigDiff = """
			[
				{
					"operation":"insert",
					"name":"UsrCode",
					"values":
						{
							"type":"crt.Input",
							"control":"$UsrCode",
							"validators":
								[
									{
										"id":"usr.MaxLengthFromSysSettingValidator",
										"params":{"settingCode":"MaxProcessLoopCount","message":"Too long"}
									}
								]
						}
				}
			]
		""";
		string viewModelConfig = """
			{"attributes":{"UsrCode":{"modelConfig":{"path":"PDS.UsrCode"}}}}
		""";
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig).Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"""
				/**SCHEMA_VALIDATORS*/
					{
						"usr.MaxLengthFromSysSettingValidator": {
							"validator":function(config){return async function(control){return null;};},
							"params":[{"name":"settingCode"},{"name":"message"}],
							"async":true
						}
					}
				/**SCHEMA_VALIDATORS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateValidatorBindingPlacement(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "validators declared on the UI element are ignored by Creatio and must be moved to the bound view-model attribute");
		result.Errors.Should().ContainSingle(error =>
				error.Contains("UsrCode") &&
				error.Contains("viewConfigDiff") &&
				error.Contains("viewModelConfig/viewModelConfigDiff"),
			because: "the error should explain that validator bindings belong on the attribute, not on the control");
	}

	[Test]
	[Description("Attribute-level validator bindings remain valid when viewConfigDiff does not declare validators")]
	public void ValidateValidatorBindingPlacement_AttributeLevelValidatorsWithoutInlineControlValidators_ReturnsValid() {
		// Arrange
		string viewConfigDiff = """
			[
				{
					"operation":"insert",
					"name":"UsrCode",
					"values":{"type":"crt.Input","control":"$UsrCode"}
				}
			]
		""";
		string viewModelConfig = """
			{
				"attributes": {
					"UsrCode": {
						"modelConfig":{"path":"PDS.UsrCode"},
						"validators":
							{
								"CodeLength": {
									"type":"usr.MaxLengthFromSysSettingValidator",
									"params":
										{
											"settingCode":"MaxProcessLoopCount",
											"message":"#ResourceString(UsrCodeLength_Message)#"
										}
								}
							}
					}
				}
			}
		""";
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig).Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"""
				/**SCHEMA_VALIDATORS*/
					{
						"usr.MaxLengthFromSysSettingValidator": {
							"validator":function(config){return async function(control){return null;};},
							"params":[{"name":"settingCode"},{"name":"message"}],
							"async":true
						}
					}
				/**SCHEMA_VALIDATORS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateValidatorBindingPlacement(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "validators bound through viewModelConfig are the supported runtime shape");
		result.Errors.Should().BeEmpty(
			because: "no inline control validators are present in viewConfigDiff");
	}

	[Test]
	[Description("Canonical attribute-level validator binding `{ \"required\": { \"type\": \"usr.NotEmpty\" } }` passes ValidateValidatorBindingShape")]
	public void ValidateValidatorBindingShape_CanonicalShape_ReturnsValid() {
		// Arrange
		string viewModelConfig = """
			{"attributes":{"UsrDescription":{"modelConfig":{"path":"PDS.UsrDescription"},"validators":{"required":{"type":"usr.NotEmpty"}}}}}
		""";
		string body = BuildStaticViewModelConfigPageBody("[]", viewModelConfig);

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateValidatorBindingShape(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "the canonical shape `{ <name>: { type: \"<ValidatorType>\" } }` is the documented runtime contract and must pass without errors");
		result.Errors.Should().BeEmpty(
			because: "no anti-shape is present and the validator must remain quiet on valid input");
	}

	[Test]
	[Description("`validators: [ ... ]` declared as an array on a view-model attribute is rejected — must be an object map")]
	public void ValidateValidatorBindingShape_AttributeValidatorsAsArray_ReturnsInvalid() {
		// Arrange
		string viewModelConfig = """
			{"attributes":{"UsrDescription":{"modelConfig":{"path":"PDS.UsrDescription"},"validators":[{"type":"usr.NotEmpty"}]}}}
		""";
		string body = BuildStaticViewModelConfigPageBody("[]", viewModelConfig);

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateValidatorBindingShape(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "an array-shaped `validators` on an attribute is silently dropped by the existing chain — it must be flagged before save");
		result.Errors.Should().ContainSingle(error =>
				error.Contains("UsrDescription") && error.Contains("an array"),
			because: "the error must name the offending attribute and the wrong shape so the agent can fix it without grepping");
	}

	[Test]
	[Description("Named validator entry declared as an array (`{ required: [{ type: ... }] }`) is rejected — each entry must be an object")]
	public void ValidateValidatorBindingShape_NamedEntryAsArray_ReturnsInvalid() {
		// Arrange
		string viewModelConfig = """
			{"attributes":{"UsrDescription":{"modelConfig":{"path":"PDS.UsrDescription"},"validators":{"required":[{"type":"usr.NotEmpty"}]}}}}
		""";
		string body = BuildStaticViewModelConfigPageBody("[]", viewModelConfig);

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateValidatorBindingShape(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "wrapping a validator entry in an array is silently skipped by `TryGetValidatorType` — the body must be rejected before save");
		result.Errors.Should().ContainSingle(error =>
				error.Contains("UsrDescription") && error.Contains("required") && error.Contains("an array"),
			because: "the error must identify both the attribute and the validator name so the agent can pinpoint the wrapper that needs to go");
	}

	[Test]
	[Description("Named validator entry declared as a bare string (`{ required: \"usr.NotEmpty\" }`) is rejected — must be an object with `type`")]
	public void ValidateValidatorBindingShape_NamedEntryAsString_ReturnsInvalid() {
		// Arrange
		string viewModelConfig = """
			{"attributes":{"UsrDescription":{"modelConfig":{"path":"PDS.UsrDescription"},"validators":{"required":"usr.NotEmpty"}}}}
		""";
		string body = BuildStaticViewModelConfigPageBody("[]", viewModelConfig);

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateValidatorBindingShape(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "a bare string shorthand is not the documented contract — the entry needs `{ type: \"<ValidatorType>\" }` so the agent must not ship the abbreviation");
		result.Errors.Should().ContainSingle(error =>
				error.Contains("UsrDescription") && error.Contains("required") && error.Contains("a string"),
			because: "the error must explain which entry is the wrong shape and what kind it is so the fix is mechanical");
	}

	[Test]
	[Description("Named validator entry missing the `type` property is rejected — each entry must point at a SCHEMA_VALIDATORS key via a non-empty string `type`")]
	public void ValidateValidatorBindingShape_NamedEntryMissingType_ReturnsInvalid() {
		// Arrange
		string viewModelConfig = """
			{"attributes":{"UsrDescription":{"modelConfig":{"path":"PDS.UsrDescription"},"validators":{"required":{"params":{"message":"#ResourceString(M)#"}}}}}}
		""";
		string body = BuildStaticViewModelConfigPageBody("[]", viewModelConfig);

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateValidatorBindingShape(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "an entry without `type` has nothing to dispatch to in SCHEMA_VALIDATORS — the body would attach the validator metadata to the attribute without a runtime target");
		result.Errors.Should().ContainSingle(error =>
				error.Contains("UsrDescription") && error.Contains("required") && error.Contains("type"),
			because: "the error must name both the attribute and the missing property so the agent can add the type pointer without re-reading guidance");
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
		string viewModelConfig = """
			{
				"attributes": {
					"UsrName": {
						"modelConfig":{"path":"PDS.UsrName"},
						"validators":
							{
								"AllUpperCase": {
									"type":"usr.AllUpperCase",
									"params":{"message":"$Resources.Strings.UsrUpperCaseValidator_Message"}
								}
							}
					}
				}
			}
		""";
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
		string viewModelConfig = """
			{
				"attributes": {
					"UsrName": {
						"modelConfig":{"path":"PDS.UsrName"},
						"validators":
							{
								"AllUpperCase": {
									"type":"usr.AllUpperCase",
									"params":{"message":"#ResourceString(UsrUpperCaseValidator_Message)#"}
								}
							}
					}
				}
			}
		""";
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
		string viewModelConfigDiff = """
			[
				{
					"operation":"merge",
					"path":["attributes"],
					"values":
						{
							"UsrName": {
								"modelConfig":{"path":"PDS.UsrName"},
								"validators":
									{
										"Upper": {
											"type":"usr.Upper",
											"params":{"message":"$Resources.Strings.UsrMsg"}
										}
									}
							}
						}
				}
			]
		""";
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
		string viewModelConfigDiff = """
			[
				{
					"operation":"merge",
					"path":["handlers"],
					"values":
						{
							"UsrPseudoHandler": {
								"validators":
									{
										"Upper": {
											"type":"usr.Upper",
											"params":{"message":"$Resources.Strings.UsrMsg"}
										}
									}
							}
						}
				}
			]
		""";
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
		string viewModelConfigDiff = """
			[
				{
					"operation":"merge",
					"path":["handlers","attributes"],
					"values":
						{
							"UsrPseudoHandler": {
								"validators":
									{
										"Upper": {
											"type":"usr.Upper",
											"params":{"message":"$Resources.Strings.UsrMsg"}
										}
									}
							}
						}
				}
			]
		""";
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
	[Description("Root-level diff merge operations without path still participate in validator param validation")]
	public void ValidateValidatorParamResourceBindings_PathlessRootMergeWithAttributeValidators_ReturnsInvalid() {
		// Arrange
		string viewModelConfigDiff = """
			[
				{
					"operation":"merge",
					"values":
						{
							"UsrName": {
								"validators":
									{
										"Upper": {
											"type":"usr.Upper",
											"params":{"message":"$Resources.Strings.UsrMsg"}
										}
									}
							}
						}
				}
			]
		""";
		string body = BuildDiffBackedPageBody("[]", viewModelConfigDiff);

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateValidatorParamResourceBindings(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "root-level merge operations without an explicit path are still valid diff shapes and must be validated");
		result.Errors.Should().ContainSingle(error => error.Contains("#ResourceString(UsrMsg)#"),
			because: "validator param validation must still run for pathless root merges that bind resource strings reactively");
	}

	[Test]
	[Description("Root-level diff merge operations without validators do not produce validator param errors")]
	public void ValidateValidatorParamResourceBindings_PathlessRootMergeWithoutValidators_ReturnsValid() {
		// Arrange
		string viewModelConfigDiff = """
			[
				{
					"operation":"merge",
					"values":
						{
							"UsrPseudoHandler":
								{
									"request":"usr.DoSomething",
									"params":{"message":"$Resources.Strings.UsrMsg"}
								}
						}
				}
			]
		""";
		string body = BuildDiffBackedPageBody("[]", viewModelConfigDiff);

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateValidatorParamResourceBindings(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "pathless root merges should not fail validator validation when their values do not define attribute validators");
		result.Errors.Should().BeEmpty(
			because: "non-validator payloads inside a pathless root merge must not be misread as validator bindings");
	}

	[Test]
	[Description("Custom validator whose name contains 'MaxLength' is rejected in favour of the built-in crt.MaxLength.")]
	public void ValidateStandardValidatorUsage_CustomMaxLengthStyleValidator_ReturnsInvalid() {
		// Arrange
		string viewConfigDiff = """
			[
				{
					"operation":"insert",
					"name":"UsrName",
					"values":{"type":"crt.Input","control":"$UsrName"}
				}
			]
		""";
		string viewModelConfig = """
			{
				"attributes": {
					"UsrName": {
						"modelConfig":{"path":"PDS.UsrName"},
						"validators":
							{
								"NameMaxLength": {
									"type":"usr.NameMaxLength",
									"params":{"message":"#ResourceString(UsrNameMaxLength_Message)#"}
								}
							}
					}
				}
			}
		""";
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig).Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"""
				/**SCHEMA_VALIDATORS*/
					{
						"usr.NameMaxLength": {
							"validator":
								function(config){
									return function(control){
										if (control.value && control.value.length >= 5) {
											return {"usr.NameMaxLength": { message: config.message }};
										} return null;
									};
								},
							"params":[{"name":"message"}],
							"async":false
						}
					}
				/**SCHEMA_VALIDATORS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateStandardValidatorUsage(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "a custom validator whose name contains 'MaxLength' re-implements crt.MaxLength and must be replaced with the built-in");
		result.Errors.Should().ContainSingle(error =>
			error.Contains("usr.NameMaxLength") && error.Contains("crt.MaxLength"),
			because: "the error should identify both the rejected validator and the built-in replacement");
	}

	[Test]
	[Description("Control binding to a proxy view-model attribute (flat form, no path:[]) gets an error message that names both the proxy binding and the expected datasource-derived binding.")]
	public void ValidateInsertedFieldSelfConsistency_ProxyBindingFlatForm_ErrorNamesExpectedDatasourceBinding() {
		// Arrange
		string body = BuildDiffBackedPageBody(
			"""
				[
					{
						"operation":"insert",
						"name":"UsrStatus",
						"values":
							{
								"type":"crt.ComboBox",
								"label":"$Resources.Strings.PDS_UsrStatus",
								"control":"$UsrStatus"
							}
					}
				]
			""",
			"""
				[
					{
						"operation":"merge",
						"values":{"UsrStatus":{"modelConfig":{"path":"PDS.UsrStatus"}}}
					}
				]
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateInsertedFieldSelfConsistency(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "the attribute is declared in flat form without the required path:[] nesting");
		result.Errors.Should().Contain(error =>
			error.Contains("$UsrStatus") && error.Contains("$PDS_UsrStatus"),
			because: "the error must name both the rejected proxy binding and the expected datasource-derived binding so the agent knows what to replace it with");
	}

	[Test]
	[Description("Custom validator whose name does not contain 'MaxLength' is not rejected — only MaxLength re-implementations are flagged.")]
	public void ValidateStandardValidatorUsage_CustomNonMaxLengthValidator_ReturnsValid() {
		// Arrange
		string body = BuildDiffBackedPageBody(
			"""
				[
					{
						"operation":"insert",
						"name":"UsrName",
						"values":{"type":"crt.Input","control":"$PDS_UsrName"}
					}
				]
			""",
			"""
				[
					{
						"operation":"merge",
						"path":[],
						"values":{"attributes":{"PDS_UsrName":{"modelConfig":{"path":"PDS.UsrName"}}}}
					}
				]
			""");
		string body2 = body.Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"""
				/**SCHEMA_VALIDATORS*/
					{
						"usr.UpperCaseOnly": {
							"validator":
								function(config){
									return function(control){
										if (control.value && control.value !== control.value.toUpperCase()) {
											return {"usr.UpperCaseOnly": { message: config.message }};
										} return null;
									};
								},
							"params":[{"name":"message"}],
							"async":false
						}
					}
				/**SCHEMA_VALIDATORS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateStandardValidatorUsage(body2);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "validators without 'MaxLength' in their name are not flagged as built-in reimplementations");
		result.Errors.Should().BeEmpty(
			because: "only MaxLength-named validators trigger the built-in replacement error");
	}

	[Test]
	[Description("Non-standard custom validators remain allowed when no built-in validator obviously matches the rule.")]
	public void ValidateStandardValidatorUsage_CustomDomainValidator_ReturnsValid() {
		// Arrange
		string viewConfigDiff = """
			[
				{
					"operation":"insert",
					"name":"UsrName",
					"values":{"type":"crt.Input","control":"$UsrName"}
				}
			]
		""";
		string viewModelConfig = """
			{
				"attributes": {
					"UsrName": {
						"modelConfig":{"path":"PDS.UsrName"},
						"validators":
							{
								"UpperCase": {
									"type":"usr.UpperCaseValidator",
									"params":{"message":"#ResourceString(UsrUpperCaseValidator_Message)#"}
								}
							}
					}
				}
			}
		""";
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig).Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"""
				/**SCHEMA_VALIDATORS*/
					{
						"usr.UpperCaseValidator": {
							"validator":
								function(config){
									return function(control){
										const value = control.value;
										if (!value || value === value.toUpperCase()) {
											return null;
										} return {"usr.UpperCaseValidator": { message: config.message }};
									};
								},
							"params":[{"name":"message"}],
							"async":false
						}
					}
				/**SCHEMA_VALIDATORS*/
			""");

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
		string viewConfigDiff = """
			[
				{
					"operation":"insert",
					"name":"UsrName",
					"values":{"type":"crt.Input","control":"$UsrName"}
				}
			]
		""";
		string viewModelConfig = """
			{
				"attributes": {
					"UsrName": {
						"modelConfig":{"path":"PDS.UsrName"},
						"validators":{"NameMaxLength":{"type":"crt.MaxLength","params":{"max":4}}}
					}
				}
			}
		""";
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
		string viewConfigDiff = """
			[
				{
					"operation":"insert",
					"name":"UsrName",
					"values":{"type":"crt.Input","control":"$UsrName"}
				}
			]
		""";
		string viewModelConfig = """
			{
				"attributes": {
					"UsrName": {
						"modelConfig":{"path":"PDS.UsrName"},
						"validators":{"NameMaxLength":{"type":"crt.MaxLength","params":{"maxLength":4}}}
					}
				}
			}
		""";
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
		string viewConfigDiff = """
			[
				{
					"operation":"insert",
					"name":"UsrName",
					"values":{"type":"crt.Input","control":"$UsrName"}
				}
			]
		""";
		string viewModelConfig = """
			{
				"attributes": {
					"UsrName": {
						"modelConfig":{"path":"PDS.UsrName"},
						"validators":
							{
								"NameMaxLength": {
									"type":"crt.MaxLength",
									"params":{"maxLength":4,"message":"#ResourceString(UsrNameTooLong)#"}
								}
							}
					}
				}
			}
		""";
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
		string viewConfigDiff = """
			[
				{
					"operation":"insert",
					"name":"UsrName",
					"values":{"type":"crt.Input","control":"$UsrName"}
				}
			]
		""";
		string viewModelConfig = """
			{
				"attributes": {
					"UsrName": {
						"modelConfig":{"path":"PDS.UsrName"},
						"validators":
							{
								"Required":
									{
										"type":"crt.Required",
										"params":{"message":"#ResourceString(UsrRequired)#"}
									}
							}
					}
				}
			}
		""";
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
		string viewConfigDiff = """
			[
				{
					"operation":"insert",
					"name":"UsrName",
					"values":{"type":"crt.Input","control":"$UsrName"}
				}
			]
		""";
		string viewModelConfig = """
			{
				"attributes": {
					"UsrName": {
						"modelConfig":{"path":"PDS.UsrName"},
						"validators":
							{
								"NoParams":
									{
										"type":"usr.NoParamsValidator",
										"params":{"message":"#ResourceString(UsrMsg)#"}
									}
							}
					}
				}
			}
		""";
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig).Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"""
				/**SCHEMA_VALIDATORS*/
					{
						"usr.NoParamsValidator": {
							"validator":function(){return function(){return null;};},
							"params":[],
							"async":false
						}
					}
				/**SCHEMA_VALIDATORS*/
			""");

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
		string viewConfigDiff = """
			[
				{
					"operation":"insert",
					"name":"UsrName",
					"values":{"type":"crt.Input","control":"$UsrName"}
				}
			]
		""";
		string viewModelConfig = """
			{
				"attributes": {
					"UsrName": {
						"modelConfig":{"path":"PDS.UsrName"},
						"validators":
							{
								"NoParams":
									{
										"type":"usr.NoParamsValidator",
										"params":{"message":"#ResourceString(UsrMsg)#"}
									}
							}
					}
				}
			}
		""";
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig).Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"""
				/**SCHEMA_VALIDATORS*/
					{
						"usr.NoParamsValidator":
							{
								"validator":function(){return function(){return null;};},
								"async":false
							}
					}
				/**SCHEMA_VALIDATORS*/
			""");

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
		string viewConfigDiff = """
			[
				{
					"operation":"insert",
					"name":"UsrName",
					"values":{"type":"crt.Input","control":"$UsrName"}
				}
			]
		""";
		string viewModelConfig = """
			{
				"attributes": {
					"UsrName": {
						"modelConfig":{"path":"PDS.UsrName"},
						"validators":{"NameMaxLength":{"type":"crt.MaxLength","params":{"max":4}}}
					}
				}
			}
		""";
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig).Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"""
				/**SCHEMA_VALIDATORS*/
					{
						"crt.MaxLength": {
							"validator":function(){return function(){return null;};},
							"params":[{"name":"max"}],
							"async":false
						}
					}
				/**SCHEMA_VALIDATORS*/
			""");

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
		string viewConfigDiff = """
			[
				{
					"operation":"insert",
					"name":"UsrName",
					"values":{"type":"crt.Input","control":"$UsrName"}
				}
			]
		""";
		string viewModelConfig = """
			{
				"attributes": {
					"UsrName": {
						"modelConfig":{"path":"PDS.UsrName"},
						"validators":
							{
								"Upper": {
									"type":"usr.UpperCaseValidator",
									"params":{"message":"#ResourceString(UsrMsg)#"}
								}
							}
					}
				}
			}
		""";
		// SCHEMA_VALIDATORS uses unquoted JS property: params: [{ "name": "message" }]
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig).Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"""
				/**SCHEMA_VALIDATORS*/
					{
						"usr.UpperCaseValidator": {
							validator:function(config){return function(control){return null;};},
							params:[{"name":"message"}],
							async:false
						}
					}
				/**SCHEMA_VALIDATORS*/
			""");

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
		string viewConfigDiff = """
			[
				{
					"operation":"insert",
					"name":"UsrName",
					"values":{"type":"crt.Input","control":"$UsrName"}
				}
			]
		""";
		string viewModelConfig = """
			{
				"attributes": {
					"UsrName": {
						"modelConfig":{"path":"PDS.UsrName"},
						"validators":
							{
								"Upper": {
									"type":"usr.UpperCaseValidator",
									"params":{"message":"#ResourceString(UsrMsg)#"}
								}
							}
					}
				}
			}
		""";
		// SCHEMA_VALIDATORS uses fully unquoted JS properties: params: [{ name: "message" }]
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig).Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"""
				/**SCHEMA_VALIDATORS*/
					{
						"usr.UpperCaseValidator": {
							validator:function(config){return function(control){return null;};},
							params:[{name:"message"}],
							async:false
						}
					}
				/**SCHEMA_VALIDATORS*/
			""");

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
		string viewConfigDiff = """
			[
				{
					"operation":"insert",
					"name":"UsrName",
					"values":{"type":"crt.Input","control":"$UsrName"}
				}
			]
		""";
		string viewModelConfig = """
			{
				"attributes": {
					"UsrName": {
						"modelConfig":{"path":"PDS.UsrName"},
						"validators":
							{
								"Upper": {
									"type":"usr.UpperCaseValidator",
									"params":{"message":"#ResourceString(UsrMsg)#"}
								}
							}
					}
				}
			}
		""";
		// params array exists but has no message param
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig).Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"""
				/**SCHEMA_VALIDATORS*/
					{
						"usr.UpperCaseValidator": {
							validator:function(config){return function(control){return null;};},
							params:[{name:"settingCode"}],
							async:false
						}
					}
				/**SCHEMA_VALIDATORS*/
			""");

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
		string viewModelConfig = """
			{"attributes":{"UsrName":{"modelConfig":{"path":"PDS.UsrName"}}}}
		""";
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
			"""
				/**SCHEMA_VALIDATORS*/
					{
						"usr.OnlyDigits": {
							"validator":
								function(config){
									return function(control){
										var v=control.value;
										if(v&&!/^\d+$/.test(v)){
											return{"usr.OnlyDigits":{message:config.message}};
										}return null;
									};
								},
							"params":[{"name":"message"}],
							"async":false
						}
					}
				/**SCHEMA_VALIDATORS*/
			""");

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
			"""
				/**SCHEMA_VALIDATORS*/
					{
						"usr.OnlyDigits": {
							"validator":
								function(config){
									return function(control){
										var v=control.value;
										if(v&&!/^\d+$/.test(v)){return{"usr.OnlyDigits":true};}return null;
									};
								},
							"params":[],
							"async":false
						}
					}
				/**SCHEMA_VALIDATORS*/
			""");

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
			"""
				/**SCHEMA_VALIDATORS*/
					{
						"usr.OnlyDigits": {
							"validator":
								function(config){
									return function(control){
										var v=control.value;
										if(v&&!/^\d+$/.test(v)){
											return{"usr.OnlyDigits":{message:"Only digits allowed"}};
										}return null;
									};
								},
							"params":[],
							"async":false
						}
					}
				/**SCHEMA_VALIDATORS*/
			""");

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
			"""
				/**SCHEMA_VALIDATORS*/
					{
						"usr.OnlyDigits": {
							"validator":
								function(config){
									return function(control){
										var v=control.value;
										if(v&&!/^\d+$/.test(v)){
											return{"usr.OnlyDigits":{details:{field:"UsrName"},message:config.message}};
										}return null;
									};
								},
							"params":[{"name":"message"}],
							"async":false
						}
					}
				/**SCHEMA_VALIDATORS*/
			""");

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
			"""
				/**SCHEMA_VALIDATORS*/
					{
						"usr.OnlyDigits": {
							"validator":
								function(config){
									return function(control){
										var v=control.value;
										if(v&&!/^\d+$/.test(v)){return{"usr.OnlyDigits":{}};}return null;
									};
								},
							"params":[],
							"async":false
						}
					}
				/**SCHEMA_VALIDATORS*/
			""");

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
			"""
				,"params":[{"name":"message"}],"async":false}}
			""";
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
			"""
				{
					"usr.PatternValidator": {
						"validator":
							function(config){
								return function(control){
									var v=control.value;
									if(v&&!/^[{a-z}]+$/.test(v)){return{"usr.PatternValidator":{message:config.message}};}return null;
								};
							},
						"params":[{"name":"message"}],
						"async":false
					}
				}
			""";
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

	[Test]
	[Description("Non-PDS data source direct binding passes validation")]
	public void ValidateStandardFieldBindings_NonPdsDirectBinding_ReturnsValid() {
		string viewConfigDiff = """
			[
				{
					"operation":"insert",
					"name":"UsrStatus",
					"values":
						{
							"type":"crt.ComboBox",
							"label":"$Resources.Strings.DS1_UsrStatus",
							"control":"$DS1_UsrStatus"
						}
				}
			]
		""";
		string viewModelConfig = """
			{"attributes":{"DS1_UsrStatus":{"modelConfig":{"path":"DS1.UsrStatus"}}}}
		""";
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig);

		var result = SchemaValidationService.ValidateStandardFieldBindings(body);

		result.IsValid.Should().BeTrue("because the field uses direct datasource binding for a non-PDS datasource");
		result.Errors.Should().BeEmpty();
		result.Warnings.Should().BeEmpty();
	}

	[Test]
	[Description("Non-PDS data source Name field uses $Name binding")]
	public void BuildExpectedBinding_NonPdsNameField_ReturnsNameBinding() {
		string binding = SchemaValidationService.BuildExpectedBinding("DS1.Name");

		binding.Should().Be("$Name", "because any datasource Name field maps to the $Name shorthand");
	}

	[Test]
	[Description("Control bound to non-PDS datasource binding on attribute with validators is rejected")]
	public void ValidateValidatorControlBindings_NonPdsBinding_AttributeHasValidators_ReturnsInvalid() {
		string viewConfigDiff = """
			[
				{
					"operation":"insert",
					"name":"UsrStatus",
					"values":{"type":"crt.ComboBox","control":"$DS1_UsrStatus"}
				}
			]
		""";
		string viewModelConfig = """
			{
				"attributes": {
					"UsrStatus": {
						"modelConfig":{"path":"DS1.UsrStatus"},
						"validators":{"Required":{"type":"crt.Required"}}
					}
				}
			}
		""";
		string body = BuildStaticViewModelConfigPageBody(viewConfigDiff, viewModelConfig);

		SchemaValidationResult result = SchemaValidationService.ValidateValidatorControlBindings(body);

		result.IsValid.Should().BeFalse(
			because: "a control bound to '$DS1_UsrStatus' will never trigger the validator on attribute 'UsrStatus'");
		result.Errors.Should().ContainSingle(
			error => error.Contains("UsrStatus") && error.Contains("$DS1_UsrStatus") && error.Contains("$UsrStatus"),
			because: "the error should identify the wrong datasource binding and the correct view-model attribute binding");
	}

	#region ValidateConverterDeclarations

	[Test]
	[Description("Null body returns valid without throwing")]
	public void ValidateConverterDeclarations_NullBody_ReturnsValid() {
		SchemaValidationResult result = SchemaValidationService.ValidateConverterDeclarations(null);
		result.IsValid.Should().BeTrue("because a null body is handled by the early-return guard");
		result.Errors.Should().BeEmpty("because a null body produces no validation errors");
	}

	[Test]
	[Description("Empty string body returns valid without throwing")]
	public void ValidateConverterDeclarations_EmptyBody_ReturnsValid() {
		SchemaValidationResult result = SchemaValidationService.ValidateConverterDeclarations(string.Empty);
		result.IsValid.Should().BeTrue("because an empty body is handled by the early-return guard");
		result.Errors.Should().BeEmpty("because an empty body produces no validation errors");
	}

	[Test]
	[Description("Empty converters object passes validation")]
	public void ValidateConverterDeclarations_EmptyConverters_ReturnsValid() {
		SchemaValidationResult result = SchemaValidationService.ValidateConverterDeclarations(ValidListPageBody);
		result.IsValid.Should().BeTrue("because an empty SCHEMA_CONVERTERS object has no keys to validate");
		result.Errors.Should().BeEmpty("because no keys means no format errors");
	}

	[Test]
	[Description("Converter with correct usr. prefix passes validation")]
	public void ValidateConverterDeclarations_CorrectPrefixedKey_ReturnsValid() {
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"""
				/**SCHEMA_CONVERTERS*/
					{
						"usr.ToUpperCase": function(value) {
							return value?.toUpperCase() ?? '';
						}
					}
				/**SCHEMA_CONVERTERS*/
			""");
		SchemaValidationResult result = SchemaValidationService.ValidateConverterDeclarations(body);
		result.IsValid.Should().BeTrue("because 'usr.ToUpperCase' has the required VendorPrefix.Name dot-format");
		result.Errors.Should().BeEmpty("because a correctly prefixed key produces no errors");
	}

	[Test]
	[Description("Async arrow converter with correct prefix passes validation")]
	public void ValidateConverterDeclarations_AsyncArrowConverterCorrectKey_ReturnsValid() {
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"""
				/**SCHEMA_CONVERTERS*/
					{
						"usr.FormatPhone": async (value) => { return value; }
					}
				/**SCHEMA_CONVERTERS*/
			""");
		SchemaValidationResult result = SchemaValidationService.ValidateConverterDeclarations(body);
		result.IsValid.Should().BeTrue("because 'usr.FormatPhone' has the required dot-format");
		result.Errors.Should().BeEmpty("because async arrow shape with a correct key produces no errors");
	}

	[Test]
	[Description("Converter key without a dot fails validation with a descriptive runtime-error message")]
	public void ValidateConverterDeclarations_KeyWithoutDot_ReturnsInvalid() {
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"""
				/**SCHEMA_CONVERTERS*/
					{
						"UsrPhoneCallConverter": function(value) { return value; }
					}
				/**SCHEMA_CONVERTERS*/
			""");
		SchemaValidationResult result = SchemaValidationService.ValidateConverterDeclarations(body);
		result.IsValid.Should().BeFalse("because 'UsrPhoneCallConverter' is missing the required dot separator");
		result.Errors.Should().ContainSingle(
			e => e.Contains("UsrPhoneCallConverter") && e.Contains("usr.UsrPhoneCallConverter") && e.Contains("VendorPrefix"),
			because: "the error should name the offending key, suggest the fix, and explain the runtime consequence");
	}

	[Test]
	[Description("Multiple converters — one valid, one missing dot — reports exactly one error for the bad key")]
	public void ValidateConverterDeclarations_MixedKeys_ReportsOnlyBadOne() {
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"""
				/**SCHEMA_CONVERTERS*/
					{
						"usr.ToUpperCase": function(value) { return value?.toUpperCase() ?? ''; },
						"BadConverter": function(value) { return value; }
					}
				/**SCHEMA_CONVERTERS*/
			""");
		SchemaValidationResult result = SchemaValidationService.ValidateConverterDeclarations(body);
		result.IsValid.Should().BeFalse("because 'BadConverter' is missing the dot");
		result.Errors.Should().ContainSingle(e => e.Contains("BadConverter"),
			because: "only the key without a dot should generate an error");
		result.Errors.Should().NotContain(e => e.Contains("usr.ToUpperCase"),
			because: "the valid key should not generate an error");
	}

	[Test]
	[Description("crt.* converter declared in SCHEMA_CONVERTERS passes format validation (wrong practice, but not a dot-format error)")]
	public void ValidateConverterDeclarations_CrtPrefixedKey_ReturnsValid() {
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"""
				/**SCHEMA_CONVERTERS*/
					{
						"crt.SomeConverter": function(value) { return value; }
					}
				/**SCHEMA_CONVERTERS*/
			""");
		SchemaValidationResult result = SchemaValidationService.ValidateConverterDeclarations(body);
		result.IsValid.Should().BeTrue(
			"because the dot-format check passes for 'crt.SomeConverter' — declaring crt.* is unnecessary but not a format error");
	}

	[Test]
	[Description("No-paren single-arg arrow converter without a dot fails validation")]
	public void ValidateConverterDeclarations_NoParenArrowMissingDot_ReturnsInvalid() {
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"""
				/**SCHEMA_CONVERTERS*/{ "BadArrow": value => value }/**SCHEMA_CONVERTERS*/
			""");
		SchemaValidationResult result = SchemaValidationService.ValidateConverterDeclarations(body);
		result.IsValid.Should().BeFalse("because 'BadArrow' is missing the required dot separator");
		result.Errors.Should().ContainSingle(e => e.Contains("BadArrow") && e.Contains("VendorPrefix"),
			because: "no-paren single-arg arrow converters must be checked by the same dot-format rule");
	}

	[Test]
	[Description("No-paren single-arg arrow converter with correct prefix passes validation")]
	public void ValidateConverterDeclarations_NoParenArrowCorrectKey_ReturnsValid() {
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"""
				/**SCHEMA_CONVERTERS*/{ "usr.Trim": value => value.trim() }/**SCHEMA_CONVERTERS*/
			""");
		SchemaValidationResult result = SchemaValidationService.ValidateConverterDeclarations(body);
		result.IsValid.Should().BeTrue("because 'usr.Trim' has the required dot-format");
		result.Errors.Should().BeEmpty("because no-paren arrow shape with a correct key produces no errors");
	}

	[Test]
	[Description("ES6 method-shorthand converter without a dot fails validation")]
	public void ValidateConverterDeclarations_MethodShorthandMissingDot_ReturnsInvalid() {
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"""
				/**SCHEMA_CONVERTERS*/{ "BadShorthand"(value) { return value; } }/**SCHEMA_CONVERTERS*/
			""");
		SchemaValidationResult result = SchemaValidationService.ValidateConverterDeclarations(body);
		result.IsValid.Should().BeFalse("because 'BadShorthand' is missing the required dot separator");
		result.Errors.Should().ContainSingle(e => e.Contains("BadShorthand") && e.Contains("VendorPrefix"),
			because: "ES6 method-shorthand converters must be checked by the same dot-format rule");
	}

	[Test]
	[Description("Quoted property name inside a converter body is not flagged as a top-level key")]
	public void ValidateConverterDeclarations_NestedStringKey_DoesNotFalsePositive() {
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"""
				/**SCHEMA_CONVERTERS*/
					{
						"usr.Outer": function(value) {
							return JSON.stringify({ "nested_no_dot": value });
						}
					}
				/**SCHEMA_CONVERTERS*/
			""");
		SchemaValidationResult result = SchemaValidationService.ValidateConverterDeclarations(body);
		result.IsValid.Should().BeTrue(
			"because 'nested_no_dot' lives inside the converter's body, not at the SCHEMA_CONVERTERS top level");
		result.Errors.Should().BeEmpty("because nested-string keys must not be flagged as top-level converter keys");
	}

	[Test]
	[Description("Error message mentions a placeholder vendor prefix and 'usr.' as an example, not as a hardcoded rename")]
	public void ValidateConverterDeclarations_ErrorMessage_MentionsVendorPrefixPlaceholder() {
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"""
				/**SCHEMA_CONVERTERS*/{ "PhoneCall": function(value) { return value; } }/**SCHEMA_CONVERTERS*/
			""");
		SchemaValidationResult result = SchemaValidationService.ValidateConverterDeclarations(body);
		result.IsValid.Should().BeFalse("because 'PhoneCall' is missing the required dot separator");
		result.Errors.Should().ContainSingle(
			e => e.Contains("<vendor>.PhoneCall") && e.Contains("for example 'usr.PhoneCall'"),
			because: "the rename suggestion should not hardcode 'usr.' for vendors that ship a different prefix");
	}

	[Test]
	[Description("Unquoted identifier-key converter without a dot fails validation")]
	public void ValidateConverterDeclarations_IdentifierKeyMissingDot_ReturnsInvalid() {
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"""
				/**SCHEMA_CONVERTERS*/{ BadConverter: value => value }/**SCHEMA_CONVERTERS*/
			""");
		SchemaValidationResult result = SchemaValidationService.ValidateConverterDeclarations(body);
		result.IsValid.Should().BeFalse(
			"because identifier-key syntax cannot contain a dot, so 'BadConverter' violates the VendorPrefix.Name rule");
		result.Errors.Should().ContainSingle(
			e => e.Contains("BadConverter") && e.Contains("VendorPrefix"),
			because: "the validator must recognize unquoted identifier keys, not only quoted strings");
	}

	[Test]
	[Description("Unquoted ES6 method-shorthand converter without a dot fails validation")]
	public void ValidateConverterDeclarations_IdentifierMethodShorthandMissingDot_ReturnsInvalid() {
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"""
				/**SCHEMA_CONVERTERS*/{ BadShorthand(value) { return value; } }/**SCHEMA_CONVERTERS*/
			""");
		SchemaValidationResult result = SchemaValidationService.ValidateConverterDeclarations(body);
		result.IsValid.Should().BeFalse("because 'BadShorthand' has no dot");
		result.Errors.Should().ContainSingle(
			e => e.Contains("BadShorthand") && e.Contains("VendorPrefix"),
			because: "method-shorthand with an unquoted identifier key must be checked too");
	}

	[Test]
	[Description("Mixed quoted and identifier keys — both bad keys are reported")]
	public void ValidateConverterDeclarations_MixedQuotedAndIdentifierBadKeys_ReportsBoth() {
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"""
				/**SCHEMA_CONVERTERS*/
					{
						"usr.Good": function(value) { return value; },
						BadIdent: value => value,
						"BadQuoted": function(value) { return value; }
					}
				/**SCHEMA_CONVERTERS*/
			""");
		SchemaValidationResult result = SchemaValidationService.ValidateConverterDeclarations(body);
		result.IsValid.Should().BeFalse("because two keys violate the dot-format rule");
		result.Errors.Should().HaveCount(2,
			because: "both the unquoted identifier and the quoted dot-less key must be reported");
		result.Errors.Should().Contain(e => e.Contains("BadIdent"));
		result.Errors.Should().Contain(e => e.Contains("BadQuoted"));
		result.Errors.Should().NotContain(e => e.Contains("usr.Good"),
			because: "the prefixed key is well-formed");
	}

	#endregion

	#region ValidateHandlerStructure - request format

	[Test]
	[Description("Handler entry whose request value matches the VendorPrefix.HandlerName format passes validation")]
	public void ValidateHandlerStructure_RequestWithCorrectVendorPrefix_ReturnsValid() {
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
			"""
				/**SCHEMA_HANDLERS*/
					[
						{
							request: "crt.HandleViewModelInitRequest",
							handler: async (request, next) => { await next?.handle(request); }
						}
					]
				/**SCHEMA_HANDLERS*/
			""");
		SchemaValidationResult result = SchemaValidationService.ValidateHandlerStructure(body);
		result.IsValid.Should().BeTrue(
			"because 'crt.HandleViewModelInitRequest' is a well-formed VendorPrefix.HandlerName value");
		result.Errors.Should().BeEmpty("because a correctly prefixed request value produces no errors");
	}

	[Test]
	[Description("Handler entry whose request value misses the dot fails validation with a descriptive runtime-error message")]
	public void ValidateHandlerStructure_RequestWithoutDot_ReturnsInvalid() {
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
			"""
				/**SCHEMA_HANDLERS*/
					[
						{
							request: "BadHandlerRequest",
							handler: async (request, next) => { await next?.handle(request); }
						}
					]
				/**SCHEMA_HANDLERS*/
			""");
		SchemaValidationResult result = SchemaValidationService.ValidateHandlerStructure(body);
		result.IsValid.Should().BeFalse(
			"because 'BadHandlerRequest' violates the VendorPrefix.HandlerName format");
		result.Errors.Should().ContainSingle(
			e => e.Contains("BadHandlerRequest") && e.Contains("VendorPrefix.HandlerName") && e.Contains("page-schema-handlers"),
			because: "the error must name the offending request value, reference the format, and direct the agent at the handler guidance");
	}

	[Test]
	[Description("Handler entry with a leading-dot request value fails validation as malformed")]
	public void ValidateHandlerStructure_RequestStartingWithDot_ReturnsInvalid() {
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
			"""
				/**SCHEMA_HANDLERS*/
					[
						{
							request: ".LeadingDot",
							handler: async (request, next) => { await next?.handle(request); }
						}
					]
				/**SCHEMA_HANDLERS*/
			""");
		SchemaValidationResult result = SchemaValidationService.ValidateHandlerStructure(body);
		result.IsValid.Should().BeFalse(
			"because '.LeadingDot' has an empty vendor prefix and breaks Creatio's parser");
		result.Errors.Should().ContainSingle(
			e => e.Contains(".LeadingDot") && e.Contains("VendorPrefix"),
			because: "the validator should reject malformed request values, not only ones missing the dot entirely");
	}

	[Test]
	[Description("Mixed handler array with one good and one bad request reports exactly one error for the bad entry")]
	public void ValidateHandlerStructure_MixedRequestValues_ReportsOnlyBadOne() {
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
			"""
				/**SCHEMA_HANDLERS*/
					[
						{
							request: "crt.HandleViewModelInitRequest",
							handler: async (req, next) => { await next?.handle(req); }
						},
						{
							request: "BadRequest",
							handler: async (req, next) => { await next?.handle(req); }
						}
					]
				/**SCHEMA_HANDLERS*/
			""");
		SchemaValidationResult result = SchemaValidationService.ValidateHandlerStructure(body);
		result.IsValid.Should().BeFalse("because 'BadRequest' violates the VendorPrefix.HandlerName format");
		result.Errors.Should().ContainSingle(e => e.Contains("BadRequest"),
			because: "only the malformed request value should generate an error");
		result.Errors.Should().NotContain(e => e.Contains("crt.HandleViewModelInitRequest"),
			because: "the correctly prefixed request should not generate an error");
	}

	[Test]
	[Description("Empty handlers array passes validation (no entries to check)")]
	public void ValidateHandlerStructure_EmptyHandlers_ReturnsValid() {
		SchemaValidationResult result = SchemaValidationService.ValidateHandlerStructure(ValidListPageBody);
		result.IsValid.Should().BeTrue("because an empty SCHEMA_HANDLERS array has no entries to validate");
		result.Errors.Should().BeEmpty("because no entries means no format errors");
	}

	[Test]
	[Description("Handler entry with a trailing-dot request value fails validation as malformed")]
	public void ValidateHandlerStructure_RequestEndingWithDot_ReturnsInvalid() {
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
			"""
				/**SCHEMA_HANDLERS*/
					[
						{
							request: "usr.",
							handler: async (request, next) => { await next?.handle(request); }
						}
					]
				/**SCHEMA_HANDLERS*/
			""");
		SchemaValidationResult result = SchemaValidationService.ValidateHandlerStructure(body);
		result.IsValid.Should().BeFalse(
			"because 'usr.' has an empty handler name after the dot and breaks Creatio's parser");
		result.Errors.Should().ContainSingle(
			e => e.Contains("usr.") && e.Contains("VendorPrefix"),
			because: "trailing-dot request values must be flagged so the agent fixes them before the runtime does");
	}

	[Test]
	[Description("Handler entry with a multi-dot request value fails validation as malformed")]
	public void ValidateHandlerStructure_RequestWithMultipleDots_ReturnsInvalid() {
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
			"""
				/**SCHEMA_HANDLERS*/
					[
						{
							request: "usr.sub.HandleRequest",
							handler: async (request, next) => { await next?.handle(request); }
						}
					]
				/**SCHEMA_HANDLERS*/
			""");
		SchemaValidationResult result = SchemaValidationService.ValidateHandlerStructure(body);
		result.IsValid.Should().BeFalse(
			"because Creatio expects exactly one dot between the vendor prefix and the handler name");
		result.Errors.Should().ContainSingle(
			e => e.Contains("usr.sub.HandleRequest") && e.Contains("VendorPrefix"),
			because: "multi-dot request values must be flagged before the runtime parser rejects them");
	}

	#endregion

	#region ValidateValidatorDeclarations

	[Test]
	[Description("Null body returns valid without throwing")]
	public void ValidateValidatorDeclarations_NullBody_ReturnsValid() {
		SchemaValidationResult result = SchemaValidationService.ValidateValidatorDeclarations(null);
		result.IsValid.Should().BeTrue("because a null body is handled by the early-return guard");
		result.Errors.Should().BeEmpty("because a null body produces no validation errors");
	}

	[Test]
	[Description("Empty string body returns valid without throwing")]
	public void ValidateValidatorDeclarations_EmptyBody_ReturnsValid() {
		SchemaValidationResult result = SchemaValidationService.ValidateValidatorDeclarations(string.Empty);
		result.IsValid.Should().BeTrue("because an empty body is handled by the early-return guard");
		result.Errors.Should().BeEmpty("because an empty body produces no validation errors");
	}

	[Test]
	[Description("Empty validators object passes validation")]
	public void ValidateValidatorDeclarations_EmptyValidators_ReturnsValid() {
		SchemaValidationResult result = SchemaValidationService.ValidateValidatorDeclarations(ValidListPageBody);
		result.IsValid.Should().BeTrue("because an empty SCHEMA_VALIDATORS object has no keys to validate");
		result.Errors.Should().BeEmpty("because no keys means no format errors");
	}

	[Test]
	[Description("Validator with correct usr. prefix passes validation")]
	public void ValidateValidatorDeclarations_CorrectPrefixedKey_ReturnsValid() {
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"""
				/**SCHEMA_VALIDATORS*/{ "usr.RequiredValidator": { params: [] } }/**SCHEMA_VALIDATORS*/
			""");
		SchemaValidationResult result = SchemaValidationService.ValidateValidatorDeclarations(body);
		result.IsValid.Should().BeTrue("because 'usr.RequiredValidator' has the required VendorPrefix.Name dot-format");
		result.Errors.Should().BeEmpty("because a correctly prefixed key produces no errors");
	}

	[Test]
	[Description("Validator key without a dot fails validation with a descriptive runtime-error message")]
	public void ValidateValidatorDeclarations_KeyWithoutDot_ReturnsInvalid() {
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"""
				/**SCHEMA_VALIDATORS*/{ "RequiredValidator": { params: [] } }/**SCHEMA_VALIDATORS*/
			""");
		SchemaValidationResult result = SchemaValidationService.ValidateValidatorDeclarations(body);
		result.IsValid.Should().BeFalse("because 'RequiredValidator' lacks a dot and violates the VendorPrefix.ValidatorName format");
		result.Errors.Should().Contain(error => error.Contains("RequiredValidator") && error.Contains("VendorPrefix.ValidatorName"),
			"because the error message should mention both the key name and the required format");
	}

	[Test]
	[Description("Mixed validators with good and bad keys reports only the bad one")]
	public void ValidateValidatorDeclarations_MixedKeys_ReportsOnlyBadOne() {
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"""
				/**SCHEMA_VALIDATORS*/
					{
						"usr.GoodValidator": { params: [] },
						"BadValidator": { params: [] }
					}
				/**SCHEMA_VALIDATORS*/
			""");
		SchemaValidationResult result = SchemaValidationService.ValidateValidatorDeclarations(body);
		result.IsValid.Should().BeFalse("because 'BadValidator' lacks a dot");
		result.Errors.Should().HaveCount(1, "because only 'BadValidator' fails the validation");
		result.Errors[0].Should().Contain("BadValidator", "because the error should mention the bad key");
	}

	#endregion

	#region VendorPrefixedName format edge cases

	[Test]
	[Description("Converter key starting with a dot is rejected as malformed even though it contains a dot")]
	public void ValidateConverterDeclarations_KeyStartingWithDot_ReturnsInvalid() {
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"""
				/**SCHEMA_CONVERTERS*/{ ".LeadingDot": value => value }/**SCHEMA_CONVERTERS*/
			""");
		SchemaValidationResult result = SchemaValidationService.ValidateConverterDeclarations(body);
		result.IsValid.Should().BeFalse(
			"because a leading dot leaves the prefix empty and breaks Creatio's VendorPrefix.Name parser");
		result.Errors.Should().ContainSingle(e => e.Contains(".LeadingDot") && e.Contains("VendorPrefix"),
			because: "the validator should reject malformed keys, not only keys missing the dot entirely");
	}

	[Test]
	[Description("Converter key ending with a dot is rejected as malformed")]
	public void ValidateConverterDeclarations_KeyEndingWithDot_ReturnsInvalid() {
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"""
				/**SCHEMA_CONVERTERS*/{ "usr.": value => value }/**SCHEMA_CONVERTERS*/
			""");
		SchemaValidationResult result = SchemaValidationService.ValidateConverterDeclarations(body);
		result.IsValid.Should().BeFalse(
			"because an empty name after the dot breaks Creatio's VendorPrefix.Name parser");
		result.Errors.Should().ContainSingle(e => e.Contains("usr.") && e.Contains("VendorPrefix"),
			because: "the validator should reject trailing-dot keys, not accept them as 'has a dot'");
	}

	[Test]
	[Description("Converter key with two dots is rejected as malformed")]
	public void ValidateConverterDeclarations_KeyWithMultipleDots_ReturnsInvalid() {
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"""
				/**SCHEMA_CONVERTERS*/{ "usr.sub.Name": value => value }/**SCHEMA_CONVERTERS*/
			""");
		SchemaValidationResult result = SchemaValidationService.ValidateConverterDeclarations(body);
		result.IsValid.Should().BeFalse(
			"because Creatio expects exactly one dot between prefix and name");
		result.Errors.Should().ContainSingle(e => e.Contains("usr.sub.Name") && e.Contains("VendorPrefix"),
			because: "multi-dot keys should be flagged so the agent fixes them before the runtime does");
	}

	[Test]
	[Description("Validator error message references the page-schema-validators guidance, not the handler one")]
	public void ValidateValidatorDeclarations_ErrorMessage_ReferencesValidatorGuidance() {
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"""
				/**SCHEMA_VALIDATORS*/{ "BadValidator": { params: [] } }/**SCHEMA_VALIDATORS*/
			""");
		SchemaValidationResult result = SchemaValidationService.ValidateValidatorDeclarations(body);
		result.IsValid.Should().BeFalse("because 'BadValidator' has no dot");
		result.Errors.Should().ContainSingle(
			e => e.Contains("page-schema-validators") && !e.Contains("page-schema-handlers"),
			because: "validator errors must point the agent at validator guidance, not handler guidance");
	}

	#endregion

	#region ValidateCustomValidatorFactoryShape

	[Test]
	[Description("Empty body passes validator factory shape check without errors")]
	public void ValidateCustomValidatorFactoryShape_EmptyBody_ReturnsValid() {
		// Arrange & Act
		SchemaValidationResult result = SchemaValidationService.ValidateCustomValidatorFactoryShape(string.Empty);

		// Assert
		result.IsValid.Should().BeTrue("because there is nothing to validate");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Page body with empty SCHEMA_VALIDATORS passes validator factory shape check")]
	public void ValidateCustomValidatorFactoryShape_EmptyValidators_ReturnsValid() {
		// Arrange & Act
		SchemaValidationResult result = SchemaValidationService.ValidateCustomValidatorFactoryShape(ValidListPageBody);

		// Assert
		result.IsValid.Should().BeTrue("because the validators section is an empty object");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Canonical factory validator with quoted property keys passes the factory shape check")]
	public void ValidateCustomValidatorFactoryShape_CanonicalQuotedKeys_ReturnsValid() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"""
				/**SCHEMA_VALIDATORS*/
					{
						"usr.UpperCaseValidator": {
							"validator":
								function(config){
									return function(control){
										var v=control.value;
										if(!v||v===v.toUpperCase())return null;
										return{"usr.UpperCaseValidator":{message:config.message}};
									};
								},
							"params":[{"name":"message"}],
							"async":false
						}
					}
				/**SCHEMA_VALIDATORS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateCustomValidatorFactoryShape(body);

		// Assert
		result.IsValid.Should().BeTrue("because the validator follows the canonical factory shape");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Canonical factory validator with unquoted property keys passes the factory shape check")]
	public void ValidateCustomValidatorFactoryShape_CanonicalUnquotedKeys_ReturnsValid() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"""
				/**SCHEMA_VALIDATORS*/
					{
						"usr.UpperCaseValidator": {
							validator:function(config){return function(control){return null;};},
							params:[{name:"message"}],
							async:false
						}
					}
				/**SCHEMA_VALIDATORS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateCustomValidatorFactoryShape(body);

		// Assert
		result.IsValid.Should().BeTrue("because unquoted JS property keys are valid syntax for SCHEMA_VALIDATORS");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Async outer factory function (async config => returns inner function) passes the factory shape check")]
	public void ValidateCustomValidatorFactoryShape_AsyncOuterFactory_ReturnsValid() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"""
				/**SCHEMA_VALIDATORS*/
					{
						"usr.AsyncValidator": {
							"validator":
								async function(config){
									return async function(control){return null;};
								},
							"params":[{"name":"message"}],
							"async":true
						}
					}
				/**SCHEMA_VALIDATORS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateCustomValidatorFactoryShape(body);

		// Assert
		result.IsValid.Should().BeTrue("because async factories are valid as long as the outer returns the inner function");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Arrow factory (config) => (control) => result passes the factory shape check")]
	public void ValidateCustomValidatorFactoryShape_ArrowFactory_ReturnsValid() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"""
				/**SCHEMA_VALIDATORS*/
					{
						"usr.ArrowValidator": {
							"validator":(config) => (control) => null,
							"params":[{"name":"message"}],
							"async":false
						}
					}
				/**SCHEMA_VALIDATORS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateCustomValidatorFactoryShape(body);

		// Assert
		result.IsValid.Should().BeTrue("because curried arrow functions are a valid factory form");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Function returning an arrow inner validator passes the factory shape check")]
	public void ValidateCustomValidatorFactoryShape_FunctionReturningArrow_ReturnsValid() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"""
				/**SCHEMA_VALIDATORS*/
					{
						"usr.MixedValidator": {
							"validator":function(config){return (control) => null;},
							"params":[{"name":"message"}],
							"async":false
						}
					}
				/**SCHEMA_VALIDATORS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateCustomValidatorFactoryShape(body);

		// Assert
		result.IsValid.Should().BeTrue("because returning an arrow function from a function expression is a valid factory");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Method-shorthand factory 'validator(config) { return function(control) {...}; }' passes the factory shape check")]
	public void ValidateCustomValidatorFactoryShape_MethodShorthandFactory_ReturnsValid() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"""
				/**SCHEMA_VALIDATORS*/
					{
						"usr.ShorthandValidator": {
							validator(config){return function(control){return null;};},
							params:[{name:"message"}],
							async:false
						}
					}
				/**SCHEMA_VALIDATORS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateCustomValidatorFactoryShape(body);

		// Assert
		result.IsValid.Should().BeTrue("because method-shorthand syntax is valid JS for object methods and the body still returns a function");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Built-in crt.* validator names are skipped — only usr.* validators are checked for factory shape")]
	public void ValidateCustomValidatorFactoryShape_CrtPrefixSkipped_ReturnsValid() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"""
				/**SCHEMA_VALIDATORS*/{"crt.Required":{}}/**SCHEMA_VALIDATORS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateCustomValidatorFactoryShape(body);

		// Assert
		result.IsValid.Should().BeTrue("because crt.* validators are referenced but not declared with bodies in SCHEMA_VALIDATORS");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Validator type without a vendor prefix dot is skipped — naming is enforced by ValidateValidatorDeclarations")]
	public void ValidateCustomValidatorFactoryShape_MalformedNameSkipped_ReturnsValid() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"""
				/**SCHEMA_VALIDATORS*/
					{
						"BadName": {
							validator: function(c){return function(x){return null;};}
						}
					}
				/**SCHEMA_VALIDATORS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateCustomValidatorFactoryShape(body);

		// Assert
		result.IsValid.Should().BeTrue("because malformed validator names are reported by ValidateValidatorDeclarations, not the factory shape check");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Validator using 'validate' key instead of 'validator' is rejected with a clear renaming hint")]
	public void ValidateCustomValidatorFactoryShape_WrongKeyValidate_ReturnsInvalid() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"""
				/**SCHEMA_VALIDATORS*/
					{
						"usr.PhoneFormatValidator": {
							async:false,
							params:[{"name":"message"}],
							validate:
								function(value,config){
									if(!value)return null;
									return{"usr.PhoneFormatValidator":{message:config.message}};
								}
						}
					}
				/**SCHEMA_VALIDATORS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateCustomValidatorFactoryShape(body);

		// Assert
		result.IsValid.Should().BeFalse("because Creatio runtime ignores keys other than 'validator', so the validator never executes");
		result.Errors.Should().ContainSingle(e =>
				e.Contains("usr.PhoneFormatValidator") && e.Contains("'validate'") && e.Contains("'validator'") &&
				e.Contains("page-schema-validators"),
			because: "the error must name the offending validator and the wrong key, and point to the validator guidance");
	}

	[Test]
	[Description("Validator using 'fn' key instead of 'validator' is rejected as a misleading alias")]
	public void ValidateCustomValidatorFactoryShape_WrongKeyFn_ReturnsInvalid() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"""
				/**SCHEMA_VALIDATORS*/
					{
						"usr.MyValidator": {fn:function(c){return function(x){return null;};}}
					}
				/**SCHEMA_VALIDATORS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateCustomValidatorFactoryShape(body);

		// Assert
		result.IsValid.Should().BeFalse("because 'fn' is not the runtime-recognised key");
		result.Errors.Should().ContainSingle(e => e.Contains("usr.MyValidator") && e.Contains("'fn'"),
			because: "the misleading alias should be reported alongside the validator name");
	}

	[Test]
	[Description("Validator missing the 'validator' key entirely is rejected with a missing-key message")]
	public void ValidateCustomValidatorFactoryShape_MissingValidatorKey_ReturnsInvalid() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"""
				/**SCHEMA_VALIDATORS*/
					{
						"usr.NoBody": {params:[{"name":"message"}],async:false}
					}
				/**SCHEMA_VALIDATORS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateCustomValidatorFactoryShape(body);

		// Assert
		result.IsValid.Should().BeFalse("because the validator object has no 'validator' key at all");
		result.Errors.Should().ContainSingle(e =>
				e.Contains("usr.NoBody") && e.Contains("missing the required 'validator' key"),
			because: "the error must explain that the canonical 'validator' key is required");
	}

	[Test]
	[Description("Validator whose 'validator' value is a string literal instead of a function is rejected")]
	public void ValidateCustomValidatorFactoryShape_ValidatorValueIsString_ReturnsInvalid() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"""
				/**SCHEMA_VALIDATORS*/{"usr.StringValidator":{validator:"not a function"}}/**SCHEMA_VALIDATORS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateCustomValidatorFactoryShape(body);

		// Assert
		result.IsValid.Should().BeFalse("because the validator value must be a function expression");
		result.Errors.Should().ContainSingle(e => e.Contains("usr.StringValidator") && e.Contains("not a function"),
			because: "the error must name the validator and report that the value is not callable");
	}

	[Test]
	[Description("Validator whose 'validator' value is an object literal instead of a function is rejected")]
	public void ValidateCustomValidatorFactoryShape_ValidatorValueIsObject_ReturnsInvalid() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"""
				/**SCHEMA_VALIDATORS*/{"usr.ObjectValidator":{validator:{check:true}}}/**SCHEMA_VALIDATORS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateCustomValidatorFactoryShape(body);

		// Assert
		result.IsValid.Should().BeFalse("because the validator value must be a function, not an object");
		result.Errors.Should().ContainSingle(e => e.Contains("usr.ObjectValidator") && e.Contains("not a function"));
	}

	[Test]
	[Description("Flat 'validator: function(value, config) {...}' instead of factory is rejected with the factory-shape hint")]
	public void ValidateCustomValidatorFactoryShape_FlatFunctionInsteadOfFactory_ReturnsInvalid() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"""
				/**SCHEMA_VALIDATORS*/
					{
						"usr.FlatValidator": {
							validator:
								function(value,config){
									if(!value)return null;
									return{"usr.FlatValidator":{message:config.message}};
								},
							params:[{"name":"message"}],
							async:false
						}
					}
				/**SCHEMA_VALIDATORS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateCustomValidatorFactoryShape(body);

		// Assert
		result.IsValid.Should().BeFalse("because a flat function never returns the inner validator the runtime expects");
		result.Errors.Should().ContainSingle(e =>
				e.Contains("usr.FlatValidator") && e.Contains("flat") && e.Contains("factory"),
			because: "the error must call out the flat-vs-factory mismatch and point to the canonical shape");
	}

	[Test]
	[Description("Flat single-arrow validator '(control) => null' is rejected (no inner returned function)")]
	public void ValidateCustomValidatorFactoryShape_FlatSingleArrow_ReturnsInvalid() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"""
				/**SCHEMA_VALIDATORS*/
					{
						"usr.FlatArrowValidator":
							{
								validator:(control) => null,
								params:[{"name":"message"}]
							}
					}
				/**SCHEMA_VALIDATORS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateCustomValidatorFactoryShape(body);

		// Assert
		result.IsValid.Should().BeFalse("because a single arrow that returns a primitive is flat, not a factory");
		result.Errors.Should().ContainSingle(e => e.Contains("usr.FlatArrowValidator") && e.Contains("factory"));
	}

	[Test]
	[Description("Flat function whose body contains the literal text 'return function' inside a string is still rejected")]
	public void ValidateCustomValidatorFactoryShape_FlatFunctionWithMisleadingStringLiteral_ReturnsInvalid() {
		// Arrange — the string literal 'return function' must NOT be treated as a real factory return.
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"""
				/**SCHEMA_VALIDATORS*/
					{
						"usr.MisleadingValidator": {
							validator:
								function(value,config){
									var hint="return function(control){...}";
									if(!value)return null;
									return null;
								},
							params:[{"name":"message"}]
						}
					}
				/**SCHEMA_VALIDATORS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateCustomValidatorFactoryShape(body);

		// Assert
		result.IsValid.Should().BeFalse("because string-literal content must not be treated as real factory-return code");
		result.Errors.Should().ContainSingle(e => e.Contains("usr.MisleadingValidator") && e.Contains("factory"),
			because: "the sanitiser must blank string literals before the regex scan to avoid false positives");
	}

	#endregion

	#region ValidateConverterFunctionShape

	[Test]
	[Description("Empty body passes converter function shape check without errors")]
	public void ValidateConverterFunctionShape_EmptyBody_ReturnsValid() {
		// Arrange & Act
		SchemaValidationResult result = SchemaValidationService.ValidateConverterFunctionShape(string.Empty);

		// Assert
		result.IsValid.Should().BeTrue();
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Page body with empty SCHEMA_CONVERTERS passes the function shape check")]
	public void ValidateConverterFunctionShape_EmptyConverters_ReturnsValid() {
		// Arrange & Act
		SchemaValidationResult result = SchemaValidationService.ValidateConverterFunctionShape(ValidListPageBody);

		// Assert
		result.IsValid.Should().BeTrue("because the converters section is an empty object");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Converter as classic function expression passes the function shape check")]
	public void ValidateConverterFunctionShape_FunctionExpression_ReturnsValid() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"""
				/**SCHEMA_CONVERTERS*/
					{
						"usr.ToUpperCase": function(value){
							return value && value.toUpperCase();
						}
					}
				/**SCHEMA_CONVERTERS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateConverterFunctionShape(body);

		// Assert
		result.IsValid.Should().BeTrue();
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Converter as arrow function passes the function shape check")]
	public void ValidateConverterFunctionShape_ArrowFunction_ReturnsValid() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"""
				/**SCHEMA_CONVERTERS*/
					{
						"usr.ToCallDisplay": (value) => value ? "Call: " + value : ""
					}
				/**SCHEMA_CONVERTERS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateConverterFunctionShape(body);

		// Assert
		result.IsValid.Should().BeTrue();
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Async converter (function or arrow) passes the function shape check")]
	public void ValidateConverterFunctionShape_AsyncFunction_ReturnsValid() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"""
				/**SCHEMA_CONVERTERS*/
					{
						"usr.FormatPhone": async function(value){return value;}
					}
				/**SCHEMA_CONVERTERS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateConverterFunctionShape(body);

		// Assert
		result.IsValid.Should().BeTrue("because async converters are explicitly allowed");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Method-shorthand converter '\"usr.X\"(value) {...}' passes the function shape check")]
	public void ValidateConverterFunctionShape_MethodShorthand_ReturnsValid() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"""
				/**SCHEMA_CONVERTERS*/{"usr.ShorthandConverter"(value){return value;}}/**SCHEMA_CONVERTERS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateConverterFunctionShape(body);

		// Assert
		result.IsValid.Should().BeTrue("because method-shorthand syntax is valid JS for object-literal methods");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Built-in crt.* converter names are skipped — only usr.* converters are checked for function shape")]
	public void ValidateConverterFunctionShape_CrtPrefixSkipped_ReturnsValid() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"""
				/**SCHEMA_CONVERTERS*/{"crt.ToBoolean":{}}/**SCHEMA_CONVERTERS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateConverterFunctionShape(body);

		// Assert
		result.IsValid.Should().BeTrue("because crt.* converters are built-in and not declared with bodies in SCHEMA_CONVERTERS");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Converter whose value is an object literal instead of a function is rejected")]
	public void ValidateConverterFunctionShape_ObjectLiteralValue_ReturnsInvalid() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"""
				/**SCHEMA_CONVERTERS*/{"usr.WrongShape":{transform:"upper"}}/**SCHEMA_CONVERTERS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateConverterFunctionShape(body);

		// Assert
		result.IsValid.Should().BeFalse("because converters must be function expressions, not config objects");
		result.Errors.Should().ContainSingle(e =>
				e.Contains("usr.WrongShape") && e.Contains("not callable") && e.Contains("page-schema-converters"),
			because: "the error must identify the converter and point to the converter guidance");
	}

	[Test]
	[Description("Converter whose value is a string literal instead of a function is rejected")]
	public void ValidateConverterFunctionShape_StringValue_ReturnsInvalid() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"""
				/**SCHEMA_CONVERTERS*/{"usr.StringConverter":"upperCase"}/**SCHEMA_CONVERTERS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateConverterFunctionShape(body);

		// Assert
		result.IsValid.Should().BeFalse("because a string is not callable");
		result.Errors.Should().ContainSingle(e => e.Contains("usr.StringConverter") && e.Contains("not callable"));
	}

	[Test]
	[Description("Converter whose value is an array literal instead of a function is rejected")]
	public void ValidateConverterFunctionShape_ArrayValue_ReturnsInvalid() {
		// Arrange
		string body = ValidListPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"""
				/**SCHEMA_CONVERTERS*/{"usr.ArrayConverter":[1,2,3]}/**SCHEMA_CONVERTERS*/
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateConverterFunctionShape(body);

		// Assert
		result.IsValid.Should().BeFalse("because an array is not callable");
		result.Errors.Should().ContainSingle(e => e.Contains("usr.ArrayConverter") && e.Contains("not callable"));
	}

	#endregion

        #region ValidateMobileBody

	[Test]
	[Description("Returns valid result when the mobile body contains only allowed top-level keys.")]
	public void ValidateMobileBody_WhenBodyIsValidMobileJson_ReturnsValid() {
		// Arrange
		string body = """
		              {
		                "viewConfigDiff": [],
		                "viewModelConfigDiff": [],
		                "modelConfigDiff": []
		              }
		              """;

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileBody(body);

		// Assert
		result.IsValid.Should().BeTrue("because a well-formed mobile JSON body should pass validation");
		result.Errors.Should().BeEmpty("because no disallowed keys are present");
	}

	[Test]
	[Description("Rejects a mobile body that contains a 'validators' section.")]
	public void ValidateMobileBody_WhenBodyContainsValidators_ReturnsError() {
		// Arrange
		string body = """
		              {
		                "viewConfigDiff": [],
		                "validators": {}
		              }
		              """;

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileBody(body);

		// Assert
		result.IsValid.Should().BeFalse("because mobile pages do not support validators");
		result.Errors.Should().ContainSingle(e => e.Contains("validators"),
			because: "the error must identify the disallowed 'validators' key");
	}

	[Test]
	[Description("Rejects a mobile body that contains a 'handlers' section.")]
	public void ValidateMobileBody_WhenBodyContainsHandlers_ReturnsError() {
		// Arrange
		string body = """
		              {
		                "viewConfigDiff": [],
		                "handlers": []
		              }
		              """;

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileBody(body);

		// Assert
		result.IsValid.Should().BeFalse("because mobile pages do not support handlers");
		result.Errors.Should().ContainSingle(e => e.Contains("handlers"),
			because: "the error must identify the disallowed 'handlers' key");
	}

	[Test]
	[Description("Rejects a mobile body that contains a 'converters' section.")]
	public void ValidateMobileBody_WhenBodyContainsConverters_ReturnsError() {
		// Arrange
		string body = """
		              {
		                "viewConfigDiff": [],
		                "converters": {}
		              }
		              """;

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileBody(body);

		// Assert
		result.IsValid.Should().BeFalse("because mobile pages do not support custom converters");
		result.Errors.Should().ContainSingle(e => e.Contains("converters"),
			because: "the error must identify the disallowed 'converters' key");
	}

	[Test]
	[Description("Reports multiple errors when the mobile body contains more than one disallowed key.")]
	public void ValidateMobileBody_WhenBodyContainsMultipleDisallowedKeys_ReportsAllErrors() {
		// Arrange
		string body = """
		              {
		                "viewConfigDiff": [],
		                "validators": {},
		                "handlers": []
		              }
		              """;

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileBody(body);

		// Assert
		result.IsValid.Should().BeFalse("because both 'validators' and 'handlers' are disallowed in mobile pages");
		result.Errors.Should().HaveCount(2,
			because: "each disallowed key should produce a distinct error");
	}

	[Test]
	[Description("Returns an error when the body is not valid JSON.")]
	public void ValidateMobileBody_WhenBodyIsNotValidJson_ReturnsError() {
		// Arrange
		string body = "this is not json";

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileBody(body);

		// Assert
		result.IsValid.Should().BeFalse("because invalid JSON should fail mobile body validation");
		result.Errors.Should().NotBeEmpty("because the JSON parse error should be reported");
	}

	[Test]
	[Description("Rejects a mobile body where a diff property is not a JSON array.")]
	[TestCase("viewConfigDiff")]
	[TestCase("viewModelConfigDiff")]
	[TestCase("modelConfigDiff")]
	public void ValidateMobileBody_WhenDiffPropertyIsNotArray_ReturnsError(string propertyName) {
		// Arrange
		string body = $$"""{ "{{propertyName}}": {} }""";

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileBody(body);

		// Assert
		result.IsValid.Should().BeFalse($"because '{propertyName}' must be a JSON array");
		result.Errors.Should().Contain(e => e.Contains(propertyName) && e.Contains("array"),
			$"because the error must identify '{propertyName}' and the expected type");
	}

	[Test]
	[Description("Accepts a mobile body where diff properties are valid JSON arrays.")]
	public void ValidateMobileBody_WhenDiffPropertiesAreArrays_ReturnsValid() {
		// Arrange
		string body = """
		              {
		                "viewConfigDiff": [],
		                "viewModelConfigDiff": [],
		                "modelConfigDiff": []
		              }
		              """;

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileBody(body);

		// Assert
		result.IsValid.Should().BeTrue("because all diff properties are valid JSON arrays");
		result.Errors.Should().BeEmpty("because no validation errors should be raised");
	}

	[Test]
	[Description("Rejects a mobile body where viewModelConfig is not a JSON object.")]
	public void ValidateMobileBody_WhenViewModelConfigIsNotObject_ReturnsError() {
		// Arrange
		string body = """{ "viewModelConfig": [] }""";

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileBody(body);

		// Assert
		result.IsValid.Should().BeFalse("because 'viewModelConfig' must be a JSON object");
		result.Errors.Should().Contain(e => e.Contains("viewModelConfig") && e.Contains("object"),
			"because the error must identify 'viewModelConfig' and the expected type");
	}

	[Test]
	[Description("Rejects a mobile body where modelConfig is not a JSON object.")]
	public void ValidateMobileBody_WhenModelConfigIsNotObject_ReturnsError() {
		// Arrange
		string body = """{ "modelConfig": "string" }""";

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileBody(body);

		// Assert
		result.IsValid.Should().BeFalse("because 'modelConfig' must be a JSON object");
		result.Errors.Should().Contain(e => e.Contains("modelConfig") && e.Contains("object"),
			"because the error must identify 'modelConfig' and the expected type");
	}

	[Test]
	[Description("Accepts a mobile body where viewModelConfig and modelConfig are valid JSON objects.")]
	public void ValidateMobileBody_WhenConfigPropertiesAreObjects_ReturnsValid() {
		// Arrange
		string body = """
		              {
		                "viewConfigDiff": [],
		                "viewModelConfig": { "attributes": {} },
		                "modelConfig": { "dataSources": {} }
		              }
		              """;

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileBody(body);

		// Assert
		result.IsValid.Should().BeTrue("because config properties are valid JSON objects");
		result.Errors.Should().BeEmpty("because no validation errors should be raised");
	}

	[Test]
	[Description("Reports multiple errors when both diff and config properties have wrong types.")]
	public void ValidateMobileBody_WhenMultiplePropertiesHaveWrongType_ReportsAllErrors() {
		// Arrange
		string body = """{ "viewConfigDiff": {}, "viewModelConfig": [] }""";

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileBody(body);

		// Assert
		result.IsValid.Should().BeFalse("because both properties have invalid types");
		result.Errors.Should().HaveCount(2,
			"because each property with the wrong type should produce a distinct error");
	}

	[Test]
	[Description("Rejects a mobile body that contains an unknown root property.")]
	public void ValidateMobileBody_WhenUnknownRootProperty_ReturnsError() {
		// Arrange
		string body = """
		              {
		                "viewConfigDiff": [],
		                "customProperty": true
		              }
		              """;

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileBody(body);

		// Assert
		result.IsValid.Should().BeFalse("because 'customProperty' is not an allowed mobile root property");
		result.Errors.Should().ContainSingle(e => e.Contains("customProperty") && e.Contains("Unknown"),
			"because the error must identify the unknown property");
	}

	[Test]
	[Description("Does not double-report disallowed keys (validators, handlers, converters) as unknown properties.")]
	public void ValidateMobileBody_WhenDisallowedKeyPresent_DoesNotDoubleReport() {
		// Arrange
		string body = """{ "viewConfigDiff": [], "validators": {} }""";

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileBody(body);

		// Assert
		result.IsValid.Should().BeFalse("because 'validators' is disallowed on mobile");
		result.Errors.Should().HaveCount(1,
			"because 'validators' should only be reported once by the disallowed-key check, not also by the unknown-property check");
	}

	[Test]
	[Description("Reports both disallowed and unknown properties without cross-contamination.")]
	public void ValidateMobileBody_WhenBothDisallowedAndUnknownProperties_ReportsSeparateErrors() {
		// Arrange
		string body = """{ "viewConfigDiff": [], "handlers": [], "foo": 42 }""";

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileBody(body);

		// Assert
		result.IsValid.Should().BeFalse("because both disallowed and unknown properties are present");
		result.Errors.Should().HaveCount(2,
			"because 'handlers' gets one error and 'foo' gets one error");
		result.Errors.Should().Contain(e => e.Contains("handlers"),
			"because 'handlers' should be reported as disallowed");
		result.Errors.Should().Contain(e => e.Contains("foo") && e.Contains("Unknown"),
			"because 'foo' should be reported as unknown");
	}

        #endregion

	#region ValidateMobileNoValidatorReferences

	[Test]
	[Description("Returns valid when no attribute in viewModelConfigDiff binds a validators property.")]
	public void ValidateMobileNoValidatorReferences_WhenNoValidators_ReturnsValid() {
		// Arrange
		string body = """
		              {
		                "viewConfigDiff": [],
		                "viewModelConfigDiff": [
		                  {
		                    "operation": "merge",
		                    "path": ["attributes"],
		                    "values": {
		                      "UsrName": {
		                        "modelConfig": { "path": "PDS.UsrName" }
		                      }
		                    }
		                  }
		                ],
		                "modelConfigDiff": []
		              }
		              """;

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileNoValidatorReferences(body);

		// Assert
		result.IsValid.Should().BeTrue("because no attribute binds validators");
		result.Errors.Should().BeEmpty("because there are no validator references to report");
	}

	[Test]
	[Description("Reports an error when a viewModelConfigDiff attribute binds a validators property.")]
	public void ValidateMobileNoValidatorReferences_WhenDiffAttributeBindsValidators_ReturnsError() {
		// Arrange
		string body = """
		              {
		                "viewConfigDiff": [],
		                "viewModelConfigDiff": [
		                  {
		                    "operation": "merge",
		                    "path": ["attributes"],
		                    "values": {
		                      "UsrName": {
		                        "modelConfig": { "path": "PDS.UsrName" },
		                        "validators": {
		                          "Required": { "type": "crt.Required" }
		                        }
		                      }
		                    }
		                  }
		                ],
		                "modelConfigDiff": []
		              }
		              """;

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileNoValidatorReferences(body);

		// Assert
		result.IsValid.Should().BeFalse("because mobile pages do not support validator bindings");
		result.Errors.Should().ContainSingle(e => e.Contains("UsrName"),
			because: "the error must identify the attribute that binds validators");
	}

	[Test]
	[Description("Reports an error when a viewModelConfig attribute binds a validators property.")]
	public void ValidateMobileNoValidatorReferences_WhenConfigAttributeBindsValidators_ReturnsError() {
		// Arrange
		string body = """
		              {
		                "viewConfigDiff": [],
		                "viewModelConfig": {
		                  "attributes": {
		                    "UsrEmail": {
		                      "modelConfig": { "path": "PDS.UsrEmail" },
		                      "validators": {
		                        "EmailFormat": { "type": "usr.EmailValidator" }
		                      }
		                    }
		                  }
		                },
		                "modelConfigDiff": []
		              }
		              """;

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileNoValidatorReferences(body);

		// Assert
		result.IsValid.Should().BeFalse("because mobile pages do not support validator bindings");
		result.Errors.Should().ContainSingle(e => e.Contains("UsrEmail"),
			because: "the error must identify the attribute that binds validators");
	}

	[Test]
	[Description("Reports multiple errors when several attributes in viewModelConfigDiff bind validators.")]
	public void ValidateMobileNoValidatorReferences_WhenMultipleAttributesBindValidators_ReportsAll() {
		// Arrange
		string body = """
		              {
		                "viewConfigDiff": [],
		                "viewModelConfigDiff": [
		                  {
		                    "operation": "merge",
		                    "path": ["attributes"],
		                    "values": {
		                      "UsrName": {
		                        "modelConfig": { "path": "PDS.UsrName" },
		                        "validators": { "Required": { "type": "crt.Required" } }
		                      },
		                      "UsrPhone": {
		                        "modelConfig": { "path": "PDS.UsrPhone" },
		                        "validators": { "PhoneFormat": { "type": "usr.PhoneValidator" } }
		                      }
		                    }
		                  }
		                ],
		                "modelConfigDiff": []
		              }
		              """;

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileNoValidatorReferences(body);

		// Assert
		result.IsValid.Should().BeFalse("because both attributes bind validators on a mobile page");
		result.Errors.Should().HaveCount(2,
			because: "each attribute with a validators binding should produce a distinct error");
	}

	[Test]
	[Description("Returns valid when the body is empty or null — edge case handled gracefully.")]
	public void ValidateMobileNoValidatorReferences_WhenBodyIsEmpty_ReturnsValid() {
		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileNoValidatorReferences("");

		// Assert
		result.IsValid.Should().BeTrue("because an empty body has no validator references to report");
	}

	[Test]
	[Description("Returns valid when the body is invalid JSON — JSON errors are reported by ValidateMobileBody.")]
	public void ValidateMobileNoValidatorReferences_WhenBodyIsInvalidJson_ReturnsValid() {
		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileNoValidatorReferences("not json");

		// Assert
		result.IsValid.Should().BeTrue("because JSON parsing errors are reported by ValidateMobileBody, not this method");
	}

	[Test]
	[Description("Returns valid when viewModelConfigDiff entries do not target the attributes path.")]
	public void ValidateMobileNoValidatorReferences_WhenDiffDoesNotTargetAttributes_ReturnsValid() {
		// Arrange
		string body = """
		              {
		                "viewConfigDiff": [],
		                "viewModelConfigDiff": [
		                  {
		                    "operation": "merge",
		                    "path": ["details"],
		                    "values": {
		                      "SomeDetail": { "validators": { "X": { "type": "crt.Required" } } }
		                    }
		                  }
		                ],
		                "modelConfigDiff": []
		              }
		              """;

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileNoValidatorReferences(body);

		// Assert
		result.IsValid.Should().BeTrue(
			"because the diff targets 'details' not 'attributes', so ShouldScanAsAttributesContainer should skip it");
	}

        #endregion

	#region ValidateMobileComponentTypes

	[Test]
	[Description("Returns valid when all mobile component types are in the allowed set.")]
	public void ValidateMobileComponentTypes_WhenAllTypesAllowed_ReturnsValid() {
		// Arrange
		string body = """
		              {
		                "viewConfigDiff": [
		                  {"operation":"insert","name":"Field1","values":{"type":"crt.Input"}},
		                  {"operation":"insert","name":"Field2","values":{"type":"crt.Button"}}
		                ]
		              }
		              """;
		HashSet<string> allowed = new(StringComparer.OrdinalIgnoreCase) { "crt.Input", "crt.Button" };
		HashSet<string> webOnly = new(StringComparer.OrdinalIgnoreCase);

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileComponentTypes(body, allowed, webOnly);

		// Assert
		result.IsValid.Should().BeTrue("because both component types are in the allowed set");
		result.Warnings.Should().BeEmpty("because no web-only components were found");
	}

	[Test]
	[Description("Produces a warning when a web-only component type is used in a mobile page.")]
	public void ValidateMobileComponentTypes_WhenWebOnlyTypeUsed_ReturnsWarning() {
		// Arrange
		string body = """
		              {
		                "viewConfigDiff": [
		                  {"operation":"insert","name":"Grid1","values":{"type":"crt.DataGrid"}}
		                ]
		              }
		              """;
		HashSet<string> allowed = new(StringComparer.OrdinalIgnoreCase) { "crt.Input", "crt.Button" };
		HashSet<string> webOnly = new(StringComparer.OrdinalIgnoreCase) { "crt.DataGrid" };

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileComponentTypes(body, allowed, webOnly);

		// Assert
		result.IsValid.Should().BeTrue("because web-only types produce warnings, not errors");
		result.Warnings.Should().ContainSingle(w => w.Contains("crt.DataGrid"),
			because: "the warning must identify the web-only component type");
	}

	[Test]
	[Description("Returns valid when viewConfigDiff is empty or absent.")]
	public void ValidateMobileComponentTypes_WhenNoViewConfigDiff_ReturnsValid() {
		// Arrange
		string body = """{"viewModelConfigDiff":[]}""";
		HashSet<string> allowed = new(StringComparer.OrdinalIgnoreCase) { "crt.Input" };
		HashSet<string> webOnly = new(StringComparer.OrdinalIgnoreCase);

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileComponentTypes(body, allowed, webOnly);

		// Assert
		result.IsValid.Should().BeTrue("because there are no component types to validate");
	}

	[Test]
	[Description("Silently allows unknown types that are in neither mobile nor web registry (custom components).")]
	public void ValidateMobileComponentTypes_WhenCustomType_ReturnsValid() {
		// Arrange
		string body = """
		              {
		                "viewConfigDiff": [
		                  {"operation":"insert","name":"X","type":"usr.CustomWidget"}
		                ]
		              }
		              """;
		HashSet<string> allowed = new(StringComparer.OrdinalIgnoreCase) { "crt.Input" };
		HashSet<string> webOnly = new(StringComparer.OrdinalIgnoreCase) { "crt.DataGrid" };

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileComponentTypes(body, allowed, webOnly);

		// Assert
		result.IsValid.Should().BeTrue("because custom types not in either registry should be allowed");
		result.Warnings.Should().BeEmpty("because the type is not a known web-only component");
	}

	#endregion

	#region ValidateMobileViewConfigDiffStructure

	[Test]
	[Description("Returns valid when all viewConfigDiff entries have operation and name.")]
	public void ValidateMobileViewConfigDiffStructure_WhenAllEntriesValid_ReturnsValid() {
		// Arrange
		string body = """
		              {
		                "viewConfigDiff": [
		                  {"operation":"insert","name":"Field1","values":{"type":"crt.Input"}},
		                  {"operation":"merge","name":"Field2","values":{"visible":false}}
		                ]
		              }
		              """;

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileViewConfigDiffStructure(body);

		// Assert
		result.IsValid.Should().BeTrue("because all entries have both operation and name");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Reports error when a viewConfigDiff entry is missing operation.")]
	public void ValidateMobileViewConfigDiffStructure_WhenMissingOperation_ReturnsError() {
		// Arrange
		string body = """
		              {
		                "viewConfigDiff": [
		                  {"name":"Field1","values":{"type":"crt.Input"}}
		                ]
		              }
		              """;

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileViewConfigDiffStructure(body);

		// Assert
		result.IsValid.Should().BeFalse("because the entry is missing the operation property");
		result.Errors.Should().ContainSingle(e => e.Contains("operation"),
			because: "the error must identify the missing property");
	}

	[Test]
	[Description("Reports error when a viewConfigDiff entry is missing name.")]
	public void ValidateMobileViewConfigDiffStructure_WhenMissingName_ReturnsError() {
		// Arrange
		string body = """
		              {
		                "viewConfigDiff": [
		                  {"operation":"insert","values":{"type":"crt.Input"}}
		                ]
		              }
		              """;

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileViewConfigDiffStructure(body);

		// Assert
		result.IsValid.Should().BeFalse("because the entry is missing the name property");
		result.Errors.Should().ContainSingle(e => e.Contains("name"),
			because: "the error must identify the missing property");
	}

	[Test]
	[Description("Reports error listing both missing properties when entry has neither operation nor name.")]
	public void ValidateMobileViewConfigDiffStructure_WhenMissingBoth_ReportsBoth() {
		// Arrange
		string body = """
		              {
		                "viewConfigDiff": [
		                  {"values":{"type":"crt.Input"}}
		                ]
		              }
		              """;

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileViewConfigDiffStructure(body);

		// Assert
		result.IsValid.Should().BeFalse("because both required properties are missing");
		result.Errors.Should().ContainSingle(e => e.Contains("operation") && e.Contains("name"),
			because: "both missing properties should be reported in a single error");
	}

	[Test]
	[Description("Returns valid when viewConfigDiff is absent.")]
	public void ValidateMobileViewConfigDiffStructure_WhenNoViewConfigDiff_ReturnsValid() {
		// Arrange
		string body = """{"viewModelConfigDiff":[]}""";

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileViewConfigDiffStructure(body);

		// Assert
		result.IsValid.Should().BeTrue("because there is no viewConfigDiff to validate");
	}

	#endregion

	#region ValidateMobileFieldBindings

	[Test]
	[Description("Returns valid when all $-bindings match declared viewModelConfigDiff attributes.")]
	public void ValidateMobileFieldBindings_WhenBindingsMatchAttributes_ReturnsValid() {
		// Arrange
		string body = """
		              {
		                "viewConfigDiff": [
		                  {"operation":"merge","name":"Field1","values":{"control":"$UsrName"}}
		                ],
		                "viewModelConfigDiff": [
		                  {"operation":"merge","path":["attributes"],"values":{"UsrName":{}}}
		                ]
		              }
		              """;

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileFieldBindings(body);

		// Assert
		result.IsValid.Should().BeTrue("because $UsrName matches the declared UsrName attribute");
	}

	[Test]
	[Description("Reports an error when a $-binding references an undeclared attribute.")]
	public void ValidateMobileFieldBindings_WhenBindingMissesAttribute_ReturnsError() {
		// Arrange
		string body = """
		              {
		                "viewConfigDiff": [
		                  {"operation":"merge","name":"Field1","values":{"control":"$UsrMissing"}}
		                ],
		                "viewModelConfigDiff": [
		                  {"operation":"merge","path":["attributes"],"values":{"UsrName":{}}}
		                ]
		              }
		              """;

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileFieldBindings(body);

		// Assert
		result.IsValid.Should().BeFalse("because $UsrMissing is not declared in viewModelConfigDiff");
		result.Errors.Should().ContainSingle(e => e.Contains("UsrMissing"),
			because: "the error must identify the undeclared attribute");
	}

	[Test]
	[Description("Strips converter pipe from binding before cross-referencing.")]
	public void ValidateMobileFieldBindings_WhenBindingHasConverterPipe_StripsConverter() {
		// Arrange
		string body = """
		              {
		                "viewConfigDiff": [
		                  {
		                  	"operation":"merge",
		                  	"name":"F",
		                  	"values":{"value":"$UsrName | crt.InvertBooleanValue"}
		                  }
		                ],
		                "viewModelConfigDiff": [
		                  {"operation":"merge","path":["attributes"],"values":{"UsrName":{}}}
		                ]
		              }
		              """;

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileFieldBindings(body);

		// Assert
		result.IsValid.Should().BeTrue("because the attribute name after stripping the converter pipe matches");
	}

	[Test]
	[Description("Returns valid when there are no viewModelConfigDiff attributes to cross-check.")]
	public void ValidateMobileFieldBindings_WhenNoViewModelAttributes_ReturnsValid() {
		// Arrange
		string body = """
		              {
		                "viewConfigDiff": [
		                  {"operation":"merge","name":"F","values":{"control":"$UsrName"}}
		                ]
		              }
		              """;

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileFieldBindings(body);

		// Assert
		result.IsValid.Should().BeTrue("because there are no declared attributes to cross-check against");
	}

	[Test]
	[Description("Skips $Resources bindings without reporting errors.")]
	public void ValidateMobileFieldBindings_WhenResourceBinding_SkipsBinding() {
		// Arrange
		string body = """
		              {
		                "viewConfigDiff": [
		                  {"operation":"merge","name":"F","values":{"caption":"$Resources.Strings.Title"}}
		                ],
		                "viewModelConfigDiff": [
		                  {"operation":"merge","path":["attributes"],"values":{"UsrName":{}}}
		                ]
		              }
		              """;

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileFieldBindings(body);

		// Assert
		result.IsValid.Should().BeTrue("because $Resources.Strings.* bindings are resource references, not attribute bindings");
	}

	[Test]
	[Description("Non-attributes viewModelConfigDiff entries (e.g. path:[\"title\"]) should not register their values keys as declared attributes.")]
	public void ValidateMobileFieldBindings_WhenNonAttributesDiffEntry_DoesNotOverCollect() {
		// Arrange — one real attribute (UsrKnown) declared under path:["attributes"],
		// plus a non-attribute entry targeting path:["title"] whose values key "caption"
		// must NOT be treated as a declared attribute.
		// $UsrMissing is bound in view but not declared → should be an error.
		string body = """
		              {
		                "viewModelConfigDiff": [
		                  { "operation": "merge", "path": ["attributes"], "values": { "UsrKnown": { "modelConfig": { "path": "PDS.UsrKnown" } } } },
		                  { "operation": "merge", "path": ["title"], "values": { "caption": "My Page" } }
		                ],
		                "viewConfigDiff": [
		                  { "operation": "insert", "name": "UsrField1", "values": { "type": "crt.Input", "label": "$UsrKnown" } },
		                  {
		                  	"operation": "insert",
		                  	"name": "UsrField2",
		                  	"values": { "type": "crt.Input", "label": "$UsrMissing" }
		                  }
		                ]
		              }
		              """;

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileFieldBindings(body);

		// Assert — "caption" from the title entry must NOT be treated as a declared attribute
		result.IsValid.Should().BeFalse("because $UsrMissing is bound in view but not declared as an attribute");
		result.Errors.Should().Contain(e => e.Contains("UsrMissing"),
			because: "the non-attributes entry should not mask the missing attribute");
		result.Errors.Should().NotContain(e => e.Contains("UsrKnown"),
			because: "UsrKnown is properly declared under the attributes path");
	}

	#endregion

	#region ValidateMobileStandardFieldBindings

	[Test]
	[Description("Mobile: label using attribute-name resource key for a DS-bound attribute does not warn when the key is absent from resources — the platform auto-provides captions under the attribute name")]
	public void ValidateMobileStandardFieldBindings_LabelResourceKeyMissingButDsBound_ReturnsNoWarning() {
		string body = """
		              {
		                "viewConfigDiff": [
		                  {
		                  	"operation":"merge",
		                  	"name":"UsrName",
		                  	"values":
		                  		{
		                  			"type":"crt.Input",
		                  			"label":"$Resources.Strings.UsrName",
		                  			"control":"$UsrName"
		                  		}
		                  }
		                ],
		                "viewModelConfigDiff": [
		                  {
		                  	"operation":"merge",
		                  	"path":["attributes"],
		                  	"values":{"UsrName":{"modelConfig":{"path":"PDS.UsrName"}}}
		                  }
		                ]
		              }
		              """;

		SchemaValidationResult result = SchemaValidationService.ValidateMobileStandardFieldBindings(
			body,
			new Dictionary<string, string> { ["PDS_UsrRequesterName"] = "Requester Name" });

		result.IsValid.Should().BeTrue("because DS-bound caption keys are auto-provided by the platform under the view-model attribute name");
		result.Errors.Should().BeEmpty();
		result.Warnings.Should().BeEmpty("because the label key equals the DS-bound attribute name and the platform auto-provides the caption");
	}

	[Test]
	[Description("Mobile: label using path-with-underscores resource key warns when the key is missing from resources and is not the auto-provided attribute-name form")]
	public void ValidateMobileStandardFieldBindings_LabelResourceKeyIsPathWithUnderscoresAndMissing_ReturnsWarning() {
		string body = """
		              {
		                "viewConfigDiff": [
		                  {
		                  	"operation":"merge",
		                  	"name":"UsrName",
		                  	"values":
		                  		{
		                  			"type":"crt.Input",
		                  			"label":"$Resources.Strings.PDS_UsrName",
		                  			"control":"$UsrName"
		                  		}
		                  }
		                ],
		                "viewModelConfigDiff": [
		                  {
		                  	"operation":"merge",
		                  	"path":["attributes"],
		                  	"values":{"UsrName":{"modelConfig":{"path":"PDS.UsrName"}}}
		                  }
		                ]
		              }
		              """;

		SchemaValidationResult result = SchemaValidationService.ValidateMobileStandardFieldBindings(
			body,
			new Dictionary<string, string> { ["PDS_UsrRequesterName"] = "Requester Name" });

		result.IsValid.Should().BeTrue("because a missing label resource is a recoverable warning, not a hard failure");
		result.Errors.Should().BeEmpty();
		result.Warnings.Should().ContainSingle(w => w.Contains("PDS_UsrName") && w.Contains("render blank"),
			"because the platform auto-provides captions under the attribute name 'UsrName', not under the path-with-underscores form 'PDS_UsrName'");
	}

	[Test]
	[Description("Mobile: label using a DS-bound SIBLING attribute name that is not the field's own binding attribute warns — auto-provide is keyed by the control's binding attribute, not by an arbitrary alias sharing the same DS path.")]
	public void ValidateMobileStandardFieldBindings_LabelResourceKeyIsSiblingAttributeOnSameDsPath_ReturnsWarning() {
		string body = """
		              {
		                "viewConfigDiff": [
		                  {
		                  	"operation":"merge",
		                  	"name":"UsrName",
		                  	"values":
		                  		{
		                  			"type":"crt.Input",
		                  			"label":"$Resources.Strings.UsrNameAlias",
		                  			"control":"$UsrName"
		                  		}
		                  }
		                ],
		                "viewModelConfigDiff": [
		                  {
		                  	"operation":"merge",
		                  	"path":["attributes"],
		                  	"values":
		                  		{
		                  			"UsrName":{"modelConfig":{"path":"PDS.UsrName"}},
		                  			"UsrNameAlias":{"modelConfig":{"path":"PDS.UsrName"}}
		                  		}
		                  }
		                ]
		              }
		              """;

		SchemaValidationResult result = SchemaValidationService.ValidateMobileStandardFieldBindings(
			body,
			new Dictionary<string, string> { ["SomeUnrelatedKey"] = "value" });

		result.IsValid.Should().BeTrue("because a missing label resource is a recoverable warning, not a hard failure");
		result.Errors.Should().BeEmpty();
		result.Warnings.Should().ContainSingle(w => w.Contains("UsrNameAlias") && w.Contains("render blank"),
			"because the label key is a different attribute than the control's binding attribute 'UsrName', so the platform does not auto-provide the caption under it");
	}

	#endregion

	#region ValidateSchemaDepsCompleteness

	[Test]
	[Description("Returns a warning when handlers use sdk. but SCHEMA_DEPS omits @creatio-devkit/common.")]
	public void ValidateSchemaDepsCompleteness_WhenSdkUsedButDepMissing_ReturnsWarning() {
		// Arrange
		string body =
			"""
				define(
					"Module",
					/**SCHEMA_DEPS*/["css!Module"]/**SCHEMA_DEPS*/,
					/**SCHEMA_ARGS*/(css)/**SCHEMA_ARGS*/ => ({/**SCHEMA_HANDLERS*/[{request:"crt.HandleViewModelInitRequest",handler: async (request, next) => { sdk.HandlerChainService; }}]/**SCHEMA_HANDLERS*/})
				);
			""";

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateSchemaDepsCompleteness(body);

		// Assert
		result.IsValid.Should().BeTrue("because missing deps is a warning, not an error");
		result.Warnings.Should().ContainSingle(w => w.Contains("@creatio-devkit/common"),
			because: "handlers reference sdk. but the dependency is not in SCHEMA_DEPS");
	}

	[Test]
	[Description("Returns no warning when handlers use sdk. and SCHEMA_DEPS includes @creatio-devkit/common.")]
	public void ValidateSchemaDepsCompleteness_WhenSdkUsedAndDepPresent_ReturnsClean() {
		// Arrange
		string body =
			"""
				define(
					"Module",
					/**SCHEMA_DEPS*/["@creatio-devkit/common"]/**SCHEMA_DEPS*/,
					/**SCHEMA_ARGS*/(sdk)/**SCHEMA_ARGS*/ => ({/**SCHEMA_HANDLERS*/[{request:"crt.HandleViewModelInitRequest",handler: async (request, next) => { sdk.HandlerChainService; }}]/**SCHEMA_HANDLERS*/})
				);
			""";

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateSchemaDepsCompleteness(body);

		// Assert
		result.IsValid.Should().BeTrue("because the dependency is properly declared");
		result.Warnings.Should().BeEmpty("because the SDK dependency is present in SCHEMA_DEPS");
	}

	[Test]
	[Description("Returns no warning when handlers do not reference sdk at all.")]
	public void ValidateSchemaDepsCompleteness_WhenNoSdkUsage_ReturnsClean() {
		// Arrange
		string body =
			"""
				define(
					"Module",
					/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/,
					/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ => ({/**SCHEMA_HANDLERS*/[{request:"crt.HandleViewModelInitRequest",handler: async (request, next) => { console.log("hello"); }}]/**SCHEMA_HANDLERS*/})
				);
			""";

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateSchemaDepsCompleteness(body);

		// Assert
		result.IsValid.Should().BeTrue("because no SDK references are present");
		result.Warnings.Should().BeEmpty("because handlers don't use sdk");
	}

	[Test]
	[Description("Returns clean result when there are no SCHEMA_HANDLERS.")]
	public void ValidateSchemaDepsCompleteness_WhenNoHandlers_ReturnsClean() {
		// Arrange
		string body =
			"""
				define(
					"Module",
					/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/,
					/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ => ({})
				);
			""";

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateSchemaDepsCompleteness(body);

		// Assert
		result.IsValid.Should().BeTrue("because there are no handlers to check");
		result.Warnings.Should().BeEmpty("because there is nothing to cross-reference");
	}

	[Test]
	[Description("A local variable named 'sdk' without property access should not trigger the SDK dependency warning.")]
	public void ValidateSchemaDepsCompleteness_WhenLocalVariableNamedSdk_DoesNotWarn() {
		// Arrange — "sdk" appears but not as "sdk." or "sdk["
		string body =
			"""
				define(
					"Module",
					/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/,
					/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ => ({/**SCHEMA_HANDLERS*/[{request:"crt.HandleViewModelInitRequest",handler: async (request, next) => { const sdk = 42; return sdk + 1; }}]/**SCHEMA_HANDLERS*/})
				);
			""";

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateSchemaDepsCompleteness(body);

		// Assert
		result.IsValid.Should().BeTrue("because the word sdk is a local variable, not an SDK access");
		result.Warnings.Should().BeEmpty("because \\bsdk\\s*[.[] does not match a bare identifier assignment");
	}

	#endregion

	#region ValidateContextAccessAwait

	[Test]
	[Description("Warns when a module-scope helper reads $context[\"Attr\"] without await inside a ?? chain.")]
	public void ValidateContextAccessAwait_WhenHelperReadsContextWithoutAwait_ReturnsWarning() {
		// Arrange — reproduces the real bug: the un-awaited read lives in a free helper function,
		// not inside the SCHEMA_HANDLERS array, so a handler-only scan would miss it.
		string body =
			"""
				define(
					"Module",
					/**SCHEMA_DEPS*/["@creatio-devkit/common"]/**SCHEMA_DEPS*/,
					function/**SCHEMA_ARGS*/(sdk)/**SCHEMA_ARGS*/ {
						const sync = async ($context, fmt) => {
							const current = fmt ?? $context["UsrPhoneFormatMode"] ?? await getFormat();
							await $context.set("UsrCountryCode", current);
						};
						return {/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/};
					}
				);
			""";

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateContextAccessAwait(body);

		// Assert
		result.IsValid.Should().BeTrue("because an un-awaited read is a warning, not a blocking error");
		result.Warnings.Should().ContainSingle(w => w.Contains("UsrPhoneFormatMode") && w.Contains("await"),
			because: "the helper reads $context[\"UsrPhoneFormatMode\"] without awaiting the asynchronous accessor");
	}

	[Test]
	[Description("Warns when an un-awaited $context read is passed as a call argument.")]
	public void ValidateContextAccessAwait_WhenUnAwaitedReadPassedAsArgument_ReturnsWarning() {
		// Arrange
		string body =
			"""
				define(
					"Module",
					/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/,
					function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {
						return {
							handlers: /**SCHEMA_HANDLERS*/[
								{
									request: "crt.HandleViewModelInitRequest",
									handler: async (request, next) => {
										sync(request.$context, request.$context["UsrPhoneNumber"]);
									}
								}
							]/**SCHEMA_HANDLERS*/
						};
					}
				);
			""";

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateContextAccessAwait(body);

		// Assert
		result.IsValid.Should().BeTrue("because the finding is advisory");
		result.Warnings.Should().ContainSingle(w => w.Contains("UsrPhoneNumber"),
			because: "request.$context[\"UsrPhoneNumber\"] is passed on without await, yielding a Promise argument");
	}

	[Test]
	[Description("Does not warn when every $context read is awaited.")]
	public void ValidateContextAccessAwait_WhenReadsAreAwaited_ReturnsClean() {
		// Arrange
		string body =
			"""
				define(
					"Module",
					/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/,
					function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {
						return {
							handlers: /**SCHEMA_HANDLERS*/[
								{
									request: "crt.HandleViewModelInitRequest",
									handler: async (request, next) => {
										const v = await request.$context["UsrPhoneNumber"];
										const m = await $context["UsrMode"];
									}
								}
							]/**SCHEMA_HANDLERS*/
						};
					}
				);
			""";

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateContextAccessAwait(body);

		// Assert
		result.IsValid.Should().BeTrue("because all reads are awaited");
		result.Warnings.Should().BeEmpty("because every $context bracket read is preceded by await");
	}

	[Test]
	[Description("Does not warn for a bracket assignment target, which is a write rather than a read.")]
	public void ValidateContextAccessAwait_WhenBracketIsAssignmentTarget_ReturnsClean() {
		// Arrange — '$context["X"] =' is a write target, not an un-awaited read.
		string body =
			"""
				define(
					"Module",
					/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/,
					function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {
						return {
							handlers: /**SCHEMA_HANDLERS*/[
								{
									request: "crt.HandleViewModelInitRequest",
									handler: async (request, next) => {
										request.$context["UsrTransient"] = next;
									}
								}
							]/**SCHEMA_HANDLERS*/
						};
					}
				);
			""";

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateContextAccessAwait(body);

		// Assert
		result.IsValid.Should().BeTrue("because a write target is not flagged");
		result.Warnings.Should().BeEmpty("because '$context[\"UsrTransient\"] =' is an assignment, not a read");
	}

	[Test]
	[Description("Still warns when an un-awaited read is used in an equality comparison.")]
	public void ValidateContextAccessAwait_WhenUnAwaitedReadInComparison_ReturnsWarning() {
		// Arrange — '==' must not be mistaken for an assignment '='.
		string body =
			"""
				define(
					"Module",
					/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/,
					function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {
						return {
							handlers: /**SCHEMA_HANDLERS*/[
								{
									request: "crt.HandleViewModelInitRequest",
									handler: async (request, next) => {
										if ($context["UsrMode"] === "local") {
											return;
										}
									}
								}
							]/**SCHEMA_HANDLERS*/
						};
					}
				);
			""";

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateContextAccessAwait(body);

		// Assert
		result.IsValid.Should().BeTrue("because the finding is advisory");
		result.Warnings.Should().ContainSingle(w => w.Contains("UsrMode"),
			because: "an un-awaited read compared with === resolves the Promise object, never the value");
	}

	[Test]
	[Category("Unit")]
	[Description("Still warns when an un-awaited read is followed by '=>', which is an arrow and not an assignment target.")]
	public void ValidateContextAccessAwait_WhenReadFollowedByArrow_ReturnsWarning() {
		// Arrange — '=>' must not be mistaken for an assignment '='; the read is still un-awaited.
		string body =
			"""
				define(
					"Module",
					/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/,
					function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {
						return {
							handlers: /**SCHEMA_HANDLERS*/[
								{
									request: "crt.HandleViewModelInitRequest",
									handler: async (request, next) => {
										const pick = $context["UsrMode"] => "x";
									}
								}
							]/**SCHEMA_HANDLERS*/
						};
					}
				);
			""";

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateContextAccessAwait(body);

		// Assert
		result.IsValid.Should().BeTrue("because the finding is advisory");
		result.Warnings.Should().ContainSingle(w => w.Contains("UsrMode"),
			because: "'=>' after a bracket read is an arrow, not an assignment, so the un-awaited read is still flagged");
	}

	[Test]
	[Description("Does not warn for $context.set or $context.executeRequest method calls.")]
	public void ValidateContextAccessAwait_WhenMethodCallsOnly_ReturnsClean() {
		// Arrange — '.set(' / '.executeRequest(' are method calls, not bracket reads.
		string body =
			"""
				define(
					"Module",
					/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/,
					function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {
						return {
							handlers: /**SCHEMA_HANDLERS*/[
								{
									request: "crt.HandleViewModelInitRequest",
									handler: async (request, next) => {
										await request.$context.set("UsrName", "x");
										await request.$context.executeRequest({
											type: "usr.Req",
											$context: request.$context
										});
									}
								}
							]/**SCHEMA_HANDLERS*/
						};
					}
				);
			""";

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateContextAccessAwait(body);

		// Assert
		result.IsValid.Should().BeTrue("because there are no bracket reads at all");
		result.Warnings.Should().BeEmpty("because method-call forms on $context are not bracket attribute reads");
	}

	[Test]
	[Description("Reports each distinct un-awaited attribute name once, even across multiple reads.")]
	public void ValidateContextAccessAwait_WhenSameAttributeReadTwice_WarnsOnce() {
		// Arrange
		string body =
			"""
				define(
					"Module",
					/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/,
					function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {
						return {
							handlers: /**SCHEMA_HANDLERS*/[
								{
									request: "crt.HandleViewModelInitRequest",
									handler: async (request, next) => {
										const a = $context["UsrMode"];
										const b = $context["UsrMode"];
									}
								}
							]/**SCHEMA_HANDLERS*/
						};
					}
				);
			""";

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateContextAccessAwait(body);

		// Assert
		result.IsValid.Should().BeTrue("because the finding is advisory");
		result.Warnings.Should().ContainSingle(w => w.Contains("UsrMode"),
			because: "duplicate reads of the same attribute are de-duplicated into a single warning");
	}

	[Test]
	[Description("Returns clean result for null or empty body.")]
	public void ValidateContextAccessAwait_WhenBodyEmpty_ReturnsClean() {
		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateContextAccessAwait(string.Empty);

		// Assert
		result.IsValid.Should().BeTrue("because an empty body has nothing to scan");
		result.Warnings.Should().BeEmpty("because there is no content to flag");
	}

	#endregion

	#region CollectMobileViewModelPaths

	[Test]
	[Description("Mobile body with viewModelConfigDiff containing modelConfig.path entries returns those paths keyed by attribute name.")]
	public void CollectMobileViewModelPaths_WithViewModelConfigDiff_ReturnsBoundPaths() {
		// Arrange
		string body = """
		              {
		                "viewModelConfigDiff": [
		                  {
		                    "operation": "merge",
		                    "path": ["attributes"],
		                    "values": {
		                      "UsrName": { "modelConfig": { "path": "Name" } },
		                      "UsrCode": { "modelConfig": { "path": "Code" } }
		                    }
		                  }
		                ]
		              }
		              """;

		// Act
		Dictionary<string, string> result = SchemaValidationService.CollectMobileViewModelPaths(body);

		// Assert
		result.Should().ContainKey("UsrName", because: "attributes with modelConfig.path are DS-bound and must be collected");
		result["UsrName"].Should().Be("Name", because: "the recorded path must equal the modelConfig.path value");
		result.Should().ContainKey("UsrCode", because: "every attribute with modelConfig.path must be collected");
		result["UsrCode"].Should().Be("Code", because: "the recorded path must equal the modelConfig.path value");
	}

	[Test]
	[Description("Mobile body with full-form viewModelConfig.attributes returns DS-bound paths.")]
	public void CollectMobileViewModelPaths_WithFullViewModelConfig_ReturnsBoundPaths() {
		// Arrange
		string body = """
		              {
		                "viewModelConfig": {
		                  "attributes": {
		                    "UsrName": { "modelConfig": { "path": "Name" } }
		                  }
		                }
		              }
		              """;

		// Act
		Dictionary<string, string> result = SchemaValidationService.CollectMobileViewModelPaths(body);

		// Assert
		result.Should().ContainKey("UsrName",
			because: "full-form viewModelConfig with attributes still binds attributes to data source columns and must be detected");
		result["UsrName"].Should().Be("Name", because: "the recorded path must equal the modelConfig.path value");
	}

	[Test]
	[Description("Invalid JSON returns an empty dictionary instead of throwing.")]
	public void CollectMobileViewModelPaths_InvalidJson_ReturnsEmpty() {
		// Act
		Dictionary<string, string> result = SchemaValidationService.CollectMobileViewModelPaths("{not valid");

		// Assert
		result.Should().BeEmpty(because: "the helper must be tolerant of malformed bodies and return an empty result");
	}

	[Test]
	[Description("Empty body returns an empty dictionary without scanning.")]
	public void CollectMobileViewModelPaths_EmptyBody_ReturnsEmpty() {
		// Act
		Dictionary<string, string> result = SchemaValidationService.CollectMobileViewModelPaths(string.Empty);

		// Assert
		result.Should().BeEmpty(because: "an empty body has no attributes to collect");
	}

	[Test]
	[Description("Web AMD body (not JSON object root) returns an empty dictionary because mobile collection only walks JSON.")]
	public void CollectMobileViewModelPaths_WebAmdBody_ReturnsEmpty() {
		// Act
		Dictionary<string, string> result = SchemaValidationService.CollectMobileViewModelPaths(ValidListPageBody);

		// Assert
		result.Should().BeEmpty(because: "an AMD-wrapped body is not a JSON object and the mobile collector must not throw or invent entries");
	}

	[Test]
	[Description("Diff entry without modelConfig.path values is skipped without throwing.")]
	public void CollectMobileViewModelPaths_DiffWithoutModelConfig_ReturnsEmpty() {
		// Arrange
		string body = """
		              {
		                "viewModelConfigDiff": [
		                  {
		                  	"operation": "merge",
		                  	"path": ["attributes"],
		                  	"values": { "UsrCaption": { "caption": "Hi" } }
		                  }
		                ]
		              }
		              """;

		// Act
		Dictionary<string, string> result = SchemaValidationService.CollectMobileViewModelPaths(body);

		// Assert
		result.Should().BeEmpty(because: "attributes without a modelConfig.path are not DS-bound and must not be reported");
	}

	#endregion

	#region ValidateChartWidgetConfig

	// Minimal merged typeDefinitions mirroring the real registry shapes the walk traverses:
	// ChartWidgetConfig (per-component) -> series -> ChartSeriesConfig -> data
	// (WidgetDataConfig<WidgetDataProvidingConfig, ...>) -> WidgetDataProvidingConfig -> aggregation.column.
	// It deliberately keeps the two registry-content quirks the bridges patch: the series item type is the
	// bare "SeriesConfig" (alias bridge) and 'data' is a raw generic string (generic bridge).
	private const string ChartTypeDefinitionsJson =
		"""
		{
		  "typeDefinitions": {
		    "ChartWidgetConfig": { "fields": {
		      "title": { "type": "string", "required": true },
		      "series": { "type": "array", "items": { "type": "SeriesConfig" }, "required": true },
		      "color": { "type": "string" },
		      "theme": { "type": "string" }
		    }},
		    "ChartSeriesConfig": { "fields": {
		      "type": { "type": "string", "required": true },
		      "color": { "type": "string", "required": true },
		      "label": { "type": "string", "required": true },
		      "legend": { "type": "object", "required": true },
		      "data": { "type": "WidgetDataConfig<WidgetDataProvidingConfig, NumberFormat | DateTimeFormat | StringFormat>", "required": true },
		      "dataLabel": { "type": "object" }
		    }},
		    "WidgetDataConfig": { "fields": {
		      "providing": { "type": "TProvidingConfig", "required": true },
		      "formatting": { "type": "TFormat", "required": true }
		    }},
		    "WidgetDataProvidingConfig": { "fields": {
		      "attribute": { "type": "string", "required": true },
		      "schemaName": { "type": "string", "required": true },
		      "aggregation": { "type": "object", "required": true, "shape": {
		        "column": { "type": "AggregationFunctionColumn", "required": true }
		      }}
		    }}
		  }
		}
		""";

	private static IReadOnlyDictionary<string, JsonElement> ChartTypeDefs() {
		using JsonDocument document = JsonDocument.Parse(ChartTypeDefinitionsJson);
		var map = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
		foreach (JsonProperty property in document.RootElement.GetProperty("typeDefinitions").EnumerateObject()) {
			map[property.Name] = property.Value.Clone();
		}
		return map;
	}

	private const string ValidChartProviding =
		"""{"attribute":"A","schemaName":"Account","aggregation":{"column":{"expression":{}}}}""";

	private static string DoughnutSeries(string providingJson) =>
		"""{"type":"doughnut","label":"L","legend":{"enabled":false},"data":{"providing":__P__}}"""
			.Replace("__P__", providingJson);

	private static string ChartPageBody(string seriesArrayJson, string operation = "insert") {
		string viewConfigDiff =
			"""[{"operation":"__OP__","name":"TestChart","parentName":"Main","propertyName":"items","index":0,"values":{"type":"crt.ChartWidget","config":{"title":"#ResourceString(TestChart_title)#","series":__SERIES__}}}]"""
				.Replace("__OP__", operation)
				.Replace("__SERIES__", seriesArrayJson);
		return BuildDiffBackedPageBody(viewConfigDiff, "[]");
	}

	[Test]
	[Description("Registry-driven: an aggregation present but without its required 'column' fails — this is the empty-chart bug, caught via the registry's aggregation.shape.column required flag.")]
	public void ValidateChartWidgetConfig_AggregationMissingColumn_ReturnsInvalid() {
		string providing = """{"attribute":"A","schemaName":"Account","aggregation":{"expression":{}}}""";
		string body = ChartPageBody("[" + DoughnutSeries(providing) + "]");

		var result = SchemaValidationService.ValidateChartWidgetConfig(body, ChartTypeDefs());

		result.IsValid.Should().BeFalse("because the registry marks aggregation.column required");
		result.Errors.Should().Contain(e => e.Contains("TestChart") && e.Contains("aggregation") && e.Contains("column"),
			"because the diagnostic should name the chart and the missing aggregation.column");
	}

	[Test]
	[Description("Registry-driven: a well-formed doughnut (no color, no formatting) passes — proves both bridges reach the data and the optionality suppressions hold.")]
	public void ValidateChartWidgetConfig_WellFormedDoughnut_ReturnsValid() {
		string body = ChartPageBody("[" + DoughnutSeries(ValidChartProviding) + "]");

		var result = SchemaValidationService.ValidateChartWidgetConfig(body, ChartTypeDefs());

		result.IsValid.Should().BeTrue("because the bridges reach the data and color/formatting are suppressed");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Registry-driven: a series with no aggregation at all fails (aggregation is required on the providing type).")]
	public void ValidateChartWidgetConfig_AggregationMissingEntirely_ReturnsInvalid() {
		string providing = """{"attribute":"A","schemaName":"Account"}""";
		string body = ChartPageBody("[" + DoughnutSeries(providing) + "]");

		var result = SchemaValidationService.ValidateChartWidgetConfig(body, ChartTypeDefs());

		result.IsValid.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Contains("aggregation"));
	}

	[Test]
	[Description("Registry-driven: a providing block without schemaName fails.")]
	public void ValidateChartWidgetConfig_MissingSchemaName_ReturnsInvalid() {
		string providing = """{"attribute":"A","aggregation":{"column":{"expression":{}}}}""";
		string body = ChartPageBody("[" + DoughnutSeries(providing) + "]");

		var result = SchemaValidationService.ValidateChartWidgetConfig(body, ChartTypeDefs());

		result.IsValid.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Contains("schemaName"));
	}

	[Test]
	[Description("Scope: fields outside the data block (e.g. title at the chart-config level) are NOT checked — a chart with a well-formed data block but no title passes.")]
	public void ValidateChartWidgetConfig_MissingTitle_NotFlagged() {
		string vcd =
			"""[{"operation":"insert","name":"TestChart","values":{"type":"crt.ChartWidget","config":{"series":__SERIES__}}}]"""
				.Replace("__SERIES__", "[" + DoughnutSeries(ValidChartProviding) + "]");
		string body = BuildDiffBackedPageBody(vcd, "[]");

		var result = SchemaValidationService.ValidateChartWidgetConfig(body, ChartTypeDefs());

		result.IsValid.Should().BeTrue("because title sits above the data block and is out of scope");
		result.Errors.Should().NotContain(e => e.Contains("title"));
	}

	[Test]
	[Description("Scope: cosmetic series fields like 'color' are not checked (only the data block is), so a doughnut without color passes.")]
	public void ValidateChartWidgetConfig_DoughnutWithoutColor_ReturnsValid() {
		string body = ChartPageBody("[" + DoughnutSeries(ValidChartProviding) + "]");

		var result = SchemaValidationService.ValidateChartWidgetConfig(body, ChartTypeDefs());

		result.IsValid.Should().BeTrue("because cosmetic fields outside the data block are not validated");
		result.Errors.Should().NotContain(e => e.Contains("color"));
	}

	[Test]
	[Description("Scope: cosmetic fields are not checked for ANY series type — a bar without 'color' also passes, as long as its data block is well-formed.")]
	public void ValidateChartWidgetConfig_BarWithoutColor_ReturnsValid() {
		string barSeries = """{"type":"bar","label":"L","legend":{"enabled":false},"data":{"providing":__P__}}"""
			.Replace("__P__", ValidChartProviding);
		string body = ChartPageBody("[" + barSeries + "]");

		var result = SchemaValidationService.ValidateChartWidgetConfig(body, ChartTypeDefs());

		result.IsValid.Should().BeTrue("because color is a cosmetic field outside the data block and is not validated");
		result.Errors.Should().NotContain(e => e.Contains("color"));
	}

	[Test]
	[Description("Scope: a merge operation is not checked for required fields — a base schema may legitimately supply them.")]
	public void ValidateChartWidgetConfig_MergeOperation_NotChecked() {
		string providing = """{"attribute":"A","schemaName":"Account","aggregation":{"expression":{}}}""";
		string body = ChartPageBody("[" + DoughnutSeries(providing) + "]", operation: "merge");

		var result = SchemaValidationService.ValidateChartWidgetConfig(body, ChartTypeDefs());

		result.IsValid.Should().BeTrue("because a merge legitimately omits fields the base schema provides");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Fail-open: an empty or null registry yields a passing result so an offline run never blocks a save.")]
	public void ValidateChartWidgetConfig_RegistryUnavailable_ReturnsValid() {
		string providing = """{"attribute":"A","schemaName":"Account","aggregation":{"expression":{}}}""";
		string body = ChartPageBody("[" + DoughnutSeries(providing) + "]");

		SchemaValidationService.ValidateChartWidgetConfig(body, new Dictionary<string, JsonElement>())
			.IsValid.Should().BeTrue("because an empty registry means 'unavailable', not 'invalid'");
		SchemaValidationService.ValidateChartWidgetConfig(body, null)
			.IsValid.Should().BeTrue("because a null registry is fail-open");
	}

	[Test]
	[Description("A page with no crt.ChartWidget produces no chart-widget errors.")]
	public void ValidateChartWidgetConfig_NoChartWidget_ReturnsValid() {
		string body = BuildDiffBackedPageBody(
			"""[{"operation":"insert","name":"SomeInput","values":{"type":"crt.Input","control":"$SomeAttr"}}]""",
			"[]");

		var result = SchemaValidationService.ValidateChartWidgetConfig(body, ChartTypeDefs());

		result.IsValid.Should().BeTrue("because the validator only inspects crt.ChartWidget nodes");
		result.Errors.Should().BeEmpty();
	}

	#endregion
}
