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
	internal const string RuleHandlerUsesDeprecatedContextApi = "handler-uses-deprecated-context-api";
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
		Visit(ast, default, findings);
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

	private readonly record struct VisitContext(bool InsideValidators, bool InsideConverters);

	private static void Visit(Node node, VisitContext ctx, List<PageBodyLintFinding> findings) {
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
			Visit(child, childCtx, findings);
		}
	}

	// When descending into the VALUE of a Property whose key is "validators" or
	// "converters", set the corresponding flag for the subtree. Nested validators
	// are not a documented shape so we do not pop the flag mid-walk; the
	// recursion exits naturally when each Property subtree finishes.
	private static VisitContext ComputeChildContext(Node parent, Node child, VisitContext currentCtx) {
		if (parent is not Property prop || !ReferenceEquals(child, prop.Value)) {
			return currentCtx;
		}
		string key = TryGetStaticPropertyName(prop);
		return key switch {
			"validators" => new VisitContext(InsideValidators: true, InsideConverters: currentCtx.InsideConverters),
			"converters" => new VisitContext(InsideValidators: currentCtx.InsideValidators, InsideConverters: true),
			_ => currentCtx
		};
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
			return;
		}
		findings.Add(new PageBodyLintFinding(
			Rule: rule,
			Severity: LintSeverity.Error,
			Line: prop.Location.Start.Line,
			Column: prop.Location.Start.Column + 1,
			Message: message));
	}

	private static void CheckProperty(Property prop, VisitContext ctx, List<PageBodyLintFinding> findings) {
		string key = TryGetStaticPropertyName(prop);
		if (key is null) {
			return;
		}
		// Rule 5: `params: []` — empty array literal — guidance says NEVER valid.
		if (key == "params" && prop.Value is ArrayExpression arr && arr.Elements.Count == 0) {
			findings.Add(new PageBodyLintFinding(
				Rule: RuleValidatorParamsEmpty,
				Severity: LintSeverity.Error,
				Line: prop.Location.Start.Line,
				Column: prop.Location.Start.Column + 1,
				Message: "`params: []` is never valid; every custom validator requires at minimum `{ name: \"message\" }` so the user sees an error message"));
		}
		// Rule 7: custom converter key uses reserved `crt.*` namespace. Bounded
		// to keys that sit directly inside the `converters` object via the
		// VisitContext flag set by ComputeChildContext.
		if (ctx.InsideConverters && key.StartsWith("crt.", StringComparison.Ordinal)) {
			findings.Add(new PageBodyLintFinding(
				Rule: RuleConverterCrtPrefixReserved,
				Severity: LintSeverity.Error,
				Line: prop.Location.Start.Line,
				Column: prop.Location.Start.Column + 1,
				Message: $"Custom converter `{key}` uses the reserved `crt.*` namespace; only Creatio built-in converters may use this prefix"));
		}
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
		// Rule 8: deprecated request API surface (request.viewModel / .sender /
		// .$get / .$set / .$context.get). Only flag chains rooted at `request`
		// to avoid hitting unrelated objects that share these property names.
		if (!IsRootedAtRequest(member)) {
			return;
		}
		string match = (member.Property is Identifier id ? id.Name : null) switch {
			"viewModel" => "viewModel",
			"sender" => "sender",
			"$get" => "$get",
			"$set" => "$set",
			_ => null
		};
		if (match is null && IsContextDotGet(member)) {
			match = "$context.get";
		}
		if (match is null) {
			return;
		}
		findings.Add(new PageBodyLintFinding(
			Rule: RuleHandlerUsesDeprecatedContextApi,
			Severity: LintSeverity.Warning,
			Line: member.Location.Start.Line,
			Column: member.Location.Start.Column + 1,
			Message: $"`request.{match}` is deprecated in deployed page-body handlers; use the documented `request.$context.<Attr>` reactive read/write or the `request.next?.handle(request)` chain forwarder"));
	}

	private static void CheckReturnStatement(ReturnStatement ret, VisitContext ctx, List<PageBodyLintFinding> findings) {
		// Rule 6: validator declaration must not return a literal. Bounded via
		// VisitContext.InsideValidators so the rule only fires inside the
		// `validators` schema section — a plain `return true` elsewhere is fine.
		if (!ctx.InsideValidators) {
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
