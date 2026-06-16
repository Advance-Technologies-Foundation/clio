using System;
using System.Collections.Generic;
using System.Reflection;
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
	[Description("Label using a sibling DS-bound attribute name that does NOT match the column code warns — the platform auto-provides captions only when the resource key equals the entity column code (last segment of the DS path), not for arbitrary aliases sharing the same path.")]
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
			"because the alias does not match the column code 'UsrName' so the platform does not auto-provide the caption");
	}

	[Test]
	[Description("Label using a DS-bound attribute name that does NOT match the column code warns — auto-provide is keyed by column code, not by arbitrary attribute name. Production evidence: PDS_UsrCompleted attribute bound to PDS.UsrCompleted column does NOT auto-provide caption because resource key 'PDS_UsrCompleted' differs from column code 'UsrCompleted'.")]
	public void ValidateStandardFieldBindings_LabelResourceKeyIsAttributeNameNotMatchingColumnCode_ReturnsWarning() {
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
						"values":{"UsrLabel":{"modelConfig":{"path":"PDS.UsrFullName"}}}
					}
				]
			""");

		var result = SchemaValidationService.ValidateStandardFieldBindings(
			body,
			new Dictionary<string, string> { ["SomeOtherKey"] = "Other" });

		result.IsValid.Should().BeTrue("because a missing label resource is a recoverable warning, not a hard failure");
		result.Errors.Should().BeEmpty();
		result.Warnings.Should().ContainSingle(w => w.Contains("UsrLabel") && w.Contains("render blank"),
			"because UsrLabel does not match the column code 'UsrFullName' so the platform does not auto-provide the caption");
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
	[Description("Insert of a new field control with a label resource matching the DS-bound binding attribute name is accepted — the platform auto-provides the caption from the entity column.")]
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
								"label":"$Resources.Strings.UsrEstimatedMinutes",
								"control":"$UsrEstimatedMinutes"
							}
					}
				]
			""",
			"""
				[
					{
						"operation":"merge",
						"values":{"UsrEstimatedMinutes":{"modelConfig":{"path":"PDS.UsrEstimatedMinutes"}}}
					}
				]
			""");

		var result = SchemaValidationService.ValidateInsertedFieldSelfConsistency(body);

		result.IsValid.Should().BeTrue("because the binding attribute is declared with a DS-bound model path and the label key matches the attribute name, so the platform auto-provides the caption");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("Insert of a new field whose binding attribute uses the path-with-underscores naming form (e.g. PDS_UsrCompleted bound to PDS.UsrCompleted) is rejected when the label resource is not registered. The platform auto-provides captions only when the resource key matches the entity column code (last segment of the DS path), so the path-with-underscores form is NOT auto-provided even when declared as a DS-bound attribute.")]
	public void ValidateInsertedFieldSelfConsistency_InsertWithPdsUnderscoreAttributeAndNoResources_ReturnsInvalid() {
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
					}
				]
			""",
			"""
				[
					{
						"operation":"merge",
						"values":{"PDS_UsrCompleted":{"modelConfig":{"path":"PDS.UsrCompleted"}}}
					}
				]
			""");

		var result = SchemaValidationService.ValidateInsertedFieldSelfConsistency(body);

		result.IsValid.Should().BeFalse("because the resource key 'PDS_UsrCompleted' does not match the column code 'UsrCompleted', so the platform does not auto-provide the caption");
		result.Errors.Should().ContainSingle(error =>
			error.Contains("UsrCompleted") &&
			error.Contains("PDS_UsrCompleted") &&
			error.Contains("render blank"),
			"because the diagnostic should name the field, the unregistered resource key, and what will go wrong at runtime");
	}

	[Test]
	[Description("Insert of a new field control with the label resource passed in 'resources' is accepted.")]
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
	[Description("Custom validators with any logic are allowed — guidance steers AI toward standard validators; runtime validation does not second-guess custom implementations.")]
	public void ValidateStandardValidatorUsage_CustomMaxLengthStyleValidator_ReturnsValid() {
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
		result.IsValid.Should().BeTrue(
			because: "runtime validation no longer second-guesses whether a custom validator duplicates a built-in; guidance steers the AI at authoring time");
		result.Errors.Should().BeEmpty(
			because: "custom validators with .length checks are valid — only structural schema errors are rejected at runtime");
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
	[Description("Mobile: label using a sibling DS-bound attribute name that does NOT match the column code warns — auto-provide is keyed by entity column code (last segment of DS path), not by arbitrary alias.")]
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
			"because the alias does not match the column code 'UsrName' so the platform does not auto-provide the caption");
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

	#region ValidateMobileDataSourceAttributeTypes

	[Test]
	[Description("A data-source attribute with a related/lookup path (contains '.') but no 'type' is flagged.")]
	public void ValidateMobileDataSourceAttributeTypes_WhenRelatedPathHasNoType_ReturnsError() {
		string body = """
		              {
		                "modelConfigDiff": [
		                  { "operation": "merge", "path": [], "values": {
		                    "dataSources": { "PDS": { "config": { "attributes": {
		                      "QualifiedContactJobTitle": { "path": "QualifiedContact.JobTitle" } } } } } } }
		                ]
		              }
		              """;

		SchemaValidationResult result = SchemaValidationService.ValidateMobileDataSourceAttributeTypes(body);

		result.IsValid.Should().BeFalse();
		result.Errors.Should().ContainSingle(e => e.Contains("QualifiedContactJobTitle") && e.Contains("no \"type\""));
	}

	[Test]
	[Description("A related/lookup-path attribute that declares a 'type' passes.")]
	public void ValidateMobileDataSourceAttributeTypes_WhenRelatedPathHasType_ReturnsValid() {
		string body = """
		              {
		                "modelConfigDiff": [
		                  { "operation": "merge", "path": [], "values": {
		                    "dataSources": { "PDS": { "config": { "attributes": {
		                      "QualifiedContactJobTitle": { "path": "QualifiedContact.JobTitle", "type": "ForwardReference" } } } } } } }
		                ]
		              }
		              """;

		SchemaValidationResult result = SchemaValidationService.ValidateMobileDataSourceAttributeTypes(body);

		result.IsValid.Should().BeTrue();
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("An own column (dot-free path) needs no 'type' and is not flagged.")]
	public void ValidateMobileDataSourceAttributeTypes_WhenOwnColumnHasNoType_ReturnsValid() {
		string body = """
		              {
		                "modelConfigDiff": [
		                  { "operation": "merge", "path": [], "values": {
		                    "dataSources": { "PDS": { "config": { "attributes": {
		                      "LeadName": { "path": "LeadName" } } } } } } }
		                ]
		              }
		              """;

		SchemaValidationResult result = SchemaValidationService.ValidateMobileDataSourceAttributeTypes(body);

		result.IsValid.Should().BeTrue(because: "a dot-free path is an own column and needs no related-column type");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("A viewModel attribute (modelConfig.path, no direct path) is not mistaken for a data-source attribute.")]
	public void ValidateMobileDataSourceAttributeTypes_WhenViewModelAttribute_NotFlagged() {
		string body = """
		              {
		                "viewModelConfigDiff": [
		                  { "operation": "merge", "path": ["attributes"], "values": {
		                    "QualifiedContactJobTitle": { "modelConfig": { "path": "PDS.QualifiedContactJobTitle" } } } }
		                ]
		              }
		              """;

		SchemaValidationResult result = SchemaValidationService.ValidateMobileDataSourceAttributeTypes(body);

		result.IsValid.Should().BeTrue(because: "viewModel attributes bind via modelConfig.path, not a direct data-source path");
		result.Errors.Should().BeEmpty();
	}

	[Test]
	[Description("A list / viewElement-scoped data source is scanned too, not just the page data source.")]
	public void ValidateMobileDataSourceAttributeTypes_WhenListScopedDataSource_IsAlsoFlagged() {
		string body = """
		              {
		                "viewConfigDiff": [
		                  { "operation": "insert", "name": "ProductsList", "values": {
		                    "type": "crt.List",
		                    "modelConfig": { "dataSources": { "ProductsDS": { "config": { "attributes": {
		                      "OrderOwner": { "path": "Order.Owner" } } } } } } } }
		                ]
		              }
		              """;

		SchemaValidationResult result = SchemaValidationService.ValidateMobileDataSourceAttributeTypes(body);

		result.IsValid.Should().BeFalse();
		result.Errors.Should().ContainSingle(e => e.Contains("OrderOwner"));
	}

	[Test]
	[Description("The check is wired into ValidateMobilePage: a typeless related-path attribute lands in the blocking errors list.")]
	public void ValidateMobilePage_WhenRelatedPathHasNoType_AddsBlockingError() {
		string body = """
		              {
		                "viewConfigDiff": [],
		                "viewModelConfigDiff": [],
		                "modelConfigDiff": [
		                  { "operation": "merge", "path": [], "values": {
		                    "dataSources": { "PDS": { "config": { "attributes": {
		                      "OrderOwner": { "path": "Order.Owner" } } } } } } }
		                ]
		              }
		              """;
		var empty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		(List<string> errors, List<string> _) = SchemaValidationService.ValidateMobilePage(body, empty, empty);

		errors.Should().Contain(e => e.Contains("OrderOwner") && e.Contains("no \"type\""));
	}

	#endregion
}
