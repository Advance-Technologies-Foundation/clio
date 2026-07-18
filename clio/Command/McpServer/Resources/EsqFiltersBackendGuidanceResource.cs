using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical guidance for constructing ESQ filters with the native Creatio backend C# API.
/// </summary>
[McpServerResourceType]
public sealed class EsqFiltersBackendGuidanceResource {
	private const string ResourceUri = "docs://mcp/guides/esq-filters/backend";

	/// <summary>
	/// Canonical native C# ESQ filter construction guidance accessible through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       # clio MCP backend ESQ filter construction guide

		       ## Scope and ownership
		       Use this guide only to CREATE filters with Creatio's native backend C#
		       `EntitySchemaQuery` API. For JavaScript, page JSON, or DataService request JSON, read
		       `esq-filters-frontend`. To interpret `esq.Filters` inside a query executor or other
		       runtime backend code, read `esq-filter-parsing`.

		       The examples below are intentionally limited to behavior verified in the clio Virtual
		       Entity guidance lab by comparing the complete runtime filter tree produced by native C#
		       with the tree produced by an ATF.Repository/DataService query.

		       ## Create the query and select columns
		       ```csharp
		       EntitySchemaQuery esq = new(userConnection.EntitySchemaManager, "UsrCodexVirtualRecord");
		       esq.AddColumn("Id");
		       esq.AddColumn("UsrName");
		       esq.AddColumn("UsrSequenceNumber");
		       ```

		       `esq.Filters` is the root `EntitySchemaQueryFilterCollection`. A new query has an enabled,
		       non-negated AND root with zero direct items. Do not add a synthetic child merely to
		       represent an empty filter.

		       ## Create verified compare leaves
		       ```csharp
		       IEntitySchemaQueryFilterItem nameEquals = esq.CreateFilterWithParameters(
		           FilterComparisonType.Equal,
		           "UsrName",
		           "Some Value");

		       IEntitySchemaQueryFilterItem sequenceGreater = esq.CreateFilterWithParameters(
		           FilterComparisonType.Greater,
		           "UsrSequenceNumber",
		           10);

		       IEntitySchemaQueryFilterItem sequenceLess = esq.CreateFilterWithParameters(
		           FilterComparisonType.Less,
		           "UsrSequenceNumber",
		           0);
		       ```

		       Add multiple leaves directly to `esq.Filters` for a flat AND:
		       ```csharp
		       esq.Filters.Add(nameEquals);
		       esq.Filters.Add(sequenceGreater);
		       ```

		       ## Complete lab-verified scalar Compare catalog
		       Use the comparison type that states the intended operation; do not synthesize a
		       different operator plus group negation.
		       The calls below are independent recipes. Add only the predicates required by the query;
		       adding every positive and negative example to one AND group would be contradictory.

		       ```csharp
		       esq.Filters.Add(esq.CreateFilterWithParameters(
		           FilterComparisonType.NotEqual, "UsrSequenceNumber", 10));
		       esq.Filters.Add(esq.CreateFilterWithParameters(
		           FilterComparisonType.LessOrEqual, "UsrSequenceNumber", 20));
		       esq.Filters.Add(esq.CreateFilterWithParameters(
		           FilterComparisonType.GreaterOrEqual, "UsrSequenceNumber", 0));

		       esq.Filters.Add(esq.CreateFilterWithParameters(
		           FilterComparisonType.StartWith, "UsrName", "Alpha"));
		       esq.Filters.Add(esq.CreateFilterWithParameters(
		           FilterComparisonType.Contain, "UsrName", "middle"));
		       esq.Filters.Add(esq.CreateFilterWithParameters(
		           FilterComparisonType.EndWith, "UsrName", "Omega"));

		       esq.Filters.Add(esq.CreateFilterWithParameters(
		           FilterComparisonType.NotStartWith, "UsrName", "Alpha"));
		       esq.Filters.Add(esq.CreateFilterWithParameters(
		           FilterComparisonType.NotContain, "UsrName", "middle"));
		       esq.Filters.Add(esq.CreateFilterWithParameters(
		           FilterComparisonType.NotEndWith, "UsrName", "Omega"));
		       ```

