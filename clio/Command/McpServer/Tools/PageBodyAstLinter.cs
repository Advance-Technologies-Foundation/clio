using System;
using System.Collections.Generic;
using System.Linq;
using Acornima.Ast;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Deterministic AST lint pass applied to a Freedom UI page body AFTER
/// <see cref="PageBodySyntaxValidator"/> succeeds and BEFORE the body reaches
/// <see cref="PageBodySamplingService"/> or Creatio.
///
/// Background: the syntactic floor catches grammar errors
/// but not the semantic anti-patterns described in the guidance resources
/// The matching authoring rules are delivered by external page-schema knowledge articles.
/// Each rule below maps to one or more "DO NOT" entries in those guides.
///
/// Severity model:
/// <list type="bullet">
///   <item><see cref="LintSeverity.Error"/> findings block the write (the body is
///   NOT sent to Creatio). Reserved for structural violations the deployed
///   platform rejects or silently mishandles.</item>
///   <item><see cref="LintSeverity.Warning"/> findings are reported but do NOT
///   block the write — symmetric with the existing regex-based
///   <c>ValidateContextAccessAwait</c>. Reserved for soft anti-patterns where
///   the page may still render.</item>
/// </list>
///
/// Existing regex-based validators in <c>SchemaValidationService</c> are
/// intentionally NOT migrated by this class — they ship side-by-side. The
/// linter complements them with AST-precise checks for anti-patterns regexes
/// cannot reliably express.
/// </summary>
internal static class PageBodyAstLinter {

	#region Rule identifiers

	// The lint pass only ships rules that have NO regex counterpart in
	// SchemaValidationService / SchemaHandlerValidationService. Anything the
	// regex layer already detects with established wording is intentionally
	// NOT duplicated here — duplicate detection would never reach the canonical
	// path (regex runs first and wins on overlap) and would only widen the
	// false-positive surface through any rule-scoping bug. Reviewer audit on
	// 2026-06-11 removed six previously-shipped duplicate rules
	// (`handlers-must-be-array`, `validators-must-be-object`,
	// `converters-must-be-object`, `validator-params-empty`,
	// `handler-uses-deprecated-context-api`, `handler-uses-nonexistent-request-api`)
	// covered respectively by `SchemaHandlerValidationService.cs:60`,
	// `ValidateJavaScriptObjectMarkers`, `ValidateCustomValidatorParamCompleteness`,
	// and `ForbiddenHandlerApiRules` (which catches all five forbidden
	// patterns including `$get` / `$set` as Errors).
	//
	// NOTE: the `request-type-missing-Request-suffix` rule is intentionally NOT
	// shipped either. The naive form misfires on Freedom UI component types in
	// `viewConfigDiff` entries (`"type": "crt.ComboBox"`, `"crt.Input"`,
	// `"crt.MaxLength"` — UI element types, not request dispatch payloads). A
	// correct form would bound the rule to the argument ObjectExpression of
	// `sdk.HandlerChainService.instance.process(...)` — deferred to a follow-up
	// ticket.
	internal const string RuleValidatorBadReturnLiteral = "validator-bad-return-literal";
	internal const string RuleConverterCrtPrefixReserved = "converter-crt-prefix-reserved";
	internal const string RuleBodyTooDeeplyNested = "body-too-deeply-nested";
	internal const string RuleHandlerUsesContextExecuteRequest = "handler-uses-context-execute-request";
	internal const string RuleConverterFetchCall = "converter-fetch-call";
	internal const string RuleEntityDataSourceStaticFilters = "entity-data-source-static-filters";
	internal const string RuleHandlerAttributeChangeUnscopedWrite = "handler-attribute-change-unscoped-write";
	internal const string RuleUndefinedSectionCall = "undefined-section-call";

	#endregion

	#region Public API

	/// <summary>
	/// Walks the AST once and accumulates findings for every rule that triggers.
	/// </summary>
	public static IReadOnlyList<PageBodyLintFinding> Lint(Script ast) {
		if (ast is null) {
			return Array.Empty<PageBodyLintFinding>();
		}
		var findings = new List<PageBodyLintFinding>();
		Visit(ast, default, depth: 0, findings);
		CheckUndefinedSectionCalls(ast, findings);
		return findings;
	}

	/// <summary>
	/// Renders a list of <see cref="LintSeverity.Error"/> findings into the canonical
	/// agent-facing error string. Mirrors <see cref="PageBodySyntaxValidator.FormatError"/>'s
	/// wire format so consumers see a consistent shape across the syntax / lint stages.
	/// </summary>
	public static string FormatErrors(IReadOnlyList<PageBodyLintFinding> errors) {
		if (errors is null || errors.Count == 0) {
			throw new ArgumentException(
				"FormatErrors is only meaningful when at least one error finding is present.",
				nameof(errors));
		}
		string lines = string.Join("; ", errors.Select(FormatFinding));
		return $"Page body lint failed: {lines}. The body was NOT sent to Creatio.";
	}

	/// <summary>
	/// Canonical operator-facing rendering of a single lint finding —
	/// <c>line {Line}, column {Column}: {Rule} — {Message}</c>. Centralised so the
	/// wire format stays stable across every call site (the linter's own
	/// FormatErrors, the per-warning lists rendered by PageUpdateTool / PageSyncTool /
	/// PageValidateTool) — drift between sites would silently change what tests
	/// and operators key on.
	/// </summary>
	public static string FormatFinding(PageBodyLintFinding finding) =>
		$"line {finding.Line}, column {finding.Column}: {finding.Rule} — {finding.Message}";

	#endregion

	#region AST traversal

	// AST traversal depth cap: prevents StackOverflowException on adversarial
	// or LLM-truncated bodies with extreme bracket nesting that Acornima's
	// stack-guarded parser accepts (it reports a clean AST) but a naive
	// recursive visitor would crash on. A .NET StackOverflowException cannot
	// be caught, so the MCP server process would die mid-call. The cap maps
	// the overflow into a blocking lint finding instead.
	internal const int MaxAstDepth = 1000;

