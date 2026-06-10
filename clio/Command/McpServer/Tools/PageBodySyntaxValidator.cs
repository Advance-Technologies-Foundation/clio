using System;
using System.Threading;
using Acornima;
using Acornima.Ast;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Deterministic JavaScript syntax check applied to a Freedom UI page body before
/// it reaches <see cref="PageBodySamplingService"/> and before it is persisted.
///
/// Background (ENG-89796): the LLM-based semantic review covers cross-section
/// references but does NOT check syntax. A real production incident wrote a body
/// containing <c>await request.$context.X = Y</c> (an <c>await</c> expression
/// cannot be an assignment target → <c>SyntaxError</c>); <c>update-page</c>
/// reported success because the body was never parsed, and the page failed to
/// load only in the browser. The same gap applies to every syntax-level failure
/// (unbalanced braces, broken template literals, mismatched <c>SCHEMA_*</c>
/// marker pairs, etc.) — all deterministic and detectable by a parser in ms.
///
/// Library choice rationale lives in <c>spec/adr/adr-ENG-89796-page-body-syntax-validator.md</c>.
/// Short version: Acornima 1.6.2 — BSD-3-Clause, pure C#, no native deps, ES2022+
/// spec coverage, active successor to the archived Esprima.NET (same author),
/// internally used by Jint. Rejected alternatives: Esprima.NET (archived),
/// Jint full interpreter (heavy, uses Acornima anyway), ClearScript / V8
/// (native binaries hostile to a global dotnet tool), NiL.JS / YantraJS
/// (niche), Node subprocess (adds a Node runtime dependency), Microsoft.JScript
/// (deprecated), hand-written regex scanner (JS grammar is context-sensitive).
/// </summary>
internal static class PageBodySyntaxValidator {

	// Acornima.Parser instances are documented as not thread-safe. Validate is invoked
	// from BOTH (a) the McpToolExecutionLock-guarded write section AND (b) the pre-write
	// MCP handler body that runs OUTSIDE that lock (e.g. PageSyncTool's pre-sampling
	// syntax pass and PageUpdateTool's pre-ValidateBody check). A static singleton
	// would race in path (b). A ThreadLocal slot gives every calling thread its own
	// reusable Parser — zero contention, allocation amortised across calls on the
	// same thread, and correct under any concurrency the MCP server may introduce.
	private static readonly ThreadLocal<Parser> ParserSlot = new(() => new Parser());

	/// <summary>
	/// Parses <paramref name="body"/> as a JavaScript Script and returns either
	/// <see cref="PageBodySyntaxValidationResult.Valid"/> or a failure result
	/// carrying the line, column, and message of the first syntax error.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Uses <see cref="Parser.ParseScript(string,bool,string?)"/> — Creatio page bodies
	/// are AMD modules wrapped in <c>define(...)</c>, which parses cleanly as a
	/// top-level <c>ExpressionStatement</c>. <c>ParseModule</c> is intentionally NOT
	/// used because it imposes the strict-mode + import/export grammar that page
	/// bodies do not satisfy (top-level <c>this</c>, AMD <c>define</c> call, etc.).
	/// </para>
	/// <para>
	/// Empty / whitespace-only / <c>null</c> bodies parse as an empty Script (zero
	/// body nodes) and are reported as valid here. Higher layers separately reject
	/// empty bodies before this method is invoked, so the empty-body short-circuit
	/// is not the validator's responsibility.
	/// </para>
	/// <para>
	/// Performance: a 5 KB body parses in &lt;5 ms warm and a 50 KB body well under
	/// the AC's &lt;50 ms budget (Day-0 probe: 50 KB comment-only body parsed in
	/// &lt;1 ms). The shared parser instance amortises allocation across calls.
	/// </para>
	/// </remarks>
	public static PageBodySyntaxValidationResult Validate(string body) =>
		ValidateAndParse(body, out _);

	/// <summary>
	/// Same contract as <see cref="Validate"/> but additionally yields the parsed
	/// <see cref="Script"/> AST on success so that downstream consumers (e.g.
	/// <see cref="PageBodyAstLinter"/>) can reuse it without re-parsing the body.
	/// </summary>
	/// <remarks>
	/// On failure <paramref name="ast"/> is set to <c>null</c>. On success it carries
	/// the AST root returned by <see cref="Parser.ParseScript(string,bool,string?)"/>.
	/// </remarks>
	public static PageBodySyntaxValidationResult ValidateAndParse(string body, out Script ast) {
		ast = null;
		if (body is null) {
			return PageBodySyntaxValidationResult.Valid;
		}
		try {
			ast = ParserSlot.Value!.ParseScript(body);
			return PageBodySyntaxValidationResult.Valid;
		} catch (SyntaxErrorException ex) {
			// Acornima reports 1-based line and 0-based column. Normalise the column
			// to 1-based so the {line, column} pair is consistent with what most
			// JavaScript tooling (Node, Babel, ESLint) reports and what a Creatio
			// developer will see in their browser's DevTools.
			return PageBodySyntaxValidationResult.Invalid(
				line: ex.LineNumber,
				column: ex.Column + 1,
				message: ex.Description ?? ex.Message);
		}
	}

	/// <summary>
	/// Renders a failure <see cref="PageBodySyntaxValidationResult"/> as the canonical
	/// agent-facing error string used by every MCP write path that wraps this validator
	/// (<c>update-page</c>, <c>sync-pages</c>). Centralising the format here keeps the
	/// wire contract stable across tools — drift between call sites would silently change
	/// what callers and the `Contain(...)` test assertions expect.
	/// </summary>
	public static string FormatError(PageBodySyntaxValidationResult result) {
		if (result.IsValid) {
			throw new ArgumentException("FormatError is only meaningful for invalid results.", nameof(result));
		}
		return $"JavaScript syntax error at line {result.Line}, column {result.Column}: " +
			$"{result.Message}. The body was NOT sent to Creatio.";
	}
}

/// <summary>
/// Result of <see cref="PageBodySyntaxValidator.Validate"/>. On failure the line
/// and column are 1-based and the message is the parser's human-readable
/// description of the first syntax error encountered.
/// </summary>
internal readonly record struct PageBodySyntaxValidationResult(
	bool IsValid,
	int Line,
	int Column,
	string Message) {

	public static PageBodySyntaxValidationResult Valid { get; } =
		new(IsValid: true, Line: 0, Column: 0, Message: null);

	public static PageBodySyntaxValidationResult Invalid(int line, int column, string message) =>
		new(IsValid: false, Line: line, Column: column, Message: message);
}
