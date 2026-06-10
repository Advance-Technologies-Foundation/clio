using System.Collections.Generic;
using System.Linq;
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

	[Test]
	[Description("`handlers: { ... }` (object literal instead of array) raises a handlers-must-be-array Error finding — the page body contract requires an array, the platform rejects objects, the LLM-only sampling cannot catch this")]
	public void Lint_ShouldEmitError_WhenHandlersIsObjectLiteral() {
		string body =
			"define(\"X\", [], function() { return { handlers: { request: \"crt.HandleViewModelInitRequest\" }, " +
			"converters: {}, validators: {} }; });";

		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		findings.Should().ContainSingle(f =>
			f.Rule == PageBodyAstLinter.RuleHandlersMustBeArray && f.Severity == LintSeverity.Error,
			because: "a `handlers: { ... }` object literal is the agent-hallucination shape the guidance explicitly bans");
	}

	[Test]
	[Description("`validators: [ ... ]` (array instead of object) raises a validators-must-be-object Error — symmetric agent-hallucination with the handlers case")]
	public void Lint_ShouldEmitError_WhenValidatorsIsArrayLiteral() {
		string body =
			"define(\"X\", [], function() { return { handlers: [], converters: {}, validators: [] }; });";

		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		findings.Should().ContainSingle(f =>
			f.Rule == PageBodyAstLinter.RuleValidatorsMustBeObject && f.Severity == LintSeverity.Error,
			because: "the page contract keys `validators` as an object map of validator name → declaration; an array is rejected by the platform");
	}

	[Test]
	[Description("`converters: [ ... ]` (array instead of object) raises a converters-must-be-object Error")]
	public void Lint_ShouldEmitError_WhenConvertersIsArrayLiteral() {
		string body =
			"define(\"X\", [], function() { return { handlers: [], converters: [], validators: {} }; });";

		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		findings.Should().ContainSingle(f =>
			f.Rule == PageBodyAstLinter.RuleConvertersMustBeObject && f.Severity == LintSeverity.Error,
			because: "the page contract keys `converters` as an object map of converter name → declaration; an array is rejected by the platform");
	}

	[Test]
	[Description("`params: []` (empty array) inside a validator block raises a validator-params-empty Error — guidance says NEVER valid because every custom validator requires a message param")]
	public void Lint_ShouldEmitError_WhenValidatorParamsIsEmptyArray() {
		string body =
			"define(\"X\", [], function() { return { handlers: [], converters: {}, validators: { " +
			"\"usr.V\": { validator: function() { return function() { return null; }; }, params: [] } " +
			"} }; });";

		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		findings.Should().ContainSingle(f =>
			f.Rule == PageBodyAstLinter.RuleValidatorParamsEmpty && f.Severity == LintSeverity.Error,
			because: "every custom validator MUST declare at minimum a `message` param so the user sees an error string; `params: []` blocks that contract");
	}

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

	#endregion

	#region Tests: behavioural warnings (non-blocking severity)

	[TestCase("request.viewModel", TestName = "Lint_ShouldEmitWarning_WhenHandlerUsesRequestViewModel")]
	[TestCase("request.sender", TestName = "Lint_ShouldEmitWarning_WhenHandlerUsesRequestSender")]
	[TestCase("request.$get(\"x\")", TestName = "Lint_ShouldEmitWarning_WhenHandlerUsesRequestDollarGet")]
	[TestCase("request.$set(\"x\", 1)", TestName = "Lint_ShouldEmitWarning_WhenHandlerUsesRequestDollarSet")]
	[TestCase("request.$context.get(\"x\")", TestName = "Lint_ShouldEmitWarning_WhenHandlerUsesContextDotGet")]
	[Description("Deprecated handler request API surfaces fire a Warning — they do NOT block the write (existing regex validators may also catch the same patterns with their own messaging) but the lint warning surfaces alongside")]
	public void Lint_ShouldEmitWarning_WhenHandlerUsesDeprecatedContextApi(string deprecatedExpression) {
		string body =
			"define(\"X\", [], function() { return { handlers: [{ " +
			"request: \"crt.HandleViewModelInitRequest\", " +
			"handler: async function(request, next) { var x = " + deprecatedExpression + "; return next?.handle(request); } }], " +
			"converters: {}, validators: {} }; });";

		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		findings.Should().Contain(f =>
			f.Rule == PageBodyAstLinter.RuleHandlerUsesDeprecatedContextApi && f.Severity == LintSeverity.Warning,
			because: "deprecated handler APIs are warnings (existing regex validators handle the error-severity side); the lint warning gives an AST-precise location alongside the legacy message");
	}

	[Test]
	[Description("`sdk.HandlerChainService.instance.process({ ... })` raises a handler-uses-handler-chain-service-instance Warning — should be `request.$context.executeRequest(...)` per guidance, but warning-only because the SDK pattern is still valid in advanced flows")]
	public void Lint_ShouldEmitWarning_WhenHandlerUsesHandlerChainServiceInstance() {
		string body =
			"define(\"X\", [], function(sdk) { return { handlers: [{ " +
			"request: \"crt.HandleViewModelInitRequest\", " +
			"handler: async function(request, next) { await sdk.HandlerChainService.instance.process({ type: \"crt.OpenPageRequest\" }); return next?.handle(request); } }], " +
			"converters: {}, validators: {} }; });";

		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		findings.Should().ContainSingle(f =>
			f.Rule == PageBodyAstLinter.RuleHandlerUsesHandlerChainServiceInstance && f.Severity == LintSeverity.Warning,
			because: "the guidance prefers `request.$context.executeRequest(...)` for in-page dispatches; the SDK chain service call is permitted but worth flagging");
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
	[Description("`viewModel` property on an unrelated object (NOT a handler `request` argument) does NOT trigger the deprecated-context-api rule — the rule is bounded to MemberExpressions rooted at `request`")]
	public void Lint_ShouldNotEmitDeprecatedApi_WhenMemberAccessIsNotRootedAtRequest() {
		string body =
			"define(\"X\", [], function() { var foo = { viewModel: null }; return { " +
			"handlers: [], converters: {}, validators: {} }; });";

		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		findings.Should().NotContain(f => f.Rule == PageBodyAstLinter.RuleHandlerUsesDeprecatedContextApi,
			because: "the rule fires only on chains rooted at `request` so unrelated objects that happen to declare a `viewModel` field are not falsely flagged");
	}

	[Test]
	[Description("Spread elements (`...base`) inside the schema return-object do not crash the visitor and are not falsely matched against schema-section shape rules — defensive against agent-generated `{ ...defaults, handlers: [], ... }` shapes")]
	public void Lint_ShouldHandleSpreadElement_WithoutCrashOrFalsePositive() {
		string body =
			"define(\"X\", [], function() { var defaults = { handlers: [] }; return { ...defaults, " +
			"converters: {}, validators: {} }; });";

		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		findings.Should().NotContain(f =>
			f.Rule == PageBodyAstLinter.RuleHandlersMustBeArray
			|| f.Rule == PageBodyAstLinter.RuleValidatorsMustBeObject
			|| f.Rule == PageBodyAstLinter.RuleConvertersMustBeObject,
			because: "spread elements are not Property nodes and must be skipped by the visitor without emitting a false positive on the section-shape rules");
	}

	[Test]
	[Description("Computed property keys (`['handlers']: { ... }`) do NOT trigger handlers-must-be-array — TryGetStaticPropertyName returns null on computed keys, keeping rules bounded to authored static keys")]
	public void Lint_ShouldNotEmitSchemaShape_WhenSectionKeyIsComputed() {
		string body =
			"define(\"X\", [], function() { var k = \"handlers\"; return { " +
			"[k]: {}, converters: {}, validators: {} }; });";

		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		findings.Should().NotContain(f => f.Rule == PageBodyAstLinter.RuleHandlersMustBeArray,
			because: "a computed property key `[k]` does not statically declare the section name; deciding whether it equals \"handlers\" at parse time is undecidable, so the linter must abstain rather than guess");
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
			Rule: PageBodyAstLinter.RuleHandlersMustBeArray,
			Severity: LintSeverity.Error,
			Line: 3,
			Column: 1,
			Message: "handlers must be an array literal");
		PageBodyLintFinding e2 = new(
			Rule: PageBodyAstLinter.RuleValidatorParamsEmpty,
			Severity: LintSeverity.Error,
			Line: 12,
			Column: 7,
			Message: "params must not be empty");

		// Act
		string rendered = PageBodyAstLinter.FormatErrors([e1, e2]);

		// Assert
		rendered.Should()
			.Contain("handlers-must-be-array", because: "the rule id must be visible to the operator")
			.And.Contain("line 3, column 1", because: "the precise location must be visible")
			.And.Contain("validator-params-empty", because: "every distinct error must be enumerated")
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

}
