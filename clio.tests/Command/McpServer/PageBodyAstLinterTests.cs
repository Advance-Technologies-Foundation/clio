using System.Collections.Generic;
using System.Linq;
using System.Text;
using Acornima.Ast;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
internal class PageBodyAstLinterTests {

	#region Helpers

	// Parse a body with the syntax validator's ValidateAndParse so the linter
	// sees exactly the AST shape the production tools feed in.
	private static Script ParseOrThrow(string body) {
		PageBodySyntaxValidationResult result =
			PageBodySyntaxValidator.ValidateAndParse(body, out Script ast);
		result.IsValid.Should().BeTrue(
			because: "lint fixtures must always be syntactically valid — the syntax gate is a separate concern");
		return ast;
	}

	private static IReadOnlyList<PageBodyLintFinding> LintBody(string body) =>
		PageBodyAstLinter.Lint(ParseOrThrow(body));

	#endregion

	#region Tests: clean bodies (no findings)

	[Test]
	[Description("Canonical create-page-shaped body with handlers as array, validators as object, converters as object emits zero findings — the validator must not fire on the normal happy path")]
	public void Lint_ShouldReturnEmpty_WhenBodyMatchesCanonicalShape() {
		string body =
			"define(\"Test_FormPage\", [], function() { return { " +
			"viewConfigDiff: [], viewModelConfigDiff: [], modelConfigDiff: [], " +
			"handlers: [], converters: {}, validators: {} }; });";

		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		findings.Should().BeEmpty(
			because: "the canonical empty-shaped body must never raise lint findings — a non-empty result here would break every legitimate create-page round-trip");
	}

	[Test]
	[Description("A handler call to an undeclared module-scope helper raises an undefined-section-call Error — this is the Page Designer failure mode where the helper is removed but the handler survives")]
	public void Lint_ShouldEmitError_WhenHandlerCallsUndeclaredHelper() {
		// Arrange
		string body =
			"define(\"X\", [], function() { return { handlers: [{ " +
			"request: \"crt.HandleViewModelInitRequest\", " +
			"handler: async (request, next) => { await missingModuleHelper(request); return next?.handle(request); } }], " +
			"converters: {}, validators: {} }; });";

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		// Assert
		findings.Should().ContainSingle(f =>
			f.Rule == PageBodyAstLinter.RuleUndefinedSectionCall && f.Severity == LintSeverity.Error,
			because: "a handler that calls a helper absent from the page body fails at runtime with ReferenceError and must be blocked before sync-pages saves it");
		findings.Single(f => f.Rule == PageBodyAstLinter.RuleUndefinedSectionCall).Message.Should()
			.Contain("missingModuleHelper", because: "the finding must name the missing helper so the operator can restore it");
	}

	[Test]
	[Description("A handler call to a module-scope helper declared before the return object is accepted — declarations in the factory scope are visible to handler callbacks")]
	public void Lint_ShouldNotEmitError_WhenHandlerCallsDeclaredFactoryHelper() {
		// Arrange
		string body =
			"define(\"X\", [], function() { var applyFilter = async function(request) { return request; }; " +
			"return { handlers: [{ request: \"crt.HandleViewModelInitRequest\", " +
			"handler: async (request, next) => { await applyFilter(request); return next?.handle(request); } }], " +
			"converters: {}, validators: {} }; });";

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		// Assert
		findings.Should().NotContain(f => f.Rule == PageBodyAstLinter.RuleUndefinedSectionCall,
			because: "a helper declared in the AMD factory scope is available to handlers and must not be reported as missing");
	}
	[Test]
	[Description("A name declared inside an UNRELATED nested function does not satisfy a handler call — resolution walks the lexical scope chain outwards, so a sibling scope's `const missingModuleHelper` cannot mask the handler's ReferenceError")]
	public void Lint_ShouldEmitError_WhenTheOnlyDeclarationLivesInASiblingScope() {
		// Arrange
		string body =
			"define(\"X\", [], function() { " +
			"function unrelated() { const missingModuleHelper = function() { return 1; }; return missingModuleHelper; } " +
			"return { handlers: [{ request: \"crt.HandleViewModelInitRequest\", " +
			"handler: async (request, next) => { await missingModuleHelper(request); return next?.handle(request); } }], " +
			"converters: {}, validators: {} }; });";

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		// Assert
		findings.Should().ContainSingle(f =>
			f.Rule == PageBodyAstLinter.RuleUndefinedSectionCall && f.Message.Contains("missingModuleHelper"),
			because: "the handler still throws ReferenceError at runtime — a binding in a function it cannot see must not silence the gate");
	}

	[Test]
	[Description("A destructuring KEY is not a binding — `const { alpha: beta } = source` declares `beta` only, so a handler call to `alpha()` is still reported")]
	public void Lint_ShouldEmitError_WhenHandlerCallsADestructuringKey() {
		// Arrange
		string body =
			"define(\"X\", [], function() { const source = {}; const { alpha: beta } = source; " +
			"return { handlers: [{ request: \"crt.HandleViewModelInitRequest\", " +
			"handler: async (request, next) => { alpha(); return next?.handle(request); } }], " +
			"converters: {}, validators: {} }; });";

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		// Assert
		findings.Should().ContainSingle(f =>
			f.Rule == PageBodyAstLinter.RuleUndefinedSectionCall && f.Message.Contains("alpha"),
			because: "the key names the property being read from `source`, it never introduces the name `alpha` into scope");
	}

	[Test]
	[Description("The alias a destructuring pattern binds IS in scope — `const { alpha: beta } = source` makes `beta()` legitimate")]
	public void Lint_ShouldNotEmitError_WhenHandlerCallsADestructuringAlias() {
		// Arrange
		string body =
			"define(\"X\", [], function() { const source = {}; const { alpha: beta } = source; " +
			"return { handlers: [{ request: \"crt.HandleViewModelInitRequest\", " +
			"handler: async (request, next) => { beta(); return next?.handle(request); } }], " +
			"converters: {}, validators: {} }; });";

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		// Assert
		findings.Should().NotContain(f => f.Rule == PageBodyAstLinter.RuleUndefinedSectionCall,
			because: "the alias is the actual binding position and must resolve");
	}

	[TestCase("alert")]
	[TestCase("require")]
	[TestCase("queueMicrotask")]
	[TestCase("btoa")]
	[TestCase("atob")]
	[TestCase("structuredClone")]
	[TestCase("confirm")]
	[TestCase("define")]
	[TestCase("setTimeout")]
	[TestCase("parseInt")]
	[Description("A callable supplied by the browser, the AMD loader or the language itself is never reported — the rule blocks the write, so a name missing from the catalog would reject a page that runs correctly")]
	public void Lint_ShouldNotEmitError_WhenHandlerCallsARuntimeGlobal(string globalName) {
		// Arrange
		string body =
			"define(\"X\", [], function() { return { handlers: [{ request: \"crt.HandleViewModelInitRequest\", " +
			"handler: async (request, next) => { " + globalName + "(\"x\"); return next?.handle(request); } }], " +
			"converters: {}, validators: {} }; });";

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		// Assert
		findings.Should().NotContain(f => f.Rule == PageBodyAstLinter.RuleUndefinedSectionCall,
			because: $"`{globalName}` is provided by the page runtime and calling it is legitimate");
	}

	[Test]
	[Description("Repeated calls to the same undeclared name collapse into one finding plus one omitted-count summary — an LLM-truncated body used to produce one finding per occurrence and a multi-megabyte error string")]
	public void Lint_ShouldDeduplicateRepeatedUndefinedCalls() {
		// Arrange
		string repeated = string.Concat(Enumerable.Repeat("brokenHelper(); ", 5000));
		string body =
			"define(\"X\", [], function() { return { handlers: [{ request: \"crt.HandleViewModelInitRequest\", " +
			"handler: async (request, next) => { " + repeated + "return next?.handle(request); } }], " +
			"converters: {}, validators: {} }; });";

		// Act
		List<PageBodyLintFinding> findings = LintBody(body)
			.Where(f => f.Rule == PageBodyAstLinter.RuleUndefinedSectionCall).ToList();

		// Assert
		findings.Should().HaveCount(2,
			because: "one finding names the callee, the second states how many call sites were left out");
		findings[0].Message.Should().Contain("brokenHelper");
		findings[1].Message.Should().Contain("4999 further call site",
			because: "the omitted count must be stated rather than silently dropped");
		PageBodyAstLinter.FormatErrors(findings).Length.Should().BeLessThan(20_000,
			because: "the agent-facing error string has to stay usable regardless of how broken the body is");
	}