	// Per-recursion context propagated to children to bound rule scopes.
	// The flags are orthogonal — each addresses one rule's scoping problem.
	// `EnclosingPropertyKey` is DIFFERENT from the other three: it is NOT
	// propagated through the subtree, only set for the exact node that is a
	// property's `Value` (recomputed to null on every other edge — see
	// ComputeChildContext) — it answers "which property, if any, owns ME
	// directly", not "am I anywhere under property X".
	private readonly record struct VisitContext(
		bool InsideValidators,
		bool InsideConverters,
		bool EnclosingFunctionIsValidatorInstance,
		string EnclosingPropertyKey);

	private static void Visit(Node node, VisitContext ctx, int depth, List<PageBodyLintFinding> findings) {
		if (depth > MaxAstDepth) {
			findings.Add(new PageBodyLintFinding(
				Rule: RuleBodyTooDeeplyNested,
				Severity: LintSeverity.Error,
				Line: node.Location.Start.Line,
				Column: node.Location.Start.Column + 1,
				Message: $"Page body AST exceeds the safe traversal depth ({MaxAstDepth}). The lint pass refuses to walk further to avoid a StackOverflowException that would kill the MCP server process."));
			return;
		}
		switch (node) {
			case ObjectExpression obj:
				CheckSchemaSectionShapes(obj, findings);
				CheckEntityDataSourceStaticFilters(obj, ctx, findings);
				CheckUnscopedAttributeChangeHandler(obj, depth, findings);
				break;
			case Property prop:
				CheckProperty(prop, ctx, findings);
				break;
			case CallExpression call:
				CheckCallExpression(call, ctx, findings);
				break;
			case ReturnStatement ret:
				CheckReturnStatement(ret, ctx, findings);
				break;
		}
		foreach (Node child in node.ChildNodes) {
			VisitContext childCtx = ComputeChildContext(node, child, ctx);
			Visit(child, childCtx, depth + 1, findings);
		}
	}

	// Compute the VisitContext for `child` when descending from `parent`.
	// Three rules currently depend on context:
	//   1) `validator-bad-return-literal` (CheckReturnStatement) fires only
	//      when `EnclosingFunctionIsValidatorInstance` is true — i.e. the
	//      nearest enclosing function IS the validator-instance function
	//      (the function returned by `validator: function(...) { return fn; }`).
	//      Returns inside nested helpers or `.filter(...)` callbacks must not
	//      be flagged because they belong to the helper/callback, not the
	//      validator contract.
	//   2) `converter-fetch-call` (CheckCallExpression) fires on `fetch(...)`
	//      anywhere under `converters` — `InsideConverters` is sufficient
	//      because the anti-pattern is render-time HTTP regardless of
	//      function nesting depth.
	//   3) `handler-uses-context-execute-request` (CheckCallExpression) has
	//      no schema-section gate — handler dispatch through `$context` is
	//      wrong in any deployed page-body location.
	// `params-empty` and `crt-prefix-reserved` are now driven directly off
	// the validators/converters ObjectExpression in CheckSchemaSectionShape
	// (direct-child walk), not through this context — see the comments on
	// CheckValidatorParamsEmptyOnDirectEntries / CheckConvertersDirectKeys.
	private static VisitContext ComputeChildContext(Node parent, Node child, VisitContext currentCtx) {
		if (parent is Property prop && ReferenceEquals(child, prop.Value)) {
			string key = TryGetStaticPropertyName(prop);
			if (key == "validators") {
				return currentCtx with { InsideValidators = true, EnclosingFunctionIsValidatorInstance = false, EnclosingPropertyKey = key };
			}
			if (key == "converters") {
				return currentCtx with { InsideConverters = true, EnclosingPropertyKey = key };
			}
			return currentCtx with { EnclosingPropertyKey = key };
		}
		// Identify the validator-instance function: the IFunction that is the
		// `Argument` of a `return` statement (so the enclosing factory's body
		// `return function(value) { ... }` shape is matched). Whenever we
		// descend INTO any IFunction node inside the validators subtree we
		// recompute the flag from scratch — `true` only if the parent
		// transition is a ReturnStatement.Argument transition, otherwise
		// `false`. This handles:
		//   - The factory itself (descended via Property -> IFunction): false.
		//   - The validator-instance (descended via ReturnStatement -> IFunction): true.
		//   - A nested helper declared in the factory body (e.g.
		//     `function isEmpty(v) { ... }` or `[].filter(function(i){...})`):
		//     false, because its parent is a BlockStatement / CallExpression,
		//     not a ReturnStatement.Argument.
		// Non-IFunction children keep the parent's flag — recursion into the
		// body, params, etc. inherits whichever scope we're currently in.
		if (currentCtx.InsideValidators && child is IFunction) {
			bool isValidatorInstance = parent is ReturnStatement ret
				&& ReferenceEquals(child, ret.Argument);
			return currentCtx with { EnclosingFunctionIsValidatorInstance = isValidatorInstance, EnclosingPropertyKey = null };
		}
		return currentCtx with { EnclosingPropertyKey = null };
	}

	#endregion

	#region Undefined section calls

	private const string FetchGlobalName = "fetch";

	// The blocking `undefined-section-call` rule may only reject a bare call when the callee is
	// genuinely absent, so the catalog of names the runtime supplies has to be explicit and
	// complete rather than a short sample. It is split by the runtime that provides each name, so
	// a future addition lands in the right group and stays reviewable. A name missing from here
	// turns a working page into a rejected write, which is why the groups err on the wide side.

	// ECMAScript: the global object's own properties per the language specification.
	private static readonly string[] EcmaScriptGlobals = [
		"AggregateError", "Array", "ArrayBuffer", "Atomics", "BigInt", "BigInt64Array", "BigUint64Array",
		"Boolean", "DataView", "Date", "decodeURI", "decodeURIComponent", "encodeURI", "encodeURIComponent",
		"Error", "escape", "eval", "EvalError", "FinalizationRegistry", "Float32Array", "Float64Array",
		"Function", "globalThis", "Infinity", "Int16Array", "Int32Array", "Int8Array", "Intl", "isFinite",
		"isNaN", "JSON", "Map", "Math", "NaN", "Number", "Object", "parseFloat", "parseInt", "Promise",
		"Proxy", "RangeError", "ReferenceError", "Reflect", "RegExp", "Set", "SharedArrayBuffer", "String",
		"Symbol", "SyntaxError", "TypeError", "Uint16Array", "Uint32Array", "Uint8Array", "Uint8ClampedArray",
		"undefined", "unescape", "URIError", "WeakMap", "WeakRef", "WeakSet"
	];

