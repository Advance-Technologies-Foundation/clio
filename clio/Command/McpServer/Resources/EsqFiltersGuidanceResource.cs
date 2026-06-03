using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for authoring ESQ-style filters through clio MCP.
/// </summary>
[McpServerResourceType]
public sealed class EsqFiltersGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/esq-filters";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Canonical guidance article accessible by name through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP ESQ filters guide

		       Scope
		       - Use this guide whenever you are authoring static or generated ESQ-style filters for widgets, page payloads, lookup narrowing, or declarative business-rule actions.
		       - The goal is to avoid frequent filter-generation defects: wrong column paths, wrong lookup value shape, wrong date handling, and unsupported assumptions about range or relationship filters.
		       - Read this guide before writing a filter when the requirement mentions lookup values, date-relative wording, path traversal, or nested/related-record conditions.

		       Column-path normalization
		       - Treat lookup path segments without a trailing `Id` as the canonical runtime form.
		       - When a business requirement or source example says `CreatedById`, `AccountId`, `OwnerId`, or `[Contact:AccountId]`, write the filter path as `CreatedBy`, `Account`, `Owner`, or `[Contact:Account]` unless the target contract explicitly requires the raw `...Id` form.
		       - Apply the same normalization to forward paths and backward-reference brackets.
		       - Examples:
		         - `CreatedById` -> `CreatedBy`
		         - `Account.OwnerId` -> `Account.Owner`
		         - `[Contact:AccountId].Id` -> `[Contact:Account].Id`
		       - Frequent mistake: copying schema column names with `Id` suffix directly from DB-oriented thinking into runtime filter payloads. For authoring guidance, prefer normalized object-path semantics.

		       Compare-filter conversion guidance
		       - A normal scalar compare filter should use a schema-column left expression and a parameter right expression.
		       - Infer parameter type conservatively:
		         - boolean literal -> boolean
		         - integer literal -> integer
		         - float/decimal literal -> float/number
		         - plain text -> text/string
		         - ISO date/time string -> date or datetime, not text
		       - If the comparison value is a date/datetime, author the filter as a date-aware comparison rather than a text comparison.
		       - For date/datetime comparisons, trim-to-date behavior may be required by the consuming runtime when the requirement is date-based rather than exact timestamp-based.
		       - Frequent mistake: treating `"2026-05-28"` as text and generating a string comparison instead of a date comparison.

		       Lookup-filter conversion guidance
		       - Prefer a resolved lookup GUID as the comparison value for static lookup filters.
		       - If the lookup value is known only by display text, resolve it first; do not guess or fabricate GUIDs.
		       - If GUID resolution remains ambiguous or unavailable, fall back to a string comparison on the lookup display path, for example `CreatedBy.Name`, `Country.Name`, or `Account.Owner.Name`.
		       - Treat single-value and multi-value lookup filters as the same conceptual family: a lookup filter may need one resolved value or several resolved values.
		       - For multi-value lookup requirements such as "USA or UK", keep the values together as one lookup membership filter instead of expanding them into ad-hoc duplicated scalar filters unless the target contract explicitly requires that expansion.
		       - Frequent mistakes:
		         - putting raw business text like `"Supervisor"` into a lookup-id slot
		         - mixing GUID and display-name values in one filter
		         - using `CONTAIN` on a lookup-id field instead of filtering by GUID or by display-name path
		         - inventing a reference schema name without confirming the lookup target

		       Relative-date conversion guidance
		       - Requirements like "this year", "previous month", "next 7 days", "today", "tomorrow", "anniversary today", or "exact time 14:30" should be authored as relative-date/date-part semantics, not as plain text equality.
		       - Distinguish these families:
		         - period macros: previous/current/next hour/day/week/month/quarter/halfyear/year
		         - parameterized relative windows: previous/next N hours or N days
		         - date-part checks: day-of-week, day-of-month, month, exact year
		         - exact time-of-day checks
		       - When the requirement clearly expresses date math or calendar semantics, do not degrade it to a plain compare filter with a text literal.
		       - Exact time comparisons can be timezone-sensitive. If the filter is intended to match user-facing local time, preserve that intent instead of assuming server-local interpretation.
		       - Frequent mistakes:
		         - generating `"CURRENT_YEAR"` as a string parameter instead of a relative-date expression
		         - turning "within next 7 days" into `GREATER_OR_EQUAL now` plus a hand-built text date
		         - losing timezone intent for exact time-of-day comparisons

		       Authoring rules for common problem cases
		       - `BETWEEN` is usually not one atomic compare leaf. Model it as lower bound plus upper bound joined by `AND`, unless the target surface has a first-class range shape.
		       - If the requirement references child records or "entities that have related X", do not flatten it into a fake scalar path. Use the target surface's backward-reference / EXISTS semantics.
		       - If the target surface accepts nested groups, preserve explicit `AND` / `OR` grouping from the requirement; do not silently collapse everything into one flat list.
		       - If the logical operation is absent or unclear, do not assume a permissive OR unless the requirement actually reads that way. Clarify from context or preserve the tool's required default only when the contract mandates one.

		       Verification checklist
		       - Confirm that every lookup path points to the intended reference schema.
		       - Confirm that every GUID-backed lookup value is real and not fabricated.
		       - Confirm that date-like strings are treated as dates, not text.
		       - Confirm that `Id`-suffixed paths have been normalized when the runtime expects object paths.
		       - Confirm that child-entity conditions use backward-reference semantics when needed.

		       Related guidance
		       - Read `indicator-widget` when the filter is part of a `crt.IndicatorWidget` aggregate payload.
		       - Read `business-rules` when the filter is being authored for `apply-static-filter` or another declarative rule action.
		       - Read `page-modification` for page-body save mechanics after the filter shape is decided.
		       """
	};

	/// <summary>
	/// Returns the canonical guidance article for ESQ-style filter authoring.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "esq-filters-guidance")]
	[Description("Returns canonical MCP guidance for ESQ-style filter authoring, including normalized column paths, lookup-value handling, and relative-date pitfalls.")]
	public ResourceContents GetGuide() => Guide;
}
