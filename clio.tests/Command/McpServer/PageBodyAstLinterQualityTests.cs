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
internal class PageBodyAstLinterQualityTests {

	#region Helpers

	private static IReadOnlyList<PageBodyLintFinding> LintBody(string body) {
		PageBodySyntaxValidationResult result = PageBodySyntaxValidator.ValidateAndParse(body, out Script ast);
		result.IsValid.Should().BeTrue(because: "lint fixtures must be syntactically valid JavaScript");
		return PageBodyAstLinter.Lint(ast);
	}

	private static List<PageBodyLintFinding> Findings(string body, string rule) =>
		LintBody(body).Where(f => f.Rule == rule).ToList();

	// A page whose sections are the given JavaScript fragments.
	private static string Page(string handlers = "[]", string converters = "{}", string validators = "{}",
		string viewConfigDiff = "[]") =>
		"define(\"UsrTest_FormPage\", [], function() { return { "
		+ "viewConfigDiff: " + viewConfigDiff + ", viewModelConfigDiff: [], modelConfigDiff: [], "
		+ "handlers: " + handlers + ", converters: " + converters + ", validators: " + validators + " }; });";

	private static string Handler(string request, string body) =>
		"{ request: \"" + request + "\", handler: async (request, next) => {\n" + body + "\nreturn next?.handle(request);\n} }";

	private static string Lines(int count) {
		StringBuilder builder = new();
		for (int i = 0; i < count; i++) {
			builder.Append("const v").Append(i).Append(" = ").Append(i).Append(";\n");
		}
		return builder.ToString();
	}

	#endregion

	#region function-too-long

	[Test]
	[Description("A handler longer than the function-length limit raises a function-too-long warning that states its length and the limit")]
	public void Lint_ShouldWarnFunctionTooLong_WhenHandlerExceedsLimit() {
		// Arrange
		string body = Page("[" + Handler("usr.LongRequest", Lines(PageBodyAstLinter.MaxFunctionLines)) + "]");

		// Act
		List<PageBodyLintFinding> findings = Findings(body, PageBodyAstLinter.RuleFunctionTooLong);

		// Assert
		findings.Should().ContainSingle(because: "exactly one function in the page is over the limit");
		findings[0].Severity.Should().Be(LintSeverity.Warning, because: "a long function works and must not block the save");
		findings[0].Message.Should().Contain($"limit {PageBodyAstLinter.MaxFunctionLines}",
			because: "the reader needs the threshold to know how far to cut");
	}

	[Test]
	[Description("A handler within the function-length limit raises no function-too-long warning")]
	public void Lint_ShouldNotWarnFunctionTooLong_WhenHandlerWithinLimit() {
		// Arrange
		string body = Page("[" + Handler("usr.ShortRequest", Lines(10)) + "]");

		// Act
		List<PageBodyLintFinding> findings = Findings(body, PageBodyAstLinter.RuleFunctionTooLong);

		// Assert
		findings.Should().BeEmpty(because: "a 13-line handler is well under the limit");
	}

	#endregion

	#region handler-too-many-nested-functions

	[Test]
	[Description("A handler declaring more named functions than the limit raises a warning naming them")]
	public void Lint_ShouldWarnNestedFunctions_WhenHandlerDeclaresTooMany() {
		// Arrange
		string helpers = "const a = () => 1; const b = () => 2; function c() { return 3; } const d = function() { return 4; };";
		string body = Page("[" + Handler("usr.NestedRequest", helpers) + "]");

		// Act
		List<PageBodyLintFinding> findings = Findings(body, PageBodyAstLinter.RuleHandlerTooManyNestedFunctions);

		// Assert
		findings.Should().ContainSingle(because: "four named functions exceed the limit of three");
		findings[0].Message.Should().Contain("a, b, c, d", because: "the warning must name the helpers to move");
	}

	[Test]
	[Description("Inline callbacks do not count as nested helpers, so a handler with three helpers and callbacks raises no warning")]
	public void Lint_ShouldNotWarnNestedFunctions_WhenOnlyCallbacksExceedLimit() {
		// Arrange
		string code = "const a = () => 1; const b = () => 2; const c = () => 3; "
			+ "const ids = [1, 2].map(x => x).filter(x => x).map(function(x) { return x; });";
		string body = Page("[" + Handler("usr.CallbackRequest", code) + "]");

		// Act
		List<PageBodyLintFinding> findings = Findings(body, PageBodyAstLinter.RuleHandlerTooManyNestedFunctions);

		// Assert
		findings.Should().BeEmpty(because: "three named helpers are at the limit and inline callbacks are not helpers");
	}

	#endregion

	#region duplicate-function-in-page