	[Test]
	[Description("Distinct undeclared names are capped and the summary reports how many were omitted, so a body with hundreds of broken calls still yields a bounded response")]
	public void Lint_ShouldCapDistinctUndefinedCallNames() {
		// Arrange
		int total = PageBodyAstLinter.MaxUndefinedSectionCallNames * 3;
		string manyNames = string.Concat(Enumerable.Range(0, total).Select(i => $"broken{i}(); "));
		string body =
			"define(\"X\", [], function() { return { handlers: [{ request: \"crt.HandleViewModelInitRequest\", " +
			"handler: async (request, next) => { " + manyNames + "return next?.handle(request); } }], " +
			"converters: {}, validators: {} }; });";

		// Act
		List<PageBodyLintFinding> findings = LintBody(body)
			.Where(f => f.Rule == PageBodyAstLinter.RuleUndefinedSectionCall).ToList();

		// Assert
		findings.Should().HaveCount(PageBodyAstLinter.MaxUndefinedSectionCallNames + 1,
			because: "the cap keeps the listed names bounded and adds exactly one summary finding");
		findings[^1].Message.Should().Contain(
			$"{total - PageBodyAstLinter.MaxUndefinedSectionCallNames} further undeclared name(s)",
			because: "the operator has to know the report is partial and by how much");
	}

	[TestCase("try { } catch (handleIt) { handleIt(); }", TestName = "catch clause parameter")]
	[TestCase("for (const step of []) { step(); }", TestName = "for-of loop head")]
	[TestCase("if (true) { var later = function() { return 1; }; } later();", TestName = "var hoisted out of a block")]
	[TestCase("helperBelow(); function helperBelow() { return 1; }", TestName = "function declared after the call")]
	[Description("Every binding form a handler body may legitimately use resolves against the scope chain — a false positive here blocks a page that runs correctly")]
	public void Lint_ShouldNotEmitError_ForLegitimateBindingForms(string handlerBody) {
		// Arrange
		string body =
			"define(\"X\", [], function() { return { handlers: [{ request: \"crt.HandleViewModelInitRequest\", " +
			"handler: async (request, next) => { " + handlerBody + " return next?.handle(request); } }], " +
			"converters: {}, validators: {} }; });";

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		// Assert
		findings.Should().NotContain(f => f.Rule == PageBodyAstLinter.RuleUndefinedSectionCall,
			because: "the callee is bound by the handler body itself");
	}

	[Test]
	[Description("Validator declaration with `return null` inside the inner async function is allowed — null signals \"no error\" per the validator contract and must NOT be flagged as validator-bad-return-literal")]
	public void Lint_ShouldAllowNullReturn_InValidatorFactory() {
		string body =
			"define(\"X\", [], function() { return { handlers: [], converters: {}, validators: { " +
			"\"usr.MyValidator\": { validator: function(config) { return async function(control) { return null; }; }, " +
			"params: [{ name: \"message\" }], async: true } } }; });";

		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		findings.Should().NotContain(f => f.Rule == PageBodyAstLinter.RuleValidatorBadReturnLiteral,
			because: "`return null` is the validator-contract-compliant way to say \"value passes\" — flagging it would break every guarded-pass validator that production code already ships");
	}

	#endregion

	#region Tests: structural errors (fail-fast severity)

	[TestCase("return true;", TestName = "Lint_ShouldEmitError_WhenValidatorReturnsLiteralTrue")]
	[TestCase("return false;", TestName = "Lint_ShouldEmitError_WhenValidatorReturnsLiteralFalse")]
	[TestCase("return \"msg\";", TestName = "Lint_ShouldEmitError_WhenValidatorReturnsHardcodedString")]
	[TestCase("return {};", TestName = "Lint_ShouldEmitError_WhenValidatorReturnsEmptyObject")]
	[Description("Validator factory must not return literal `true`, `false`, a string, or an empty object literal — these violate the `{ \"<Type>\": { message } }` contract")]
	public void Lint_ShouldEmitError_WhenValidatorReturnsBadLiteral(string badReturn) {
		string body =
			"define(\"X\", [], function() { return { handlers: [], converters: {}, validators: { " +
			"\"usr.V\": { validator: function() { return function(value) { " + badReturn + " }; }, " +
			"params: [{ name: \"message\" }] } } }; });";

		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		findings.Should().ContainSingle(f =>
			f.Rule == PageBodyAstLinter.RuleValidatorBadReturnLiteral && f.Severity == LintSeverity.Error,
			because: "the guidance explicitly bans literal `true / false / {} / hardcoded-string` returns from validator factories because they fail to surface a user-visible message");
	}

	[Test]
	[Description("Legitimate validator with a nested array-callback predicate (`.filter(function(i){ return true; })`) must NOT raise validator-bad-return-literal — the inner `return true` belongs to the predicate, not the validator itself, and blocking it would reject correct JavaScript")]
	public void Lint_ShouldNotEmitError_WhenValidatorBodyContainsNestedCallbackReturningLiteral() {
		string body =
			"define(\"X\", [], function() { return { handlers: [], converters: {}, validators: { " +
			"\"usr.AllValid\": { validator: function(c) { return function(value) { " +
			"if (value.items.filter(function(i){ return true; }).length) { return { \"usr.AllValid\": { message: c.message } }; } " +
			"return null; " +
			"}; }, params: [{ name: \"message\" }] } " +
			"} }; });";

		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		findings.Should().NotContain(f => f.Rule == PageBodyAstLinter.RuleValidatorBadReturnLiteral,
			because: "the only `return true` here is inside a `.filter(...)` predicate at function-depth 3, NOT a validator factory or its inner validator function — the lint pass must scope the rule by function nesting so it does not block legitimate JavaScript that happens to live inside the validators subtree");
	}

	[Test]
	[Description("Custom converter declared with the reserved `crt.*` prefix raises a converter-crt-prefix-reserved Error — only Creatio built-in converters may use this namespace")]
	public void Lint_ShouldEmitError_WhenCustomConverterUsesCrtPrefix() {
		string body =
			"define(\"X\", [], function() { return { handlers: [], converters: { " +
			"\"crt.Custom\": function(v) { return v; } " +
			"}, validators: {} }; });";

		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		findings.Should().ContainSingle(f =>
			f.Rule == PageBodyAstLinter.RuleConverterCrtPrefixReserved && f.Severity == LintSeverity.Error,
			because: "the `crt.*` namespace is reserved for Creatio built-in converters; agents occasionally invent `crt.UsrX` custom converters and they collide with future platform-level names");
	}

	[Test]
	[Description("`crt.*` keys nested inside a converter function body (e.g. a local lookup map) must NOT raise converter-crt-prefix-reserved — the rule is scoped to direct property entries of the converters object, and the lookup table is opaque from the rule's perspective")]
	public void Lint_ShouldNotEmitError_WhenCrtKeyAppearsInsideConverterFunctionBody() {
		string body =
			"define(\"X\", [], function() { return { handlers: [], converters: { " +
			"\"usr.Label\": function(v) { var labels = { \"crt.A\": \"Alpha\", \"crt.B\": \"Beta\" }; return labels[v] || v; } " +
			"}, validators: {} }; });";

		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		findings.Should().NotContain(f => f.Rule == PageBodyAstLinter.RuleConverterCrtPrefixReserved,
			because: "the `crt.A` / `crt.B` keys are inside a private lookup map declared in a legitimate converter function body — they are not custom converter declarations, so the rule must not block the save");
	}

