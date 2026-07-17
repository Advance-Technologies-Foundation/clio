using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical guidance for parsing runtime ESQ filter trees in Creatio backend C# code.
/// </summary>
[McpServerResourceType]
public sealed class EsqFilterParsingGuidanceResource {
	private const string ResourceUri = "docs://mcp/guides/esq-filter-parsing";

	/// <summary>
	/// Canonical runtime C# ESQ filter parsing guidance accessible through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       # clio MCP backend ESQ filter parsing guide

		       ## Scope and ownership
		       Use this guide when backend C# receives an `EntitySchemaQuery` and must interpret
		       `esq.Filters`, especially in an `IEntityQueryExecutor` for a virtual entity. This guide
		       owns runtime tree traversal and semantic parsing. Filter creation belongs exclusively to
		       `esq-filters-frontend` or `esq-filters-backend`.

		       The rules below are based on full runtime-shape comparisons from the clio Virtual Entity
		       guidance lab. They do not require access to Creatio backend source code.

		       ## Parse a tree, not a flat list
		       `esq.Filters` is an `EntitySchemaQueryFilterCollection`. Each item can be another
		       `EntitySchemaQueryFilterCollection` or a leaf such as an `EntitySchemaQueryFilter`.
		       Traverse every level and preserve the collection's logical operator. Treat filters as
		       remotely supplied input: enforce explicit maximum depth and total-node limits before
		       allocating the complete shape. Prefer iterative traversal for large supported limits; if a
		       recursive implementation is used, guard the depth before descending.

		       ```csharp
		       private FilterNode ParseGroup(
		           EntitySchemaQueryFilterCollection group,
		           int depth,
		           FilterParseBudget budget) {
		           budget.CountNode(depth); // Throws before configured depth/node limits are exceeded.
		           List<FilterNode> children = new();
		           foreach (IEntitySchemaQueryFilterItem item in group) {
		               children.Add(ParseItem(item, depth + 1, budget));
		           }

		           return new FilterGroupNode(
		               group.LogicalOperation,
		               group.IsEnabled,
		               group.IsNot,
		               children);
		       }

		       private FilterNode ParseItem(
		           IEntitySchemaQueryFilterItem item,
		           int depth,
		           FilterParseBudget budget) => item switch {
		           EntitySchemaQueryFilterCollection group => ParseGroup(group, depth, budget),
		           EntitySchemaQueryFilter filter => ParseCompare(filter, depth, budget),
		           _ => throw new NotSupportedException(
		               $"Unsupported ESQ filter item type '{item.GetType().FullName}'.")
		       };
		       ```

		       The DTO and budget names above are illustrative. Define conservative limits appropriate to
		       the endpoint and provider, count both groups and leaves, and reject excess depth or breadth
		       with a secret-safe diagnostic that contains limits and node type but not parameter values.
		       Keep the guarded dispatch and fail-closed behavior; adapt the resulting nodes to the data
		       source used by the virtual entity.

		       ## Group envelope semantics
		       Capture these properties before interpreting children:
		       - `LogicalOperation`: combines direct children as AND or OR.
		       - `IsEnabled`: disabled-filter semantics are not yet verified; reject a disabled group or leaf
		         instead of guessing how its parent should combine the remaining children.
		       - `IsNot`: negates the group's result; this is pending a dedicated lab proof.
		       - Items: preserve nesting and order for diagnostics and native-vs-DataService shape tests.

		       An empty enabled AND root is the normal no-filter envelope. It has zero items and must
		       evaluate as true, so every source record remains eligible. Do not treat it as an error or
		       as a false predicate.

		       ## Verified shapes to recognize
		       ```text
		       no filter               AND()
		       A && B                  AND(A, B)
		       A || B                  AND(OR(A, B))
		       A && (B || C)           AND(A, OR(B, C))
		       (A && B) || C           AND(OR(AND(A, B), C))
		       ```

		       The outer AND envelope in the OR cases is significant when comparing shapes. A parser may
		       simplify it only after capturing the source shape, and only if the consumer explicitly
		       wants semantic normalization rather than structural equality.

		       ## Parse verified Compare leaves
		       For `EntitySchemaQueryFilter`, inspect:
		       - `ComparisonType` for the operator.
		       - `LeftExpression` for the column expression and its column path.
		       - the right-expression collection for parameter expressions and their typed values.

		       Lab-verified leaves currently include:
		       - string `Equal` on `UsrName` with `"Some Value"`;
		       - integer `Greater` on `UsrSequenceNumber` with `10`;
		       - integer `Less` on `UsrSequenceNumber` with `0`.

		       A scalar authored by `CreateFilterWithParameters` or through ATF.Repository/DataService is
		       represented at runtime through the right-expression collection. Do not assume that a
		       singular authoring property implies a singular runtime property.

		       ## Evaluation rules for the verified subset
		       1. Reject a disabled item; disabled-item and resulting empty-group semantics remain unverified.
		       2. Recursively evaluate every enabled child.
		       3. AND requires every enabled child; OR requires at least one enabled child.
		       4. Empty AND is true. Do not guess empty OR or negation behavior until validated.
		       5. Evaluate the leaf using the typed value. Reject unsupported expression or operator shapes
		          with a diagnostic that includes the runtime item type, operator, and column path.

		       ## Structural comparison for tests
		       To prove that two authoring paths produce the same filter, serialize a neutral shape model
		       containing, for every group and leaf:
		       - runtime node kind;
		       - enabled and negated state;
		       - group logical operation and ordered children;
		       - leaf comparison type;
		       - left expression kind and column path;
		       - every right expression kind, data-value type, and value.

		       Compare the complete tree. Checking only returned rows proves semantic behavior but can hide
		       structural differences such as a flat root OR versus root AND containing a nested OR.

		       ## Coverage boundary
		       Verified now: group envelope/nesting and the three Compare leaves listed above. The
		       frontend guide supplies the validation backlog: disabled/IsNot, all Compare operators and
		       data types, IsNull, In, Between, lookups, dates/macros, Exists/subqueries/aggregates, and
		       Segment. Add parsing rules here only after native C# and DataService produce an asserted
		       runtime shape and the lab proves result behavior.
		       """
	};

	/// <summary>
	/// Returns canonical runtime C# ESQ filter parsing guidance.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "esq-filter-parsing-guidance")]
	[Description("Returns lab-verified guidance for recursively parsing EntitySchemaQuery.Filters in Creatio backend C# code.")]
	public ResourceContents GetGuide() => Guide;
}