		       The verified representative mapping is:
		       - MediumText `Equal` and Integer `NotEqual`;
		       - Integer `Less`, `LessOrEqual`, `Greater`, and `GreaterOrEqual`;
		       - MediumText `StartWith`, `NotStartWith`, `Contain`, `NotContain`, `EndWith`, and
		         `NotEndWith`.

		       This completes the scalar operator catalog without claiming that every operator was tested
		       against every column type.

		       Negated C# string predicates sent through ATF/DataService arrived as the dedicated
		       negative comparison types. They did not use group `IsNot`.

		       ## Create IsNull and IsNotNull leaves
		       Null comparisons are left-only filters. Use the dedicated APIs; do not pass a null parameter
		       to `CreateFilterWithParameters`:
		       ```csharp
		       esq.Filters.Add(esq.CreateIsNullFilter("UsrDescription"));
		       esq.Filters.Add(esq.CreateIsNotNullFilter("UsrName"));
		       ```

		       Both native leaves matched ATF/DataService predicates `UsrDescription == null` and
		       `UsrName != null` exactly at the runtime boundary. Each leaf had comparison type `IsNull` or
		       `IsNotNull`, one schema-column left expression, and zero right expressions. Do not create or
		       expect a parameter expression for either operator.

		       The SQL oracle exposed type-specific MediumText behavior: on the verified PostgreSQL platform,
		       Creatio compiled text `IsNull` as `column = ''` and text `IsNotNull` as `NOT column = ''`.
		       This is Creatio's empty-string storage semantics, not a reason to rewrite the ESQ operator and
		       not a general null rule for Integer, Guid, lookup, or date columns.

		       ## Create In membership filters
		       Native backend ESQ represents membership with `FilterComparisonType.Equal` plus a collection of
		       parameter values:
		       ```csharp
		       object[] sequenceNumbers = { 10, 30 };
		       esq.Filters.Add(esq.CreateFilterWithParameters(
		           FilterComparisonType.Equal,
		           "UsrSequenceNumber",
		           sequenceNumbers));
		       ```

		       Cardinality controls SQL generation while the runtime comparison type remains `Equal`:
		       - one value produces one right expression and SQL `column = value`;
		       - two or more values produce one right expression per value and SQL `column IN (...)`.

		       Guard empty input before constructing the filter. Handle it as an always-false result in the
		       owning query/executor contract; do not add no filter, because that would broaden the query. Returning
		       an empty `EntityCollection` is one executor-specific implementation, not part of filter construction.

		       The lab proved that an empty array remains an executor-visible Equal leaf with zero right
		       expressions, but physical SQL compilation emits invalid `column = ` text. Do not execute that
		       shape against the database and never omit it in a way that broadens the query.

		       Always pass an `object[]`. A value-type array such as `Guid[]` can bind as one array-valued
		       `params object[]` argument instead of several parameters. Convert it explicitly when needed:
		       ```csharp
		       object[] ownerIdsAsParameters = ownerIds.Cast<object>().ToArray();
		       ```

		       DataService uses serialized `filterType: 4` to distinguish In from Compare while building ESQ.
		       That transport discriminator is not present on the resulting `EntitySchemaQueryFilter`; backend
		       runtime code must use the right-expression count and its own supported-query contract.

		       ## Create Between filters
		       Native backend ESQ has a first-class inclusive two-boundary form:
		       ```csharp
		       esq.Filters.Add(esq.CreateFilterWithParameters(
		           FilterComparisonType.Between,
		           "UsrSequenceNumber",
		           10,
		           30));
		       ```

		       The runtime result is one `Between` leaf whose `RightExpressions` contain the lower value first and
		       the upper value second. Both are included; the verified SQL was `BETWEEN 10 AND 30`.

		       An equivalent inclusive range can be authored as two ordinary leaves under AND:
		       ```csharp
		       esq.Filters.Add(esq.CreateFilterWithParameters(
		           FilterComparisonType.GreaterOrEqual, "UsrSequenceNumber", 10));
		       esq.Filters.Add(esq.CreateFilterWithParameters(
		           FilterComparisonType.LessOrEqual, "UsrSequenceNumber", 30));
		       ```

		       That alternative remains two independent Compare leaves and compiled to `>= 10 AND <= 30`; Creatio
		       does not normalize it into a Between leaf. Choose the representation required by the owning contract
		       and test its complete shape rather than treating the two forms as structurally interchangeable.