	[Test]
	[Description("Validator factory with a nested helper function declared inside its body (`function isEmpty(v) { if (!v) return true; return false; }`) must NOT raise validator-bad-return-literal — the helper's `return true / false` belongs to the helper, not the validator-instance function, and blocking it would reject legitimate JavaScript")]
	public void Lint_ShouldNotEmitError_WhenValidatorFactoryDeclaresNestedHelperFunction() {
		string body =
			"define(\"X\", [], function() { return { handlers: [], converters: {}, validators: { " +
			"\"usr.Phone\": { validator: function(config) { " +
			"function isEmpty(v) { if (!v) return true; return false; } " +
			"return function(control) { return isEmpty(control.value) ? null : { \"usr.Phone\": { message: config.message } }; }; " +
			"}, params: [{ name: \"message\" }] } } }; });";

		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		findings.Should().NotContain(f => f.Rule == PageBodyAstLinter.RuleValidatorBadReturnLiteral,
			because: "the `return true / return false` here belong to the nested `isEmpty` helper, not the validator-instance function; scoping the rule by enclosing-function identity is what keeps the gate from rejecting legitimate JS");
	}

	[Test]
	[Description("A pathologically deep AST is rejected by SOME deterministic gate (the parser's stack guard OR the linter's depth cap) — the test pins the either/or contract that a deep body cannot kill the MCP process, not which specific gate fires. The exact stack threshold is platform-dependent (1 MB on Windows vs 8 MB on macOS/Linux), so asserting only one specific gate would make the test platform-conditional.")]
	public void Lint_ShouldShortCircuit_WhenBodyAstNestingExceedsParserOrLinterCap() {
		// Body whose expression literal nests Array literals 1200 deep — past
		// the linter's MaxAstDepth and (on a 1 MB Windows stack) also past the
		// Acornima parser's own stack guard.
		int depth = PageBodyAstLinter.MaxAstDepth + 200;
		string nested = new string('[', depth) + new string(']', depth);
		string body =
			"define(\"X\", [], function() { var x = " + nested + "; return { handlers: [], converters: {}, validators: {} }; });";

		PageBodySyntaxValidationResult parserResult = PageBodySyntaxValidator.ValidateAndParse(body, out Script ast);

		if (!parserResult.IsValid) {
			// Parser stack guard fired first — the body is rejected before the
			// lint pass ever sees it. The either/or contract is satisfied: the
			// process did not die.
			parserResult.Message.Should().NotBeNullOrEmpty(
				because: "the syntax validator must surface a structured error rather than crash when the parser stack guard fires");
			return;
		}

		// Parser accepted the body (e.g. on a runner with the default macOS /
		// Linux 8 MB stack). The lint cap must now reject it.
		IReadOnlyList<PageBodyLintFinding> findings = PageBodyAstLinter.Lint(ast);
		findings.Should().Contain(f =>
			f.Rule == PageBodyAstLinter.RuleBodyTooDeeplyNested && f.Severity == LintSeverity.Error,
			because: "when the parser stack guard does not fire, the linter cap must reject the body before .NET's uncatchable StackOverflowException kills the MCP server process");
	}

	#endregion

	#region Tests: behavioural warnings (non-blocking severity)

	[Test]
	[Description("`request.$context.executeRequest({ ... })` raises a handler-uses-context-execute-request Warning — Creatio Academy SCHEMA_HANDLERS examples use `sdk.HandlerChainService.instance.process(...)`, executeRequest is not part of the documented @creatio-devkit/common public surface")]
	public void Lint_ShouldEmitWarning_WhenHandlerUsesContextExecuteRequest() {
		string body =
			"define(\"X\", [], function() { return { handlers: [{ " +
			"request: \"crt.HandleViewModelInitRequest\", " +
			"handler: async function(request, next) { await request.$context.executeRequest({ type: \"crt.OpenPageRequest\" }); return next?.handle(request); } }], " +
			"converters: {}, validators: {} }; });";

		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		findings.Should().ContainSingle(f =>
			f.Rule == PageBodyAstLinter.RuleHandlerUsesContextExecuteRequest && f.Severity == LintSeverity.Warning,
			because: "Academy uniformly uses `sdk.HandlerChainService.instance.process(...)` in SCHEMA_HANDLERS examples; `request.$context.executeRequest(...)` is reachable but undocumented and may break across minor versions");
	}

	[Test]
	[Description("Direct `fetch(...)` call raises a converter-fetch-call Warning — non-cached HTTP fires on every render when placed in a converter; outside converters the warning is informational")]
	public void Lint_ShouldEmitWarning_WhenBodyContainsBareFetchCall() {
		string body =
			"define(\"X\", [], function() { return { handlers: [], converters: { " +
			"\"usr.Lookup\": function(v) { return fetch(\"/api/lookup?id=\" + v); } " +
			"}, validators: {} }; });";

		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		findings.Should().ContainSingle(f =>
			f.Rule == PageBodyAstLinter.RuleConverterFetchCall && f.Severity == LintSeverity.Warning,
			because: "non-cached HTTP inside a converter re-fires on every render of the bound control; flagging the call site alerts the operator");
	}

	#endregion

	#region Tests: scoping and false-positive bounds

	[Test]
	[Description("`return null` from a top-level function (not inside the `validators` schema section) does NOT trigger validator-bad-return-literal — the rule is bounded to the validators subtree via VisitContext.InsideValidators")]
	public void Lint_ShouldNotEmitValidatorReturn_WhenReturnIsOutsideValidatorsBlock() {
		string body =
			"define(\"X\", [], function() { var f = function() { return null; }; return { " +
			"handlers: [], converters: {}, validators: {} }; });";

		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		findings.Should().NotContain(f => f.Rule == PageBodyAstLinter.RuleValidatorBadReturnLiteral,
			because: "a `return null` outside the validators schema section is not a validator-contract violation; bounding the rule via VisitContext keeps it noise-free");
	}

	[Test]
	[Description("`crt.*` keys outside the converters schema section (e.g. on a handler `request` field) are NOT flagged — the converter-crt-prefix rule is bounded to the converters subtree")]
	public void Lint_ShouldNotEmitConverterCrt_WhenCrtKeyIsOutsideConvertersBlock() {
		string body =
			"define(\"X\", [], function() { return { handlers: [{ " +
			"request: \"crt.HandleViewModelInitRequest\", handler: async function(request, next) { return next?.handle(request); } }], " +
			"converters: {}, validators: {} }; });";

		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		findings.Should().NotContain(f => f.Rule == PageBodyAstLinter.RuleConverterCrtPrefixReserved,
			because: "the `request: \"crt.HandleViewModelInitRequest\"` value is the canonical handler-binding shape and must not be conflated with reserved-converter-namespace usage");
	}

	[Test]
	[Description("Spread elements (`...base`) inside the schema return-object do not crash the visitor — defensive against agent-generated `{ ...defaults, converters: {...} }` shapes that would otherwise hit a cast in the converters direct-child walk")]
	public void Lint_ShouldHandleSpreadElement_WithoutCrashOrFalsePositive() {
		string body =
			"define(\"X\", [], function() { var defaults = { converters: {} }; return { ...defaults, " +
			"converters: {}, validators: {} }; });";

		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		findings.Should().NotContain(f => f.Rule == PageBodyAstLinter.RuleConverterCrtPrefixReserved,
			because: "spread elements are not Property nodes and must be skipped by the converters direct-child walk without emitting false positives or crashing the visitor");
	}

	[Test]
	[Description("`fetch(...)` calls OUTSIDE the converters schema section are NOT flagged — the rule is bounded via VisitContext.InsideConverters so a legitimate handler-side `fetch` (rare but valid) does not produce noise")]
	public void Lint_ShouldNotEmitFetchWarning_WhenFetchIsOutsideConvertersBlock() {
		string body =
			"define(\"X\", [], function() { return { handlers: [{ " +
			"request: \"crt.HandleViewModelInitRequest\", " +
			"handler: async function(request, next) { await fetch(\"/api/ping\"); return next?.handle(request); } }], " +
			"converters: {}, validators: {} }; });";

		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		findings.Should().NotContain(f => f.Rule == PageBodyAstLinter.RuleConverterFetchCall,
			because: "the converter-fetch-call rule's reason to exist is the per-render render-fire pattern specific to converters; firing on every fetch elsewhere would generate noise that buries real findings");
	}