	// Browser: names a Freedom UI page runs against in the host document. Kept to what page code
	// realistically calls or reads; the probe that rejected `alert`, `btoa` and `queueMicrotask`
	// was pointing at exactly this gap.
	private static readonly string[] BrowserGlobals = [
		"AbortController", "AbortSignal", "alert", "atob", "Audio", "Blob", "btoa", "cancelAnimationFrame",
		"cancelIdleCallback", "clearInterval", "clearTimeout", "confirm", "console", "crypto", "CSS",
		"CustomEvent", "document", "DOMParser", "Element", "Event", "EventSource", "EventTarget", "File",
		"FileReader", "FormData", "getComputedStyle", FetchGlobalName, "Headers", "history", "HTMLElement",
		"Image", "IntersectionObserver", "localStorage", "location", "matchMedia", "MutationObserver",
		"navigator", "Node", "Notification", "parent", "performance", "prompt", "queueMicrotask",
		"requestAnimationFrame", "requestIdleCallback", "Request", "ResizeObserver", "Response", "screen",
		"self", "sessionStorage", "setInterval", "setTimeout", "structuredClone", "TextDecoder",
		"TextEncoder", "top", "URL", "URLSearchParams", "WebSocket", "window", "Worker", "XMLHttpRequest"
	];

	// AMD: a Freedom UI page body is an AMD module, so the loader's own names are always in scope.
	private static readonly string[] AmdGlobals = ["define", "require", "requirejs"];

	// Creatio: the platform namespaces a page body may reach without declaring them.
	private static readonly string[] CreatioGlobals = ["BPMSoft", "crt", "Ext", "sdk", "Terrasoft"];

	internal static readonly IReadOnlyCollection<string> KnownRuntimeGlobals =
		new HashSet<string>(
			EcmaScriptGlobals.Concat(BrowserGlobals).Concat(AmdGlobals).Concat(CreatioGlobals),
			StringComparer.Ordinal);

	// One finding per distinct callee name, and at most this many names. An LLM-truncated body can
	// repeat the same broken call thousands of times; without a bound, FormatErrors concatenated
	// every occurrence into a multi-megabyte string that no MCP client can use. Names past the cap
	// are replaced by a single summary finding that states how many were left out, so the response
	// stays bounded without pretending the rest do not exist.
	internal const int MaxUndefinedSectionCallNames = 20;

	/// <summary>
	/// A lexical scope and its chain of enclosing scopes. Resolution walks outwards, so a name
	/// declared in a sibling or nested function is invisible here - which is the whole point: a
	/// single flat name set made the rule accept `missingHelper()` as soon as ANY unrelated
	/// function in the body happened to declare that name.
	/// </summary>
	private sealed class LexicalScope {

		private readonly HashSet<string> _names = new(StringComparer.Ordinal);
		private readonly LexicalScope _parent;

		public LexicalScope(LexicalScope parent){
			_parent = parent;
		}

		public void Declare(string name){
			if (!string.IsNullOrEmpty(name)) {
				_names.Add(name);
			}
		}

		public bool IsDeclared(string name){
			for (LexicalScope scope = this; scope is not null; scope = scope._parent) {
				if (scope._names.Contains(name)) {
					return true;
				}
			}
			return false;
		}

	}

	// The directive that switches a script or a function body into strict mode.
	private const string UseStrictDirective = "use strict";

	/// <summary>
	/// Where an omitted occurrence sat. The summary finding reports only a position, so an omitted
	/// call must not cost a whole finding with its interpolated message.
	/// </summary>
	private readonly record struct OmittedCallLocation(int Line, int Column);

	/// <summary>
	/// Accumulates the rule's findings so the dedupe and the cap apply across the whole walk.
	/// </summary>
	private sealed class UndefinedCallBudget {

		private readonly HashSet<string> _omitted = new(StringComparer.Ordinal);
		private readonly HashSet<string> _reported = new(StringComparer.Ordinal);

		public int OmittedNameCount => _omitted.Count;

		public int OmittedOccurrenceCount { get; private set; }

		public int ReportedNameCount => _reported.Count;

		/// <summary>Returns true when this occurrence must become a finding of its own.</summary>
		public bool ShouldReport(string name){
			if (_reported.Contains(name)) {
				OmittedOccurrenceCount++;
				return false;
			}
			if (_reported.Count >= MaxUndefinedSectionCallNames) {
				_omitted.Add(name);
				OmittedOccurrenceCount++;
				return false;
			}
			_reported.Add(name);
			return true;
		}

	}

	private static void CheckUndefinedSectionCalls(Script ast, List<PageBodyLintFinding> findings) {
		LexicalScope scriptScope = new(null);
		foreach (string global in KnownRuntimeGlobals) {
			scriptScope.Declare(global);
		}
		bool strict = HasUseStrictDirective(ast.Body);
		DeclareHoistedNames(ast, scriptScope, depth: 0, strict, atStatementLevel: true);
		DeclareBlockNames(ast.Body, scriptScope, depth: 0);
		UndefinedCallBudget budget = new();
		OmittedCallLocation? lastOmitted = null;
		ScanForUndefinedSectionCalls(ast, scriptScope, insideSection: false, depth: 0, strict, findings,
			budget, ref lastOmitted);
		if (budget.OmittedOccurrenceCount > 0 && lastOmitted.HasValue) {
			findings.Add(BuildOmittedSummary(lastOmitted.Value, budget));
		}
	}

