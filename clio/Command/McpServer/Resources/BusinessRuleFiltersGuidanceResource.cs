using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides the canonical AI-facing contract for the apply-static-filter action's friendly filter.
/// Split out of the general business-rules guide so a filter task loads only the filter contract.
/// </summary>
[McpServerResourceType]
public sealed class BusinessRuleFiltersGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/business-rule-filters";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Returns the canonical guidance article for the apply-static-filter friendly filter contract.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "business-rule-filters-guidance")]
	[Description("Returns canonical MCP guidance for the apply-static-filter friendly filter contract: leaf comparisons, lookup values, forward paths, nested groups, backward EXISTS/NOT_EXISTS and COUNT/SUM/AVG/MIN/MAX aggregations, relative-date and current-user macros, age/birthday translation, multilingual handling, and the discovery flow.")]
	public ResourceContents GetGuide() => Guide;

	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP apply-static-filter — friendly filter contract

		       Scope
		       - This is the full filter contract for the `apply-static-filter` entity business-rule action.
		       - For routing (when to use apply-static-filter vs other actions) and the action shape, see the `business-rules` guide.
		       - `apply-static-filter` narrows a target lookup by a static ESQ filter; `rootSchemaName` is inferred from the target lookup's reference schema — never sent by the caller.

		       action shape
		       - { "type": "apply-static-filter", "targetAttribute": "<EntityLookupAttribute>", "filter": { ... } }

		       filter group fields
		       - `logicalOperation`: "AND" or "OR" (required).
		       - `filters`: array of leaf conditions on the lookup's reference schema.
		       - `groups`: nested filter groups for (A AND B) OR (A AND C) compositions.
		       - `backwardReferenceFilters`: array of clauses against child schemas pointing back at the lookup root via a lookup column. Each clause is either an EXISTS/NOT_EXISTS existence check OR a COUNT/SUM/AVG/MIN/MAX aggregation threshold.

		       leaf fields
		       - `columnPath`: rooted at the TARGET lookup's reference schema (NOT the rule's entity). Example: when filtering a `City` lookup, columnPath segments resolve on the `City` schema. Forward-paths through Lookup chains supported (e.g. "Country.Name").
		       - `comparisonType`: UPPER_SNAKE_CASE tokens, DISTINCT from the kebab-case tokens used in the rule's condition group (`equal`, `is-filled-in`, ...). Allowed: EQUAL, NOT_EQUAL, IS_NULL, IS_NOT_NULL, GREATER, GREATER_OR_EQUAL, LESS, LESS_OR_EQUAL, CONTAIN, NOT_CONTAIN, START_WITH, NOT_START_WITH, END_WITH, NOT_END_WITH.
		       - To require that a field is filled, use IS_NOT_NULL on that column directly. Do NOT build a backward EXISTS as a workaround for a simple "is filled" check.
		       - `value`: omitted for IS_NULL/IS_NOT_NULL; scalar JSON for other tokens; JSON array of strings allowed only on Lookup columns with EQUAL/NOT_EQUAL (multi-value IN).
		       - `valueMacros`: dynamic value instead of a constant; mutually exclusive with `value`. Use for relative dates and current-user filters. Date macros (Yesterday, Today, Tomorrow, PreviousWeek/CurrentWeek/NextWeek, ...Month, ...Quarter, ...HalfYear, ...Year, ...Hour) require a Date/DateTime/Time column. CurrentUser and CurrentUserContact require a Lookup column and EQUAL/NOT_EQUAL. N-style macros (NextNDays, PreviousNDays, NextNHours, PreviousNHours) require `valueMacrosArgument` (positive integer). Example: limit Activity by "due in the next 5 days" → { columnPath: "DueDate", comparisonType: "LESS_OR_EQUAL", valueMacros: "NextNDays", valueMacrosArgument: 5 }.

		       lookup values
		       - GUID string is used directly; non-GUID strings are resolved against the lookup's primary display column (no-match and ambiguous matches are rejected).

		       non-English / localized prompts
		       - The friendly contract is language-neutral — map natural-language terms to platform `Name` columns and UPPER_SNAKE_CASE tokens regardless of prompt language (e.g. ES "cuentas"→Account, "actividades"→Activity, "responsable"→the Assignee lookup, "este año"→CurrentYear macros, "más de 10"→COUNT GREATER 10). Schema/column resolution and macros are unaffected by language. The ONLY language-sensitive step is resolving a non-GUID lookup VALUE: matching is exact against the stored display value, so a localized term (e.g. "Estados Unidos" when the record is stored as "United States") will fail. For any lookup value that the user phrased in a non-English (or uncertain) form, call `odata-read` first to find the actual stored value or its Id, then pass that exact value or the GUID.

		       backward reference shape
		       - `"referenceColumnPath": "[ChildSchema:LinkColumn]"`, `"comparisonType": "EXISTS" | "NOT_EXISTS"`, optional nested `"filter": { ... }`.
		       - The bracket form is BARE: `[ChildSchema:LinkColumn]` with NO `.Id` suffix and NO trailing column segment. The builder appends `.Id` and stamps the platform-canonical EXISTS shape (`dataValueType=Integer`, `isAggregative=true`) for you. Writing `[Contact:Account].Id` in the friendly contract is rejected by structural validation.
		       - Bracket notation `[Schema:Column]` is used ONLY for the main relationship at `referenceColumnPath`. Inside the nested `filter` you are already in the child-schema context — reference its columns directly (e.g. `"columnPath": "Status"`), NEVER prefix with the bracket form, NEVER nest another backward reference / exists inside it.
		       - Mix forward and backward freely: a leaf condition on a forward Lookup column (e.g. `Type EQUAL "Customer"`) belongs in `filters`, while existence checks against child schemas (e.g. "no Leads") belong in `backwardReferenceFilters`. Both can coexist in the same group under the same logical operator.
		       - Worked example — "limit Account lookup to accounts that have at least one Contact": targetAttribute=`Account`, filter=`{ "logicalOperation": "AND", "backwardReferenceFilters": [ { "referenceColumnPath": "[Contact:Account]", "comparisonType": "EXISTS" } ] }`. The link column is the Lookup on `Contact` that points back to `Account` (here named `Account`); the builder emits `leftExpression.columnPath = "[Contact:Account].Id"` on the wire.

		       backward reference AGGREGATION shape (counts/sums)
		       - Add `"aggregationType": "COUNT" | "SUM" | "AVG" | "MIN" | "MAX"`, a relational/equality `"comparisonType"` (GREATER, GREATER_OR_EQUAL, LESS, LESS_OR_EQUAL, EQUAL, NOT_EQUAL), and a numeric `"aggregationValue"`. Do NOT use EXISTS/NOT_EXISTS together with aggregationType.
		       - COUNT counts child rows — OMIT `aggregationColumnPath`. SUM/AVG/MIN/MAX aggregate a numeric child column — `aggregationColumnPath` is REQUIRED and must resolve to a numeric column on the child schema.
		       - "more than N <children>" / "at least N" / "fewer than N" → COUNT with the matching comparison. Worked example — "contacts with more than 10 activities": targetAttribute=`<ContactLookup>`, filter=`{ "logicalOperation": "AND", "backwardReferenceFilters": [ { "referenceColumnPath": "[Activity:Contact]", "aggregationType": "COUNT", "comparisonType": "GREATER", "aggregationValue": 10 } ] }`. ">10" → GREATER 10; "at least 10" → GREATER_OR_EQUAL 10.
		       - An optional nested `"filter"` on the child schema narrows what is counted/summed (e.g. count only completed activities).
		       - Out of scope: date-valued aggregation thresholds (e.g. "most recent order date") — aggregationValue must be a number.

		       translating natural-language temporal/age phrases
		       - "created/modified this year / current week / last quarter / today / tomorrow / next 5 days" → relative-date macros via `valueMacros` on CreatedOn/ModifiedOn or the relevant Date column (CurrentYear, CurrentWeek, PreviousQuarter, Today, Tomorrow, NextNDays+arg). Do NOT hardcode calendar dates for relative phrases.
		       - "birthday today/tomorrow" (day-and-month match ignoring year) → `valueMacros: "DayOfYearTodayPlusDaysOffset"` with `valueMacrosArgument` = day offset (0=today, 1=tomorrow), applied to whichever Date column actually holds the birth date on the reference schema (confirm its name first; do not assume it is called BirthDate). This is NOT the same as `Today`/`Tomorrow` (which match the full date including year).
		       - "age = N" / "age < N" / "aged between X and Y": resolve the reference schema's columns FIRST (get-entity-schema-properties or dataforge-get-table-columns) and decide from what actually exists — never assume. (a) If a numeric age column exists, filter it directly — "age = N" → <AgeColumn> EQUAL N, "age < N" → <AgeColumn> LESS N, "aged between X and Y" → <AgeColumn> GREATER_OR_EQUAL X AND LESS_OR_EQUAL Y. (b) Otherwise, if a birth-date column exists, translate to a date range: resolve today's date (call GetCurrentDateTime if unsure), then <BirthDateColumn> GREATER_OR_EQUAL (today − Y years) AND LESS_OR_EQUAL (today − X years) for "between X and Y"; "age = N" → a one-year window; "age < N" → <BirthDateColumn> GREATER (today − N years). Emit fixed ISO date constants (column arithmetic inside filter values is not supported). Pick the real column names from the schema metadata, not from these placeholders.

		       discovery flow (ALWAYS, no assumptions)
		       - Before building any filter, resolve the target lookup's reference schema and confirm every columnPath segment and its data value type with `get-entity-schema-properties` (or `dataforge-get-table-columns`). Do not assume a column exists, is absent, or has a given type from its name or from prior schemas — verify against THIS environment's schema. The schema-aware validator rejects unknown columns and type-mismatched comparisons, so resolving first avoids a failed call. Use `find-entity-schema` to locate the schema and `odata-read` to resolve any non-GUID lookup value to a real Id.
		       """
	};
}
