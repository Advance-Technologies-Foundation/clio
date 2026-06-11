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
/// (<c>PageSchemaHandlersGuidanceResource</c>, <c>PageSchemaValidatorsGuidanceResource</c>,
/// <c>PageSchemaConvertersGuidanceResource</c>, <c>PageModificationGuidanceResource</c>).
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

	internal const string RuleHandlersMustBeArray = "handlers-must-be-array";
	internal const string RuleValidatorsMustBeObject = "validators-must-be-object";
	internal const string RuleConvertersMustBeObject = "converters-must-be-object";
	// NOTE: the `request-type-missing-Request-suffix` rule is intentionally NOT
	// shipped in this lint pass. The naive form (any `type: "crt.X"` string
	// literal that does not end in `Request`) misfires on Freedom UI component
	// types in `viewConfigDiff` entries (`"type": "crt.ComboBox"`, `"crt.Input"`,
	// `"crt.MaxLength"` — UI element types, not request dispatch payloads). A
	// correct form would bound the rule to ObjectExpressions that are the
	// argument to `request.$context.executeRequest(...)` or
	// `sdk.HandlerChainService.instance.process(...)` — that bounding is
	// deferred to a follow-up ticket so this PR ships only rules with crisp
	// AST patterns.
	internal const string RuleValidatorParamsEmpty = "validator-params-empty";
	internal const string RuleValidatorBadReturnLiteral = "validator-bad-return-literal";
	internal const string RuleConverterCrtPrefixReserved = "converter-crt-prefix-reserved";
	internal const string RuleBodyTooDeeplyNested = "body-too-deeply-nested";
	internal const string RuleHandlerUsesDeprecatedContextApi = "handler-uses-deprecated-context-api";
	internal const string RuleHandlerUsesNonexistentRequestApi = "handler-uses-nonexistent-request-api";
	internal const string RuleHandlerUsesContextExecuteRequest = "handler-uses-context-execute-request";
	internal const string RuleConverterFetchCall = "converter-fetch-call";

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
		string lines = string.Join("; ",
			errors.Select(f => $"line {f.Line}, column {f.Column}: {f.Rule} — {f.Message}"));
		return $"Page body lint failed: {lines}. The body was NOT sent to Creatio.";
	}

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
	private readonly record struct VisitContext(
		bool InsideValidators,
		bool InsideConverters,
		bool EnclosingFunctionIsValidatorInstance);

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
				break;
			case Property prop:
				CheckProperty(prop, ctx, findings);
				break;
			case CallExpression call:
				CheckCallExpression(call, ctx, findings);
				break;
			case MemberExpression member:
				CheckMemberExpression(member, findings);
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
				return currentCtx with { InsideValidators = true, EnclosingFunctionIsValidatorInstance = false };
			}
			if (key == "converters") {
				return currentCtx with { InsideConverters = true };
			}
			return currentCtx;
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
			return currentCtx with { EnclosingFunctionIsValidatorInstance = isValidatorInstance };
		}
		return currentCtx;
	}

	#endregion

	#region Rule implementations

	// Rules 1-3: detect `handlers: {...}`, `validators: [...]`, `converters: [...]`.
	// All three keys can appear at any depth (the schema return-object is the
	// canonical location, but agents occasionally nest these inside SCHEMA_*
	// markers via spread or alternative shapes). Walking every ObjectExpression
	// covers all locations without requiring a structural pre-pass.
	private static void CheckSchemaSectionShapes(ObjectExpression obj, List<PageBodyLintFinding> findings) {
		foreach (Node element in obj.Properties) {
			if (TryGetInitProperty(element, out Property prop, out string key)) {
				CheckSchemaSectionShape(prop, key, findings);
			}
		}
	}

	// Match plain init properties carrying a static key. Skips shorthand
	// methods (`handlers() { ... }`), accessors (`get handlers() { ... }`),
	// spread elements, and computed-key properties — rules 1-3 only target
	// authored static keys on init properties.
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

	private static void CheckSchemaSectionShape(Property prop, string key, List<PageBodyLintFinding> findings) {
		(string rule, string message, bool expectedShape) = key switch {
			"handlers" => (RuleHandlersMustBeArray, "`handlers` must be an array literal; an object literal here is rejected by the Freedom UI page contract", prop.Value is ArrayExpression),
			"validators" => (RuleValidatorsMustBeObject, "`validators` must be an object literal keyed by validator name; an array is rejected by the Freedom UI page contract", prop.Value is ObjectExpression),
			"converters" => (RuleConvertersMustBeObject, "`converters` must be an object literal keyed by converter name; an array is rejected by the Freedom UI page contract", prop.Value is ObjectExpression),
			_ => (null, null, true)
		};
		if (rule is null || expectedShape || IsNullOrUndefined(prop.Value)) {
			ApplyDirectChildRules(prop, key, findings);
			return;
		}
		findings.Add(new PageBodyLintFinding(
			Rule: rule,
			Severity: LintSeverity.Error,
			Line: prop.Location.Start.Line,
			Column: prop.Location.Start.Column + 1,
			Message: message));
	}

	// Section-bounded rules whose intent is "applies only to direct property
	// entries of the validators/converters object, not to nested object
	// literals". Running them off the section's ObjectExpression avoids the
	// false-positive class of "fires anywhere under the subtree" that a
	// VisitContext-only gate would suffer from.
	private static void ApplyDirectChildRules(Property prop, string key, List<PageBodyLintFinding> findings) {
		if (prop.Value is not ObjectExpression sectionObj) {
			return;
		}
		switch (key) {
			case "validators":
				CheckValidatorParamsEmptyOnDirectEntries(sectionObj, findings);
				break;
			case "converters":
				CheckConvertersDirectKeys(sectionObj, findings);
				break;
		}
	}

	// `params: []` on a custom validator's own configuration object — the
	// shape is `{ "<name>": { validator: fn, params: [...] } }`, and the
	// `params` we care about is the sibling of `validator`, not some
	// arbitrary `params: []` deep inside a factory body (e.g. a request
	// payload constructed by the validator itself). Walks the validators
	// object's direct entries and inspects each entry value's direct
	// `params` property only.
	private static void CheckValidatorParamsEmptyOnDirectEntries(ObjectExpression validatorsObj, List<PageBodyLintFinding> findings) {
		foreach (Node element in validatorsObj.Properties) {
			if (!TryGetInitProperty(element, out Property entry, out _)) {
				continue;
			}
			if (entry.Value is not ObjectExpression entryObj) {
				continue;
			}
			foreach (Node entryChild in entryObj.Properties) {
				if (!TryGetInitProperty(entryChild, out Property inner, out string innerKey) || innerKey != "params") {
					continue;
				}
				if (inner.Value is ArrayExpression arr && arr.Elements.Count == 0) {
					findings.Add(new PageBodyLintFinding(
						Rule: RuleValidatorParamsEmpty,
						Severity: LintSeverity.Error,
						Line: inner.Location.Start.Line,
						Column: inner.Location.Start.Column + 1,
						Message: "`params: []` on a custom validator is never valid; every entry requires at minimum `{ name: \"message\" }` so the user sees an error message"));
				}
			}
		}
	}

	// Custom converter names declared with the reserved `crt.*` namespace.
	// The rule applies only to keys that ARE direct entries of the
	// converters object; a lookup-table inside a converter's closure such
	// as `{ "crt.X": "label" }` is unrelated and must not be flagged.
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
			Identifier { Name: "fetch" } => true,
			MemberExpression { Property: Identifier { Name: "fetch" }, Computed: false, Object: Identifier { Name: "globalThis" } } => true,
			MemberExpression { Property: Identifier { Name: "fetch" }, Computed: false, Object: Identifier { Name: "window" } } => true,
			_ => false
		};

	private static void CheckMemberExpression(MemberExpression member, List<PageBodyLintFinding> findings) {
		// Rule 8: request API surface checks. Only flag chains rooted at `request`
		// to avoid hitting unrelated objects that share these property names.
		// Split severity:
		//   - `request.$get(...)` / `request.$set(...)` — these methods DO NOT
		//     EXIST on the handler-chain BaseRequest. Calling them throws
		//     `TypeError: request.$get is not a function` at runtime on first
		//     invocation, so the page is functionally broken — Error severity
		//     blocks the write under `handler-uses-nonexistent-request-api`.
		//   - `request.viewModel` / `.sender` / `.$context.get(...)` — legacy
		//     surfaces that may still resolve to something in some flows but
		//     are not part of the documented handler API. Warning severity
		//     under `handler-uses-deprecated-context-api`.
		if (!IsRootedAtRequest(member)) {
			return;
		}
		string propertyName = member.Property is Identifier id ? id.Name : null;
		if (propertyName is "$get" or "$set") {
			findings.Add(new PageBodyLintFinding(
				Rule: RuleHandlerUsesNonexistentRequestApi,
				Severity: LintSeverity.Error,
				Line: member.Location.Start.Line,
				Column: member.Location.Start.Column + 1,
				Message: $"`request.{propertyName}` does not exist on the handler-chain request object and throws `TypeError: request.{propertyName} is not a function` at runtime; use `await request.$context[\"<Attr>\"]` to read and `await request.$context.set(\"<Attr>\", <value>)` to write"));
			return;
		}
		string deprecatedMatch = propertyName switch {
			"viewModel" => "viewModel",
			"sender" => "sender",
			_ => null
		};
		if (deprecatedMatch is null && IsContextDotGet(member)) {
			deprecatedMatch = "$context.get";
		}
		if (deprecatedMatch is null) {
			return;
		}
		findings.Add(new PageBodyLintFinding(
			Rule: RuleHandlerUsesDeprecatedContextApi,
			Severity: LintSeverity.Warning,
			Line: member.Location.Start.Line,
			Column: member.Location.Start.Column + 1,
			Message: $"`request.{deprecatedMatch}` is deprecated in deployed page-body handlers; use the documented `request.$context[\"<Attr>\"]` reactive read/write or the `request.next?.handle(request)` chain forwarder"));
	}

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

	private static bool IsRootedAtRequest(MemberExpression member) {
		Node current = member.Object;
		while (current is MemberExpression inner) {
			current = inner.Object;
		}
		return current is Identifier { Name: "request" };
	}

	private static bool IsContextDotGet(MemberExpression member) =>
		// matches `request.$context.get`
		member.Property is Identifier { Name: "get" }
		&& member.Object is MemberExpression { Property: Identifier { Name: "$context" } };

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
