using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Routes AI callers to the canonical frontend, backend, or runtime parsing ESQ filter guidance.
/// </summary>
[McpServerResourceType]
public sealed class EsqFiltersGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/esq-filters";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;
	private const string FrontendResourcePath = ResourcePath + "/frontend";
	private const string FrontendResourceUri = DocsScheme + "://" + FrontendResourcePath;

	/// <summary>
	/// Canonical ESQ filter family router accessible by name through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       # clio MCP ESQ filters guidance family

		       ## Purpose
		       This article is the stable entry point for ESQ filter work. It routes to exactly one
		       construction guide for the caller's API surface and to the parsing guide only when code
		       receives a runtime C# filter tree. Detailed filter rules live in the child guides, not here.

		       ## GATE: choose the owner before writing code
		       - JavaScript, Freedom UI, page JSON, or a DataService SelectQuery payload:
		         read `esq-filters-frontend`.
		       - Native Creatio backend C# using `EntitySchemaQuery`:
		         read `esq-filters-backend`.
		       - Runtime C# code that receives and interprets `EntitySchemaQuery.Filters`:
		         read `esq-filter-parsing`.
		       - Comparing a filter created through DataService with a native backend filter:
		         read the matching construction guide and `esq-filter-parsing`; compare the runtime
		         tree, not the two authoring syntaxes.

		       ## Shared boundary
		       - `esq` owns the surrounding SelectQuery envelope, selected columns, expressions,
		         aggregation, and the master enum tables.
		       - `esq-filters-frontend` owns serialized JavaScript/DataService filter construction.
		       - `esq-filters-backend` owns native C# `EntitySchemaQuery` filter construction.
		       - `esq-filter-parsing` owns runtime C# traversal and interpretation.
		       - Do not copy detailed filter rules between these articles. Cross-link to the owner.

		       ## Current backend validation status
		       The backend construction and parsing guides currently publish the lab-verified group
		       envelope, nesting, disabled nodes, group negation, primitive Integer/MediumText Compare,
		       MediumText null checks, Integer membership cardinalities, and inclusive Between ranges.
		       Other filter families remain explicitly marked pending until the same native-vs-DataService
		       runtime-shape test proves and promotes them.
		       """
	};

	/// <summary>
	/// Canonical frontend and DataService JSON filter guidance.
	/// </summary>
	internal static readonly TextResourceContents FrontendGuide = new() {
		Uri = FrontendResourceUri,
		MimeType = "text/plain",
		Text = """
		       # clio MCP frontend ESQ filter construction guide

		       ## Scope
		       - Use this guide whenever you author or edit a serialized JavaScript/DataService ESQ filter tree: indicator/chart/list widget filters, page quick-filters, lookup narrowing, or DataService SelectQuery filters.
		       - For native Creatio backend C# construction, use `esq-filters-backend`. For runtime C# parsing of `EntitySchemaQuery.Filters`, use `esq-filter-parsing`.
		       - This guide covers the filter contract exhaustively: every filter type, every comparison operator, the value shape required for every column data type, the complete date/time macro catalog, forward and backward references, and nested logical groups.
		       - Read `esq` first for the surrounding query envelope (root schema, columns, aggregation, expression building blocks, and the master enum tables). This guide focuses only on the `filters` tree and the leaf filters inside it.
		       - Shape in one line: filters are JSON with NUMERIC enum values (`filterType`/`comparisonType`/`dataValueType` are integers, never strings) and values wrapped in a `parameter` envelope — the exact shape stored in Creatio page bodies and DataService SelectQuery bodies.

		       ## Filter tree structure
		       - A filter tree is built from GROUPS (filterType 6) that nest leaves and other groups, plus the leaves that do the actual comparing. This section covers the group envelope, AND/OR nesting, and the fields every leaf shares; the leaf KINDS are in Filter types below.

		       ### Group envelope (filterType 6)
		       - The root of every filter tree is a filter GROUP. It is never omitted, even for one condition.
		       - Shape: `{ "items": { ... }, "logicalOperation": 0, "isEnabled": true, "filterType": 6, "rootSchemaName": "<RootSchema>" }`.
		         - `items` is a keyed MAP (object), not an array. Keys are arbitrary unique strings; real pages use GUIDs (`"3714ebf4-41a3-..."`) or stable names (`"columnIsNotNullFilter"`). The key only has to be unique within the group.
		         - `logicalOperation`: 0 = AND, 1 = OR. This combines all direct children of the group.
		         - `filterType`: 6 marks the object as a group (FilterGroup).
		         - `rootSchemaName`: the entity the paths are resolved against.
		         - `isEnabled`: true. A disabled group/leaf is ignored at runtime.
		       - To nest groups (mix AND and OR), place another `filterType: 6` object as one of the `items`. There is no depth limit.
		       - An empty filter is still the full group envelope: `{ "items": {}, "logicalOperation": 0, "isEnabled": true, "filterType": 6, "rootSchemaName": "<Schema>" }`.

		       ### Nested AND / OR groups
		       - To express "(A AND B) OR C", build an outer OR group (logicalOperation 1) whose `items` contain an inner AND group (logicalOperation 0, items A and B) plus leaf C.
		       - Preserve explicit grouping from the requirement; do not silently collapse nested logic into one flat list. If the logical operator is unclear, do not assume a permissive OR — clarify or default to AND only when the surface mandates a default.

		       ### Leaf anatomy
		       - Common fields shared by every leaf (compare/in/isnull/between/exists):
		         - `filterType`: the leaf kind (see Filter types below).
		         - `comparisonType`: the operator (see Operators below).
		         - `isEnabled`: true.
		         - `trimDateTimeParameterToDate`: true to compare only the date part of a DateTime column (drops the time); false otherwise.
		         - `leftExpression`: the column (or function) being filtered, almost always `{ "expressionType": 0, "columnPath": "<Path>" }`.
		         - `rightExpression` (singular): the value/macro/function for compare filters.
		         - `rightExpressions` (array): the value list for In filters AND for lookup equality (see Lookups below).
		         - `rightGreaterExpression` / `rightLessExpression`: the bounds for Between filters.
		         - `dataValueType`: the data type of the compared column/value (numeric; see `esq` master table). Often emitted by the client; set it for lookups (10), booleans (12), dates (7/8/9).
		         - `referenceSchemaName`: the target schema of a lookup column (e.g. "Contact", "Country"). Required for lookup leaves.
		         - `isAggregative`: true only for Exists/backward-reference leaves.
		         - `key`, `leftExpressionCaption`: optional metadata the designer emits; safe to include but not required for the filter to run.

		       ## Filter types (`filterType`)
		       - Quick map: 1 Compare, 2 IsNull, 3 Between, 4 In, 5 Exists, 6 FilterGroup (see Group envelope above), 7 Segment. Each leaf type is detailed below.

		       ### Compare (filterType 1)
		       - A scalar comparison: `leftExpression` + `comparisonType` + `rightExpression`. The default leaf for "`<column>` `<operator>` `<value>`"; pick the operator from Operators and the value shape from Values.

		       ### IsNull (filterType 2)
		       - Null test with only `leftExpression` (no right side). Is null: `comparisonType: 1`, `isNull: true`. Is not null: `comparisonType: 2`, `isNull: false`.

		       ### Between (filterType 3)
		       - `comparisonType: 0`. The platform's field names are counterintuitive: `rightLessExpression` = first/lower bound and `rightGreaterExpression` = second/upper bound. This ordering is required by DataService and was verified against native C# runtime shape; do not reinterpret the names as comparison directions.
		       - Example (age 5..25): `{ "filterType": 3, "comparisonType": 0, "isEnabled": true, "leftExpression": { "expressionType": 0, "columnPath": "Age" }, "rightLessExpression": { "expressionType": 2, "parameter": { "dataValueType": 4, "value": 5 } }, "rightGreaterExpression": { "expressionType": 2, "parameter": { "dataValueType": 4, "value": 25 } } }`.
		       - Alternative many surfaces prefer: model the range as two CompareFilters (Greater_or_equal lower bound + Less_or_equal upper bound) joined by an AND group. Use the first-class Between only when the surface expects it.

		       ### In (filterType 4) — multi-value membership
		       - A `rightExpressions` array of parameter expressions, all of the same `dataValueType`. Example: `Age IN (1,2,3,4)` -> four `{ "expressionType": 2, "parameter": { "dataValueType": 4, "value": <n> } }` entries.
		       - The same shape carries lookup equality/membership (see Lookups below). For lookups set `dataValueType: 10` and `referenceSchemaName`.

		       ### Exists (filterType 5) — child-record conditions
		       - Use this when the requirement is an EXISTENCE test over related/child records ("accounts that have at least one contact", "contacts who declined an invite").
		       - Shape: `filterType: 5`, `isAggregative: true`, `comparisonType: 15` (Exists) or `16` (Not_exists), and a backward-reference path on the left (see Column paths & references > Backward references below for the bracket grammar): `leftExpression: { "expressionType": 0, "columnPath": "[Contact:Account].Id" }`.
		         - `dataValueType` on the Exists leaf is optional and cosmetic (the platform infers it); if you emit it, use `0` (Guid) for the `.Id` leaf. What actually matters is the bracketed backward path, `isAggregative: true`, the Exists/Not_exists `comparisonType`, and the child-rooted `subFilters`.
		       - Child conditions live in `subFilters`, which is itself a `filterType: 6` group whose `rootSchemaName` is the joined child schema:
		         `"subFilters": { "items": { ... }, "logicalOperation": 0, "isEnabled": true, "filterType": 6, "rootSchemaName": "<JoinedTable>" }`.
		         - Inside `subFilters`, column paths are relative to the joined/child schema and do NOT use bracket notation — you are already in the child context (e.g. `"columnPath": "Age"`, not `"[Contact:Account].Age"`).
		         - Empty `subFilters.items` ({}) means "any related child exists".
		       - For an AGGREGATE over child records (count/avg/sum, e.g. "total opportunity amount > $50,000") use a Compare leaf with an aggregation sub-query instead — see Column paths & references > Backward references below.
		       - Complete filter — Accounts that have at least one Contact in United States (replace `<country-guid>` with a resolved Country Id): `{ "items": { "hasContactInCountry": { "filterType": 5, "comparisonType": 15, "isEnabled": true, "isAggregative": true, "leftExpression": { "expressionType": 0, "columnPath": "[Contact:Account].Id" }, "subFilters": { "items": { "country": { "filterType": 4, "comparisonType": 3, "isEnabled": true, "dataValueType": 10, "referenceSchemaName": "Country", "leftExpression": { "expressionType": 0, "columnPath": "Country" }, "rightExpressions": [ { "expressionType": 2, "parameter": { "dataValueType": 10, "value": { "Id": "<country-guid>", "value": "<country-guid>", "displayValue": "United States", "Name": "United States" } } } ] } }, "logicalOperation": 0, "isEnabled": true, "filterType": 6, "rootSchemaName": "Contact" } } }, "logicalOperation": 0, "isEnabled": true, "filterType": 6, "rootSchemaName": "Account" }`.

		       ### Segment (filterType 7)
		       - Membership in a saved SysDataSegment: `{ "filterType": 7, "comparisonType": 15|16, "isEnabled": true, "segmentFilterOptions": { "segmentId": "<SysDataSegment.Id>" } }`. comparisonType 15 = IN segment, 16 = NOT IN.

		       ## Operators (`comparisonType`)
		       - 0 = Between, 1 = Is_null, 2 = Is_not_null, 3 = Equal, 4 = Not_equal, 5 = Less, 6 = Less_or_equal, 7 = Greater, 8 = Greater_or_equal, 9 = Start_with, 10 = Not_start_with, 11 = Contain, 12 = Not_contain, 13 = End_with, 14 = Not_end_with, 15 = Exists, 16 = Not_exists.
		       - Which operators are valid per column family (mirror of the platform filter-builder):
		         - Text/string columns: 3, 4, 9, 10, 11, 12, 13, 14, 1, 2 (equal/not-equal/starts/contains/ends + null tests).
		         - Number/Money/Float/Integer columns: 3, 4, 5, 6, 7, 8, 1, 2 (and 0 Between).
		         - Date/DateTime/Time columns: 3, 4, 5 (Before), 6 (OnOrBefore), 7 (After), 8 (OnOrAfter), 0 (Between), 1, 2.
		         - Lookup columns: 3, 4, 1, 2 only. NEVER use 11 (Contain) on a lookup column. (Exists/Not_exists 15/16 apply only to the backward-reference Exists leaf, not to a direct lookup column compare.)
		         - Boolean columns: 3 (= true) and 4 (= false) only — there is no dedicated boolean operator; compare against a boolean parameter value.

		       ## Column paths & references
		       - A `columnPath` points at a column relative to the group's `rootSchemaName`. It has three shapes: a DIRECT column, a FORWARD reference (to-one, dotted), or a BACKWARD reference (to-many, bracketed). See `esq` for the full reference grammar.

		       ### Direct & forward references
		       - Direct column: a plain column name resolved against the group's `rootSchemaName` (`Name`, `Age`, `CreatedOn`).
		       - Lookup columns use the Creatio LOOKUP column name (`CreatedBy`, `Account`, `Owner`, `Country`, `QualifyStatus`), NOT the database/OData foreign-key column. The `<Name>Id` form (`CreatedById`, `AccountId`) is the GUID column the database and OData expose; ESQ paths strip the trailing `Id` (`CreatedById` -> `CreatedBy`, `AccountId` -> `Account`), and the reference value lives in that lookup segment directly.
		       - Forward references traverse to-one lookups with dots: `Account.Owner.Name`, `QualifyStatus.IsFinal`, `Contact.Country.TimeZone`. The last segment is the actual column being compared; pick its data type accordingly.
		       - The only legitimate `Id` segment is the primary key `Id` itself (e.g. the `.Id` leaf of a backward reference). A lookup segment is the column name on its own (`Account`), and `Account` already resolves to the related record.

		       ### Backward references (child-record conditions)
		       - A backward (to-many / reverse-join) reference points from the root entity at its CHILD records. Use it whenever the requirement is about related/child records ("accounts that have at least one contact", "accounts whose opportunities total more than $50,000"). It cannot yield a single scalar on its own, so it is always consumed by an Exists test or an aggregate (below).
		       - Bracket grammar (memorize this exact pattern): `[JoinedTable:JoinedTableRelationColumn].JoinedTableColumn`.
		         - `JoinedTable` = the child schema you join in (e.g. `Contact`).
		         - `JoinedTableRelationColumn` = the lookup column INSIDE the joined/child table that points back to the root entity (e.g. `Account` on the Contact table). This is the join key; it is a column on the CHILD, not on the root.
		         - `JoinedTableColumn` = the child column the join targets/aggregates, usually `Id` for existence, or a value column for an aggregate.
		         - Worked reading of `[Contact:Account].Id` from root `Account`: "join the `Contact` table on its `Account` column back to this `Account`, then look at the child `Id`" -> "accounts that have at least one contact".
		       - Chaining: backward joins can be chained, and chained with forward references.
		         - Double backward: `"[Touch:Contact].[TouchAction:Touch].Id"` (from root Contact: its Touches, then those Touches' TouchActions).
		         - Mixed forward+backward: `"Contact.[Case:Assignee].Id"` (from root Employee: forward to its Contact via the `Contact` lookup, then backward to Cases whose `Assignee` is that Contact -> "employees who have at least one assigned case").
		       - EXISTENCE use — an Exists leaf (filterType 5): "do any matching child records exist?" See Filter types > Exists above for the leaf shape and child-rooted `subFilters`.
		       - AGGREGATE use — a Compare leaf (filterType 1) whose `leftExpression` is an aggregation sub-query over the bracketed path, compared to a number ("accounts whose opportunities created this year total more than $50,000"). The sub-query expression carries `expressionType: 3` (SubQuery), `functionType: 2` (Aggregation), `aggregationType` (1 Count, 2 Sum, 3 Avg, 4 Min, 5 Max), the bracketed path in `columnPath`, and a child-rooted `subFilters` group; the leaf is `isAggregative: true`. See `esq` for the full sub-query expression shape and aggregation enums.
		         - The child `subFilters` MUST sit INSIDE this SubQuery `leftExpression` — that is the only copy the platform applies (verified against a live environment). The Freedom UI designer also mirrors an identical `subFilters` onto the leaf itself (sibling of `leftExpression`/`rightExpression`), but that leaf-level copy is IGNORED for the aggregation; do not author the child conditions there and do not rely on it.
		         - Complete example — Accounts whose opportunities created this year total more than $50,000 (verified shape; the child `subFilters` sits inside the SubQuery `leftExpression`): `{ "items": { "pipelineThisYear": { "filterType": 1, "comparisonType": 7, "isEnabled": true, "isAggregative": true, "dataValueType": 6, "leftExpression": { "expressionType": 3, "functionType": 2, "aggregationType": 2, "columnPath": "[Opportunity:Account].Amount", "subFilters": { "items": { "thisYear": { "filterType": 1, "comparisonType": 3, "isEnabled": true, "trimDateTimeParameterToDate": true, "dataValueType": 7, "leftExpression": { "expressionType": 0, "columnPath": "CreatedOn" }, "rightExpression": { "expressionType": 1, "functionType": 1, "macrosType": 19 } } }, "logicalOperation": 0, "isEnabled": true, "filterType": 6, "rootSchemaName": "Opportunity" } }, "rightExpression": { "expressionType": 2, "parameter": { "dataValueType": 6, "value": 50000 } } } }, "logicalOperation": 0, "isEnabled": true, "filterType": 6, "rootSchemaName": "Account" }`.
		       - Do NOT flatten a child-record requirement into a fake flat `columnPath` on the root. If there is no direct lookup path, it needs a backward reference.

		       ## Values (`dataValueType`)
		       - Each leaf value is wrapped in a `parameter` envelope — `{ "expressionType": 2, "parameter": { "dataValueType": <n>, "value": <v> } }` — and the `value` shape depends on the data type:
		       - Text (1): `{ "expressionType": 2, "parameter": { "dataValueType": 1, "value": "Acme" } }`.
		       - Integer (4): `value` is a JSON number, e.g. `5`.
		       - Float (5) / Money (6): `value` is a JSON number, e.g. `12.5`.
		       - Boolean (12): `value` is JSON `true`/`false`; set the leaf `dataValueType` to 12 too.
		       - Guid (0): `value` is the GUID string. Used for raw Id comparisons and on `.Id` leaves of backward references.
		       - Enum (11): `value` is the enumeration GUID/value as the schema defines it.
		       - Lookup (10): the value is an OBJECT, never a bare GUID — see Lookups below.
		       - Date (8) / DateTime (7) / Time (9): a JSON-encoded date string with its own encoding and macro rules — see Dates: macros & date-parts below.

		       ## Lookups
		       - A lookup equality is serialized as an IN filter (filterType 4), even for a single value (the current-user macros below are the one exception — they use a CompareFilter):
		         - `filterType: 4`, `comparisonType: 3` (Equal), `dataValueType: 10`, `referenceSchemaName: "<LookupTargetSchema>"`.
		         - `leftExpression`: `{ "expressionType": 0, "columnPath": "<LookupColumn>" }`.
		         - `rightExpressions`: an ARRAY. Each entry is `{ "expressionType": 2, "parameter": { "dataValueType": 10, "value": { "Id": "<guid>", "value": "<guid>", "displayValue": "<text>", "Name": "<text>" } } }`.
		       - The parameter `value` is an OBJECT carrying both the record Id (`Id`/`value`) and its `displayValue`. Do not pass a bare GUID for a lookup. Resolve the GUID first (do not fabricate it) — for example via an ESQ select against the lookup schema, or the platform lookup-resolution path.
		       - Multi-value lookup ("USA or UK"): keep it ONE lookup IN filter with several objects in `rightExpressions`; do not expand into duplicated scalar filters.
		       - Current-user / current-user-contact lookups are the exception: they use a CompareFilter (filterType 1) whose `rightExpression` is a macro — `{ "expressionType": 1, "functionType": 1, "macrosType": 1 }` for current user (SysAdminUnit) or `macrosType: 2` for current user's Contact.
		       - Display-name fallback: if a stable GUID is unavailable, filter on the lookup display path as TEXT instead, e.g. `CreatedBy.Name = "Supervisor"` or `Country.Name = "United States"` (filterType 1, dataValueType 1). Do not mix this text path with GUID value-objects in the same filter.
		       - Complete filter — Contacts whose `Country` is United States (replace `<country-guid>` with a resolved Country Id): `{ "items": { "country": { "filterType": 4, "comparisonType": 3, "isEnabled": true, "dataValueType": 10, "referenceSchemaName": "Country", "leftExpression": { "expressionType": 0, "columnPath": "Country" }, "rightExpressions": [ { "expressionType": 2, "parameter": { "dataValueType": 10, "value": { "Id": "<country-guid>", "value": "<country-guid>", "displayValue": "United States", "Name": "United States" } } } ] } }, "logicalOperation": 0, "isEnabled": true, "filterType": 6, "rootSchemaName": "Contact" }`.

		       ## Dates: macros & date-parts
		       - A date condition takes one of three forms: a literal date VALUE, a relative-period MACRO, or a DATE-PART check. Relative or recurring wording ("this year", "previous month", "next 7 days", "today", "current quarter", "exact time 14:30", "each Monday", "anniversary today") must be authored as a macro (RIGHT side) or a date-part expression (LEFT side), never a plain text/date literal.

		       ### Date values
		       - Date (8) / DateTime (7) / Time (9): the `value` must be a JSON-ENCODED string — the ISO value wrapped in its own quotes, e.g. `"value": "\"2026-01-01T00:00:00.000Z\""` for DateTime and `"value": "\"2026-01-01\""` for Date. A plain ISO string (`"value": "2026-01-01T00:00:00.000Z"`) is rejected with `ArgumentNullException: value cannot be null`. For a date-only intent on a DateTime column set `trimDateTimeParameterToDate: true`. Do NOT treat a date string as Text. Exact time-of-day comparisons are timezone-sensitive — preserve the user-local intent rather than assuming server-local time.
		         - `dateValue` companion: on any Date/DateTime/Time parameter the Freedom UI designer may emit a sibling `dateValue` (the same instant in UTC) next to `value` (the user-local JSON-encoded string), e.g. `"value": "\"2026-01-01T09:30:00.000\""` with `"dateValue": "2026-01-01T06:30:00.000Z"`. `value` drives the comparison; `dateValue` is only the UTC mirror — include it solely to round-trip the designer shape.
		       ### Relative-date macros
		       - Right side: `{ "expressionType": 1, "functionType": 1, "macrosType": <n> }`, combined with `comparisonType: 3` (Equal) on the date column to mean "falls within that period".
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
		       - Parameterized macros (24/25/26/27, 38/39/40) carry N as a `functionArgument` — prefer them for "next/previous N days/hours" over hand-building a date range with Greater_or_equal: `{ "expressionType": 1, "functionType": 1, "macrosType": 25, "functionArgument": { "expressionType": 2, "parameter": { "dataValueType": 4, "value": 30 } } }` = "previous 30 days".

		       ### Date-part checks
		       - A date-PART check (every Monday, each 14th, a specific month/year, an exact time) uses a DatePart FUNCTION on the LEFT side rather than a macro: `leftExpression = { "expressionType": 1, "functionType": 3, "datePartType": <n>, "functionArgument": { "expressionType": 0, "columnPath": "<DateColumn>" } }`. The RIGHT-side value type depends on the datePartType:
		         - datePartType values: 1 Day, 2 Week, 3 Month, 4 Year, 5 Weekday, 6 Hour, 7 HourMinute.
		         - Day (1) / Week (2) / Month (3) / Year (4) / Weekday (5) / Hour (6) compare (Equal) to an INTEGER (`parameter.dataValueType: 4`).
		         - HourMinute (7) is the EXCEPTION: compare (Equal) to a TIME value (`parameter.dataValueType: 9`), NOT an integer — an integer right side is rejected with `ArgumentNullException: value cannot be null`. See the worked exact-time example below.
		         - "each Monday" -> datePartType 5 (Weekday) = the weekday number; "each 14th" -> datePartType 1 (Day) = 14; "each May" -> datePartType 3 (Month) = 5; "in 1985" -> datePartType 4 (Year) = 1985.
		         - "created at exact time 11:06 (any date)" -> datePartType 7 (HourMinute) Equal a Time value — matches the hour+minute on ANY date (the date inside the value is ignored). This is the shape the Freedom UI designer emits. Verified leaf (matches 11:06 on any date, excludes 11:07): `{ "filterType": 1, "comparisonType": 3, "isEnabled": true, "trimDateTimeParameterToDate": true, "isAggregative": false, "dataValueType": 7, "leftExpression": { "expressionType": 1, "functionType": 3, "datePartType": 7, "functionArgument": { "expressionType": 0, "columnPath": "CreatedOn" } }, "rightExpression": { "expressionType": 2, "parameter": { "dataValueType": 9, "dateValue": "2026-06-11T08:06:00.000Z", "value": "\"2026-06-11T11:06:00.000\"" } } }`. To match an exact INSTANT instead (specific date AND time), drop the DatePart wrapper and compare the plain `CreatedOn` column to a DateTime value, or AND this HourMinute leaf with Day/Month/Year DatePart leaves.
		         - A fixed calendar year/month is a DatePart check, NOT a relative macro (the year/month/quarter macros only express periods relative to now). Full example, "contacts created in calendar year 2021": `{ "filterType": 1, "comparisonType": 3, "isEnabled": true, "leftExpression": { "expressionType": 1, "functionType": 3, "datePartType": 4, "functionArgument": { "expressionType": 0, "columnPath": "CreatedOn" } }, "rightExpression": { "expressionType": 2, "parameter": { "dataValueType": 4, "value": 2021 } } }`. The leaf carries no `dataValueType` of its own; the integer year/month/day goes in the right `parameter` (dataValueType 4, Integer).
		       - Complete filter — Contacts created in May of the current year (a relative macro AND a date-part check combined in one AND group): `{ "items": { "thisYear": { "filterType": 1, "comparisonType": 3, "isEnabled": true, "trimDateTimeParameterToDate": true, "dataValueType": 7, "leftExpression": { "expressionType": 0, "columnPath": "CreatedOn" }, "rightExpression": { "expressionType": 1, "functionType": 1, "macrosType": 19 } }, "inMay": { "filterType": 1, "comparisonType": 3, "isEnabled": true, "leftExpression": { "expressionType": 1, "functionType": 3, "datePartType": 3, "functionArgument": { "expressionType": 0, "columnPath": "CreatedOn" } }, "rightExpression": { "expressionType": 2, "parameter": { "dataValueType": 4, "value": 5 } } } }, "logicalOperation": 0, "isEnabled": true, "filterType": 6, "rootSchemaName": "Contact" }`.

		       ## Validate before saving
		       - Validate the whole tree by running it: wrap the filter group in a SelectQuery (COUNT(Id)) and call the `execute-esq` tool against a live environment. A parse error or an obviously wrong count exposes a malformed filter before you save it to a page. See `esq` for the SelectQuery envelope.

		       ## Related guidance
		       - Read `esq` for the SelectQuery envelope, columns/select, expression building blocks, the forward/backward reference grammar, aggregations, and the master enum tables.
		       - Read `page-modification` (and `page-modification-overview` for page-body save mechanics) after the filter shape is decided.
		       """
	};

	/// <summary>
	/// Returns the canonical guidance article for ESQ-style filter authoring.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "esq-filters-guidance")]
	[Description("Routes ESQ filter work to the canonical frontend construction, backend construction, or runtime parsing guidance without duplicating their detailed rules.")]
	public ResourceContents GetGuide() => Guide;

	/// <summary>
	/// Returns canonical frontend and DataService JSON filter construction guidance.
	/// </summary>
	[McpServerResource(UriTemplate = FrontendResourceUri, Name = "esq-filters-frontend-guidance")]
	[Description("Returns canonical guidance for serialized JavaScript, Freedom UI, page JSON, and DataService ESQ filter construction.")]
	public ResourceContents GetFrontendGuide() => FrontendGuide;
}
