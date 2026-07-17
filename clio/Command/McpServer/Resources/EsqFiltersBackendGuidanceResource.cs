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
		       - The lab provider deliberately used `StringComparison.OrdinalIgnoreCase`; case-variant
		         results prove that provider policy, not Creatio/PostgreSQL collation behavior.

		       ## Coverage boundary
		       Verified now: group envelope/nesting and all scalar Compare operators using representative
		       Integer and MediumText values. Pending lab validation before publishing construction
		       recipes: disabled filters, group `IsNot`, Boolean/Guid Compare values, IsNull, In, Between,
		       lookup values, dates and macros, Exists/subqueries/aggregates, and Segment filters. Use the
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
