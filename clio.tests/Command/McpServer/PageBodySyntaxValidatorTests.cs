using System.Diagnostics;
using System.Linq;
using System.Text;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
internal class PageBodySyntaxValidatorTests {

	#region Constants: Private

	// Representative `create-page` AMD module body with the canonical SCHEMA_*
	// marker envelope. Verified by the Day-0 probe to parse cleanly via
	// Acornima.Parser.ParseScript in ≈30 ms cold / <5 ms warm.
	private const string CreatePageRepresentativeBody =
		"define(\"Test_FormPage\", " +
		"/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
		"function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { " +
		"return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ " +
		"}; });";

	// `create-page` output with all SCHEMA_* sections populated — exercises handlers,
	// converters and validators in their typical agent-generated shape so the parser
	// has to walk through every section, not just empty stubs.
	private const string CreatePagePopulatedSectionsBody =
		"define(\"Usr_FormPage\", " +
		"/**SCHEMA_DEPS*/[\"@creatio-devkit/common\"]/**SCHEMA_DEPS*/, " +
		"function/**SCHEMA_ARGS*/(sdk)/**SCHEMA_ARGS*/ { " +
		"return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[" +
			"{ operation: \"insert\", name: \"UsrTitle\", values: { layoutConfig: { column: 1, row: 1 } } }" +
		"]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[" +
			"{ attributes: { UsrTitle: { modelConfig: { path: \"PDS.UsrTitle\" } } } }" +
		"]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[" +
			"{ request: \"crt.HandleViewModelAttributeChangeRequest\", " +
			"handler: async function(request, next) { return next?.handle(request); } }" +
		"]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{ " +
			"\"usr.UpperCase\": function(v) { return v ? String(v).toUpperCase() : v; } " +
		"}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{ " +
			"\"usr.NotEmpty\": { validator: function() { return function(value) { return { invalid: !value }; }; } } " +
		"}/**SCHEMA_VALIDATORS*/ " +
		"}; });";

	// Incident-reproducer body: `await X = Y` — `await` is an expression and cannot
	// be an assignment target. The actual broken body from the production incident
	// that motivated the validator.
	private const string IncidentBrokenBody =
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

	#endregion

	#region Tests: positive (valid bodies)