	[Test]
	[Description("The same helper body declared in two handlers raises a duplicate warning even when the copies have different names")]
	public void Lint_ShouldWarnDuplicateFunction_WhenTwoHandlersDeclareSameHelper() {
		// Arrange
		string first = Handler("usr.First", "const idOf = (v) => v && v.value ? v.value : v; idOf(1);");
		string second = Handler("usr.Second", "const lookupId = (v) => v && v.value ? v.value : v; lookupId(2);");
		string body = Page("[" + first + "," + second + "]");

		// Act
		List<PageBodyLintFinding> findings = Findings(body, PageBodyAstLinter.RuleDuplicateFunctionInPage);

		// Assert
		findings.Should().ContainSingle(because: "one helper is copied once");
		findings[0].Message.Should().Contain("`lookupId` repeats the body of `idOf`",
			because: "the warning must point from the copy to the original");
	}

	[Test]
	[Description("A helper that reads its own handler's variables is a binding of that handler, so its copies in other handlers raise no duplicate warning")]
	public void Lint_ShouldNotWarnDuplicateFunction_WhenHelperUsesHandlerVariables() {
		// Arrange
		string wrapper = "const { $context, scopes } = request; "
			+ "const process = (config) => sdk.HandlerChainService.instance.process({ ...config, $context, scopes: [...scopes] }); "
			+ "await process({ type: \"usr.Other\" });";
		string body = Page("[" + Handler("usr.First", wrapper) + "," + Handler("usr.Second", wrapper) + "]");

		// Act
		List<PageBodyLintFinding> findings = Findings(body, PageBodyAstLinter.RuleDuplicateFunctionInPage);

		// Assert
		findings.Should().BeEmpty(
			because: "the wrapper closes over the handler's $context and scopes and cannot move to a module unchanged");
	}

	[Test]
	[Description("Helpers with different bodies, and copies of a trivial helper, raise no duplicate warning")]
	public void Lint_ShouldNotWarnDuplicateFunction_WhenBodiesDifferOrAreTrivial() {
		// Arrange
		string first = Handler("usr.First", "const idOf = (v) => v && v.value ? v.value : v; const same = (v) => v;");
		string second = Handler("usr.Second", "const nameOf = (v) => v && v.displayValue ? v.displayValue : v; const same = (v) => v;");
		string body = Page("[" + first + "," + second + "]");

		// Act
		List<PageBodyLintFinding> findings = Findings(body, PageBodyAstLinter.RuleDuplicateFunctionInPage);

		// Assert
		findings.Should().BeEmpty(because: "different bodies are different helpers, and `v => v` is too small to move");
	}

	#endregion

	#region catch-log-only

	[TestCase("catch (e) { console.error(e); }")]
	[TestCase("catch (e) { }")]
	[TestCase("catch { console.warn(\"failed\"); console.log(\"x\"); }")]
	[Description("A catch block that is empty or only writes to the console raises a catch-log-only warning")]
	public void Lint_ShouldWarnCatchLogOnly_WhenCatchOnlyLogs(string catchClause) {
		// Arrange
		string body = Page("[" + Handler("usr.Save", "try { await request.$context.save(); } " + catchClause) + "]");

		// Act
		List<PageBodyLintFinding> findings = Findings(body, PageBodyAstLinter.RuleCatchLogOnly);

		// Assert
		findings.Should().ContainSingle(because: "the error is swallowed and the page carries on");
	}

	[Test]
	[Description("A catch block that reports the error to the user raises no catch-log-only warning")]
	public void Lint_ShouldNotWarnCatchLogOnly_WhenCatchHandlesError() {
		// Arrange
		string code = "try { await request.$context.save(); } catch (e) { console.error(e); "
			+ "await request.$context.executeRequest({ type: \"crt.ShowDialogRequest\" }); return; }";
		string body = Page("[" + Handler("usr.Save", code) + "]");

		// Act
		List<PageBodyLintFinding> findings = Findings(body, PageBodyAstLinter.RuleCatchLogOnly);

		// Assert
		findings.Should().BeEmpty(because: "the catch block does more than log: it shows a dialog and stops");
	}

	#endregion

	#region numeric-comparison-type

	[Test]
	[Description("A bare numeric comparisonType in a handler raises a warning; the named sdk.ComparisonType value does not")]
	public void Lint_ShouldWarnNumericComparisonType_OnlyForBareNumbers() {
		// Arrange
		string code = "const a = { comparisonType: 3 }; const b = { comparisonType: sdk.ComparisonType.Equal };";
		string body = Page("[" + Handler("usr.Filter", code) + "]");

		// Act
		List<PageBodyLintFinding> findings = Findings(body, PageBodyAstLinter.RuleNumericComparisonType);

		// Assert
		findings.Should().ContainSingle(because: "only the bare number hides the operator");
	}

	[Test]
	[Description("A numeric comparisonType outside the code sections (a static filter in view-model config) raises no warning")]
	public void Lint_ShouldNotWarnNumericComparisonType_OutsideSections() {
		// Arrange
		string body = Page(viewConfigDiff: "[{ \"values\": { \"filter\": { \"comparisonType\": 3 } } }]");

		// Act
		List<PageBodyLintFinding> findings = Findings(body, PageBodyAstLinter.RuleNumericComparisonType);

		// Assert
		findings.Should().BeEmpty(because: "serialized filter JSON in config sections uses numbers by design");
	}