	/// <summary>
	/// True when a statement list opens with a "use strict" directive prologue.
	/// </summary>
	private static bool HasUseStrictDirective(in NodeList<Statement> statements) {
		foreach (Statement statement in statements) {
			if (statement is not ExpressionStatement {Expression: StringLiteral literal}) {
				//The prologue ends at the first statement that is not a string literal expression.
				return false;
			}
			if (literal.Value == UseStrictDirective) {
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// True when this function runs in strict mode: its enclosing code already did, or its own body
	/// opens with the directive.
	/// </summary>
	private static bool IsStrictFunction(Node node, bool enclosingStrict) =>
		enclosingStrict
		|| (node is IFunction {Body: BlockStatement body} && HasUseStrictDirective(body.Body));

	private static PageBodyLintFinding BuildOmittedSummary(
		OmittedCallLocation lastOmitted, UndefinedCallBudget budget) {
		string namesPart = budget.OmittedNameCount > 0
			? $"{budget.OmittedNameCount} further undeclared name(s) past the first "
				+ $"{MaxUndefinedSectionCallNames}, and "
			: string.Empty;
		return new PageBodyLintFinding(
			Rule: RuleUndefinedSectionCall,
			Severity: LintSeverity.Error,
			Line: lastOmitted.Line,
			Column: lastOmitted.Column,
			Message: $"{namesPart}{budget.OmittedOccurrenceCount} further call site(s) of undeclared "
				+ $"identifiers were omitted from this report; {budget.ReportedNameCount} distinct name(s) "
				+ "are listed above. Fix the listed ones and re-run validate-page to see the rest.");
	}

	/// <summary>
	/// Declares everything hoisted into one function-level scope: `var` names and function
	/// declarations, found through nested blocks but NOT through nested functions, whose own
	/// declarations belong to their own scope.
	/// </summary>
	private static void DeclareHoistedNames(Node node, LexicalScope scope, int depth, bool strict,
		bool atStatementLevel) {
		if (node is null || depth > MaxAstDepth) {
			return;
		}
		//Only a statement list can put code after a return; in an if/else the branches are siblings.
		bool tracksUnreachable = node is Acornima.Ast.Program or BlockStatement or SwitchCase;
		bool afterReturn = false;
		foreach (Node child in node.ChildNodes) {
			if (afterReturn && child is not FunctionDeclaration) {
				//Past a return the binding still hoists, but its initializer never runs, so calling
				//it throws ReferenceError or TypeError. Only a function declaration stays callable.
				continue;
			}
			switch (child) {
				case VariableDeclaration {Kind: VariableDeclarationKind.Var} varDeclaration:
					foreach (VariableDeclarator declarator in varDeclaration.Declarations) {
						DeclareBindings(declarator.Id, scope, depth + 1);
					}
					break;
				case FunctionDeclaration {Id: not null} functionDeclaration:
					//In strict code a function declared inside a block belongs to that block, so
					//hoisting it out of one would accept a call that throws ReferenceError at
					//runtime. DeclareBlockNames declares it in its own block scope instead.
					if (!strict || atStatementLevel) {
						scope.Declare(functionDeclaration.Id.Name);
					}
					//A function declaration opens its own scope; nothing inside it hoists to here.
					continue;
				case IFunction:
					//Same for a function expression or an arrow: its bindings stay inside it.
					continue;
				case ReturnStatement when tracksUnreachable:
					afterReturn = true;
					continue;
			}
			DeclareHoistedNames(child, scope, depth + 1, strict, atStatementLevel: false);
		}
	}

	/// <summary>
	/// Declares the block-scoped names of one statement list: `let`, `const`, classes, and the
	/// function declarations that are direct statements of this block.
	/// </summary>
	private static void DeclareBlockNames(in NodeList<Statement> statements, LexicalScope scope, int depth) {
		bool afterReturn = false;
		foreach (Statement statement in statements) {
			if (afterReturn && statement is not FunctionDeclaration) {
				//Unreachable: the name exists but its initializer never runs, so a handler calling
				//it fails at runtime. A function declaration is the only usable case.
				continue;
			}
			if (statement is ReturnStatement) {
				afterReturn = true;
				continue;
			}
			switch (statement) {
				case VariableDeclaration {
						Kind: VariableDeclarationKind.Let or VariableDeclarationKind.Const
							or VariableDeclarationKind.Using or VariableDeclarationKind.AwaitUsing
					} blockDeclaration:
					foreach (VariableDeclarator declarator in blockDeclaration.Declarations) {
						DeclareBindings(declarator.Id, scope, depth + 1);
					}
					break;
				case FunctionDeclaration {Id: not null} functionDeclaration:
					scope.Declare(functionDeclaration.Id.Name);
					break;
				case ClassDeclaration {Id: not null} classDeclaration:
					scope.Declare(classDeclaration.Id.Name);
					break;
			}
		}
	}

	/// <summary>
	/// Declares the names a binding target introduces - and only those. A destructuring key names
	/// the property being read, never a new binding, so `const {alpha: beta} = source` declares
	/// `beta` alone; counting `alpha` as declared is what let a later `alpha()` through unchecked.
	/// </summary>
	private static void DeclareBindings(Node node, LexicalScope scope, int depth) {
		if (node is null || depth > MaxAstDepth) {
			return;
		}
		switch (node) {
			case Identifier identifier:
				scope.Declare(identifier.Name);
				break;
			case ObjectPattern objectPattern:
				foreach (Node property in objectPattern.Properties) {
					//Property.Value is the binding target; a RestElement carries its own.
					DeclareBindings(property is Property {Value: not null} keyed ? keyed.Value : property,
						scope, depth + 1);
				}
				break;
			case ArrayPattern arrayPattern:
				foreach (Node element in arrayPattern.Elements) {
					DeclareBindings(element, scope, depth + 1);
				}
				break;
			case AssignmentPattern assignmentPattern:
				DeclareBindings(assignmentPattern.Left, scope, depth + 1);
				break;
			case RestElement restElement:
				DeclareBindings(restElement.Argument, scope, depth + 1);
				break;
		}
	}

	/// <summary>
	/// Opens the scope a node introduces, if any, and returns the scope its children see.
	/// </summary>
	private static LexicalScope OpenScope(Node node, LexicalScope scope, int depth, bool strict) {
		switch (node) {
			case IFunction function: {
				LexicalScope functionScope = new(scope);
				//A named function expression can call itself by that name from inside its body.
				if (function.Id is not null) {
					functionScope.Declare(function.Id.Name);
				}
				foreach (Node parameter in function.Params) {
					DeclareBindings(parameter, functionScope, depth + 1);
				}
				DeclareHoistedNames(function.Body, functionScope, depth + 1, strict,
					atStatementLevel: true);
				return functionScope;
			}
			case BlockStatement block: {
				LexicalScope blockScope = new(scope);
				DeclareBlockNames(block.Body, blockScope, depth + 1);
				return blockScope;
			}
			case SwitchStatement switchStatement: {
				//Every case shares one block scope, so a `let` in case A is visible in case B.
				LexicalScope switchScope = new(scope);
				foreach (SwitchCase switchCase in switchStatement.Cases) {
					DeclareBlockNames(switchCase.Consequent, switchScope, depth + 1);
				}
				return switchScope;
			}
			case CatchClause catchClause: {
				LexicalScope catchScope = new(scope);
				DeclareBindings(catchClause.Param, catchScope, depth + 1);
				return catchScope;
			}
			case ForStatement {Init: VariableDeclaration forInit}:
				return OpenLoopScope(forInit, scope, depth);
			case ForInStatement {Left: VariableDeclaration forInLeft}:
				return OpenLoopScope(forInLeft, scope, depth);
			case ForOfStatement {Left: VariableDeclaration forOfLeft}:
				return OpenLoopScope(forOfLeft, scope, depth);
			case ClassExpression {Id: not null} classExpression: {
				LexicalScope classScope = new(scope);
				classScope.Declare(classExpression.Id.Name);
				return classScope;
			}
			default:
				return scope;
		}
	}

	private static LexicalScope OpenLoopScope(VariableDeclaration head, LexicalScope scope, int depth) {
		LexicalScope loopScope = new(scope);
		foreach (VariableDeclarator declarator in head.Declarations) {
			DeclareBindings(declarator.Id, loopScope, depth + 1);
		}
		return loopScope;
	}

	private static void ScanForUndefinedSectionCalls(
		Node node,
		LexicalScope scope,
		bool insideSection,
		int depth,
		bool strict,
		List<PageBodyLintFinding> findings,
		UndefinedCallBudget budget,
		ref OmittedCallLocation? lastOmitted) {
		if (node is null || depth > MaxAstDepth) {
			return;
		}
		bool childStrict = IsStrictFunction(node, strict);
		LexicalScope childScope = OpenScope(node, scope, depth, childStrict);
		bool childInsideSection = insideSection;
		if (!insideSection && node is Property property && TryGetStaticPropertyName(property) is string key) {
			childInsideSection = key is "handlers" or "converters" or "validators";
		}
		if (insideSection && node is CallExpression {Callee: Identifier identifier}
			&& !childScope.IsDeclared(identifier.Name)) {
			//The budget decides FIRST: a truncated body can repeat one broken call tens of thousands
			//of times, and building the long interpolated message for every occurrence only to drop
			//it allocated tens of megabytes. An omitted occurrence keeps its location and nothing
			//else, which is all the summary finding reads.
			if (budget.ShouldReport(identifier.Name)) {
				findings.Add(new PageBodyLintFinding(
					Rule: RuleUndefinedSectionCall,
					Severity: LintSeverity.Error,
					Line: identifier.Location.Start.Line,
					Column: identifier.Location.Start.Column + 1,
					Message: $"Call to `{identifier.Name}()` in a handlers/converters/validators section references an identifier that is not declared in the enclosing scopes of this page body and is not a known JavaScript, browser, AMD or Creatio global. A module-scope helper may have been removed by Page Designer; re-add it before the `return` statement."));
			} else {
				lastOmitted = new OmittedCallLocation(
					identifier.Location.Start.Line, identifier.Location.Start.Column + 1);
			}
		}
		foreach (Node child in node.ChildNodes) {
			ScanForUndefinedSectionCalls(child, childScope, childInsideSection, depth + 1, childStrict,
				findings, budget, ref lastOmitted);
		}
	}

	#endregion

	#region Rule implementations

	// Walks every ObjectExpression looking for the `converters: {...}` map.
	// The crt-prefix rule applies to direct entries of that map only — a
	// `"crt.X"` key inside a nested lookup table in a converter's closure
	// is opaque to the rule.
	private static void CheckSchemaSectionShapes(ObjectExpression obj, List<PageBodyLintFinding> findings) {
		foreach (Node element in obj.Properties) {
			if (!TryGetInitProperty(element, out Property prop, out string key)) {
				continue;
			}
			if (key == "converters" && prop.Value is ObjectExpression convertersObj) {
				CheckConvertersDirectKeys(convertersObj, findings);
			}
		}
	}

	// Match plain init properties carrying a static key. Skips shorthand
	// methods (`handlers() { ... }`), accessors (`get handlers() { ... }`),
	// spread elements, and computed-key properties.
	private static bool TryGetInitProperty(Node node, out Property prop, out string key) {
		prop = null;
		key = null;
		if (node is not Property candidate || candidate.Method || candidate.Kind != PropertyKind.Init) {
			return false;
		}
		string staticKey = TryGetStaticPropertyName(candidate);
		if (staticKey is null) {
			return false;
		}
		prop = candidate;
		key = staticKey;
		return true;
	}

	// Custom converter names declared with the reserved `crt.*` namespace.
	// The rule applies only to keys that ARE direct entries of the
	// converters object; a lookup-table inside a converter's closure such
	// as `{ "crt.X": "label" }` is unrelated and must not be flagged.
	// No regex counterpart in SchemaValidationService — `crt.*` is treated
	// as a valid vendor prefix by `ValidatePrefixedDeclarations` and the
	// converter shape validators explicitly skip `crt.*` keys.
	private static void CheckConvertersDirectKeys(ObjectExpression convertersObj, List<PageBodyLintFinding> findings) {
		foreach (Node element in convertersObj.Properties) {
			if (!TryGetInitProperty(element, out Property entry, out string entryKey)) {
				continue;
			}
			if (!entryKey.StartsWith("crt.", StringComparison.Ordinal)) {
				continue;
			}
			findings.Add(new PageBodyLintFinding(
				Rule: RuleConverterCrtPrefixReserved,
				Severity: LintSeverity.Error,
				Line: entry.Location.Start.Line,
				Column: entry.Location.Start.Column + 1,
				Message: $"Custom converter `{entryKey}` uses the reserved `crt.*` namespace; only Creatio built-in converters may use this prefix"));
		}
	}

	// Rule 11: a `crt.EntityDataSource` config that carries a `filters` block. `filters` is not a
	// recognized `crt.EntityDataSource` config key (unlike entitySchemaName / attributes /
	// loadParameters / useRecordDeactivation …), so it is never applied at runtime — update-page
	// persists it and returns success while the list silently shows UNFILTERED data (ENG-93867).
	//
	// Keyed off the config SIGNATURE — an object holding BOTH a `filters` key and an `entitySchemaName`
	// key — rather than the enclosing `type: "crt.EntityDataSource"` descriptor. This matches the config
	// object whether it is emitted inline inside the full descriptor (`{ type, scope, config: { … } }`)
	// OR carried by a separate/narrower diff `merge` op that splits the descriptor from its config (the
	// config merge still carries `entitySchemaName` alongside the ignored `filters`). `entitySchemaName`
	// is unique to an EntityDataSource config, so this does NOT fire on a `crt.IndicatorWidget`'s
	// `config.data.providing.filters` — that object exposes `schemaName`, never `entitySchemaName`.
	//
	// Known residual gap: a `filters`-ONLY narrow merge into a `[…, "config"]` path, with no co-located
	// `entitySchemaName`, is not flagged — catching that needs diff-path semantics, out of scope for this
	// AST-shape Warning (the common inline + split-with-schema shapes ARE covered). No regex counterpart
	// in SchemaValidationService — the invalid shape is JSON-structural. Warning severity: an invisible
	// no-op, not a structural break, so it must not fail the write.
	//
	// False-positive carve-out (GH-1125): a Freedom UI Dashboard container's generated
	// `_designOptions` block also carries `entitySchemaName` alongside a `filters` array
	// (`{ "entitySchemaName": ..., "dependencies": [], "filters": [] }`) — the SAME
	// co-located-key signature this rule keys off, even though it is designer-owned
	// dashboard metadata, not a `crt.EntityDataSource` config, so the "filters is an
	// ignored EntityDataSource config key" claim this rule warns about does not apply to
	// it. `_designOptions` is never a legitimate `crt.EntityDataSource` config location,
	// so the object that is DIRECTLY the value of a property literally named
	// `_designOptions` is excluded outright.
	private static void CheckEntityDataSourceStaticFilters(ObjectExpression obj, VisitContext ctx, List<PageBodyLintFinding> findings) {
		if (ctx.EnclosingPropertyKey == "_designOptions") {
			return;
		}
		Property filtersProp = null;
		bool hasEntitySchemaName = false;
		foreach (Node element in obj.Properties) {
			if (!TryGetInitProperty(element, out Property prop, out string key)) {
				continue;
			}
			if (key == "filters") {
				filtersProp = prop;
			} else if (key == "entitySchemaName") {
				hasEntitySchemaName = true;
			}
		}
		if (filtersProp is null || !hasEntitySchemaName) {
			return;
		}
		findings.Add(new PageBodyLintFinding(
			Rule: RuleEntityDataSourceStaticFilters,
			Severity: LintSeverity.Warning,
			Line: filtersProp.Location.Start.Line,
			Column: filtersProp.Location.Start.Column + 1,
			Message: "`config.filters` on a `crt.EntityDataSource` is never applied — `filters` is not a recognized data-source config key. update-page persists it and returns success, but the list shows UNFILTERED data. Put a static filter in a `<CollectionAttr>_PredefinedFilter` view-model attribute referenced from the collection attribute's `modelConfig.filterAttributes` (per related-list guidance)."));
	}

	// Rule 12: a `crt.HandleViewModelAttributeChangeRequest` handler entry that is NOT scoped to the
	// triggering attribute but writes a view-model attribute through a `$context` set call. This request
	// fires on EVERY attribute change, so an unscoped handler that writes an attribute re-enters on its
	// OWN write — with a value that is no longer the one it expected — and typically clears the field it
	// just set (or loops). The canonical scope is an early attributeName guard that returns through next
	// when the changed attribute is not the target (page-schema-handlers guidance). requestArgumentPropertyName
	// does NOT scope this handler — it is silently ignored, which is exactly the trap this rule surfaces. No
	// regex counterpart in SchemaValidationService / SchemaHandlerValidationService — the self-retrigger
	// footgun is a data-flow shape, not a token match. Warning severity — the page still saves and renders,
	// and the field is just wiped at runtime.
	//
	// Keyed off the handler entry ObjectExpression: the request key equals the target literal, a handler
	// function (arrow OR shorthand method), and — inside that function's subtree — a `$context` set-call write
	// with NO attributeName reference. Referencing attributeName anywhere in the body (member access such as
	// request dot attributeName, destructuring, a comparison, or a COMPUTED bracket access on the
	// attributeName key) is treated as "author is scope-aware" and suppresses the warning. The bracket form is
	// matched as a computed member access on the attributeName property literal, NOT as a bare attributeName
	// string anywhere — an incidental literal must not suppress the warning.
	//
	// Heuristic limits (all acceptable for a non-blocking Warning; NOT "zero false positives"):
	//   - False negative: the attributeName reference is a scope-awareness PROXY, not proof the write is
	//     guarded — a handler reading attributeName for an unrelated purpose while writing UNCONDITIONALLY is
	//     missed (pinned by Lint_ShouldNotWarn_WhenAttributeNameReferencedButWriteUnconditional). A write via a
	//     local $context alias is also missed (see IsContextSetCall).
	//   - False positive: a guard hidden behind a helper call — an early return driven by a helper predicate on
	//     request — is not seen, since detecting it needs inter-procedural data-flow analysis, so such a scoped
	//     handler is still warned. The proxy trades these residuals for catching the common shapes.
	private static void CheckUnscopedAttributeChangeHandler(ObjectExpression obj, int depth, List<PageBodyLintFinding> findings) {
		Property requestProp = null;
		Property handlerProp = null;
		foreach (Node element in obj.Properties) {
			// Accept BOTH init properties (`handler: (r, n) => {}`) AND shorthand methods
			// (`async handler(r, n) {}`) — TryGetInitProperty rejects methods, which would MISS the genuine
			// bug when `handler` is written as a shorthand method.
			if (!TryGetEntryProperty(element, out Property prop, out string key)) {
				continue;
			}
			if (key == "request") {
				requestProp = prop;
			} else if (key == "handler") {
				handlerProp = prop;
			}
		}
		if (requestProp?.Value is not Literal { Value: "crt.HandleViewModelAttributeChangeRequest" }) {
			return;
		}
		if (handlerProp?.Value is not IFunction handlerFn) {
			return;
		}
		bool referencesAttributeName = false;
		bool writesContextAttribute = false;
		ScanHandlerBody((Node)handlerFn, depth, ref referencesAttributeName, ref writesContextAttribute);
		if (referencesAttributeName || !writesContextAttribute) {
			return;
		}
		findings.Add(new PageBodyLintFinding(
			Rule: RuleHandlerAttributeChangeUnscopedWrite,
			Severity: LintSeverity.Warning,
			Line: requestProp.Location.Start.Line,
			Column: requestProp.Location.Start.Column + 1,
			Message: "A `crt.HandleViewModelAttributeChangeRequest` handler that writes a view-model attribute via `$context.set(...)` is not scoped to the triggering attribute, so it re-fires on its own write and can clear the value or loop. Scope it with an early guard: `if (request.attributeName !== \"<Attr>\") return next?.handle(request);` (per page-schema-handlers guidance). If the write is an intentional cross-field recompute, still guard it so it does not re-enter on its own write — skip when `request.attributeName` is the attribute you are writing. Note: `requestArgumentPropertyName` does NOT scope this handler — it is silently ignored."));
	}

	// Like TryGetInitProperty but ALSO accepts shorthand-method properties (`handler(r, n) {}`), whose
	// `Value` is the method's function. Used for the handler-entry keys (`request`, `handler`) where a
	// method-form `handler` is legitimate; the converters / data-source rules keep using TryGetInitProperty,
	// which rejects methods.
	private static bool TryGetEntryProperty(Node node, out Property prop, out string key) {
		prop = null;
		key = null;
		if (node is not Property candidate || candidate.Computed || candidate.Kind != PropertyKind.Init) {
			return false;
		}
		string staticKey = TryGetStaticPropertyName(candidate);
		if (staticKey is null) {
			return false;
		}
		prop = candidate;
		key = staticKey;
		return true;
	}

	// Walk the handler function subtree once, collecting the two orthogonal signals the rule needs:
	//   - an `attributeName` reference — either an Identifier (member access `request.attributeName`,
	//     destructuring `const { attributeName } = request`, or a comparison) OR a COMPUTED member access
	//     whose property literal is `"attributeName"` (bracket access `request["attributeName"]`) → author is
	//     scope-aware, suppress. The bracket arm is deliberately anchored to a computed MemberExpression, not
	//     a bare `"attributeName"` string literal anywhere in the body — an incidental literal (e.g.
	//     `$context.set("attributeName", x)` or a log string) must NOT suppress the warning.
	//   - a `$context.set(...)` call (`request.$context.set` or a destructured `$context.set`) → the
	//     handler writes an attribute, which is what makes an unscoped handler self-retrigger.
	// Bounded by MaxAstDepth for the same StackOverflow reason the main traversal is; short-circuits
	// as soon as both signals are known.
	private static void ScanHandlerBody(Node node, int depth, ref bool referencesAttributeName, ref bool writesContextAttribute) {
		if (node is null || depth > MaxAstDepth) {
			return;
		}
		if (node is Identifier { Name: "attributeName" }
			or MemberExpression { Computed: true, Property: Literal { Value: "attributeName" } }) {
			referencesAttributeName = true;
		} else if (node is CallExpression call && IsContextSetCall(call.Callee)) {
			writesContextAttribute = true;
		}
		if (referencesAttributeName && writesContextAttribute) {
			return;
		}
		foreach (Node child in node.ChildNodes) {
			ScanHandlerBody(child, depth + 1, ref referencesAttributeName, ref writesContextAttribute);
			if (referencesAttributeName && writesContextAttribute) {
				return;
			}
		}
	}

	// Matches a set call on the live ViewModel context: the callee is a set member either on the
	// destructured $context identifier or on a member access whose inner property is $context (typically
	// request.$context).
	//
	// Accepted false negative (deliberate, second of two): a write through a LOCAL ALIAS that drops the
	// $context member — assigning request.$context to a local variable and calling set on that variable —
	// is NOT detected, so an unscoped handler writing that way is not flagged. Following aliases needs
	// data-flow analysis, mirroring the same alias limitation on IsContextExecuteRequest and consistent with
	// the rule's documented heuristic limits (a non-blocking Warning that tolerates residual misses; see the
	// CheckUnscopedAttributeChangeHandler doc). Pinned by Lint_ShouldNotWarn_WhenAttributeChangeHandlerWritesViaAliasedContext.
	private static bool IsContextSetCall(Node callee) =>
		callee is MemberExpression { Property: Identifier { Name: "set" }, Computed: false, Object: var target }
		&& target switch {
			Identifier { Name: "$context" } => true,
			MemberExpression { Property: Identifier { Name: "$context" }, Computed: false } => true,
			_ => false
		};

	// CheckProperty intentionally has no rules left: `params-empty` and
	// `converter-crt-prefix-reserved` now run inside CheckSchemaSectionShape
	// against the validators/converters ObjectExpression's direct property
	// children (see CheckValidatorParamsEmptyOnDirectEntries and
	// CheckConvertersDirectKeys). That removes the false-positive surface
	// of the previous "fires anywhere under the validators/converters
	// subtree" gates (e.g. `executeRequest({type, params:[]})` inside a
	// factory body or a `"crt.X"` lookup-table key inside a converter's
	// closure no longer wrongly trigger an Error).
	private static void CheckProperty(Property prop, VisitContext ctx, List<PageBodyLintFinding> findings) {
		// kept as an extension point for future Property-level rules
	}

	private static void CheckCallExpression(CallExpression call, VisitContext ctx, List<PageBodyLintFinding> findings) {
		// Rule 9: request.$context.executeRequest(...) is reachable from handler code
		// but it is NOT part of the @creatio-devkit/common public surface — Creatio
		// Academy uniformly uses sdk.HandlerChainService.instance.process(...) in
		// SCHEMA_HANDLERS examples. The reverse direction (process discouraged in
		// favour of executeRequest) was the previous guidance and is no longer correct.
		if (IsContextExecuteRequest(call.Callee)) {
			findings.Add(new PageBodyLintFinding(
				Rule: RuleHandlerUsesContextExecuteRequest,
				Severity: LintSeverity.Warning,
				Line: call.Location.Start.Line,
				Column: call.Location.Start.Column + 1,
				Message: "`request.$context.executeRequest(...)` is not part of the documented @creatio-devkit/common public API; use `sdk.HandlerChainService.instance.process({ type, $context, scopes })` in deployed page-body handlers (per Creatio Academy SCHEMA_HANDLERS examples)"));
		}
		// Rule 10: direct `fetch(...)` / `globalThis.fetch(...)` / `window.fetch(...)`
		// inside the converters schema subtree. Bounded via VisitContext.InsideConverters
		// so the warning targets the actual anti-pattern (non-cached HTTP fired on
		// every control render) and does not noise the agent with informational
		// flags on legitimate `fetch` usage elsewhere in the body.
		if (ctx.InsideConverters && IsFetchCall(call.Callee)) {
			findings.Add(new PageBodyLintFinding(
				Rule: RuleConverterFetchCall,
				Severity: LintSeverity.Warning,
				Line: call.Location.Start.Line,
				Column: call.Location.Start.Column + 1,
				Message: "Direct `fetch(...)` inside a converter fires on every render of the bound control; replace with a cached SDK service such as `SysSettingsService` (per page-schema-converters guidance)"));
		}
	}

	private static bool IsFetchCall(Node callee) =>
		callee switch {
			Identifier { Name: FetchGlobalName } => true,
			MemberExpression { Property: Identifier { Name: FetchGlobalName }, Computed: false, Object: Identifier { Name: "globalThis" } } => true,
			MemberExpression { Property: Identifier { Name: FetchGlobalName }, Computed: false, Object: Identifier { Name: "window" } } => true,
			_ => false
		};

	private static void CheckReturnStatement(ReturnStatement ret, VisitContext ctx, List<PageBodyLintFinding> findings) {
		// Rule 6: validator declaration must not return a literal. Bounded
		// to returns whose nearest enclosing function is THE validator-
		// instance function (the function returned by the factory). Other
		// returns in the validators subtree — inside the factory before its
		// own `return function(...)`, inside a `function isEmpty(v)` helper
		// declared in the factory body, inside a `.filter(function(i){...})`
		// predicate inside the instance body — must not be flagged because
		// their returns are not part of the validator contract. The
		// `EnclosingFunctionIsValidatorInstance` flag is set exactly once on
		// descent into the validator-instance function (parent is a
		// ReturnStatement whose Argument IS the IFunction). Nested IFunction
		// descents inside that subtree reset the flag to false.
		if (!ctx.EnclosingFunctionIsValidatorInstance) {
			return;
		}
		if (!IsBadValidatorReturnLiteral(ret.Argument)) {
			return;
		}
		findings.Add(new PageBodyLintFinding(
			Rule: RuleValidatorBadReturnLiteral,
			Severity: LintSeverity.Error,
			Line: ret.Location.Start.Line,
			Column: ret.Location.Start.Column + 1,
			Message: "validator return must be `{ \"<ValidatorType>\": { message: config.message } }`; literal `true` / `false` / `{}` / hardcoded-string returns are rejected — see page-schema-validators guidance. `null` and `undefined` returns are allowed (they signal \"no error\")"));
	}

	#endregion

	#region Helpers

	private static string TryGetStaticPropertyName(Property prop) {
		if (prop.Computed) {
			return null;
		}
		return prop.Key switch {
			Identifier id => id.Name,
			Literal { Value: string str } => str,
			_ => null
		};
	}

	private static bool IsNullOrUndefined(Node node) =>
		node switch {
			Literal { Value: null } => true,
			Identifier { Name: "undefined" } => true,
			_ => false
		};

	// Validator factory returns that are bad shapes per guidance:
	//   - boolean literal (true / false): swallows the message contract
	//   - string literal: never the expected `{ "<Type>": { message } }` shape
	//   - empty ObjectExpression `{}`: no error key, no message
	// Allowed shapes (NOT flagged):
	//   - `null` literal and `undefined` identifier — signal "no error"
	//   - numeric literals (not idiomatic but not the bad pattern guidance targets)
	//   - any non-literal expression (variable reference, call result, object with properties)
	private static bool IsBadValidatorReturnLiteral(Node node) {
		if (node is null) {
			return false;
		}
		if (node is Literal literal) {
			return literal.Value switch {
				true => true,
				false => true,
				string => true,
				_ => false
			};
		}
		if (node is ObjectExpression obj && obj.Properties.Count == 0) {
			return true;
		}
		return false;
	}

	private static bool IsContextExecuteRequest(Node callee) =>
		// matches `<obj>.$context.executeRequest` — typically `request.$context.executeRequest`,
		// but also catches handler-local aliases like `const ctx = request.$context; ctx.executeRequest(...)`
		// indirectly only when the inner property is `$context`; aliased ctx call sites that drop the
		// `$context` member walk are intentionally out of scope (would need data-flow analysis).
		callee is MemberExpression { Property: Identifier { Name: "executeRequest" }, Object: MemberExpression contextMember }
		&& contextMember.Property is Identifier { Name: "$context" };

	#endregion
}

/// <summary>
/// Severity of a single <see cref="PageBodyLintFinding"/>.
/// <see cref="Error"/> findings block the write (fail-fast, body not sent to Creatio).
/// <see cref="Warning"/> findings are reported but do not block.
/// </summary>
internal enum LintSeverity {
	Error,
	Warning
}

/// <summary>
/// One finding emitted by <see cref="PageBodyAstLinter"/>. Line and column are
/// 1-based, consistent with <see cref="PageBodySyntaxValidationResult"/>.
/// </summary>
internal readonly record struct PageBodyLintFinding(
	string Rule,
	LintSeverity Severity,
	int Line,
	int Column,
	string Message);