		       Serialized DataService Between uses `filterType: 3`, `comparisonType: 0`, and dedicated bound fields.
		       Counterintuitively, `rightLessExpression` carries the first/lower value and
		       `rightGreaterExpression` carries the second/upper value. DataService appends them to runtime
		       `RightExpressions` in that order. Sending only a generic `rightExpressions` array is rejected.

		       ## Create Boolean and plain Guid comparisons
		       Ordinary typed values use the same scalar native API:
		       ```csharp
		       Guid externalRecordId = new("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
		       esq.Filters.Add(esq.CreateFilterWithParameters(
		           FilterComparisonType.Equal, "UsrIsActive", true));
		       esq.Filters.Add(esq.CreateFilterWithParameters(
		           FilterComparisonType.Equal, "UsrExternalRecordId", externalRecordId));
		       ```

		       The verified runtime values remained `System.Boolean` and `System.Guid`. Their parameter expressions
		       carried `BooleanDataValueType` and `GuidDataValueType` respectively. Do not serialize or parse a Guid
		       as text and do not coerce a string-form Guid in a provider.

		       ## Create lookup equality and membership
		       Pass the logical lookup column name to native ESQ and a Guid record Id as the value:
		       ```csharp
		       esq.Filters.Add(esq.CreateFilterWithParameters(
		           FilterComparisonType.Equal, "UsrOwner", ownerId));
		       ```

		       Native ESQ resolves that logical path to runtime `UsrOwnerId`. The parameter CLR value is still
		       `System.Guid`, but its forced type is `LookupDataValueType`, not `GuidDataValueType`. This distinction
		       is why lookup Ids must not be parsed as ordinary Guid columns.

		       Multi-value lookup membership uses the same Equal-plus-collection runtime representation as In:
		       ```csharp
		       object[] ownerIdsAsParameters = ownerIds.Cast<object>().ToArray();
		       esq.Filters.Add(esq.CreateFilterWithParameters(
		           FilterComparisonType.Equal, "UsrOwner", ownerIdsAsParameters));
		       ```

		       The verified two-value form produced two Lookup-typed Guid expressions and SQL `IN`. Passing a
		       `Guid[]` directly to `params object[]` instead creates one array-valued expression. DataService accepted
		       raw Guid values tagged as Lookup for the verified equality and membership requests; designer-owned
		       frontend JSON may still require the display-value object documented in `esq-filters-frontend`.

		       ## Create Date, DateTime, and Time filters
		       Creatio Date, DateTime, and Time column parameters are all CLR `System.DateTime` values. The schema
		       column carries the semantic type, so use the exact logical column path and do not turn a temporal value
		       into text:
		       ```csharp
		       esq.Filters.Add(esq.CreateFilterWithParameters(
		           FilterComparisonType.Equal, "UsrEffectiveDate", new DateTime(2026, 7, 18)));
		       esq.Filters.Add(esq.CreateFilterWithParameters(
		           FilterComparisonType.Equal, "UsrGeneratedOn",
		           new DateTime(2026, 7, 18, 13, 45, 30, DateTimeKind.Utc)));
		       esq.Filters.Add(esq.CreateFilterWithParameters(
		           FilterComparisonType.Equal, "UsrLocalTime", new DateTime(1, 1, 1, 13, 45, 0)));
		       ```

		       DataService preserved DateTime ticks in the verified request but reconstructed a native UTC value with
		       `DateTimeKind.Unspecified`. Treat `Kind` as transport metadata unless the provider contract explicitly
		       requires timezone conversion; do not reject the same ticks merely because one path retains `Utc`.

		       A midnight parameter does not enable date-only comparison on a DateTime column. Set the leaf flag:
		       ```csharp
		       EntitySchemaQueryFilter createdOnDate =
		           (EntitySchemaQueryFilter)esq.CreateFilterWithParameters(
		               FilterComparisonType.Equal, "CreatedOn", new DateTime(2026, 7, 18));
		       createdOnDate.TrimDateTimeParameterToDate = true;
		       esq.Filters.Add(createdOnDate);
		       ```