	#endregion

	#region sequential-independent-awaits

	[Test]
	[Description("Three awaited model loads that do not use each other's result raise a sequential-independent-awaits warning")]
	public void Lint_ShouldWarnSequentialAwaits_WhenLoadsAreIndependent() {
		// Arrange
		string code = "const a = await accounts.load(); const b = await contacts.load(); const c = await orders.load();";
		string body = Page("[" + Handler("usr.Init", code) + "]");

		// Act
		List<PageBodyLintFinding> findings = Findings(body, PageBodyAstLinter.RuleSequentialIndependentAwaits);

		// Assert
		findings.Should().ContainSingle(because: "three independent loads are over the limit of two");
		findings[0].Message.Should().Contain("3 awaited model loads", because: "the count tells the reader what to merge");
	}

	[Test]
	[Description("Loads that use an earlier load's result, also through an intermediate variable, are not independent")]
	public void Lint_ShouldNotWarnSequentialAwaits_WhenLoadsDependOnEachOther() {
		// Arrange
		string code = "const model = await sdk.Model.create(\"Account\"); const rows = await model.load(); "
			+ "const id = rows[0].id; const details = await other.load({ id }); const c = await contacts.load();";
		string body = Page("[" + Handler("usr.Init", code) + "]");

		// Act
		List<PageBodyLintFinding> findings = Findings(body, PageBodyAstLinter.RuleSequentialIndependentAwaits);

		// Assert
		findings.Should().BeEmpty(because: "only two loads (create and contacts) are independent of the others");
	}

	#endregion

	#region guid-literal-data-reference

	[Test]
	[Description("A GUID literal used as data in a handler raises a guid-literal-data-reference warning")]
	public void Lint_ShouldWarnGuidLiteral_WhenUsedAsData() {
		// Arrange
		string code = "if (status.value === \"8dc41780-1234-4000-8000-000000000001\") { return; }";
		string body = Page("[" + Handler("usr.Check", code) + "]");

		// Act
		List<PageBodyLintFinding> findings = Findings(body, PageBodyAstLinter.RuleGuidLiteralDataReference);

		// Assert
		findings.Should().ContainSingle(because: "the record id is compared as data");
		findings[0].Severity.Should().Be(LintSeverity.Warning, because: "a hard-coded id works and must not block the save");
	}

	[Test]
	[Description("Local element ids (columns[].id of a list widget, or an id key in a handler) raise no GUID warning")]
	public void Lint_ShouldNotWarnGuidLiteral_ForLocalElementIds() {
		// Arrange
		string columns = "[{ \"operation\": \"insert\", \"name\": \"List\", \"values\": { \"type\": \"crt.ListWidget\", "
			+ "\"columns\": [{ \"id\": \"8dc41780-0000-4000-8000-000000000001\", \"code\": \"Name\" }] } }]";
		string code = "const column = { id: \"8dc41780-0000-4000-8000-000000000002\", code: \"Name\" };";
		string body = Page("[" + Handler("usr.Columns", code) + "]", viewConfigDiff: columns);

		// Act
		List<PageBodyLintFinding> findings = Findings(body, PageBodyAstLinter.RuleGuidLiteralDataReference);

		// Assert
		findings.Should().BeEmpty(because: "generated element ids must be unique on the page, not named in a constants module");
	}

	#endregion

	#region Severity

	[Test]
	[Description("A page that breaks every quality rule still has no Error finding, so the save is never rejected for code quality")]
	public void Lint_ShouldNeverEmitError_ForQualityRules() {
		// Arrange
		string code = Lines(PageBodyAstLinter.MaxFunctionLines)
			+ "const a = () => 1; const b = () => 2; const c = () => 3; const d = () => 4;\n"
			+ "try { await request.$context.save(); } catch (e) { console.error(e); }\n"
			+ "const f = { comparisonType: 3, value: \"8dc41780-1234-4000-8000-000000000001\" };\n"
			+ "const x = await m1.load(); const y = await m2.load(); const z = await m3.load();";
		string body = Page("[" + Handler("usr.Everything", code) + "]");
		string[] qualityRules = [
			PageBodyAstLinter.RuleFunctionTooLong, PageBodyAstLinter.RuleHandlerTooManyNestedFunctions,
			PageBodyAstLinter.RuleCatchLogOnly, PageBodyAstLinter.RuleNumericComparisonType,
			PageBodyAstLinter.RuleSequentialIndependentAwaits, PageBodyAstLinter.RuleGuidLiteralDataReference
		];

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintBody(body);

		// Assert
		findings.Select(f => f.Rule).Should().Contain(qualityRules, because: "the body breaks each of these rules once");
		findings.Should().NotContain(f => f.Severity == LintSeverity.Error,
			because: "code-quality findings are advice and must not block update-page or sync-pages");
	}

	#endregion

}