	#endregion

	#region Tests: format error helper

	[Test]
	[Description("FormatErrors joins multiple error findings into one operator-facing message ending with the canonical \"body was NOT sent to Creatio.\" tail")]
	public void FormatErrors_ShouldRenderEachFinding_WithRuleLineColumnAndMessage() {
		// Arrange — two errors on the same body
		PageBodyLintFinding e1 = new(
			Rule: PageBodyAstLinter.RuleConverterCrtPrefixReserved,
			Severity: LintSeverity.Error,
			Line: 3,
			Column: 1,
			Message: "custom converter uses the reserved `crt.*` namespace");
		PageBodyLintFinding e2 = new(
			Rule: PageBodyAstLinter.RuleValidatorBadReturnLiteral,
			Severity: LintSeverity.Error,
			Line: 12,
			Column: 7,
			Message: "validator return must be the canonical shape");

		// Act
		string rendered = PageBodyAstLinter.FormatErrors([e1, e2]);

		// Assert
		rendered.Should()
			.Contain("converter-crt-prefix-reserved", because: "the rule id must be visible to the operator")
			.And.Contain("line 3, column 1", because: "the precise location must be visible")
			.And.Contain("validator-bad-return-literal", because: "every distinct error must be enumerated")
			.And.EndWith("The body was NOT sent to Creatio.",
				because: "the tail must match the syntax validator's tail so callers can key on a single substring for both gates");
	}

	[Test]
	[Description("FormatErrors throws ArgumentException on an empty list — the helper is only meaningful when there is at least one error to render")]
	public void FormatErrors_ShouldThrow_WhenInputIsEmpty() {
		System.Action act = () => PageBodyAstLinter.FormatErrors(new List<PageBodyLintFinding>());

		act.Should().Throw<System.ArgumentException>(
			because: "FormatErrors's caller already short-circuits on success — invoking it with no errors is a contract violation worth surfacing immediately");
	}

	#endregion

	#region Tests: entity-data-source-static-filters (ENG-93867, Warning severity)

	[Test]
	[Description("A `crt.EntityDataSource` carrying a `config.filters` block raises a single entity-data-source-static-filters Warning — the key is never applied at runtime, so the list silently shows unfiltered data")]
	public void Lint_ShouldEmitWarning_WhenEntityDataSourceHasConfigFilters() {
		// Arrange
		string body =
			"define(\"X\", [], function() { return { handlers: [], converters: {}, validators: {}, modelConfigDiff: [ " +
			"{ \"operation\": \"merge\", \"path\": [\"dataSources\"], \"values\": { " +
			"\"EmailDS\": { \"type\": \"crt.EntityDataSource\", \"scope\": \"viewElement\", \"config\": { " +
			"\"entitySchemaName\": \"Activity\", \"attributes\": { \"Title\": { \"path\": \"Title\" } }, " +
			"\"filters\": { \"items\": {}, \"logicalOperation\": 0, \"isEnabled\": true, \"filterType\": 6, \"rootSchemaName\": \"Activity\" } } } } } ] }; });";

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		// Assert
		findings.Should().ContainSingle(f =>
			f.Rule == PageBodyAstLinter.RuleEntityDataSourceStaticFilters && f.Severity == LintSeverity.Warning,
			because: "config.filters on a crt.EntityDataSource is a silent no-op (the source reads only entitySchemaName + attributes); the agent must be warned to move the static filter to a _PredefinedFilter attribute, but the write must not be blocked since the body still renders");
	}

	[Test]
	[Description("A `crt.EntityDataSource` with only `entitySchemaName` + `attributes` (the canonical shape) raises no entity-data-source-static-filters finding — the rule must fire only when a `filters` key is actually present")]
	public void Lint_ShouldNotEmitWarning_WhenEntityDataSourceHasNoConfigFilters() {
		// Arrange
		string body =
			"define(\"X\", [], function() { return { handlers: [], converters: {}, validators: {}, modelConfigDiff: [ " +
			"{ \"operation\": \"merge\", \"path\": [\"dataSources\"], \"values\": { " +
			"\"EmailDS\": { \"type\": \"crt.EntityDataSource\", \"scope\": \"viewElement\", \"config\": { " +
			"\"entitySchemaName\": \"Activity\", \"attributes\": { \"Title\": { \"path\": \"Title\" } } } } } } ] }; });";

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		// Assert
		findings.Should().NotContain(f => f.Rule == PageBodyAstLinter.RuleEntityDataSourceStaticFilters,
			because: "the canonical EntityDataSource shape (entitySchemaName + attributes, no filters) is what every valid detail emits — flagging it would fire on essentially every page and destroy the signal");
	}

	[Test]
	[Description("A `crt.IndicatorWidget` whose `config.data.providing.filters` carries an inline filter raises no entity-data-source-static-filters finding — the widget legitimately reads its own providing filter, and its providing object exposes `schemaName`, never `entitySchemaName`, so the EntityDataSource config signature does not match")]
	public void Lint_ShouldNotEmitWarning_WhenIndicatorWidgetHasProvidingFilters() {
		// Arrange
		string body =
			"define(\"X\", [], function() { return { handlers: [], converters: {}, validators: {}, viewConfigDiff: [ " +
			"{ \"operation\": \"insert\", \"name\": \"IndicatorWidget_a\", \"values\": { \"type\": \"crt.IndicatorWidget\", \"config\": { " +
			"\"data\": { \"providing\": { \"schemaName\": \"Account\", \"filters\": { \"filter\": { \"items\": {}, \"filterType\": 6, \"rootSchemaName\": \"Account\" } } } } } } } ] }; });";

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		// Assert
		findings.Should().NotContain(f => f.Rule == PageBodyAstLinter.RuleEntityDataSourceStaticFilters,
			because: "the IndicatorWidget applies config.data.providing.filters at render time — that is the correct, runtime-honored mechanism for that component; its providing object carries `schemaName` (not `entitySchemaName`), so the EntityDataSource config signature does not match and the rule must not misfire");
	}

	[Test]
	[Description("A Freedom UI Dashboard container's generated `_designOptions` block carrying both `entitySchemaName` and a `filters` array raises no entity-data-source-static-filters finding — `_designOptions` is designer-owned dashboard metadata, not a `crt.EntityDataSource` config, even though it happens to share the same co-located-key signature (GH-1125)")]
	public void Lint_ShouldNotEmitWarning_WhenDashboardDesignOptionsHasEntitySchemaNameAndFilters() {
		// Arrange
		string body =
			"define(\"X\", [], function() { return { handlers: [], converters: {}, validators: {}, viewConfigDiff: [ " +
			"{ \"operation\": \"merge\", \"name\": \"Dashboards\", \"values\": { \"_designOptions\": { " +
			"\"entitySchemaName\": \"UsrExample\", \"dependencies\": [], \"filters\": [] } } } ] }; });";

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		// Assert
		findings.Should().NotContain(f => f.Rule == PageBodyAstLinter.RuleEntityDataSourceStaticFilters,
			because: "the dashboard's `_designOptions.filters` array is designer-generated dashboard metadata, not a `crt.EntityDataSource` config, so the 'filters is an ignored EntityDataSource config key' claim this rule warns about does not apply — flagging it would be a false positive that misdirects the agent toward an unrelated, non-existent fix");
	}