		       ## Create relative-period macros and date-part filters
		       Use `CreateFilter` with `EntitySchemaQueryMacrosType` rather than calculating relative boundaries in
		       application code:
		       ```csharp
		       esq.Filters.Add(esq.CreateFilter(
		           FilterComparisonType.Equal, "CreatedOn", EntitySchemaQueryMacrosType.CurrentYear));
		       esq.Filters.Add(esq.CreateFilter(
		           FilterComparisonType.Equal, "CreatedOn", EntitySchemaQueryMacrosType.PreviousNDays, 7));

		       EntitySchemaQueryExpression createdOn = esq.CreateSchemaColumnExpression("CreatedOn");
		       esq.Filters.Add(esq.CreateFilter(
		           FilterComparisonType.Equal, createdOn, EntitySchemaQueryMacrosType.Year, 2026));
		       esq.Filters.Add(esq.CreateFilter(
		           FilterComparisonType.Equal, createdOn, EntitySchemaQueryMacrosType.HourMinute,
		           new DateTime(1, 1, 1, 13, 45, 0)));
		       ```

		       These calls expand before the executor boundary:
		       - `CurrentYear` becomes a nested AND containing `>=` the start of this year and `<` the start of next year;
		       - `PreviousNDays, 7` becomes a nested half-open range from seven days ago through exclusive today;
		       - `Year, 2026` becomes one Equal leaf whose left expression is
		         `EntitySchemaDatePartQueryFunction(Year)` and whose right value is `System.Int32`;
		       - HourMinute equality becomes a nested AND from the requested minute (inclusive) to the next minute
		         (exclusive), with HourMinute functions on both sides.

		       Construct the semantic API call and let Creatio calculate period boundaries. Do not expect the runtime
		       tree to retain a macro enum or remain one leaf.

		       ### Exact ATF shape parity for three-term AND
		       ATF.Repository 2.0.3.5 translated source `A && B && C` to one flat root AND ordered
		       `C, A, B`. If a test requires byte-for-byte/shape-for-shape parity, insert the native
		       leaves in that observed order. Do not infer logical precedence from child order, and do
		       not generalize this transport-specific order to other expression shapes or ATF versions.

		       ## Create an explicit OR group
		       Do not change the root collection to OR when the desired runtime shape is the normal root
		       AND containing one nested OR group. Create the OR collection explicitly:
		       ```csharp
		       EntitySchemaQueryFilterCollection orGroup =
		           new(esq, LogicalOperationStrict.Or);
		       orGroup.Add(nameEquals);
		       orGroup.Add(sequenceGreater);
		       esq.Filters.Add(orGroup);
		       ```

		       This matches the runtime shape produced for `A || B` through ATF.Repository/DataService:
		       root AND -> nested OR -> A, B.

		       ## Mixed nesting
		       `A AND (B OR C)`:
		       ```csharp
		       EntitySchemaQueryFilterCollection nestedOr =
		           new(esq, LogicalOperationStrict.Or);
		       nestedOr.Add(sequenceGreater);
		       nestedOr.Add(sequenceLess);

		       esq.Filters.Add(nameEquals);
		       esq.Filters.Add(nestedOr);
		       ```

		       `(A AND B) OR C`:
		       ```csharp
		       EntitySchemaQueryFilterCollection nestedAnd =
		           new(esq, LogicalOperationStrict.And);
		       nestedAnd.Add(nameEquals);
		       nestedAnd.Add(sequenceGreater);

		       EntitySchemaQueryFilterCollection nestedOr =
		           new(esq, LogicalOperationStrict.Or);
		       nestedOr.Add(nestedAnd);
		       nestedOr.Add(sequenceLess);

		       esq.Filters.Add(nestedOr);
		       ```

		       Preserve the requirement's explicit grouping. Parentheses determine the collection tree;
		       do not flatten mixed AND/OR logic even when the leaf expressions are unchanged.

		       ## Disable a leaf or a complete group
		       Set `IsEnabled` on the exact item that must not contribute a condition:
		       ```csharp
		       EntitySchemaQueryFilter disabledLeaf =
		           (EntitySchemaQueryFilter)esq.CreateFilterWithParameters(
		               FilterComparisonType.Equal,
		               "UsrName",
		               "Blocked");
		       disabledLeaf.IsEnabled = false;
		       esq.Filters.Add(disabledLeaf);

		       EntitySchemaQueryFilterCollection disabledGroup =
		           new(esq, LogicalOperationStrict.Or);
		       disabledGroup.Add(nameEquals);
		       disabledGroup.Add(sequenceLess);
		       disabledGroup.IsEnabled = false;
		       esq.Filters.Add(disabledGroup);
		       ```

