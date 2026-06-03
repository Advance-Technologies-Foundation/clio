using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for creating Freedom UI business rules through clio MCP.
/// </summary>
[McpServerResourceType]
public sealed class BusinessRulesGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/business-rules";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Returns the canonical guidance article for Freedom UI business rules.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "business-rules-guidance")]
	[Description("Returns canonical MCP guidance for Freedom UI business rules: entity-level and page-level conditional logic for field/element visibility, editability, required state, and value assignment.")]
	public ResourceContents GetGuide() => Guide;

	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP business rules guide

		       Scope
		       - Use this guide whenever the requirement involves conditional field/element visibility, editability, required state, auto-assignment of a field value, CLEARING a field value, or lookup filtering.
		       - Writing into a column or clearing a column when another field changes IS a business rule. This is the most common case for two interdependent fields (changing field A auto-fills or wipes field B). Do NOT narrow business rules to only show/hide/enable/require — value population and clearing are first-class business-rule actions (`set-values`), no handler required.
		       - Business rules are SEPARATE first-class artifacts in Creatio Freedom UI. They are NOT implemented as page JavaScript code, handlers, validators, or converters.
		       - Do NOT write JavaScript handler or validator code to implement business-rule behavior. Use the dedicated MCP tools instead.

		       What is a business rule
		       - A business rule is a declarative condition → action definition that Creatio evaluates at runtime.
		       - It consists of a CONDITION GROUP (AND/OR of field-value comparisons) and one or more ACTIONS that fire when the condition is met.
		       - Business rules are created via dedicated MCP tools, not by editing page schema bodies or writing JavaScript.

		       Required MCP contract check
		       - Before creating or modifying business rules (calling `create-entity-business-rule`, `create-page-business-rule`, etc) call `get-tool-contract` to get more information about contract and see examples.

		       State-changing actions are one-way
		       - Actions that change field or element state (visibility, editability, or required state) apply only when their condition is met.
		       - A business rule does not automatically roll back state or apply the inverse action when its condition stops matching.
		       - When a requirement describes state that must switch in both directions, create an explicit inverse business rule for the opposite condition and corresponding opposite action.

		       Two levels of business rules

		       1. Entity-level business rules
		          - Scope: operate on entity schema attributes (columns).
		          - Tool: `create-entity-business-rule`
		          - Supported actions: make-editable, make-read-only, make-required, make-optional, set-values, apply-filter, apply-static-filter.
		          - Use when the rule should apply everywhere the entity is used, regardless of which page displays it.
		          - `apply-filter` targets one lookup field, compares it to one source lookup field on the current record, and may auto-generate child clear/populate rules.
		          - `apply-static-filter` narrows a target lookup by a static ESQ filter expressed in a friendly contract (constants, lookup values by GUID or display name, AND/OR groups, EXISTS/NOT_EXISTS backward references). `rootSchemaName` is inferred from the target lookup's reference schema — never sent by the caller.
		          - When the requirement sounds like a standard dependent lookup UX, prefer `populateValue=true` by default unless the user explicitly asks for one-way filtering only or the selected source/target path shape makes populate unsupported.

		       apply-static-filter friendly filter contract
		       - action shape:
		         { "type": "apply-static-filter", "targetAttribute": "<EntityLookupAttribute>", "filter": { ... } }
		       - filter group fields:
		         - `logicalOperation`: "AND" or "OR" (required).
		         - `filters`: array of leaf conditions on the lookup's reference schema.
		         - `groups`: nested filter groups for (A AND B) OR (A AND C) compositions.
		         - `backwardReferenceFilters`: array of clauses against child schemas pointing back at the lookup root via a lookup column. Each clause is either an EXISTS/NOT_EXISTS existence check OR a COUNT/SUM/AVG/MIN/MAX aggregation threshold.
		       - leaf fields:
		         - `columnPath`: rooted at the TARGET lookup's reference schema (NOT the rule's entity). Example: when filtering a `City` lookup, columnPath segments resolve on the `City` schema. Forward-paths through Lookup chains supported (e.g. "Country.Name").
		         - `comparisonType`: UPPER_SNAKE_CASE tokens, DISTINCT from the kebab-case tokens used in the rule's condition group (`equal`, `is-filled-in`, ...). Allowed: EQUAL, NOT_EQUAL, IS_NULL, IS_NOT_NULL, GREATER, GREATER_OR_EQUAL, LESS, LESS_OR_EQUAL, CONTAIN, NOT_CONTAIN, START_WITH, NOT_START_WITH, END_WITH, NOT_END_WITH.
		         - To require that a field is filled, use IS_NOT_NULL on that column directly. Do NOT build a backward EXISTS as a workaround for a simple "is filled" check.
		         - `value`: omitted for IS_NULL/IS_NOT_NULL; scalar JSON for other tokens; JSON array of strings allowed only on Lookup columns with EQUAL/NOT_EQUAL (multi-value IN).
		         - `valueMacros`: dynamic value instead of a constant; mutually exclusive with `value`. Use for relative dates and current-user filters. Date macros (Yesterday, Today, Tomorrow, PreviousWeek/CurrentWeek/NextWeek, ...Month, ...Quarter, ...HalfYear, ...Year, ...Hour) require a Date/DateTime/Time column. CurrentUser and CurrentUserContact require a Lookup column and EQUAL/NOT_EQUAL. N-style macros (NextNDays, PreviousNDays, NextNHours, PreviousNHours) require `valueMacrosArgument` (positive integer). Example: limit Activity by "due in the next 5 days" → { columnPath: "DueDate", comparisonType: "LESS_OR_EQUAL", valueMacros: "NextNDays", valueMacrosArgument: 5 }.
		       - lookup values: GUID string is used directly; non-GUID strings are resolved against the lookup's primary display column (no-match and ambiguous matches are rejected).
		       - non-English / localized prompts: the friendly contract is language-neutral — map natural-language terms to platform `Name` columns and UPPER_SNAKE_CASE tokens regardless of prompt language (e.g. ES "cuentas"→Account, "actividades"→Activity, "responsable"→the Assignee lookup, "este año"→CurrentYear macros, "más de 10"→COUNT GREATER 10). Schema/column resolution and macros are unaffected by language. The ONLY language-sensitive step is resolving a non-GUID lookup VALUE: matching is exact against the stored display value, so a localized term (e.g. "Estados Unidos" when the record is stored as "United States") will fail. For any lookup value that the user phrased in a non-English (or uncertain) form, call `odata-read` first to find the actual stored value or its Id, then pass that exact value or the GUID.
		       - backward reference shape: `"referenceColumnPath": "[ChildSchema:LinkColumn]"`, `"comparisonType": "EXISTS" | "NOT_EXISTS"`, optional nested `"filter": { ... }`.
		         - The bracket form is BARE: `[ChildSchema:LinkColumn]` with NO `.Id` suffix and NO trailing column segment. The builder appends `.Id` and stamps the platform-canonical EXISTS shape (`dataValueType=Integer`, `isAggregative=true`) for you. Writing `[Contact:Account].Id` in the friendly contract is rejected by structural validation.
		         - Bracket notation `[Schema:Column]` is used ONLY for the main relationship at `referenceColumnPath`. Inside the nested `filter` you are already in the child-schema context — reference its columns directly (e.g. `"columnPath": "Status"`), NEVER prefix with the bracket form, NEVER nest another backward reference / exists inside it.
		         - Mix forward and backward freely: a leaf condition on a forward Lookup column (e.g. `Type EQUAL "Customer"`) belongs in `filters`, while existence checks against child schemas (e.g. "no Leads") belong in `backwardReferenceFilters`. Both can coexist in the same group under the same logical operator.
		         - Worked example — "limit Account lookup to accounts that have at least one Contact": targetAttribute=`Account`, filter=`{ "logicalOperation": "AND", "backwardReferenceFilters": [ { "referenceColumnPath": "[Contact:Account]", "comparisonType": "EXISTS" } ] }`. The link column is the Lookup on `Contact` that points back to `Account` (here named `Account`); the builder emits `leftExpression.columnPath = "[Contact:Account].Id"` on the wire.
		       - backward reference AGGREGATION shape (counts/sums): add `"aggregationType": "COUNT" | "SUM" | "AVG" | "MIN" | "MAX"`, a relational/equality `"comparisonType"` (GREATER, GREATER_OR_EQUAL, LESS, LESS_OR_EQUAL, EQUAL, NOT_EQUAL), and a numeric `"aggregationValue"`. Do NOT use EXISTS/NOT_EXISTS together with aggregationType.
		         - COUNT counts child rows — OMIT `aggregationColumnPath`. SUM/AVG/MIN/MAX aggregate a numeric child column — `aggregationColumnPath` is REQUIRED and must resolve to a numeric column on the child schema.
		         - "more than N <children>" / "at least N" / "fewer than N" → COUNT with the matching comparison. Worked example — "contacts with more than 10 activities": targetAttribute=`<ContactLookup>`, filter=`{ "logicalOperation": "AND", "backwardReferenceFilters": [ { "referenceColumnPath": "[Activity:Contact]", "aggregationType": "COUNT", "comparisonType": "GREATER", "aggregationValue": 10 } ] }`. ">10" → GREATER 10; "at least 10" → GREATER_OR_EQUAL 10.
		         - An optional nested `"filter"` on the child schema narrows what is counted/summed (e.g. count only completed activities).
		         - Out of scope: date-valued aggregation thresholds (e.g. "most recent order date") — aggregationValue must be a number.
		       - translating natural-language temporal/age phrases:
		         - "created/modified this year / current week / last quarter / today / tomorrow / next 5 days" → relative-date macros via `valueMacros` on CreatedOn/ModifiedOn or the relevant Date column (CurrentYear, CurrentWeek, PreviousQuarter, Today, Tomorrow, NextNDays+arg). Do NOT hardcode calendar dates for relative phrases.
		         - "birthday today/tomorrow" (day-and-month match ignoring year) → `valueMacros: "DayOfYearTodayPlusDaysOffset"` with `valueMacrosArgument` = day offset (0=today, 1=tomorrow), applied to whichever Date column actually holds the birth date on the reference schema (confirm its name first; do not assume it is called BirthDate). This is NOT the same as `Today`/`Tomorrow` (which match the full date including year).
		         - "age = N" / "age < N" / "aged between X and Y": resolve the reference schema's columns FIRST (get-entity-schema-properties or dataforge-get-table-columns) and decide from what actually exists — never assume. (a) If a numeric age column exists, filter it directly — "age = N" → <AgeColumn> EQUAL N, "age < N" → <AgeColumn> LESS N, "aged between X and Y" → <AgeColumn> GREATER_OR_EQUAL X AND LESS_OR_EQUAL Y. (b) Otherwise, if a birth-date column exists, translate to a date range: resolve today's date (call GetCurrentDateTime if unsure), then <BirthDateColumn> GREATER_OR_EQUAL (today − Y years) AND LESS_OR_EQUAL (today − X years) for "between X and Y"; "age = N" → a one-year window; "age < N" → <BirthDateColumn> GREATER (today − N years). Emit fixed ISO date constants (column arithmetic inside filter values is not supported). Pick the real column names from the schema metadata, not from these placeholders.
		       - discovery flow (ALWAYS, no assumptions): before building any filter, resolve the target lookup's reference schema and confirm every columnPath segment and its data value type with `get-entity-schema-properties` (or `dataforge-get-table-columns`). Do not assume a column exists, is absent, or has a given type from its name or from prior schemas — verify against THIS environment's schema. The schema-aware validator rejects unknown columns and type-mismatched comparisons, so resolving first avoids a failed call. Use `find-entity-schema` to locate the schema and `odata-read` to resolve any non-GUID lookup value to a real Id.

		       2. Page-level business rules
		          - Scope: operate on page elements (UI controls from viewConfig) and page attributes (from viewModelConfig).
		          - Tool: `create-page-business-rule`
		          - Supported actions: hide-element, show-element, make-editable, make-read-only, make-required, make-optional.
		          - Use when the rule should apply only on a specific page.
		          - Element names come from `get-page` bundle.viewConfig (recursive). Attribute names come from bundle.viewModelConfig.attributes.

		       Decision tree — when to use business rules vs handlers/validators
		       - If the requirement is "when field X equals Y, then hide/show/enable/disable/require/unrequire field Z" (the field itself appears/disappears or becomes editable/required) → use a BUSINESS RULE with hide-element/show-element/make-* actions.
		       - If the requirement is "when field X equals Y, set field Z to value W" → use an ENTITY BUSINESS RULE with set-values action. Do NOT write a handler for this.
		       - If the requirement is "when field X equals Y, clear field Z" → use an ENTITY BUSINESS RULE with set-values action and an empty value for Z. Clearing a field is the same action as setting one — NO handler is needed.
		       - If the requirement is "filter lookup A by the current value of another lookup B on the same record (dependent lookups)" → use an ENTITY BUSINESS RULE with apply-filter action.
		       - If the requirement is "limit / restrict a lookup field to records matching a fixed condition" (e.g. "let users select only contacts that have a mobile phone", "show only accounts where Type = Customer", "only active users", "show only contacts in the Assignee field who ...", "show the <Field> field only for <records> where ...", "show only <records> that have at least one / more than N <children>") → use an ENTITY BUSINESS RULE with apply-static-filter action. The condition can be unconditional (always filter) or gated: "WHEN field X is Y, limit lookup Z to ..." → put X=Y in the rule's condition group and the apply-static-filter action on Z.
		         - DISAMBIGUATION: this is NOT the same as the page DataSource `staticFilters` / `filterConfig` array inside a Freedom UI `body.js`. To restrict what a lookup/field shows, do NOT hand-edit `body.js`, `filterConfig`, `dataSourceFilters`, or `modelConfig` — use `create-entity-business-rule` with apply-static-filter. The entity rule applies everywhere the lookup is used and is the supported no-code surface; manual `body.js` filter editing is page-scoped, brittle, and bypasses validation.
		         - DISAMBIGUATION (restriction vs visibility): a phrase like "show the <LookupField> only for <records> where <condition>" / "show the Assignee field only for contacts where Age = 30" means RESTRICT which records the lookup OFFERS (apply-static-filter on that lookup, rooted on its reference schema), NOT toggle the field's visibility. Do NOT set the field `visible:false`, do NOT use hide-element/show-element, and do NOT add a page attribute for this. hide-element/show-element are only for requirements about the field itself appearing or disappearing (e.g. "hide the Discount field when Type = Internal"), never about which records a lookup lists. For "...Assignee only for contacts where Age = 30": targetAttribute = the Assignee lookup, filter = { logicalOperation: AND, filters: [ { columnPath: "Age", comparisonType: "EQUAL", value: 30 } ] } (root schema Contact, inferred).
		       - If the requirement is field-value format validation (length, regex, range) → use a VALIDATOR, not a business rule.
		       - If the requirement is cross-field orchestration with side effects (API calls, process launch, data loading) → use a HANDLER.
		       - If the requirement is display-only value transformation → use a CONVERTER.
		       - When in doubt, prefer business rules over handlers for simple conditional visibility/editability/required logic.

		       Workflow
		       1. Call `get-tool-contract` for `create-entity-business-rule` or `create-page-business-rule`.
		       2. Read entity schema columns with `get-entity-schema-column-properties` or page structure with `get-page`.
		       3. Determine whether the rule is entity-level or page-level.
		       4. For state-changing requirements, decide whether the state must also return through an explicit inverse rule.
		       5. Resolve every lookup condition value and lookup set-values constant with `odata-read` structured `filters`. Example lookup read by display value: `odata-read` with entity `Contact`, filters `{ "all": [{ "field": "Name", "op": "contains", "value": "Andrew" }] }`, select `["Id","Name"]`, top `5`.
		       6. Build the condition group and actions.
		          - For `apply-filter`, use an empty condition group and put the lookup-filter configuration into the action payload.
		          - If the user did not specify otherwise and the scenario is a normal dependent lookup, default `populateValue` to `true` so the reverse helper child rule is generated too.
		       7. Call `create-entity-business-rule` or `create-page-business-rule`.
		       8. Verify by checking the entity or page on the environment.

		       Common mistakes to avoid
		       - Do NOT add visibility/editability/required toggling logic in SCHEMA_HANDLERS — use business rules.
		       - Do NOT confuse business rules with business logic. Business rules are declarative condition→action pairs, not imperative code.
		       - Do NOT write `$context.enableAttribute()`/`$context.disableAttribute()` handlers when a business rule suffices.
		       - Do NOT duplicate entity-level rules on every page. If the rule applies to the entity globally, create it at the entity level.
		       - Do NOT assume a state-changing business rule automatically reverses itself. Use an explicit inverse rule when both directions are required.
		       - Do NOT call a business-rule creation tool before reading its `get-tool-contract` entry.
		       - Do NOT use random GUIDs for lookup constants. Resolve or verify them with `odata-read`.
		       - Do NOT use `apply-filter` on non-lookup targets or non-lookup sources.
		       - Do NOT use `targetFilterPath` or `sourceFilterPath` that resolve to scalar `Guid` columns such as `Lookup.Id`; those paths must resolve to Lookup attributes.
		       - Do NOT set `populateValue=true` together with `sourceFilterPath`; current platform behavior does not support that combination cleanly.
		       - Do NOT use kebab-case comparison tokens (`equal`, `is-filled-in`) inside an apply-static-filter leaf `comparisonType`; that field uses UPPER_SNAKE_CASE (`EQUAL`, `IS_NOT_NULL`).
		       - Do NOT root apply-static-filter `columnPath` on the rule's entity; it is rooted on the target lookup's reference schema.
		       - Do NOT use a backward EXISTS to express a simple "field is filled" check; use IS_NOT_NULL on the column.
		       """
	};
}
