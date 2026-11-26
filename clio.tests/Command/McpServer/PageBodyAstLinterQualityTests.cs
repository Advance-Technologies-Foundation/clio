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
		findings[0].Message.Should().Contain($"is {PageBodyAstLinter.MaxFunctionLines + 4} lines long (limit {PageBodyAstLinter.MaxFunctionLines})",
			because: "the reader needs the measured length beside the threshold to know how far to cut");
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
		findings[0].Message.Should().Contain("declares 4 functions inside its body (limit 3): a, b, c, d",
			because: "the warning must state the count beside the limit and name the helpers to move");
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
	[Description("Two `function name() {}` helpers with one body, declared directly in two handlers, raise a duplicate warning")]
	public void Lint_ShouldWarnDuplicateFunction_WhenTwoHandlersDeclareSameFunctionDeclaration() {
		// Arrange
		string first = Handler("usr.First", "function fmt(v) { return v && v.value ? v.value : v; } fmt(1);");
		string second = Handler("usr.Second", "function fmt2(v) { return v && v.value ? v.value : v; } fmt2(2);");
		string body = Page("[" + first + "," + second + "]");

		// Act
		List<PageBodyLintFinding> findings = Findings(body, PageBodyAstLinter.RuleDuplicateFunctionInPage);

		// Assert
		findings.Should().ContainSingle(because: "a function declaration in a handler body is a helper of that handler, not a nested one");
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

	[TestCase("v && v.status && v.status.value === \"a\"", "v && v.status && v.status.value !== \"a\"")]
	[TestCase("v && v.status && v.status.value", "v || v.status || v.status.value")]
	[TestCase("v.count + v.extra + v.base", "v.count - v.extra - v.base")]
	[TestCase("{ let n = v.count; n += v.extra; return n; }", "{ let n = v.count; n -= v.extra; return n; }")]
	[TestCase("{ const n = v.count; return n + v.extra; }", "{ let n = v.count; return n + v.extra; }")]
	[TestCase("!v.status && !v.status.value && v.x", "-v.status && -v.status.value && v.x")]
	[TestCase("{ let n = v.count; n++; return n; }", "{ let n = v.count; n--; return n; }")]
	[TestCase("`${v.status.value} open ${v.x}`", "`${v.status.value} closed ${v.x}`")]
	[Description("Two helpers that differ only in an operator, a declaration kind or template text have different logic and raise no duplicate warning")]
	public void Lint_ShouldNotWarnDuplicateFunction_WhenHelpersDifferOnlyInOperator(string firstBody, string secondBody) {
		// Arrange
		string first = Handler("usr.First", "const isOpen = (v) => " + firstBody + "; isOpen(1);");
		string second = Handler("usr.Second", "const isClosed = (v) => " + secondBody + "; isClosed(2);");
		string body = Page("[" + first + "," + second + "]");

		// Act
		List<PageBodyLintFinding> findings = Findings(body, PageBodyAstLinter.RuleDuplicateFunctionInPage);

		// Assert
		findings.Should().BeEmpty(
			because: "merging helpers with opposite operators would silently change what the page does");
	}

	[Test]
	[Description("Two helpers with one body, operators included, still raise a duplicate warning")]
	public void Lint_ShouldWarnDuplicateFunction_WhenHelpersShareOperators() {
		// Arrange
		string first = Handler("usr.First", "const isOpen = (v) => v && v.status && v.status.value !== \"a\"; isOpen(1);");
		string second = Handler("usr.Second", "const notA = (v) => v && v.status && v.status.value !== \"a\"; notA(2);");
		string body = Page("[" + first + "," + second + "]");

		// Act
		List<PageBodyLintFinding> findings = Findings(body, PageBodyAstLinter.RuleDuplicateFunctionInPage);

		// Assert
		findings.Should().ContainSingle(because: "keeping operators in the shape must not hide real copies");
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
		findings[0].Message.Should().Contain("(limit 2)", because: "the threshold is stated beside the count");
	}

	[Test]
	[Description("Loads inside a helper declared in a handler are checked as their own sequence")]
	public void Lint_ShouldWarnSequentialAwaits_WhenLoadsSitInAHelperDeclaredInTheHandler() {
		// Arrange
		string code = "const loadAll = async () => { const a = await m1.load(); const b = await m2.load(); const c = await m3.load(); return [a, b, c]; }; "
			+ "await loadAll();";
		string body = Page("[" + Handler("usr.Init", code) + "]");

		// Act
		List<PageBodyLintFinding> findings = Findings(body, PageBodyAstLinter.RuleSequentialIndependentAwaits);

		// Assert
		findings.Should().ContainSingle(because: "the helper runs three independent loads one after another");
	}

	[Test]
	[Description("Assigning a new load result to an earlier load's variable writes it, it does not read it, so the loads stay independent")]
	public void Lint_ShouldWarnSequentialAwaits_WhenAVariableIsReassigned() {
		// Arrange
		string code = "let data = await a.load(); data = await b.load(); const c1 = await c.load();";
		string body = Page("[" + Handler("usr.Init", code) + "]");

		// Act
		List<PageBodyLintFinding> findings = Findings(body, PageBodyAstLinter.RuleSequentialIndependentAwaits);

		// Assert
		findings.Should().ContainSingle(because: "`data = await b.load()` does not use the value `data` held before");
	}

	[Test]
	[Description("An object key that happens to share a load result's name is not a read of that result, so the loads stay independent")]
	public void Lint_ShouldWarnSequentialAwaits_WhenOnlyAPropertyKeySharesTheResultName() {
		// Arrange
		string code = "const data = await a.load(); const b1 = await b.load({ data: 1 }); const c1 = await c.load();";
		string body = Page("[" + Handler("usr.Init", code) + "]");

		// Act
		List<PageBodyLintFinding> findings = Findings(body, PageBodyAstLinter.RuleSequentialIndependentAwaits);

		// Assert
		findings.Should().ContainSingle(because: "`{ data: 1 }` names a property, it does not read the variable `data`");
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

	[TestCase("await request.$context.executeRequest({ type: \"usr.Open\", id: \"8dc41780-1234-4000-8000-000000000001\" });")]
	[TestCase("const owner = { id: \"8dc41780-1234-4000-8000-000000000001\", displayValue: \"Supervisor\" };")]
	[TestCase("const filter = { recordId: \"8dc41780-1234-4000-8000-000000000001\" };")]
	[Description("A record id passed as data - a request id, a lookup value, a recordId - raises a GUID warning, whatever its key")]
	public void Lint_ShouldWarnGuidLiteral_WhenIdIsData(string code) {
		// Arrange
		string body = Page("[" + Handler("usr.Data", code) + "]");

		// Act
		List<PageBodyLintFinding> findings = Findings(body, PageBodyAstLinter.RuleGuidLiteralDataReference);

		// Assert
		findings.Should().ContainSingle(because: "an object that is not a page element carries a record id, not a local id");
	}

	[Test]
	[Description("Local element ids (columns[].id of a list widget, or the id of an element-shaped object in a handler) raise no GUID warning")]
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
		string copy = Handler("usr.Copy", "const idOf = (v) => v && v.value ? v.value : v; idOf(1);");
		string original = Handler("usr.Original", "const idOf = (v) => v && v.value ? v.value : v; idOf(2);");
		string body = Page("[" + Handler("usr.Everything", code) + "," + copy + "," + original + "]");
		string[] qualityRules = [
			PageBodyAstLinter.RuleFunctionTooLong, PageBodyAstLinter.RuleHandlerTooManyNestedFunctions,
			PageBodyAstLinter.RuleDuplicateFunctionInPage,
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

	#region handler-without-next

	[TestCase("async (request) => { await request.$context.set(\"A\", 1); }", "takes no `next`")]
	[TestCase("function (request) { return true; }", "takes no `next`")]
	[TestCase("async (request, next) => { await request.$context.set(\"A\", 1); }", "never calls `next.handle(request)`")]
	[TestCase("async (request, next) => { return helper(request, next); }", "never calls `next.handle(request)`")]
	[TestCase("async (request, next) => { [1].forEach(() => next.handle(request)); return true; }", "never calls `next.handle(request)`")]
	[TestCase("async (request, next) => { await next?.handle(request); }", "no `return` gives its result")]
	[TestCase("async (request, next) => { const result = await next?.handle(request); return true; }", "no `return` gives its result")]
	[TestCase("async (request, next) => { next.handle(request); if (request.value) { return true; } return false; }", "no `return` gives its result")]
	[TestCase("async (request, next) => next.handle(request) && true", "no `return` gives its result")]
	[TestCase("async (request, next) => { if (request.value) { return next?.handle(request); } }", "can reach its end without a `return`")]
	[TestCase("async (request, next) => { const r = await next.handle(request); if (r) { return; } return r; }", "a `return` after that call does not give its result")]
	[TestCase("async (request, next) => { const r = await next.handle(request); if (!r) { return false; } return r; }", "a `return` after that call does not give its result")]
	[TestCase("async (request, next) => { switch (request.mode) { case \"a\": return next.handle(request); } }", "can reach its end without a `return`")]
	[Description("A handler that takes no `next`, never calls `next.handle` in its own body, or drops that call's result raises a warning that says which")]
	public void Lint_ShouldWarnHandlerWithoutNext_WhenNextIsNeverUsed(string handler, string problem) {
		// Arrange
		string body = Page("[{ request: \"usr.PostButtonClicked\", handler: " + handler + " }]");

		// Act
		List<PageBodyLintFinding> findings = Findings(body, PageBodyAstLinter.RuleHandlerWithoutNext);

		// Assert
		findings.Should().ContainSingle(because: "the handler skips every later handler of its request");
		findings[0].Severity.Should().Be(LintSeverity.Warning, because: "code-quality rules never block a save");
		findings[0].Message.Should().Contain("`usr.PostButtonClicked`", because: "the warning names the request whose chain stops");
		findings[0].Message.Should().Contain(problem, because: "the warning says what the handler does wrong with the chain");
	}

	[TestCase("async (request, next) => { await request.$context.set(\"A\", 1); return next?.handle(request); }")]
	[TestCase("async (request, next) => { const result = await next?.handle(request); return result; }")]
	[TestCase("async (request, next) => next.handle(request)")]
	[TestCase("async (request, next = null) => { return next?.handle(request); }")]
	[TestCase("async (request, next) => { if (!request.value) { return next?.handle(request); } const r = await next?.handle(request); return r; }")]
	[TestCase("async (request, next) => { try { return await next?.handle(request); } catch (e) { throw e; } }")]
	[TestCase("async (request, next) => { try { await request.$context.save(); } finally { return next?.handle(request); } }")]
	[TestCase("async (request, next) => { switch (request.mode) { case \"a\": await request.$context.save(); return next.handle(request); case \"b\": default: return next.handle(request); } }")]
	[TestCase("async (request, next) => { return next.handle(request).then((result) => result); }")]
	[TestCase("async (request, chain) => { return chain?.handle(request); }")]
	[TestCase("async (request, next) => { let result; result = await next.handle(request); return result; }")]
	[TestCase("async (request, next) => { if (!request.value) { return; } return next?.handle(request); }")]
	[TestCase("async (request, next) => { if (request.value) { return true; } return next?.handle(request); }")]
	[TestCase("async (request, next) => { const child = page.childOf(request); if (child) { return page.openChild(request, child); } return next?.handle(request); }")]
	[TestCase("async (request, next) => { if (request.value) { return page.open(request); } else { return next?.handle(request); } }")]
	[Description("A handler that returns `next.handle(request)`, directly or through a variable assigned from it, keeps the chain and raises no warning; another return on an early path is a deliberate stop")]
	public void Lint_ShouldNotWarnHandlerWithoutNext_WhenNextIsUsed(string handler) {
		// Arrange
		string body = Page("[{ request: \"crt.HandleViewModelAttributeChangeRequest\", handler: " + handler + " }]");

		// Act
		List<PageBodyLintFinding> findings = Findings(body, PageBodyAstLinter.RuleHandlerWithoutNext);

		// Assert
		findings.Should().BeEmpty(because: "the handler hands the request on through its second parameter");
	}

	[Test]
	[Description("A returned call chain far deeper than MaxAstDepth is reported as not giving the chain's result, without overflowing the stack")]
	public void Lint_ShouldReportHandlerWithoutNext_WhenReturnedChainIsDeeperThanMaxAstDepth() {
		// Arrange
		string chain = "x" + string.Concat(Enumerable.Repeat(".a()", 50_000));
		string body = Page("[{ request: \"usr.PostButtonClicked\", handler: (request, next) => { next.handle(request); return "
			+ chain + "; } }]");
		PageBodySyntaxValidator.ValidateAndParse(body, out Script ast);

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = PageBodyAstLinter.Lint(ast);

		// Assert
		ast.Should().NotBeNull(because: "the parser reads a left-leaning call chain without recursing");
		findings.Should().Contain(f => f.Rule == PageBodyAstLinter.RuleHandlerWithoutNext,
			because: "the walk stops at MaxAstDepth and reports the handler instead of crashing the server process");
	}

	#endregion

	#region client modules

	private static bool TryLintModule(string body, out IReadOnlyList<PageBodyLintFinding> findings) {
		PageBodySyntaxValidationResult result = PageBodySyntaxValidator.ValidateAndParse(body, out Script ast);
		result.IsValid.Should().BeTrue(because: "module fixtures must be syntactically valid JavaScript");
		return PageBodyAstLinter.TryLintClientModule(ast, out findings);
	}

	private static IReadOnlyList<PageBodyLintFinding> LintModule(string body) {
		TryLintModule(body, out IReadOnlyList<PageBodyLintFinding> findings).Should().BeTrue(
			because: "the fixture is a client module");
		return findings;
	}

	private static string Module(string code) =>
		"define(\"UsrTestModule\", [\"@creatio-devkit/common\"], function(sdk) {\n\"use strict\";\n" + code + "\nreturn {};\n});";

	[Test]
	[Description("A client module is linted with the code-quality rules that apply outside a page section")]
	public void LintClientModule_ShouldReportQualityFindings_InModuleFunctions() {
		// Arrange
		string body = Module(
			"async function save($context) { try { await $context.save(); } catch (e) { console.error(e); } }\n"
			+ "const filter = { comparisonType: 3 };\n"
			+ "async function loadAll() { const a = await m1.load(); const b = await m2.load(); const c = await m3.load(); return [a, b, c]; }\n"
			+ "function longOne() {\n" + Lines(PageBodyAstLinter.MaxFunctionLines) + "}");

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintModule(body);

		// Assert
		findings.Select(f => f.Rule).Should().BeEquivalentTo(new[] {
			PageBodyAstLinter.RuleCatchLogOnly, PageBodyAstLinter.RuleNumericComparisonType,
			PageBodyAstLinter.RuleSequentialIndependentAwaits, PageBodyAstLinter.RuleFunctionTooLong
		}, because: "each of these breaks a rule that holds for module code as it does for page code");
		findings.Should().OnlyContain(f => f.Severity == LintSeverity.Warning, because: "code-quality rules never block a save");
	}

	[Test]
	[Description("A record id declared in a client module is not reported, and the module's factory is not measured as one long function")]
	public void LintClientModule_ShouldNotReportRecordIdsOrTheFactory() {
		// Arrange
		string body = Module("const STATUS = Object.freeze({ Closed: \"8dc41780-1234-4000-8000-000000000001\" });\n"
			+ Lines(PageBodyAstLinter.MaxFunctionLines * 2));

		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintModule(body);

		// Assert
		findings.Should().BeEmpty(
			because: "a module is where a page's named record ids are declared, and the factory is the module itself");
	}

	[TestCase("function() { return { viewConfigDiff: [], handlers: [] }; }")]
	[TestCase("() => ({ handlers: [], converters: {} })")]
	[TestCase("function() { const schema = { viewConfigDiff: [], handlers: [], converters: {}, validators: {} }; return schema; }")]
	[TestCase("function() { const schema = { modelConfigDiff: [] }; return schema; }")]
	[Description("A define whose factory returns page sections is a page that lost its markers, not a client module")]
	public void TryLintClientModule_ShouldReturnFalse_WhenFactoryReturnsPageSections(string factory) {
		// Act
		bool isModule = TryLintModule("define(\"UsrX_FormPage\", [], " + factory + ");",
			out IReadOnlyList<PageBodyLintFinding> findings);

		// Assert
		isModule.Should().BeFalse(because: "a page body must be validated as a page, so its missing markers are reported");
		findings.Should().NotBeNull().And.BeEmpty(because: "a body that is not a module gets an empty list, never null");
	}

	[TestCase("function(sdk) { return { handlers: [] }; }")]
	[TestCase("function(sdk) { const shared = { converters: {} }; return shared; }")]
	[TestCase("() => ({ validators: {}, format: () => 1 })")]
	[Description("A module that exports one of handlers / converters / validators and no page config section is a shared client module, not a page")]
	public void TryLintClientModule_ShouldLintModule_WhenFactoryExportsOneSharedSection(string factory) {
		// Act
		bool isModule = TryLintModule("define(\"UsrShared_Handlers\", [], " + factory + ");",
			out IReadOnlyList<PageBodyLintFinding> findings);

		// Assert
		isModule.Should().BeTrue(because: "sharing handler entries, converters or validators through a module is a valid module");
		findings.Should().BeEmpty(because: "the module breaks no code-quality rule");
	}

	[TestCase("async function load(model) { return model.load({ filters: { StatusId: \"8dc41780-1234-4000-8000-000000000001\" } }); }")]
	[TestCase("let status = \"8dc41780-1234-4000-8000-000000000001\";")]
	[TestCase("const ACTIONS = { close: (model) => model.set(\"StatusId\", \"8dc41780-1234-4000-8000-000000000001\") };")]
	[TestCase("const load = (model) => model.load(\"8dc41780-1234-4000-8000-000000000001\");")]
	[Description("A record id used in a module's code - not as the value of a const data declaration - is reported")]
	public void LintClientModule_ShouldReportRecordId_WhenUsedOutsideConstantData(string code) {
		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintModule(Module(code));

		// Assert
		findings.Should().ContainSingle(f => f.Rule == PageBodyAstLinter.RuleGuidLiteralDataReference,
			because: "moving page logic into a module must not hide a hard-coded record id");
	}

	[TestCase("const CLOSED = \"8dc41780-1234-4000-8000-000000000001\";")]
	[TestCase("const STATUS = { Closed: \"8dc41780-1234-4000-8000-000000000001\", Open: [\"8dc41780-1234-4000-8000-000000000002\"] };")]
	[TestCase("const IDS = [\"8dc41780-1234-4000-8000-000000000001\"];")]
	[Description("A record id held by a const data declaration is the named constant the rule asks for and is not reported")]
	public void LintClientModule_ShouldNotReportRecordId_WhenDeclaredAsConstantData(string code) {
		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintModule(Module(code));

		// Assert
		findings.Should().NotContain(f => f.Rule == PageBodyAstLinter.RuleGuidLiteralDataReference,
			because: "a named constant is where a record id belongs");
	}

	[Test]
	[Description("A script without a top-level define() is not a client module")]
	public void TryLintClientModule_ShouldReturnFalse_WhenScriptIsNotAModule() {
		// Act
		bool isModule = TryLintModule("const a = 1;", out IReadOnlyList<PageBodyLintFinding> findings);

		// Assert
		isModule.Should().BeFalse(because: "the caller lints such a script as a page body instead");
		findings.Should().NotBeNull().And.BeEmpty(because: "a body that is not a module gets an empty list, never null");
	}

	#endregion

	#region benchmark regression

	private static string Fixture(string name) =>
		System.IO.File.ReadAllText(System.IO.Path.Combine(TestContext.CurrentContext.TestDirectory, "Examples", "PageLintV2", name));

	[TestCase("UsrDemoV2App_FormPageModule.js", 3, 1)]
	[TestCase("UsrDemoV2App_ListPageModule.js", 1, 0)]
	[TestCase("UsrDemoV2DocumentTotals.js", 1, 0)]
	[Description("The client modules of the benchmark run that passed the page-only gate carry the findings the module lint reports")]
	public void LintClientModule_ShouldReportBenchmarkModuleFindings(string fixture, int logOnlyCatches, int numericComparisons) {
		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintModule(Fixture(fixture));

		// Assert
		findings.Count(f => f.Rule == PageBodyAstLinter.RuleCatchLogOnly).Should().Be(logOnlyCatches,
			because: "each catch that only logs or ignores the error is reported; a catch that keeps a fallback value is not");
		findings.Count(f => f.Rule == PageBodyAstLinter.RuleNumericComparisonType).Should().Be(numericComparisons,
			because: "a filter built with a bare `comparisonType` number is reported");
	}

	[Test]
	[Description("The benchmark page's Post handler that never calls next is reported; its attribute-change handler is not")]
	public void Lint_ShouldReportBenchmarkPostHandlerWithoutNext() {
		// Act
		List<PageBodyLintFinding> findings = Findings(Fixture("UsrDemoV2App_DeliveryNoteFormPage.js"),
			PageBodyAstLinter.RuleHandlerWithoutNext);

		// Assert
		findings.Should().ContainSingle(because: "only the Post button handler drops the chain");
		findings[0].Message.Should().Contain("`usr.DemoPostButtonClicked`", because: "the warning names the request");
	}

	[TestCase("UsrDemoV3App_FormPage.js")]
	[TestCase("UsrDemoV3App_ListPage.js")]
	[Description("The v3 benchmark pages' UpdateRecordRequest handlers, which stop the chain on purpose on one path and return next.handle(request) on every other, are not reported")]
	public void Lint_ShouldNotReportBenchmarkV3EarlyReturnHandlers(string fixture) {
		// Act
		List<PageBodyLintFinding> findings = Findings(FixtureV3(fixture), PageBodyAstLinter.RuleHandlerWithoutNext);

		// Assert
		findings.Should().BeEmpty(because: "an early return that opens the record's own page is a deliberate stop, not a lost result");
	}

	[Test]
	[Description("The v3 benchmark module saved with a bare numeric comparisonType carries the finding the module lint reports")]
	public void LintClientModule_ShouldReportBenchmarkV3ModuleFindings() {
		// Act
		IReadOnlyList<PageBodyLintFinding> findings = LintModule(FixtureV3("UsrDemoV3App_ReturnNoteFormPageModule.js"));

		// Assert
		findings.Should().ContainSingle(f => f.Rule == PageBodyAstLinter.RuleNumericComparisonType,
			because: "the filter in markPosted uses a bare `comparisonType: 3`");
		findings.Should().NotContain(f => f.Rule == PageBodyAstLinter.RuleCatchLogOnly,
			because: "both catches return a fallback value");
	}

	private static string FixtureV3(string name) =>
		System.IO.File.ReadAllText(System.IO.Path.Combine(TestContext.CurrentContext.TestDirectory, "Examples", "PageLintV3", name));

	#endregion

}
