using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for authoring EntitySchemaQuery (ESQ) queries through clio MCP.
/// </summary>
[McpServerResourceType]
public sealed class EsqGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/esq";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Canonical guidance article accessible by name through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP ESQ guide

		       Scope
		       - Use this guide whenever you build or read an EntitySchemaQuery (ESQ): a DataService SelectQuery body, a widget data-provider query, a page list/grid data source, or any place that carries an ESQ `columns`/`filters`/`aggregation` payload.
		       - This guide covers the query container exhaustively: the SelectQuery request envelope, the columns/select shape, the expression building blocks, the forward/backward reference column-path grammar, aggregations, and the master enum tables that the whole ESQ contract depends on.
		       - The serialized filter tree (`filters`) is large enough to warrant its own article — enter through `esq-filters`, then read `esq-filters-frontend` for every JSON filter type, operator, value shape, and date macro. This guide gives only the filter envelope and links across.

		       Query shape essentials
		       - ESQ is authored as JSON with NUMERIC enum values (`expressionType`, `functionType`, `aggregationType`, `comparisonType`, `dataValueType`, ...), explicit `expression` objects, and values wrapped in a `parameter` envelope. This is the exact shape the DataService SelectQuery endpoint accepts, that Freedom UI widgets store, and that lives inside page bodies.

		       The DataService SelectQuery request envelope
		       - Endpoint: POST to `/DataService/json/SyncReply/SelectQuery` on the Creatio instance (this is what the `execute-esq` tool wraps).
		       - Minimal body:
		         ```json
		         {
		           "rootSchemaName": "Contact",
		           "operationType": 0,
		           "columns": {
		             "items": {
		               "Id": { "expression": { "expressionType": 0, "columnPath": "Id" } },
		               "Name": { "expression": { "expressionType": 0, "columnPath": "Name" } }
		             }
		           },
		           "allColumns": false,
		           "rowCount": 50
		         }
		         ```
		       - Property reference (all names are lowerCamelCase; only those noted differ):
		         - `rootSchemaName` (string): the entity being queried. Required.
		         - `operationType` (int): 0 = Select. (Obsolete/ignored server-side on the DTO but every client still sends 0; include it.)
		         - `columns` (object): a `{ "items": { "<alias>": <SelectQueryColumn> } }` map — see Columns below.
		         - `allColumns` (bool): true selects all schema columns; usually false with an explicit `columns` map.
		         - `rowCount` (int): max rows; -1 = no limit (the default). Set a small positive value when validating a filter.
		         - `rowsOffset` (int): paging offset; pair with `isPageable: true`.
		         - `isPageable` (bool), `isDistinct` (bool), `useLocalization` (bool), `ignoreDisplayValues` (bool): common toggles.
		         - `filters` (object): the root filter group; see the filter envelope below and `esq-filters-frontend`.
		         - Hierarchy/cache fields (`isHierarchical`, `hierarchicalMaxDepth`, `serverESQCacheParameters`, ...) exist but are rarely needed for widget/filter work.
		       - To COUNT (the common validation move), select a single aggregation column instead of row data — see Aggregations.

		       Columns / select
		       - `columns.items` is a keyed MAP whose keys are the result aliases. Each value is a SelectQueryColumn.
		       - SelectQueryColumn fields: `expression` (required), `orderDirection` (0 None, 1 Asc, 2 Desc), `orderPosition` (int; -1 when unsorted), `caption` (string), `isVisible` (bool, default true).
		       - A plain column: `{ "expression": { "expressionType": 0, "columnPath": "Account.Name" } }`.
		       - An ordered column: add `"orderDirection": 2, "orderPosition": 0` to sort descending by it.

		       Expression building blocks (BaseExpression)
		       - Every left/right side and every column is a BaseExpression. The `expressionType` selects the shape:
		         - 0 SchemaColumn: `{ "expressionType": 0, "columnPath": "<Path>" }` — references a column by path.
		         - 1 Function: a computed value. Set `functionType` and the function-specific fields:
		           - functionType 1 Macros: `{ "expressionType": 1, "functionType": 1, "macrosType": <n>, "functionArgument"?: <expr> }` (relative dates / current user — full catalog in `esq-filters-frontend`).
		           - functionType 2 Aggregation: see Aggregations below.
		           - functionType 3 DatePart: `{ "expressionType": 1, "functionType": 3, "datePartType": <n>, "functionArgument": { "expressionType": 0, "columnPath": "<DateColumn>" } }` (1 Day, 2 Week, 3 Month, 4 Year, 5 Weekday, 6 Hour, 7 HourMinute).
		           - functionType 4 Length, 5 Window, 6 DateAdd, 7 DateDiff: advanced. DateDiff carries `dateDiffInterval` (0 Year, 1 Month, 2 Day, 3 Hour, 4 Minute, 5 Millisecond) and `functionArguments` (array of two operands).
		         - 2 Parameter: a literal value: `{ "expressionType": 2, "parameter": { "dataValueType": <n>, "value": <v> } }`. The `parameter.value` shape depends on the data type (see `esq-filters-frontend` value shapes). For Blob/multi-value, use `parameter.arrayValue` (string array).
		         - 3 SubQuery: a correlated sub-query over a backward-reference path: `{ "expressionType": 3, "columnPath": "[Child:Parent].Column", "subFilters": <group>, "subOrderColumn"?, "subOrderDirection"? }`. With an aggregation it also carries `functionType: 2` + `aggregationType`.
		         - 4 ArithmeticOperation: `{ "expressionType": 4, "arithmeticOperation": <n>, "leftArithmeticOperand": <expr>, "rightArithmeticOperand": <expr> }` (0 Addition, 1 Subtraction, 2 Multiplication, 3 Division).

		       Column path & reference grammar (applies to columns, order-by, and filter left expressions)
		       - A `columnPath` is resolved against the query/group `rootSchemaName`. Verify the columns you use against the real schema with `get-entity-schema-properties` (call it WITHOUT `package-name` for the merged all-packages view so custom columns from other packages are included) / `find-entity-schema` before composing the query — do NOT infer a column from its name, and do NOT treat an empty single-package read as proof a column is absent. There are two failure modes: a column that does not exist fails loudly ("Column by path ... not found in schema ..."), but a column that exists yet is the WRONG one returns wrong data with NO error. Many entities expose several similar lookups — e.g. `Activity` has `Owner`, `Author`, `Contact`, and `Account`; "who owns the activity" is `Owner`, not `Contact`. When more than one column could plausibly match the requirement, inspect the schema and pick the one whose meaning matches before trusting the result.
		       - Direct column: `Name`, `Age`, `CreatedOn`.
		       - Forward reference (one-to-one, through lookup columns): dot-separated, e.g. `Account.Owner.Name`, `Contact.Country.TimeZone`, `QualifyStatus.IsFinal`. Each non-final segment is a lookup column written by its own name (`Account`, `Owner`, `Country`); the final segment is the value column. Prefer the lookup column itself (`Account`) over its display sub-column (`Account.Name`) when you want the reference value rather than its text.
		       - A lookup segment is the lookup column name on its own (`Account`) — it already resolves to the related record. The only `Id` in a path is the primary-key `Id` leaf.
		       - Backward reference (one-to-many / reverse join): bracket syntax `[JoinedTable:JoinedTableRelationColumn].JoinedTableColumn`, where `JoinedTableRelationColumn` is the lookup column ON THE CHILD table that points back to the root. `[Contact:Account].Id` from root Account = "join the Contact table on its Account column back to this Account, then take the child Id". Backward references require aggregation or an Exists filter (a one-to-many join cannot yield a single scalar without one).
		       - Chained / mixed: brackets and dots can be chained — `[Touch:Contact].[TouchAction:Touch].Id` (double backward) or `Contact.[Case:Assignee].Id` (forward to Contact, then backward to Cases whose Assignee is that Contact). There is no fixed depth limit.
		       - When the reference is used inside a sub-query/Exists `subFilters` group, paths there are relative to the joined child schema and do NOT repeat the bracket. See the Exists section of `esq-filters-frontend`.

		       Aggregations
		       - An aggregation is a Function expression with `functionType: 2`:
		         ```json
		         {
		           "expressionType": 1,
		           "functionType": 2,
		           "aggregationType": 1,
		           "aggregationEvalType": 2,
		           "functionArgument": { "expressionType": 0, "columnPath": "Id" }
		         }
		         ```
		         - `aggregationType`: 1 Count, 2 Sum, 3 Avg, 4 Min, 5 Max (0 None, 6 TopOne).
		         - `aggregationEvalType`: 1 All, 2 Distinct (0 None). COUNT typically uses 2 (Distinct) over `Id`; SUM/AVG/MIN/MAX use 1 (All) over the business column.
		         - `functionArgument`: the column being aggregated (a SchemaColumn expression). It may itself use a forward/backward path, e.g. `Account.AnnualRevenue` or a backward sub-query.
		       - When a consumer stores the aggregation as a SelectQueryColumn, the `expression` above is nested inside a column object that adds `orderDirection`/`orderPosition`/`isVisible`.
		       - In a standalone SelectQuery, place the aggregation as the `expression` of a `columns.items` entry; the result row carries the value under that alias.

		       The filter envelope (cross-reference)
		       - `filters` on the SelectQuery is a filter GROUP: `{ "items": { ... }, "logicalOperation": 0, "isEnabled": true, "filterType": 6, "rootSchemaName": "<RootSchema>" }`.
		       - The full serialized leaf-filter contract — compare/in/between/isnull/exists, operators, value shapes, lookup objects, date macros, and backward-reference Exists — is in `esq-filters-frontend`. Do not duplicate that logic; read it.

		       Master enum reference
		       - filterType: 0 None, 1 CompareFilter, 2 IsNullFilter, 3 Between, 4 InFilter, 5 Exists, 6 FilterGroup, 7 Segment.
		       - comparisonType: 0 Between, 1 Is_null, 2 Is_not_null, 3 Equal, 4 Not_equal, 5 Less, 6 Less_or_equal, 7 Greater, 8 Greater_or_equal, 9 Start_with, 10 Not_start_with, 11 Contain, 12 Not_contain, 13 End_with, 14 Not_end_with, 15 Exists, 16 Not_exists.
		       - expressionType: 0 SchemaColumn, 1 Function, 2 Parameter, 3 SubQuery, 4 ArithmeticOperation.
		       - functionType: 0 None, 1 Macros, 2 Aggregation, 3 DatePart, 4 Length, 5 Window, 6 DateAdd, 7 DateDiff.
		       - aggregationType: 0 None, 1 Count, 2 Sum, 3 Avg, 4 Min, 5 Max, 6 TopOne.
		       - aggregationEvalType: 0 None, 1 All, 2 Distinct.
		       - datePartType: 0 None, 1 Day, 2 Week, 3 Month, 4 Year, 5 Weekday, 6 Hour, 7 HourMinute.
		       - dateDiffInterval: 0 Year, 1 Month, 2 Day, 3 Hour, 4 Minute, 5 Millisecond.
		       - arithmeticOperation: 0 Addition, 1 Subtraction, 2 Multiplication, 3 Division.
		       - logicalOperation (on a filter group): 0 And, 1 Or.
		       - orderDirection: 0 None, 1 Ascending, 2 Descending.
		       - dataValueType (most-used): 0 Guid, 1 Text, 4 Integer, 5 Float, 6 Money, 7 DateTime, 8 Date, 9 Time, 10 Lookup, 11 Enum, 12 Boolean. (Others: 13 Blob, 14 Image, 18 Color, 23 HashText, 24 SecureText, 25 File, 27 ShortText, 28 MediumText, 29 MaxSizeText, 30 LongText, 31-34 Float1-4, 40 Float8, 42 PhoneText, 43 RichText, 44 WebText, 45 EmailText, 47 Float0, 48 Money0, 49 Money1, 50 Money3. Note there is no 2/3/37 in this enum.)
		       - macrosType (date/relative + lookup macros): full catalog in `esq-filters-frontend`.

		       Worked example: count Contacts created this year, owned by the current user
		       ```json
		       {
		         "rootSchemaName": "Contact",
		         "operationType": 0,
		         "allColumns": false,
		         "columns": {
		           "items": {
		             "RecordsCount": {
		               "expression": {
		                 "expressionType": 1,
		                 "functionType": 2,
		                 "aggregationType": 1,
		                 "aggregationEvalType": 2,
		                 "functionArgument": { "expressionType": 0, "columnPath": "Id" }
		               }
		             }
		           }
		         },
		         "filters": {
		           "rootSchemaName": "Contact",
		           "filterType": 6,
		           "logicalOperation": 0,
		           "isEnabled": true,
		           "items": {
		             "CreatedThisYear": {
		               "filterType": 1,
		               "comparisonType": 3,
		               "isEnabled": true,
		               "trimDateTimeParameterToDate": true,
		               "dataValueType": 7,
		               "leftExpression": { "expressionType": 0, "columnPath": "CreatedOn" },
		               "rightExpression": { "expressionType": 1, "functionType": 1, "macrosType": 19 }
		             },
		             "OwnerIsCurrentUser": {
		               "filterType": 1,
		               "comparisonType": 3,
		               "isEnabled": true,
		               "dataValueType": 10,
		               "referenceSchemaName": "Contact",
		               "leftExpression": { "expressionType": 0, "columnPath": "Owner" },
		               "rightExpression": { "expressionType": 1, "functionType": 1, "macrosType": 2 }
		             }
		           }
		         }
		       }
		       ```

		       Running a query with execute-esq
		       - Build the SelectQuery and run it with the `execute-esq` tool to read data from a live environment — the returned rows are the query result.
		       - Response shape: `execute-esq` returns `{ success, count, rows }`. `rows` is the array of result records and `count` is the NUMBER OF ROWS — not your aggregate. For a COUNT(Id) query the answer is the aggregate value inside `rows[0]` under your column alias (e.g. `rows[0].RecordsCount`), and `count` is just 1. Result rows also include the record `Id` even when you did not select it.
		       - Running a query is also the fastest way to check a filter: a successful call confirms the schema name, every column path, and the whole filter tree parse and resolve. To check a filter before saving it anywhere, (1) take the filter group you plan to use, (2) wrap it in a SelectQuery with a single COUNT(Id) aggregation and the same `rootSchemaName`, (3) execute it, (4) compare the count to expectations, (5) only then commit it to its destination. This catches wrong paths, wrong lookup objects, and wrong macros before they reach a page.

		       Related guidance
		       - Enter through `esq-filters`; for this serialized SelectQuery surface, read `esq-filters-frontend` for the complete filter tree: every filter type and operator, value shapes per data type, lookup objects, the date-macro catalog, and backward-reference Exists filters.
		       """
	};

	/// <summary>
	/// Returns the canonical guidance article for ESQ query authoring.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "esq-guidance")]
	[Description("Returns canonical MCP guidance for EntitySchemaQuery authoring: the DataService SelectQuery envelope, columns/select, expression building blocks, the forward/backward reference column-path grammar, aggregations, and the master enum tables.")]
	public ResourceContents GetGuide() => Guide;
}