	[Test]
	[Description("Valid create-page AMD module body parses successfully as a Script — the canonical happy path that update-page / sync-pages must allow through")]
	public void Validate_ShouldReturnValid_WhenBodyIsCanonicalCreatePageOutput() {
		// Arrange / Act
		PageBodySyntaxValidationResult result = PageBodySyntaxValidator.Validate(CreatePageRepresentativeBody);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "the validator must NEVER reject the body that create-page itself produces — that would block every legitimate Freedom UI page authoring round-trip");
	}

	[Test]
	[Description("Valid create-page body with populated SCHEMA_* sections (handlers, converters, validators) parses successfully — exercises the parser through every section AI tools edit")]
	public void Validate_ShouldReturnValid_WhenBodyHasPopulatedSchemaSections() {
		// Arrange / Act
		PageBodySyntaxValidationResult result = PageBodySyntaxValidator.Validate(CreatePagePopulatedSectionsBody);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "a body with non-empty handlers/converters/validators is the normal post-edit shape — the validator must walk through every populated section without false positives");
	}

	[Test]
	[Description("Empty string body parses as an empty Script — higher layers reject empty bodies separately, so the validator must not pre-empt that path")]
	public void Validate_ShouldReturnValid_WhenBodyIsEmptyString() {
		// Arrange / Act
		PageBodySyntaxValidationResult result = PageBodySyntaxValidator.Validate(string.Empty);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "an empty source string is syntactically valid; the empty-body rejection lives in the calling tool, not in the syntax validator");
	}

	[Test]
	[Description("Null body must not crash and must return Valid — defensive boundary so callers never need to null-check before invoking the validator")]
	public void Validate_ShouldReturnValid_WhenBodyIsNull() {
		// Arrange / Act
		PageBodySyntaxValidationResult result = PageBodySyntaxValidator.Validate(null);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "treating null as valid keeps the validator a pure side-effect-free predicate — the calling tool's existing null/empty rejection runs before this and stays the single source of truth for that case");
	}

	[Test]
	[Description("Body with leading UTF-8 BOM (U+FEFF) parses successfully — clio test fixtures and editor-produced files commonly carry BOMs")]
	public void Validate_ShouldReturnValid_WhenBodyHasLeadingUtf8Bom() {
		// Arrange — use the `﻿` escape (NOT a raw BOM in the source
		// literal) so an editor / formatter cannot silently strip the leading
		// zero-width character and turn this case into a duplicate of the
		// plain-define case.
		string body = "﻿define('X', [], function() { return {}; });";

		// Act
		PageBodySyntaxValidationResult result = PageBodySyntaxValidator.Validate(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "BOM-prefixed bodies are a real-world shape when create-page output passes through editors that auto-prepend a BOM; rejecting them would block roundtrips with no actual syntax issue");
	}

	[Test]
	[Description("Body with mixed line endings (CRLF + LF in the same payload) parses successfully and Acornima reports line numbers consistently across line-ending styles")]
	public void Validate_ShouldReturnValid_WhenBodyHasMixedLineEndings() {
		// Arrange
		string body = "var a = 1;\r\nvar b = 2;\nvar c = 3;\r\n";

		// Act
		PageBodySyntaxValidationResult result = PageBodySyntaxValidator.Validate(body);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "page bodies edited cross-platform routinely carry mixed CRLF/LF; the validator must not flag this as a syntax error");
	}

	#endregion

	#region Tests: negative (the incident + crafted invalid bodies)

	[Test]
	[Description("Incident reproducer: `await request.$context.X = Y` — `await` is an expression, not a valid assignment target. This is THE body that produced the production failure the validator exists to prevent")]
	public void Validate_ShouldReturnInvalid_WhenBodyContainsAwaitAsAssignmentTarget() {
		// Arrange / Act
		PageBodySyntaxValidationResult result = PageBodySyntaxValidator.Validate(IncidentBrokenBody);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "rejecting the exact incident body that motivated this validator is its reason to exist — letting it through would be a regression to the pre-fix behaviour update-page silently writing a broken page");
		result.Line.Should().Be(6,
			because: "the await-as-assignment-target sits on line 6 of the fixture and the parser must report the line accurately so the operator can jump to it in their editor");
		result.Column.Should().Be(17,
			because: "the `await` keyword sits at 1-based column 17 (16 leading spaces in the fixture + the `a` of `await`); pinning the column proves the +1 normalisation from Acornima's 0-based ParseError.Column into the canonical 1-based wire format stays correct");
		result.Message.Should().NotBeNullOrEmpty(
			because: "an actionable failure response per the AC requires a human-readable message alongside the line/column");
	}

	[Test]
	[Description("Unbalanced opening brace at end of body is rejected — Acornima reports the position where the parser ran out of input")]
	public void Validate_ShouldReturnInvalid_WhenBodyHasUnbalancedOpeningBrace() {
		// Arrange
		string body = "function f() { return 1; ";

		// Act
		PageBodySyntaxValidationResult result = PageBodySyntaxValidator.Validate(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "an unclosed function body is a fail-fast structural error — letting it through would corrupt the page at runtime");
		result.Line.Should().BeGreaterThan(0,
			because: "the failure response must carry a 1-based line so the operator can localise the problem");
	}

	[Test]
	[Description("Unterminated template literal (backtick without its closing pair) is rejected with line/column pointing at the open quote")]
	public void Validate_ShouldReturnInvalid_WhenBodyHasUnterminatedTemplateLiteral() {
		// Arrange
		string body = "var x = `hello;";

		// Act
		PageBodySyntaxValidationResult result = PageBodySyntaxValidator.Validate(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "an unterminated template literal swallows the rest of the program; the parser must surface this rather than the calling tool persisting a broken body");
	}

	[Test]
	[Description("Mismatched parentheses are rejected — covers an entire class of agent-hallucinated bodies where a `(` was closed by `}` or never closed at all")]
	public void Validate_ShouldReturnInvalid_WhenBodyHasMismatchedParentheses() {
		// Arrange
		string body = "function f() { return ((1 + 2; }";

		// Act
		PageBodySyntaxValidationResult result = PageBodySyntaxValidator.Validate(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "mismatched parens are a deterministic syntax error that the LLM-only sampling cannot catch; the parser must");
	}

	[Test]
	[Description("Stray closing brace `}` at top level is rejected — agents occasionally emit one extra closing brace when truncating a body diff")]
	public void Validate_ShouldReturnInvalid_WhenBodyHasStrayClosingBrace() {
		// Arrange
		string body = "var x = 1; }";

		// Act
		PageBodySyntaxValidationResult result = PageBodySyntaxValidator.Validate(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "a stray top-level closing brace is a structural error the parser catches in microseconds; the validator must surface it before write");
	}

	[Test]
	[Description("Broken arrow function (open arrow body without expression or closing brace) is rejected")]
	public void Validate_ShouldReturnInvalid_WhenBodyHasBrokenArrowFunction() {
		// Arrange
		string body = "var f = (a, b) => { ";

		// Act
		PageBodySyntaxValidationResult result = PageBodySyntaxValidator.Validate(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "an unfinished arrow function body cannot become a valid script no matter what the rest of the body says");
	}

	[Test]
	[Description("Identifier starting with a digit (invalid lexer state) is rejected with a clear message")]
	public void Validate_ShouldReturnInvalid_WhenBodyHasIdentifierStartingWithDigit() {
		// Arrange
		string body = "var 1foo = 1;";

		// Act
		PageBodySyntaxValidationResult result = PageBodySyntaxValidator.Validate(body);

		// Assert
		result.IsValid.Should().BeFalse(
			because: "lexer-level invariants like identifier-must-start-with-non-digit are the easiest class of catch for the parser; surfacing them prevents an agent from inventing variable names that look syntactically OK to a regex scanner but not to a tokenizer");
	}

	#endregion

	#region Tests: performance bound (<50 ms on 50 KB per AC)

	[Test]
	[Description("Performance bound from the AC: a 50 KB body must parse in <50 ms on CI. Fixture is built from repeated populated create-page sections so the bound is exercised on real parsing work (handler arrays, validator/converter object literals, viewConfigDiff entries), not on the lexer's comment-skip fast path.")]
	public void Validate_ShouldCompleteUnder50Ms_WhenBodyIs50KbInSize() {
		// Arrange — repeat a populated create-page body fragment until the
		// resulting payload crosses 50 KB. Each repeat contributes real AST
		// work (Property, ObjectExpression, ArrayExpression, FunctionExpression
		// nodes) so a parser regression on any of those code paths would
		// surface here, unlike a comment-only filler which only exercises the
		// lexer's comment-skip loop.
		const string Fragment =
			"{ operation: \"insert\", name: \"UsrField_X\", values: { type: \"crt.Input\", control: \"$UsrField_X\", layoutConfig: { column: 1, row: 1, colSpan: 1, rowSpan: 1 } } }, ";
		var viewConfigItems = new StringBuilder();
		var validatorEntries = new StringBuilder();
		var converterEntries = new StringBuilder();
		var handlerEntries = new StringBuilder();
		int fragmentIndex = 0;
		while (viewConfigItems.Length + validatorEntries.Length + converterEntries.Length + handlerEntries.Length < 50_000) {
			viewConfigItems.Append(Fragment.Replace("UsrField_X", $"UsrField_{fragmentIndex}"));
			validatorEntries.Append($"\"usr.V{fragmentIndex}\": {{ validator: function() {{ return function(v) {{ return null; }}; }}, params: [{{ name: \"message\" }}] }}, ");
			converterEntries.Append($"\"usr.C{fragmentIndex}\": function(v) {{ return v; }}, ");
			handlerEntries.Append($"{{ request: \"usr.H{fragmentIndex}Request\", handler: async function(request, next) {{ return next?.handle(request); }} }}, ");
			fragmentIndex++;
		}
		string body =
			"define(\"Perf_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
			"function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
			"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[" + viewConfigItems.ToString().TrimEnd(' ', ',') + "]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
			"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
			"handlers: /**SCHEMA_HANDLERS*/[" + handlerEntries.ToString().TrimEnd(' ', ',') + "]/**SCHEMA_HANDLERS*/, " +
			"converters: /**SCHEMA_CONVERTERS*/{" + converterEntries.ToString().TrimEnd(' ', ',') + "}/**SCHEMA_CONVERTERS*/, " +
			"validators: /**SCHEMA_VALIDATORS*/{" + validatorEntries.ToString().TrimEnd(' ', ',') + "}/**SCHEMA_VALIDATORS*/ }; });";
		body.Length.Should().BeGreaterOrEqualTo(50_000,
			because: "the AC pins the parsing bound at 50 KB — the fixture must actually reach that size to test the right thing");

		// Warm-up (cold-start JIT compilation of Acornima is amortised once per
		// process; the AC's bound is the warm parse cost).
		PageBodySyntaxValidator.Validate(body);

		// Act — take min of 3 measurements so a transient GC pause or system
		// hiccup on a noisy CI runner does not flake the test.
		var samples = new double[3];
		for (int i = 0; i < samples.Length; i++) {
			Stopwatch sw = Stopwatch.StartNew();
			PageBodySyntaxValidationResult run = PageBodySyntaxValidator.Validate(body);
			sw.Stop();
			run.IsValid.Should().BeTrue(
				because: "the populated create-page fixture must parse successfully on every measurement");
			samples[i] = sw.Elapsed.TotalMilliseconds;
		}

		// Assert
		samples.Min().Should().BeLessThan(50.0,
			because: "the AC pins a <50 ms ceiling for a 50 KB body on CI; taking min of 3 isolates the steady-state parse cost from transient GC / system noise while keeping the bound honest about real parser work, not a comment-skip fast path");
	}

	#endregion

}