	[Test]
	[Description("A genuine `crt.EntityDataSource` config.filters sitting alongside a Dashboard's `_designOptions` block in the same page body still raises entity-data-source-static-filters — the `_designOptions` carve-out is scoped to the exact object held directly by a property literally named `_designOptions`, not to the whole page body or dashboard entry")]
	public void Lint_ShouldStillEmitWarning_WhenGenuineEntityDataSourceCoexistsWithDashboardDesignOptions() {
		// Arrange
		string body =
			"define(\"X\", [], function() { return { handlers: [], converters: {}, validators: {}, " +
			"viewConfigDiff: [ { \"operation\": \"merge\", \"name\": \"Dashboards\", \"values\": { \"_designOptions\": { " +
			"\"entitySchemaName\": \"UsrExample\", \"dependencies\": [], \"filters\": [] } } } ], " +
			"modelConfigDiff: [ { \"operation\": \"merge\", \"path\": [\"dataSources\"], \"values\": { " +
			"\"EmailDS\": { \"type\": \"crt.EntityDataSource\", \"scope\": \"viewElement\", \"config\": { " +
			"\"entitySchemaName\": \"Activity\", \"filters\": { \"items\": {}, \"filterType\": 6, \"rootSchemaName\": \"Activity\" } } } } } ] }; });";

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		// Assert
		findings.Should().ContainSingle(f => f.Rule == PageBodyAstLinter.RuleEntityDataSourceStaticFilters,
			because: "the `_designOptions` carve-out must not swallow a genuine EntityDataSource false negative elsewhere in the same body — only the object directly held by a property literally named `_designOptions` is excluded, everything else keeps the existing detection");
	}

	[Test]
	[Description("The canonical static-filter mechanism — a `_PredefinedFilter` view-model attribute referenced from the collection attribute's `filterAttributes` — raises no entity-data-source-static-filters finding because no `filters` key sits on a crt.EntityDataSource config")]
	public void Lint_ShouldNotEmitWarning_WhenStaticFilterUsesPredefinedFilterAttribute() {
		// Arrange
		string body =
			"define(\"X\", [], function() { return { handlers: [], converters: {}, validators: {}, viewModelConfigDiff: [ " +
			"{ \"operation\": \"merge\", \"path\": [\"attributes\"], \"values\": { " +
			"\"Grid\": { \"isCollection\": true, \"modelConfig\": { \"path\": \"GridDS\", \"filterAttributes\": [ { \"name\": \"Grid_PredefinedFilter\", \"loadOnChange\": true } ] } }, " +
			"\"Grid_PredefinedFilter\": { \"value\": { \"items\": {}, \"logicalOperation\": 0, \"isEnabled\": true, \"filterType\": 6, \"rootSchemaName\": \"Contact\" } } } } ] }; });";

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		// Assert
		findings.Should().NotContain(f => f.Rule == PageBodyAstLinter.RuleEntityDataSourceStaticFilters,
			because: "the _PredefinedFilter attribute + filterAttributes wiring is the correct, runtime-honored channel the guidance recommends — the rule must green-light it so agents that follow the guidance are never warned");
	}

	[Test]
	[Description("A crt.EntityDataSource config carried by a narrower/split diff merge — the config keys reach the body without an enclosing `type` descriptor in the same object — still raises entity-data-source-static-filters, because the rule keys off the config signature (filters + entitySchemaName), not the enclosing type wrapper (ENG-93867 PR review follow-up)")]
	public void Lint_ShouldEmitWarning_WhenConfigFiltersSplitFromDescriptor() {
		// Arrange — descriptor and config are split across merge ops: the config merge targets the
		// dataSources.<name>.config path and carries entitySchemaName alongside the ignored filters,
		// so the filters-bearing object has NO sibling `type`. The old type-gated rule missed this.
		string body =
			"define(\"X\", [], function() { return { handlers: [], converters: {}, validators: {}, modelConfigDiff: [ " +
			"{ \"operation\": \"merge\", \"path\": [\"dataSources\", \"EmailDS\", \"config\"], \"values\": { " +
			"\"entitySchemaName\": \"Activity\", \"attributes\": { \"Title\": { \"path\": \"Title\" } }, " +
			"\"filters\": { \"items\": {}, \"logicalOperation\": 0, \"isEnabled\": true, \"filterType\": 6, \"rootSchemaName\": \"Activity\" } } } ] }; });";

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		// Assert
		findings.Should().ContainSingle(f =>
			f.Rule == PageBodyAstLinter.RuleEntityDataSourceStaticFilters && f.Severity == LintSeverity.Warning,
			because: "config.filters is ignored no matter how the EntityDataSource config reaches the body; a split/narrower merge that still carries entitySchemaName must be flagged, not evaded just because the `type` descriptor lives in a separate operation");
	}

	[Test]
	[Description("Two crt.EntityDataSource descriptors where only one carries config.filters raise exactly one entity-data-source-static-filters finding — the rule targets the offending source and stays silent on the clean sibling")]
	public void Lint_ShouldEmitSingleWarning_WhenOnlyOneOfTwoDataSourcesHasConfigFilters() {
		// Arrange
		string body =
			"define(\"X\", [], function() { return { handlers: [], converters: {}, validators: {}, modelConfigDiff: [ " +
			"{ \"operation\": \"merge\", \"path\": [\"dataSources\"], \"values\": { " +
			"\"CleanDS\": { \"type\": \"crt.EntityDataSource\", \"scope\": \"viewElement\", \"config\": { " +
			"\"entitySchemaName\": \"Contact\", \"attributes\": { \"Name\": { \"path\": \"Name\" } } } }, " +
			"\"EmailDS\": { \"type\": \"crt.EntityDataSource\", \"scope\": \"viewElement\", \"config\": { " +
			"\"entitySchemaName\": \"Activity\", " +
			"\"filters\": { \"items\": {}, \"logicalOperation\": 0, \"isEnabled\": true, \"filterType\": 6, \"rootSchemaName\": \"Activity\" } } } } } ] }; });";

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		// Assert
		findings.Should().ContainSingle(f => f.Rule == PageBodyAstLinter.RuleEntityDataSourceStaticFilters,
			because: "the rule runs per data source and must flag only the source that actually carries config.filters, not fire once-per-page or spill onto the clean sibling");
	}

	[Test]
	[Description("The entity-data-source-static-filters finding anchors to the offending `filters` property, not to the enclosing data-source object — proven by placing `filters` on its own line and asserting the finding's line")]
	public void Lint_ShouldAnchorWarning_AtTheFiltersProperty() {
		// Arrange — the data-source object opens on line 1; the `filters` property is on line 2.
		string body =
			"define(\"X\", [], function() { return { handlers: [], converters: {}, validators: {}, modelConfigDiff: [ { \"operation\": \"merge\", \"path\": [\"dataSources\"], \"values\": { \"EmailDS\": { \"type\": \"crt.EntityDataSource\", \"config\": { \"entitySchemaName\": \"Activity\",\n" +
			"\"filters\": { \"items\": {}, \"filterType\": 6, \"rootSchemaName\": \"Activity\" } } } } } ] }; });";

		// Act
		PageBodyLintFinding finding = LintBody(body)
			.Single(f => f.Rule == PageBodyAstLinter.RuleEntityDataSourceStaticFilters);

		// Assert
		finding.Line.Should().Be(2,
			because: "the finding must point the operator at the `filters` property itself (line 2), not the data-source object opening on line 1, so the reported location is actionable");
		finding.Column.Should().BeGreaterThan(0,
			because: "the column must be a populated 1-based position, not an unset default");
	}

	#endregion

	#region Tests: handler-attribute-change-unscoped-write (self-retrigger footgun)

	[Test]
	[Description("An unscoped crt.HandleViewModelAttributeChangeRequest handler that writes an attribute via $context.set(...) raises handler-attribute-change-unscoped-write (Warning) — requestArgumentPropertyName does NOT scope it, so it re-fires on its own write and clears the field")]
	public void Lint_ShouldWarn_WhenAttributeChangeHandlerIsUnscopedAndWrites() {
		// Arrange
		string body =
			"define(\"X\", [], function() { return { converters: {}, validators: {}, handlers: [ { " +
			"request: \"crt.HandleViewModelAttributeChangeRequest\", requestArgumentPropertyName: \"UsrPhoneNumber\", " +
			"handler: async (request, next) => { await next?.handle(request); const { $context } = request; " +
			"await $context.set(\"UsrCountryCode\", request.value.substring(1, 3)); } } ] }; });";

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		// Assert
		findings.Should().ContainSingle(f =>
			f.Rule == PageBodyAstLinter.RuleHandlerAttributeChangeUnscopedWrite && f.Severity == LintSeverity.Warning,
			because: "the handler writes UsrCountryCode but is not scoped to an attribute (requestArgumentPropertyName is silently ignored), so at runtime setting the attribute re-enters the handler with the wrong value and the else-branch clears the field — exactly the ENG-95557 phone-number failure");
	}