		       Native C# retains disabled leaves and groups in `esq.Filters`, so a virtual query
		       executor can observe their complete runtime shape. Creatio SQL compilation skips a
		       disabled collection and every disabled child; the lab's generated SQL contained only
		       the enabled sibling predicate.

		       DataService is a different structural boundary: it removes disabled child filter
		       configurations while building the runtime ESQ. Therefore native and DataService requests
		       can return the same records while exposing different executor-visible trees. Do not require
		       shape equality for disabled items, and do not assume a disabled DataService item will be
		       available to backend parsing code.

		       ## Negate a complete group with IsNot
		       Set `IsNot` on the collection after adding the predicates that form the group:
		       ```csharp
		       EntitySchemaQueryFilterCollection negatedOr =
		           new(esq, LogicalOperationStrict.Or);
		       negatedOr.Add(nameEquals);
		       negatedOr.Add(sequenceLess);
		       negatedOr.IsNot = true;
		       esq.Filters.Add(negatedOr);
		       ```

		       This means `NOT(nameEquals OR sequenceLess)`. `IsNot` negates the combined collection;
		       it is not a leaf modifier and must not be replaced with a guessed comparison operator.
		       Native C# and DataService produced the same enabled, negated OR runtime group in the lab,
		       and generated SQL applied `NOT` to the composed group condition.

		       ATF.Repository 2.0.3.5 LINQ cannot author group `IsNot`: unary NOT over a logical
		       expression is rejected, and its LINQ query builder does not copy metadata `IsNot` to the
		       outgoing group. The lab validated DataService transport with a test-only decorator over
		       the public `ISelectQuery` filter contract. Treat that as a library authoring limitation,
		       not as a DataService or runtime ESQ limitation.

		       ## Execute
		       ```csharp
		       EntityCollection records = esq.GetEntityCollection(userConnection);
		       ```

		       A virtual entity query executor receives the same runtime filter tree through its ESQ.
		       Creation and parsing are separate responsibilities; do not put parsing helpers in the
		       writer merely to make a sample convenient.

		       ## Lab-verified runtime invariants
		       - Empty query: root AND, enabled, not negated, zero items.
		       - Flat `A && B`: root AND with two leaves.
		       - `A || B`: root AND with one nested OR group containing two leaves.
		       - `A && (B || C)`: root AND containing leaf A and nested OR(B,C).
		       - `(A && B) || C`: root AND containing nested OR(nested AND(A,B),C).
		       - Group and leaf `Name` values were null; item insertion order matched in the verified
		         two-term/grouped cases. The verified three-term ATF order is `C, A, B`.
		       - A scalar created with `CreateFilterWithParameters` is exposed at runtime through the
		         filter's right-expression collection.
		       - Native C# retains disabled leaves/groups, but SQL compilation omits their conditions.
		         DataService removes disabled children before the executor boundary.
		       - Collection `IsNot` negates the complete combined group and is preserved by DataService.
		       - The lab provider deliberately used `StringComparison.OrdinalIgnoreCase`; case-variant
		         results prove that provider policy, not Creatio/PostgreSQL collation behavior.

		       ## Coverage boundary
		       Verified now: group envelope/nesting, disabled leaves/groups, collection `IsNot`, and all
		       scalar Compare operators using representative Integer and MediumText values, plus text
		       `IsNull`/`IsNotNull`, Integer In cardinality boundaries, typed/lookup parameters, and Date/DateTime/Time
		       literals, trim-to-date, relative-period macros, Year, and HourMinute. Pending lab validation before
		       publishing construction recipes: Exists/subqueries/aggregates and Segment filters. Use the
		       frontend guide as a discovery checklist, but do not translate its JSON fields into guessed
		       backend APIs.
		       """
	};

	/// <summary>
	/// Returns canonical native C# ESQ filter construction guidance.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "esq-filters-backend-guidance")]
	[Description("Returns lab-verified guidance for constructing grouped ESQ filters with Creatio's native backend C# API.")]
	public ResourceContents GetGuide() => Guide;
}
