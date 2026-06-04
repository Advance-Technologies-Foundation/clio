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
		       - Use this guide whenever you author or edit an ESQ filter tree: indicator/chart/list widget filters, page quick-filters, lookup narrowing, or DataService SelectQuery filters.
		       - This guide covers the filter contract exhaustively: every filter type, every comparison operator, the value shape required for every column data type, the complete date/time macro catalog, forward and backward references, and nested logical groups.
		       - Read `esq` first for the surrounding query envelope (root schema, columns, aggregation, expression building blocks, and the master enum tables). This guide focuses only on the `filters` tree and the leaf filters inside it.

		       Filter shape essentials
		       - Filters are JSON objects with NUMERIC enum values: `filterType`, `comparisonType`, and `dataValueType` are integers (catalogs below), never strings.
		       - Every leaf carries explicit expression objects (`leftExpression`/`rightExpression`/`rightExpressions`) and values live inside a `parameter` envelope. This is exactly the shape stored in real Creatio page bodies, in an indicator widget's `config.data.providing.filters.filter`, and in a DataService SelectQuery body.
		       - A relative date is always a macro EXPRESSION (`{ "expressionType": 1, "functionType": 1, "macrosType": <n> }`), never a literal text value.

		       The filter group envelope (filterType 6)
		       - The root of every filter tree is a filter GROUP. It is never omitted, even for one condition.
		       - Shape: `{ "items": { ... }, "logicalOperation": 0, "isEnabled": true, "filterType": 6, "rootSchemaName": "<RootSchema>" }`.
		         - `items` is a keyed MAP (object), not an array. Keys are arbitrary unique strings; real pages use GUIDs (`"3714ebf4-41a3-..."`) or stable names (`"columnIsNotNullFilter"`). The key only has to be unique within the group.
		         - `logicalOperation`: 0 = AND, 1 = OR. This combines all direct children of the group.
		         - `filterType`: 6 marks the object as a group (FilterGroup).
		         - `rootSchemaName`: the entity the paths are resolved against.
		         - `isEnabled`: true. A disabled group/leaf is ignored at runtime.
		       - To nest groups (mix AND and OR), place another `filterType: 6` object as one of the `items`. There is no depth limit.
		       - Widget envelope reminder: in a widget the group lives at `config.data.providing.filters.filter`. An empty filter is `{ "items": {}, "logicalOperation": 0, "isEnabled": true, "filterType": 6, "rootSchemaName": "<Schema>" }`.

		       Anatomy of a leaf filter
		       - Common fields on a leaf (compare/in/isnull/between/exists):
		         - `filterType`: the leaf kind (see catalog below).
		         - `comparisonType`: the operator (see operator catalog below).
		         - `isEnabled`: true.
		         - `trimDateTimeParameterToDate`: true to compare only the date part of a DateTime column (drops the time); false otherwise.
		         - `leftExpression`: the column (or function) being filtered, almost always `{ "expressionType": 0, "columnPath": "<Path>" }`.
		         - `rightExpression` (singular): the value/macro/function for compare filters.
		         - `rightExpressions` (array): the value list for In filters AND for lookup equality (see lookup section).
		         - `rightGreaterExpression` / `rightLessExpression`: the bounds for Between filters.
		         - `dataValueType`: the data type of the compared column/value (numeric; see `esq` master table). Often emitted by the client; set it for lookups (10), booleans (12), dates (7/8/9).
		         - `referenceSchemaName`: the target schema of a lookup column (e.g. "Contact", "Country"). Required for lookup leaves.
		         - `isAggregative`: true only for Exists/backward-reference leaves.
		         - `key`, `leftExpressionCaption`: optional metadata the designer emits; safe to include but not required for the filter to run.

		       FilterType catalog (the `filterType` numeric values)
		       - 1 = CompareFilter. A scalar comparison: `leftExpression` + `comparisonType` + `rightExpression`.
		       - 2 = IsNullFilter. Null test: `comparisonType` 1 (Is_null) or 2 (Is_not_null), plus `isNull` true/false. No right expression.
		       - 3 = Between. Range test: `rightGreaterExpression` (lower bound) and `rightLessExpression` (upper bound), `comparisonType` 0.
		       - 4 = InFilter. Membership: `rightExpressions` array of parameter expressions. Also the canonical shape for lookup equality (single or multi value).
		       - 5 = Exists. Backward-reference / sub-query existence test over child records. `isAggregative: true`, a bracketed `leftExpression.columnPath`, `comparisonType` 15 (Exists) / 16 (Not_exists), and a `subFilters` group.
		       - 6 = FilterGroup. A nested group (see envelope above).
		       - 7 = Segment. Membership in a saved SysDataSegment: `{ "filterType": 7, "comparisonType": 15|16, "isEnabled": true, "segmentFilterOptions": { "segmentId": "<SysDataSegment.Id>" } }`. comparisonType 15 = IN segment, 16 = NOT IN.

		       Comparison-operator catalog (the `comparisonType` numeric values)
		       - 0 = Between, 1 = Is_null, 2 = Is_not_null, 3 = Equal, 4 = Not_equal, 5 = Less, 6 = Less_or_equal, 7 = Greater, 8 = Greater_or_equal, 9 = Start_with, 10 = Not_start_with, 11 = Contain, 12 = Not_contain, 13 = End_with, 14 = Not_end_with, 15 = Exists, 16 = Not_exists.
		       - Which operators are valid per column family (mirror of the platform filter-builder):
		         - Text/string columns: 3, 4, 9, 10, 11, 12, 13, 14, 1, 2 (equal/not-equal/starts/contains/ends + null tests).
		         - Number/Money/Float/Integer columns: 3, 4, 5, 6, 7, 8, 1, 2 (and 0 Between).
		         - Date/DateTime/Time columns: 3, 4, 5 (Before), 6 (OnOrBefore), 7 (After), 8 (OnOrAfter), 0 (Between), 1, 2.
		         - Lookup columns: 3, 4, 1, 2 only. NEVER use 11 (Contain) on a lookup column. (Exists/Not_exists 15/16 apply only to the backward-reference Exists leaf, not to a direct lookup column compare.)
		         - Boolean columns: 3 (= true) and 4 (= false) only — there is no dedicated boolean operator; compare against a boolean parameter value.

		       Value shapes by column data type (`dataValueType`)
		       - Text (1): `{ "expressionType": 2, "parameter": { "dataValueType": 1, "value": "Acme" } }`.
		       - Integer (4): `value` is a JSON number, e.g. `5`.
		       - Float (5) / Money (6): `value` is a JSON number, e.g. `12.5`.
		       - Boolean (12): `value` is JSON `true`/`false`; set the leaf `dataValueType` to 12 too.
		       - Guid (0): `value` is the GUID string. Used for raw Id comparisons and on `.Id` leaves of backward references.
		       - Date (8) / DateTime (7) / Time (9): the `value` must be a JSON-ENCODED string — the ISO value wrapped in its own quotes, e.g. `"value": "\"2026-01-01T00:00:00.000Z\""` for DateTime and `"value": "\"2026-01-01\""` for Date. A plain ISO string (`"value": "2026-01-01T00:00:00.000Z"`) is rejected with `ArgumentNullException: value cannot be null`. For a date-only intent on a DateTime column set `trimDateTimeParameterToDate: true`. Do NOT treat a date string as Text. Exact time-of-day comparisons are timezone-sensitive — preserve the user-local intent rather than assuming server-local time.
		       - Lookup (10): the value is an OBJECT, never a bare GUID — see the lookup section.
		       - Enum (11): `value` is the enumeration GUID/value as the schema defines it.

		       Column-path normalization
		       - `leftExpression.columnPath` is a dot-separated path resolved against the group's `rootSchemaName`.
		       - A lookup segment is written as the lookup column's own name: `CreatedBy`, `Account`, `Owner`, `Country`, `QualifyStatus`. The reference value lives in that segment directly.
		       - Forward references traverse lookups with dots: `Account.Owner.Name`, `QualifyStatus.IsFinal`, `Contact.Country.TimeZone`. The last segment is the actual column being compared; pick its data type accordingly.
		       - The only legitimate `Id` segment is the primary key `Id` itself (e.g. the `.Id` leaf of a backward reference). A lookup segment is the column name on its own (`Account`), and `Account` already resolves to the related record. See `esq` for the full forward/backward reference grammar.

		       Lookup-filter conversion guidance
		       - A lookup equality is serialized as an IN filter (filterType 4), even for a single value (the current-user macros described below are the one exception — they use a CompareFilter):
		         - `filterType: 4`, `comparisonType: 3` (Equal), `dataValueType: 10`, `referenceSchemaName: "<LookupTargetSchema>"`.
		         - `leftExpression`: `{ "expressionType": 0, "columnPath": "<LookupColumn>" }`.
		         - `rightExpressions`: an ARRAY. Each entry is `{ "expressionType": 2, "parameter": { "dataValueType": 10, "value": { "Id": "<guid>", "value": "<guid>", "displayValue": "<text>", "Name": "<text>" } } }`.
		       - The parameter `value` is an OBJECT carrying both the record Id (`Id`/`value`) and its `displayValue`. Do not pass a bare GUID for a lookup. Resolve the GUID first (do not fabricate it) — for example via an ESQ select against the lookup schema, or the platform lookup-resolution path.
		       - Multi-value lookup ("USA or UK"): keep it ONE lookup IN filter with several objects in `rightExpressions`; do not expand into duplicated scalar filters.
		       - Current-user / current-user-contact lookups are the exception: they use a CompareFilter (filterType 1) whose `rightExpression` is a macro — `{ "expressionType": 1, "functionType": 1, "macrosType": 1 }` for current user (SysAdminUnit) or `macrosType: 2` for current user's Contact.
		       - Display-name fallback: if a stable GUID is unavailable, filter on the lookup display path as TEXT instead, e.g. `CreatedBy.Name = "Supervisor"` or `Country.Name = "United States"` (filterType 1, dataValueType 1).
		       - Frequent mistakes: putting raw business text like `"Supervisor"` into a lookup value slot; mixing GUID and display-name values in one filter; using Contain (11) on a lookup-id column; inventing a `referenceSchemaName` without confirming the lookup target.

		       Relative-date conversion guidance
		       - Relative/period wording ("this year", "previous month", "next 7 days", "today", "tomorrow", "current quarter", "exact time 14:30", "each Monday", "anniversary today") must be authored as a macro or date-part expression on the RIGHT side, never as a plain text/date literal.
		       - Period macro right side: `{ "expressionType": 1, "functionType": 1, "macrosType": <n> }`, combined with `comparisonType: 3` (Equal) on the date column to mean "falls within that period".
		       - Complete macrosType catalog (numeric value -> meaning):
		         - 1 CurrentUser, 2 CurrentUserContact (lookup macros, not date macros).
		         - 3 Yesterday, 4 Today, 5 Tomorrow.
		         - 6 PreviousWeek, 7 CurrentWeek, 8 NextWeek.
		         - 9 PreviousMonth, 10 CurrentMonth, 11 NextMonth.
		         - 12 PreviousQuarter, 13 CurrentQuarter, 14 NextQuarter.
		         - 15 PreviousHalfYear, 16 CurrentHalfYear, 17 NextHalfYear.
		         - 18 PreviousYear, 19 CurrentYear, 23 NextYear.
		         - 20 PreviousHour, 21 CurrentHour, 22 NextHour.
		         - 24 NextNDays, 25 PreviousNDays, 26 NextNHours, 27 PreviousNHours (parameterized — see below).
		         - 37 DayOfYearToday, 38 DayOfYearTodayPlusDaysOffset, 39 NextNDaysOfYear, 40 PreviousNDaysOfYear (anniversary/day-of-year semantics; 38/39/40 are parameterized).
		         - (34 PrimaryColumn, 35 PrimaryDisplayColumn, 36 PrimaryImageColumn, 41 PrimaryColorColumn are predefined-column macros, not date macros.)
		       - Parameterized macros carry N as a `functionArgument`:
		         `{ "expressionType": 1, "functionType": 1, "macrosType": 25, "functionArgument": { "expressionType": 2, "parameter": { "dataValueType": 4, "value": 30 } } }` = "previous 30 days".
		       - Date-PART checks (every Monday, each 14th, a specific month/year, an exact time) use a DatePart FUNCTION on the LEFT side rather than a macro: `leftExpression = { "expressionType": 1, "functionType": 3, "datePartType": <n>, "functionArgument": { "expressionType": 0, "columnPath": "<DateColumn>" } }`, compared (Equal) to an integer.
		         - datePartType values: 1 Day, 2 Week, 3 Month, 4 Year, 5 Weekday, 6 Hour, 7 HourMinute.
		         - "each Monday" -> datePartType 5 (Weekday) = the weekday number; "each 14th" -> datePartType 1 (Day) = 14; "each May" -> datePartType 3 (Month) = 5; "in 1985" -> datePartType 4 (Year) = 1985.
		         - A fixed calendar year/month is a DatePart check, NOT a relative macro (the year/month/quarter macros only express periods relative to now). Full example, "contacts created in calendar year 2021": `{ "filterType": 1, "comparisonType": 3, "isEnabled": true, "leftExpression": { "expressionType": 1, "functionType": 3, "datePartType": 4, "functionArgument": { "expressionType": 0, "columnPath": "CreatedOn" } }, "rightExpression": { "expressionType": 2, "parameter": { "dataValueType": 4, "value": 2021 } } }`. The leaf carries no `dataValueType` of its own; the integer year/month/day goes in the right `parameter` (dataValueType 4, Integer).
		       - Frequent mistakes: putting a relative period into a text `parameter` value instead of a macro expression (use `macrosType: 19` for "this year"); turning "within next 7 days" into a hand-built date plus Greater_or_equal instead of `macrosType: 24` with a `functionArgument`; losing timezone intent for exact time-of-day comparisons.

		       Between filters
		       - filterType 3, comparisonType 0. Bounds are two separate expressions: `rightGreaterExpression` = lower bound, `rightLessExpression` = upper bound.
		       - Example (age 5..25): `{ "filterType": 3, "comparisonType": 0, "isEnabled": true, "leftExpression": { "expressionType": 0, "columnPath": "Age" }, "rightGreaterExpression": { "expressionType": 2, "parameter": { "dataValueType": 4, "value": 5 } }, "rightLessExpression": { "expressionType": 2, "parameter": { "dataValueType": 4, "value": 25 } } }`.
		       - Alternative many surfaces prefer: model the range as two CompareFilters (Greater_or_equal lower bound + Less_or_equal upper bound) joined by an AND group. Use the first-class Between only when the surface expects it.

		       In filters (multi-value membership)
		       - filterType 4 with a `rightExpressions` array of parameter expressions, all of the same `dataValueType`. Example: `Age IN (1,2,3,4)` -> four `{ "expressionType": 2, "parameter": { "dataValueType": 4, "value": <n> } }` entries.
		       - The same shape carries lookup equality/membership (see lookup section). For lookups set `dataValueType: 10` and `referenceSchemaName`.

		       IsNull filters
		       - filterType 2. Is null: `comparisonType: 1`, `isNull: true`. Is not null: `comparisonType: 2`, `isNull: false`. Only `leftExpression` is present; no right side.

		       Exists / backward-reference filters (child-record conditions)
		       - Use these when the requirement is about RELATED/CHILD records of the root entity ("accounts that have at least one contact", "opportunities with more than 10 orders", "contacts who declined an invite").
		       - Backward-path grammar (memorize this exact pattern): `[JoinedTable:JoinedTableRelationColumn].JoinedTableColumn`.
		         - `JoinedTable` = the child schema you join in (e.g. `Contact`).
		         - `JoinedTableRelationColumn` = the lookup column INSIDE the joined/child table that points back to the root entity (e.g. `Account` on the Contact table). This is the join key; it is a column on the CHILD, not on the root.
		         - `JoinedTableColumn` = the child column the join targets/aggregates, usually `Id` for existence, or a value column for an aggregate.
		         - Worked reading of `[Contact:Account].Id` from root `Account`: "join the `Contact` table on its `Account` column back to this `Account`, then look at the child `Id`" -> "accounts that have at least one contact".
		       - Shape: `filterType: 5`, `isAggregative: true`, `comparisonType: 15` (Exists) or `16` (Not_exists), and the backward path on the left:
		         `leftExpression: { "expressionType": 0, "columnPath": "[Contact:Account].Id" }`.
		         - `dataValueType` on the Exists leaf is optional and cosmetic (the platform infers it); if you emit it, use `0` (Guid) for the `.Id` leaf. What actually matters is the bracketed backward path, `isAggregative: true`, the Exists/Not_exists `comparisonType`, and the child-rooted `subFilters`.
		       - Child conditions live in `subFilters`, which is itself a `filterType: 6` group whose `rootSchemaName` is the JoinedTable (child) schema:
		         `"subFilters": { "items": { ... }, "logicalOperation": 0, "isEnabled": true, "filterType": 6, "rootSchemaName": "<JoinedTable>" }`.
		         - Inside `subFilters`, column paths are relative to the joined/child schema and do NOT use bracket notation — you are already in the child context (e.g. `"columnPath": "Age"`, not `"[Contact:Account].Age"`).
		         - Empty `subFilters.items` ({}) means "any related child exists".
		       - Backward joins can be chained, and chained with forward references:
		         - Double backward: `"[Touch:Contact].[TouchAction:Touch].Id"` (from root Contact: its Touches, then those Touches' TouchActions).
		         - Mixed forward+backward: `"Contact.[Case:Assignee].Id"` (from root Employee: forward to its Contact via the `Contact` lookup, then backward to Cases whose `Assignee` is that Contact -> "employees who have at least one assigned case").
		       - Aggregated comparison over a backward join (e.g. "accounts where contacts' average age < 30") is a CompareFilter whose `leftExpression` is an aggregation sub-query expression over the bracketed path (`functionType: 2` Aggregation, `aggregationType`, `expressionType: 3` SubQuery, with `subFilters`). See `esq` for the sub-query expression shape.
		       - Do NOT flatten a child-record requirement into a fake flat `columnPath` on the root. If there is no direct lookup path, it needs a backward-reference Exists.

		       Nested AND / OR groups
		       - To express "(A AND B) OR C", build an outer OR group (logicalOperation 1) whose `items` contain an inner AND group (logicalOperation 0, items A and B) plus leaf C.
		       - Preserve explicit grouping from the requirement; do not silently collapse nested logic into one flat list. If the logical operator is unclear, do not assume a permissive OR — clarify or default to AND only when the surface mandates a default.

		       Authoring rules for common problem cases
		       - Keep the root group envelope (`filterType: 6`, `logicalOperation`, `rootSchemaName`) even for a single condition.
		       - Resolve lookup GUIDs; never fabricate them. Fall back to the display-name TEXT path when no stable GUID exists.
		       - Treat date-like strings as dates (7/8/9), not text. Use macros for relative wording.
		       - Use backward-reference Exists for child-record conditions; never invent flat paths.

		       Verification checklist
		       - Confirm every `columnPath` resolves against the correct `rootSchemaName` (or child `rootSchemaName` inside subFilters).
		       - Confirm each column is the RIGHT one for the requirement, not just a column that exists — verify with `get-entity-schema-properties`. A plausible but wrong column (e.g. `Activity.Contact` vs `Activity.Owner`) returns wrong data WITHOUT erroring, so name-based guessing is unsafe; this matters most for the relation column in a backward reference and for ambiguous lookups.
		       - Confirm every lookup value is a real resolved object with `Id`/`displayValue`, and `referenceSchemaName` matches the lookup target.
		       - Confirm date-like values are dates/macros, not text; confirm `trimDateTimeParameterToDate` matches the date-vs-timestamp intent.
		       - Confirm every lookup segment uses the lookup column name (e.g. `Account`, `CreatedBy`); the only `Id` in a path is the primary-key leaf of a backward reference.
		       - Confirm child-entity conditions use backward-reference Exists with bracketed paths and a child-rooted `subFilters` group.
		       - Validate the whole tree by running it: wrap the filter group in a SelectQuery (COUNT(Id)) and call the `execute-esq` tool against a live environment. A parse error or an obviously wrong count exposes a malformed filter before you save it to a page. See `esq` for the SelectQuery envelope.

		       Common mistakes to avoid
		       - Using string enum values (e.g. comparisonType "EQUAL") or a flat top-level `value` next to `columnPath` instead of the numeric enums + `parameter` envelope.
		       - Putting a bare GUID (or raw text) where a lookup value OBJECT is required.
		       - Writing a relative date as a literal string/date instead of a macro/date-part expression.
		       - Using Contain on a lookup column.
		       - Dropping the `filterType: 6` group envelope, or using an array instead of a keyed `items` map.
		       - Flattening child-record conditions into non-existent flat column paths instead of a backward-reference Exists.

		       Related guidance
		       - Read `esq` for the SelectQuery envelope, columns/select, expression building blocks, the forward/backward reference grammar, aggregations, and the master enum tables.
		       - Read `indicator-widget` when the filter is part of a `crt.IndicatorWidget` aggregate payload.
		       - Read `page-modification` for page-body save mechanics after the filter shape is decided.
		       """
	};

	/// <summary>
	/// Returns the canonical guidance article for ESQ-style filter authoring.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "esq-filters-guidance")]
	[Description("Returns canonical MCP guidance for ESQ-style filter authoring: every filter type and comparison operator, value shapes per column type, the full date/time macro catalog, lookup-value handling, forward and backward references, and common generation pitfalls.")]
	public ResourceContents GetGuide() => Guide;
}