	[Test]
	[Description("An unscoped attribute-change handler written as a shorthand METHOD (`async handler(request, next) {}` rather than `handler: (request, next) => {}`) that writes an attribute must still raise the warning — the entry lookup must accept method-form properties or the genuine ENG-95557 bug is missed when the author uses method syntax")]
	public void Lint_ShouldWarn_WhenHandlerIsShorthandMethodAndUnscoped() {
		// Arrange
		string body =
			"define(\"X\", [], function() { return { converters: {}, validators: {}, handlers: [ { " +
			"request: \"crt.HandleViewModelAttributeChangeRequest\", " +
			"async handler(request, next) { await request.$context.set(\"UsrCountryCode\", \"38\"); return next?.handle(request); } } ] }; });";

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		// Assert
		findings.Should().ContainSingle(f =>
			f.Rule == PageBodyAstLinter.RuleHandlerAttributeChangeUnscopedWrite && f.Severity == LintSeverity.Warning,
			because: "shorthand-method and init-property handler forms are semantically identical; the rule must not go blind to the self-retrigger footgun just because the author used method syntax");
	}

	[Test]
	[Description("A crt.HandleViewModelAttributeChangeRequest handler scoped by an in-body BRACKET-access guard `request[\"attributeName\"]` must NOT raise the warning — the scope-awareness signal is a COMPUTED member access whose property literal is \"attributeName\", which the scan must treat like the identifier form")]
	public void Lint_ShouldNotWarn_WhenGuardUsesBracketAccessAttributeName() {
		// Arrange
		string body =
			"define(\"X\", [], function() { return { converters: {}, validators: {}, handlers: [ { " +
			"request: \"crt.HandleViewModelAttributeChangeRequest\", " +
			"handler: async (request, next) => { if (request[\"attributeName\"] !== \"UsrPhoneNumber\") { return next?.handle(request); } " +
			"await request.$context.set(\"UsrCountryCode\", \"38\"); return next?.handle(request); } } ] }; });";

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		// Assert
		findings.Should().NotContain(f => f.Rule == PageBodyAstLinter.RuleHandlerAttributeChangeUnscopedWrite,
			because: "request[\"attributeName\"] is the same scope-aware guard as request.attributeName, just via bracket access; the scan must recognise the computed member or it would falsely flag a correctly-scoped handler");
	}

	[Test]
	[Description("Specificity of the bracket-access signal: a handler that references a DIFFERENT computed bracket key `request[\"someOtherField\"]` (not attributeName) and writes via $context.set is NOT scope-aware and MUST still raise the warning — proves the computed-member match is anchored to the \"attributeName\" property literal and does not suppress on an unrelated bracket key")]
	public void Lint_ShouldWarn_WhenBracketAccessKeyIsNotAttributeNameAndWriteUnscoped() {
		// Arrange
		string body =
			"define(\"X\", [], function() { return { converters: {}, validators: {}, handlers: [ { " +
			"request: \"crt.HandleViewModelAttributeChangeRequest\", " +
			"handler: async (request, next) => { const other = request[\"someOtherField\"]; " +
			"await request.$context.set(\"UsrCountryCode\", other); return next?.handle(request); } } ] }; });";

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		// Assert
		findings.Should().ContainSingle(f =>
			f.Rule == PageBodyAstLinter.RuleHandlerAttributeChangeUnscopedWrite && f.Severity == LintSeverity.Warning,
			because: "the scope-awareness signal is a computed member access on the \"attributeName\" property specifically; a bracket read of an unrelated key does not scope the handler, so the unscoped write must still warn");
	}

	[Test]
	[Description("Regression lock for the computed-member narrowing: an incidental \"attributeName\" STRING LITERAL in a non-guard position — here the write target `$context.set(\"attributeName\", ...)` — with NO request.attributeName / request[\"attributeName\"] guard MUST still raise the warning. This fails under the earlier bare-`Literal{Value:\"attributeName\"}`-anywhere match (which wrongly suppressed) and passes under the computed-MemberExpression match, so it pins the narrowing against a silent revert")]
	public void Lint_ShouldWarn_WhenIncidentalAttributeNameLiteralButWriteUnscoped() {
		// Arrange
		string body =
			"define(\"X\", [], function() { return { converters: {}, validators: {}, handlers: [ { " +
			"request: \"crt.HandleViewModelAttributeChangeRequest\", " +
			"handler: async (request, next) => { await request.$context.set(\"attributeName\", request.value); return next?.handle(request); } } ] }; });";

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		// Assert
		findings.Should().ContainSingle(f =>
			f.Rule == PageBodyAstLinter.RuleHandlerAttributeChangeUnscopedWrite && f.Severity == LintSeverity.Warning,
			because: "a bare \"attributeName\" string literal used as a $context.set target is NOT a scope guard; only a computed member access request[\"attributeName\"] (or request.attributeName) marks scope-awareness, so this unscoped write must warn — pinning the narrowing so a revert to the bare-literal match turns this test red");
	}

	[Test]
	[Description("The removed condition suppressor stays removed: a handler carrying a `condition: { attributeName: \"X\" }` sibling but NO in-body attributeName reference, writing via $context.set, MUST now raise the warning — condition is a silently-ignored key (not in page-schema-handlers guidance) and must not scope the handler; this locks the inverse of the two deleted condition tests so an accidental reintroduction is caught")]
	public void Lint_ShouldWarn_WhenHandlerScopedOnlyByConditionAndWriteUnscoped() {
		// Arrange
		string body =
			"define(\"X\", [], function() { return { converters: {}, validators: {}, handlers: [ { " +
			"request: \"crt.HandleViewModelAttributeChangeRequest\", condition: { attributeName: \"UsrPhoneNumber\" }, " +
			"handler: async (request, next) => { await request.$context.set(\"UsrCountryCode\", \"38\"); return next?.handle(request); } } ] }; });";

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		// Assert
		findings.Should().ContainSingle(f =>
			f.Rule == PageBodyAstLinter.RuleHandlerAttributeChangeUnscopedWrite && f.Severity == LintSeverity.Warning,
			because: "condition is not a documented scoping mechanism and is silently ignored by Freedom UI; the linter must not treat it as scope, or it would stay quiet on the exact ENG-95557 defect while advising a silently-ignored key");
	}

	[Test]
	[Description("A crt.HandleViewModelAttributeChangeRequest handler scoped imperatively by an in-body request.attributeName guard must NOT raise the warning even when it writes an attribute")]
	public void Lint_ShouldNotWarn_WhenAttributeChangeHandlerGuardsOnAttributeName() {
		// Arrange
		string body =
			"define(\"X\", [], function() { return { converters: {}, validators: {}, handlers: [ { " +
			"request: \"crt.HandleViewModelAttributeChangeRequest\", " +
			"handler: async (request, next) => { if (request.attributeName !== \"UsrPhoneNumber\") { return next?.handle(request); } " +
			"await request.$context.set(\"UsrCountryCode\", \"38\"); return next?.handle(request); } } ] }; });";

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		// Assert
		findings.Should().NotContain(f => f.Rule == PageBodyAstLinter.RuleHandlerAttributeChangeUnscopedWrite,
			because: "the early `if (request.attributeName !== ...) return` guard is the canonical scope; referencing attributeName marks the author as scope-aware and must suppress the warning");
	}

	[Test]
	[Description("Accepted false negative pinned deliberately: a handler that references request.attributeName for an unrelated purpose but STILL writes unconditionally is not flagged. The attributeName reference is a scope-awareness proxy (not a guard proof); the rule accepts this rare miss to stay quiet on scoped handlers — see the rule doc comment's heuristic-limits block")]
	public void Lint_ShouldNotWarn_WhenAttributeNameReferencedButWriteUnconditional() {
		// Arrange
		string body =
			"define(\"X\", [], function() { return { converters: {}, validators: {}, handlers: [ { " +
			"request: \"crt.HandleViewModelAttributeChangeRequest\", " +
			"handler: async (request, next) => { const changed = request.attributeName; " +
			"await request.$context.set(\"UsrCountryCode\", request.value); return next?.handle(request); } } ] }; });";

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		// Assert
		findings.Should().NotContain(f => f.Rule == PageBodyAstLinter.RuleHandlerAttributeChangeUnscopedWrite,
			because: "any attributeName reference suppresses the warning by design — this is the documented, test-pinned accepted false negative in the rule's heuristic-limits block; tightening to a proof-of-guard would need data-flow analysis");
	}

