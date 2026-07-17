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

		       Lab-verified representative scalar leaves include:
		       - MediumText `Equal` and Integer `NotEqual`;
		       - Integer `Less`, `LessOrEqual`, `Greater`, and `GreaterOrEqual`;
		       - MediumText `StartWith`, `NotStartWith`, `Contain`, `NotContain`, `EndWith`, and
		         `NotEndWith`.

		       This completes the scalar operator catalog without claiming an operator/type Cartesian
		       product.

		       A scalar authored by `CreateFilterWithParameters` or through ATF.Repository/DataService is
		       represented at runtime through the right-expression collection. Do not assume that a
		       singular authoring property implies a singular runtime property.

		       Validate the complete leaf contract before evaluating it:
		       ```csharp
		       private static object ReadScalarParameter(
		           EntitySchemaQueryFilter filter,
		           string expectedColumn) {
		           if (!filter.IsEnabled ||
		               filter.LeftExpression?.ExpressionType !=
		                   EntitySchemaQueryExpressionType.SchemaColumn ||
		               filter.LeftExpression.Path != expectedColumn ||
		               filter.RightExpressions.Count != 1) {
		               throw new NotSupportedException("Unsupported Compare filter shape.");
		           }

		           EntitySchemaQueryExpression right = filter.RightExpressions.Single();
		           if (right.ExpressionType != EntitySchemaQueryExpressionType.Parameter) {
		               throw new NotSupportedException("Compare requires one parameter expression.");
		           }
		           return right.ParameterValue;
		       }
		       ```

		       Then require the CLR type appropriate to the schema column (`System.Int32` for the verified
		       Integer example and `System.String` for MediumText) and dispatch explicitly on
		       `ComparisonType`. Do not coerce an unexpected value or silently treat an unsupported
		       comparison as false. Compare `LeftExpression.Path` to the complete allowed path;
		       `SchemaColumnName` is only the terminal schema column and can collapse `Account.Name` to
		       `Name`. If the provider supports direct columns only, an exact path comparison rejects
		       forwarded/lookup filters instead of accidentally evaluating them as a root column.

		       `EntitySchemaQueryFilter` does not expose a leaf `IsNot`. Negated string predicates arrive
		       as `NotStartWith`, `NotContain`, or `NotEndWith`; evaluate those operators directly. Group
		       `IsNot` is a separate, still-unverified concern.

		       ## Evaluation rules for the verified subset
		       1. Parse and validate the complete remotely supplied tree once. Reject unsupported nodes even
		          when an earlier sibling could determine the result; otherwise invalid hidden branches bypass
		          fail-closed validation.
		       2. Reject a disabled item; disabled-item and resulting empty-group semantics remain unverified.
		       3. Evaluate only the validated tree. Short-circuit AND at the first false child and OR at the
		          first true child so expensive provider predicates are not evaluated unnecessarily.
		       4. Empty AND is true. Do not guess empty OR or negation behavior until validated.
		       5. Evaluate the leaf using the typed value. Reject unsupported expression or operator shapes
		          with a diagnostic that includes the runtime item type, operator, and column path.
		       6. Choose and document comparison semantics for the provider. The lab handler deliberately
		          used `StringComparison.OrdinalIgnoreCase`; its case-variant results do not prove Creatio
		          database collation behavior because the virtual rows never reached PostgreSQL.

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

		       Child order is part of a structural snapshot, not logical precedence. ATF.Repository 2.0.3.5
		       emitted source `A && B && C` as flat `AND(C, A, B)`. A semantic parser should evaluate the
		       AND independent of that order; a parity test should retain it and identify the authoring path
		       and ATF version.

		       ## Coverage boundary
		       Verified now: group envelope/nesting and all scalar Compare operators using representative
		       Integer and MediumText values. The frontend guide supplies the remaining validation backlog:
		       disabled/group IsNot, Boolean/Guid values, IsNull, In, Between, lookups, dates/macros,
		       Exists/subqueries/aggregates, and Segment. Add parsing rules here only after native C# and
		       DataService produce an asserted runtime shape and the lab proves result behavior.
		       """
	};

	/// <summary>
	/// Returns canonical runtime C# ESQ filter parsing guidance.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "esq-filter-parsing-guidance")]
	[Description("Returns lab-verified guidance for recursively parsing EntitySchemaQuery.Filters in Creatio backend C# code.")]
	public ResourceContents GetGuide() => Guide;
}
