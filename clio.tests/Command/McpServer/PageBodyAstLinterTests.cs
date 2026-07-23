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
	[Description("A `crt.IndicatorWidget` whose `config.data.providing.filters` carries an inline filter raises no entity-data-source-static-filters finding — the widget legitimately reads its own providing filter, and the rule is scoped to crt.EntityDataSource only")]
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
			because: "the IndicatorWidget applies config.data.providing.filters at render time — that is the correct, runtime-honored mechanism for that component, and the rule must not misfire on it just because a `filters` key appears somewhere under a `config`");
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
	[Description("A non-EntityDataSource descriptor (crt.DataGrid) carrying a DIRECT config.filters child raises no entity-data-source-static-filters finding — the rule is gated on type == crt.EntityDataSource, not on the mere presence of a direct config.filters key")]
	public void Lint_ShouldNotEmitWarning_WhenConfigFiltersOnNonEntityDataSourceType() {
		// Arrange — same direct config.filters shape as the positive case, but type is crt.DataGrid,
		// so ONLY the type gate can suppress the finding (filters IS a direct child of config here).
		string body =
			"define(\"X\", [], function() { return { handlers: [], converters: {}, validators: {}, modelConfigDiff: [ " +
			"{ \"operation\": \"merge\", \"path\": [\"dataSources\"], \"values\": { " +
			"\"SomeDS\": { \"type\": \"crt.DataGrid\", \"scope\": \"viewElement\", \"config\": { " +
			"\"entitySchemaName\": \"Activity\", " +
			"\"filters\": { \"items\": {}, \"logicalOperation\": 0, \"isEnabled\": true, \"filterType\": 6, \"rootSchemaName\": \"Activity\" } } } } } ] }; });";

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		// Assert
		findings.Should().NotContain(f => f.Rule == PageBodyAstLinter.RuleEntityDataSourceStaticFilters,
			because: "the anti-pattern is specific to crt.EntityDataSource (which ignores config.filters); other component types are out of scope, so the type gate — not the direct-child check — must be what suppresses this case");
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
			"define(\"X\", [], function() { return { handlers: [], converters: {}, validators: {}, modelConfigDiff: [ { \"operation\": \"merge\", \"path\": [\"dataSources\"], \"values\": { \"EmailDS\": { \"type\": \"crt.EntityDataSource\", \"config\": {\n" +
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

}