	[Test]
	[Description("An unscoped crt.HandleViewModelAttributeChangeRequest handler that only READS (no $context.set write) must NOT raise the warning — with no attribute write there is no self-retrigger footgun")]
	public void Lint_ShouldNotWarn_WhenUnscopedAttributeChangeHandlerDoesNotWrite() {
		// Arrange
		string body =
			"define(\"X\", [], function() { return { converters: {}, validators: {}, handlers: [ { " +
			"request: \"crt.HandleViewModelAttributeChangeRequest\", " +
			"handler: async (request, next) => { const value = request.value; return next?.handle(request); } } ] }; });";

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		// Assert
		findings.Should().NotContain(f => f.Rule == PageBodyAstLinter.RuleHandlerAttributeChangeUnscopedWrite,
			because: "the rule targets the write-driven self-retrigger; a read-only handler cannot re-trigger itself, so flagging it would be a false positive");
	}

	[Test]
	[Description("An unscoped $context.set(...) write inside a DIFFERENT request type (not crt.HandleViewModelAttributeChangeRequest) must NOT raise the warning — the self-retrigger footgun is specific to the attribute-change request")]
	public void Lint_ShouldNotWarn_WhenWritingContextInNonAttributeChangeHandler() {
		// Arrange
		string body =
			"define(\"X\", [], function() { return { converters: {}, validators: {}, handlers: [ { " +
			"request: \"crt.HandleViewModelInitRequest\", " +
			"handler: async (request, next) => { await request.$context.set(\"UsrCountryCode\", \"38\"); return next?.handle(request); } } ] }; });";

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		// Assert
		findings.Should().NotContain(f => f.Rule == PageBodyAstLinter.RuleHandlerAttributeChangeUnscopedWrite,
			because: "only crt.HandleViewModelAttributeChangeRequest fires on every attribute change; an init handler writing an attribute does not self-retrigger, so the rule must be scoped to the attribute-change request type");
	}

	[Test]
	[Description("Accepted false negative pinned deliberately (second of two): a write through a LOCAL ALIAS of $context (const ctx = request.$context; ctx.set(...)) that drops the $context member is not detected, so an unscoped handler writing that way is not flagged — following aliases needs data-flow analysis, consistent with the rule's documented heuristic limits")]
	public void Lint_ShouldNotWarn_WhenAttributeChangeHandlerWritesViaAliasedContext() {
		// Arrange
		string body =
			"define(\"X\", [], function() { return { converters: {}, validators: {}, handlers: [ { " +
			"request: \"crt.HandleViewModelAttributeChangeRequest\", " +
			"handler: async (request, next) => { const ctx = request.$context; await ctx.set(\"UsrCountryCode\", request.value); return next?.handle(request); } } ] }; });";

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		// Assert
		findings.Should().NotContain(f => f.Rule == PageBodyAstLinter.RuleHandlerAttributeChangeUnscopedWrite,
			because: "IsContextSetCall does not follow a local $context alias by design (same limitation as IsContextExecuteRequest); this test pins the accepted false negative so a future alias-following change must consciously revisit the rule's documented heuristic limits");
	}

	[Test]
	[Description("A handlers array with one scoped and one unscoped attribute-change-write entry yields EXACTLY one warning, anchored to the offending (unscoped) entry — mirrors the entity-data-source single-warning convention and pins per-entry independence")]
	public void Lint_ShouldEmitSingleWarning_WhenHandlersArrayMixesScopedAndUnscoped() {
		// Arrange — scoped entry (in-body attributeName guard) on line 1, unscoped entry on line 2 (newline before it)
		string body =
			"define(\"X\", [], function() { return { converters: {}, validators: {}, handlers: [ " +
			"{ request: \"crt.HandleViewModelAttributeChangeRequest\", " +
			"handler: async (request, next) => { if (request.attributeName !== \"UsrPhoneNumber\") { return next?.handle(request); } await request.$context.set(\"UsrCountryCode\", \"38\"); return next?.handle(request); } },\n" +
			"{ request: \"crt.HandleViewModelAttributeChangeRequest\", " +
			"handler: async (request, next) => { await request.$context.set(\"UsrOther\", request.value); return next?.handle(request); } } ] }; });";

		// Act
		PageBodyLintFinding finding = LintBody(body)
			.Single(f => f.Rule == PageBodyAstLinter.RuleHandlerAttributeChangeUnscopedWrite);

		// Assert
		finding.Severity.Should().Be(LintSeverity.Warning,
			because: "the unscoped write is advisory, not a structural break");
		finding.Line.Should().Be(2,
			because: "the finding must anchor to the unscoped entry's request property on line 2, not the scoped entry on line 1 — proving the rule fires per entry and does not spill onto the clean sibling");
	}

	[Test]
	[Description("In strict code a function declared inside a block stays in that block, so a handler calling it from outside is still reported — Node leaves the outer binding undefined and the handler throws ReferenceError")]
	public void Lint_ShouldEmitError_WhenStrictBlockFunctionIsCalledFromHandler() {
		// Arrange
		string body =
			"define(\"X\", [], function() { \"use strict\"; if (true) { function blockOnly() { return 1; } } " +
			"return { handlers: [{ request: \"crt.HandleViewModelInitRequest\", " +
			"handler: async (request, next) => { blockOnly(); return next?.handle(request); } }], " +
			"converters: {}, validators: {} }; });";

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		// Assert
		findings.Should().ContainSingle(f =>
			f.Rule == PageBodyAstLinter.RuleUndefinedSectionCall && f.Message.Contains("blockOnly"),
			because: "strict mode block-scopes the declaration, so hoisting it to the factory scope would accept a call that throws at runtime");
	}

	[Test]
	[Description("Without the strict directive the same block function DOES hoist to the factory scope, so the identical body is accepted — the rule must follow the language, not block-scope everything")]
	public void Lint_ShouldNotEmitError_WhenSloppyBlockFunctionIsCalledFromHandler() {
		// Arrange
		string body =
			"define(\"X\", [], function() { if (true) { function blockOnly() { return 1; } } " +
			"return { handlers: [{ request: \"crt.HandleViewModelInitRequest\", " +
			"handler: async (request, next) => { blockOnly(); return next?.handle(request); } }], " +
			"converters: {}, validators: {} }; });";

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		// Assert
		findings.Should().NotContain(f => f.Rule == PageBodyAstLinter.RuleUndefinedSectionCall,
			because: "sloppy-mode function declarations hoist out of the block and the call really does resolve");
	}

	[TestCase("const helper = function() { return 1; };", TestName = "post-return const")]
	[TestCase("let helper = function() { return 1; };", TestName = "post-return let")]
	[TestCase("var helper = function() { return 1; };", TestName = "post-return assigned var")]
	[TestCase("class helper {}", TestName = "post-return class")]
	[Description("A declaration placed AFTER the factory's return never runs its initializer, so a handler calling it fails at runtime and must still be reported")]
	public void Lint_ShouldEmitError_WhenHelperIsDeclaredAfterTheReturn(string declaration) {
		// Arrange
		string body =
			"define(\"X\", [], function() { " +
			"return { handlers: [{ request: \"crt.HandleViewModelInitRequest\", " +
			"handler: async (request, next) => { helper(); return next?.handle(request); } }], " +
			"converters: {}, validators: {} }; " + declaration + " });";

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		// Assert
		findings.Should().ContainSingle(f =>
			f.Rule == PageBodyAstLinter.RuleUndefinedSectionCall && f.Message.Contains("helper"),
			because: "the initializer is unreachable, so the handler throws ReferenceError or TypeError however the binding is spelled");
	}

	[Test]
	[Description("A FUNCTION DECLARATION after the factory's return is hoisted with its value and really is callable, so it must not be reported — the post-return rule stops at the one form the language keeps usable")]
	public void Lint_ShouldNotEmitError_WhenFunctionDeclarationFollowsTheReturn() {
		// Arrange
		string body =
			"define(\"X\", [], function() { " +
			"return { handlers: [{ request: \"crt.HandleViewModelInitRequest\", " +
			"handler: async (request, next) => { helper(); return next?.handle(request); } }], " +
			"converters: {}, validators: {} }; function helper() { return 1; } });";

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		// Assert
		findings.Should().NotContain(f => f.Rule == PageBodyAstLinter.RuleUndefinedSectionCall,
			because: "a function declaration is hoisted with its body, so the call resolves at runtime");
	}

	[TestCase("open", TestName = "WindowGlobal_open")]
	[TestCase("close", TestName = "WindowGlobal_close")]
	[TestCase("postMessage", TestName = "WindowGlobal_postMessage")]
	[TestCase("addEventListener", TestName = "WindowGlobal_addEventListener")]
	[TestCase("removeEventListener", TestName = "WindowGlobal_removeEventListener")]
	[TestCase("getSelection", TestName = "WindowGlobal_getSelection")]
	[Description("A bare call to a Window instance method is a standard browser global, so it must not block the write — the catalog listed constructors and free functions but not the members Window itself carries")]
	public void Lint_ShouldNotEmitError_WhenSectionCallsABareWindowMethod(string globalName) {
		// Arrange
		string body =
			"define(\"X\", [], function() { " +
			"return { handlers: [{ request: \"crt.HandleViewModelInitRequest\", " +
			$"handler: async (request, next) => {{ {globalName}(); return next?.handle(request); }} }}], " +
			"converters: {}, validators: {} }; });";

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		// Assert
		findings.Should().NotContain(f => f.Rule == PageBodyAstLinter.RuleUndefinedSectionCall,
			because: $"{globalName} is supplied by the browser on every Freedom UI page, so rejecting it turns a working page into a refused write");
	}

	[Test]
	[Description("Thousands of distinct undeclared names do not grow the report: the omitted-name count saturates at the tracked sample and says so, instead of retaining every discarded identifier")]
	public void Lint_ShouldSaturateOmittedNameCount_WhenDistinctUndeclaredNamesExceedTheSample() {
		// Arrange — far past both the reported cap and the tracked-name sample.
		int distinctNames = PageBodyAstLinter.MaxTrackedOmittedNames + PageBodyAstLinter.MaxUndefinedSectionCallNames + 500;
		var calls = new StringBuilder();
		for (int index = 0; index < distinctNames; index++) {
			calls.Append($"missingHelper{index}(); ");
		}
		string body =
			"define(\"X\", [], function() { " +
			"return { handlers: [{ request: \"crt.HandleViewModelInitRequest\", " +
			$"handler: async (request, next) => {{ {calls}return next?.handle(request); }} }}], " +
			"converters: {}, validators: {} }; });";

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		// Assert
		List<PageBodyLintFinding> reported = findings
			.Where(f => f.Rule == PageBodyAstLinter.RuleUndefinedSectionCall).ToList();
		reported.Should().HaveCount(PageBodyAstLinter.MaxUndefinedSectionCallNames + 1,
			because: "the per-name findings stay capped and one summary line closes the rule");
		reported[^1].Message.Should().Contain($"at least {PageBodyAstLinter.MaxTrackedOmittedNames}",
			because: "the distinct-name count is a floor once tracking saturates - retaining every discarded name cost megabytes on a generated page while the response still carried 21 findings");
		reported[^1].Message.Should().Contain($"{distinctNames - PageBodyAstLinter.MaxUndefinedSectionCallNames} further call site(s)",
			because: "occurrences are counted in full, since counting them costs nothing");
	}

	[Test]
	[Description("A converters map with thousands of reserved crt.* keys collapses past the per-rule cap into one counted line, instead of formatting a report measured in hundreds of kilobytes")]
	public void Lint_ShouldCapConverterKeyFindings_WhenTheMapCarriesMoreThanTheRuleCap() {
		// Arrange
		int keyCount = PageBodyAstLinter.MaxFindingsPerRule + 120;
		var keys = new StringBuilder();
		for (int index = 0; index < keyCount; index++) {
			keys.Append($"\"crt.Converter{index}\": () => {index}, ");
		}
		string body =
			"define(\"X\", [], function() { " +
			$"return {{ handlers: [], converters: {{ {keys}}}, validators: {{}} }}; }});";

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		// Assert
		List<PageBodyLintFinding> reported = findings
			.Where(f => f.Rule == PageBodyAstLinter.RuleConverterCrtPrefixReserved).ToList();
		reported.Should().HaveCount(PageBodyAstLinter.MaxFindingsPerRule + 1,
			because: "every offending key carries the same fix, so past the cap they collapse into one counted line");
		reported[^1].Message.Should().Contain($"{keyCount - PageBodyAstLinter.MaxFindingsPerRule} further converter key(s)",
			because: "the caller must still learn how many were suppressed");
	}

	[TestCase("innerWidth", TestName = "Window value property")]
	[TestCase("caches", TestName = "Host object")]
	[TestCase("Map", TestName = "Constructor that throws without new")]
	[TestCase("Math", TestName = "Namespace object")]
	[Description("A bare call to a global the runtime supplies as a VALUE is reported, because the call throws a TypeError at runtime even though the name exists")]
	public void Lint_ShouldReportBareCall_WhenTheAmbientGlobalIsNotCallable(string globalName) {
		// Arrange
		string body =
			"define(\"X\", [], function() { " +
			$"return {{ handlers: [{{ request: \"crt.R\", handler: async () => {globalName}() }}] }}; }});";

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		// Assert
		PageBodyLintFinding finding = findings
			.SingleOrDefault(f => f.Rule == PageBodyAstLinter.RuleUndefinedSectionCall);
		finding.Rule.Should().Be(PageBodyAstLinter.RuleUndefinedSectionCall,
			because: $"the whole catalog was treated as callable, so `{globalName}()` passed lint and then "
				+ "failed at runtime with a TypeError");
		finding.Message.Should().Contain("as a value rather than as a function",
			because: "\"you did not declare this\" is false for a name the runtime does supply, and would "
				+ "send the author looking for a missing helper");
	}

	[TestCase("createImageBitmap", TestName = "Callable browser global")]
	[TestCase("fetch", TestName = "Callable browser global, fetch")]
	[TestCase("parseInt", TestName = "Callable ECMAScript global")]
	[TestCase("addEventListener", TestName = "Callable Window method")]
	[TestCase("Number", TestName = "Constructor callable without new")]
	[TestCase("define", TestName = "AMD loader global")]
	[Description("A bare call to a global the runtime supplies as a callable is accepted, so splitting the catalog did not start rejecting working pages")]
	public void Lint_ShouldAcceptBareCall_WhenTheAmbientGlobalIsCallable(string globalName) {
		// Arrange
		string body =
			"define(\"X\", [], function() { " +
			$"return {{ handlers: [{{ request: \"crt.R\", handler: async () => {globalName}() }}] }}; }});";

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		// Assert
		findings.Should().NotContain(f => f.Rule == PageBodyAstLinter.RuleUndefinedSectionCall,
			because: $"`{globalName}()` is a real call the runtime answers, and blocking it would reject a "
				+ "working page");
	}

	[Test]
	[Description("The callable and value-only partitions stay disjoint and together cover the whole catalog, so no name silently falls out of both")]
	public void CallableAndNonCallableGlobals_ShouldPartitionTheCatalog() {
		// Assert
		PageBodyAstLinter.CallableRuntimeGlobals.Should()
			.NotIntersectWith(PageBodyAstLinter.NonCallableRuntimeGlobals,
				because: "a name is either callable bare or it is not");
		PageBodyAstLinter.CallableRuntimeGlobals
			.Concat(PageBodyAstLinter.NonCallableRuntimeGlobals)
			.Should().BeEquivalentTo(PageBodyAstLinter.KnownRuntimeGlobals,
				because: "a catalog entry that lands in neither partition would be rejected as undeclared");
	}

	#endregion

}
