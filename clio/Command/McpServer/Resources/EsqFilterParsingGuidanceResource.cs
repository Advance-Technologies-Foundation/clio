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
		       - `IsEnabled`: a disabled group or leaf does not contribute to its parent's condition.
		       - `IsNot`: negates the result after the enabled direct children are combined.
		       - Items: preserve nesting and order for diagnostics and native-vs-DataService shape tests.

		       Native C# retains disabled nodes in the runtime tree, but DataService removes disabled
		       child configurations before the query executor receives the ESQ. A parser must therefore
		       accept both observable shapes. Count every node that is present against the depth/node
		       budget and preserve it in structural diagnostics, but exclude a disabled node from semantic
		       evaluation. Do not require native-versus-DataService shape equality for disabled items.

		       The lab verified a disabled leaf and a disabled nested OR group beside one enabled root-AND
		       sibling: both cases evaluated exactly the enabled sibling. If removing disabled items leaves
		       any group other than the root AND envelope with zero enabled children, fail closed until that
		       empty-group shape has its own live proof; do not infer an AND/OR identity from these examples.

		       Apply group negation to the complete combined result:
		       ```csharp
		       private static bool EvaluateValidatedGroup(
		           FilterGroupNode group,
		           VirtualRecord record) {
		           IReadOnlyList<FilterNode> enabled = group.EnabledChildren;
		           if (enabled.Count == 0 &&
		               !(group.IsRoot && group.LogicalOperation == LogicalOperationStrict.And)) {
		               throw new NotSupportedException(
		                   "Only the empty root AND envelope is supported.");
		           }

		           bool result = group.LogicalOperation switch {
		               LogicalOperationStrict.And => enabled.All(child => child.Evaluate(record)),
		               LogicalOperationStrict.Or => enabled.Any(child => child.Evaluate(record)),
		               _ => throw new NotSupportedException(
		                   $"Unsupported logical operation '{group.LogicalOperation}'.")
		           };
		           return group.IsNot ? !result : result;
		       }
		       ```

		       The node API above is illustrative. During the one-time parse/validation phase, validate
		       every present node's complete shape, including disabled nodes, and cache each group's
		       enabled children. Evaluation must reuse that cached list rather than filtering and allocating
		       an array for every record; combine only enabled children and apply `IsNot` once. Creatio and
		       DataService both exposed `NOT(B OR C)` as an enabled OR collection with `IsNot == true`;
		       generated SQL negated the composed group.

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
		           if (filter.LeftExpression?.ExpressionType !=
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
		       `ReadScalarParameter` validates structure for enabled and disabled leaves alike. The parsed
		       node retains `IsEnabled`; only the cached enabled-child list controls later evaluation.

		       Null comparisons have a different verified leaf contract and must not use the scalar-parameter
		       reader:
		       ```csharp
		       private static FilterComparisonType ReadNullComparison(
		           EntitySchemaQueryFilter filter,
		           string expectedColumn) {
		           bool isNullComparison = filter.ComparisonType == FilterComparisonType.IsNull ||
		               filter.ComparisonType == FilterComparisonType.IsNotNull;
		           if (!isNullComparison ||
		               filter.LeftExpression?.ExpressionType !=
		                   EntitySchemaQueryExpressionType.SchemaColumn ||
		               filter.LeftExpression.Path != expectedColumn ||
		               filter.RightExpressions.Count != 0) {
		               throw new NotSupportedException("Unsupported null filter shape.");
		           }
		           return filter.ComparisonType;
		       }
		       ```

		       Native and DataService text null predicates produced this same left-only runtime shape. For the
		       verified MediumText columns, the provider matched Creatio's SQL oracle by evaluating `IsNull`
		       with `string.IsNullOrEmpty(value)` and `IsNotNull` with its negation. Do not generalize that
		       empty-string rule to other schema data-value types without a separate platform proof.

		       Membership leaves use the ordinary `Equal` comparison type at this runtime boundary. Parse the
		       complete right-expression collection and validate every value before evaluating any record:
		       ```csharp
		       private const int MaxMembershipValues = 100;

		       private static HashSet<int> ReadIntegerMembership(
		           EntitySchemaQueryFilter filter,
		           string expectedColumn) {
		           if (filter.ComparisonType != FilterComparisonType.Equal ||
		               filter.LeftExpression?.ExpressionType !=
		                   EntitySchemaQueryExpressionType.SchemaColumn ||
		               filter.LeftExpression.Path != expectedColumn) {
		               throw new NotSupportedException("Unsupported membership filter shape.");
		           }
		           if (filter.RightExpressions.Count > MaxMembershipValues) {
		               throw new NotSupportedException(
		                   $"Membership exceeds the {MaxMembershipValues}-value limit.");
		           }

		           return filter.RightExpressions.Select(expression => {
		               if (expression.ExpressionType !=
		                       EntitySchemaQueryExpressionType.Parameter ||
		                   expression.ParameterValue is not int value) {
		                   throw new NotSupportedException("Membership requires Integer parameters.");
		               }
		               return value;
		           }).ToHashSet();
		       }
		       ```

		       Count right expressions against a separate conservative parameter budget (or the provider's shared
		       parse budget), validate them once, and cache the set. Evaluate membership with
		       `values.Contains(record.SequenceNumber)`. An empty returned set is
		       always false; it must never fall through to an unfiltered result. One value is structurally the
		       same runtime leaf as scalar equality, and multiple values are the same leaf with multiple right
		       expressions. The serialized DataService `filterType: 4` discriminator is consumed before the
		       executor boundary, so a parser cannot recover whether a one-value Equal leaf was authored as
		       Compare or In. Support that ambiguity deliberately in the provider contract.

		       A first-class Between leaf is distinct: require `ComparisonType.Between`, the exact allowed column
		       path, and exactly two ordered parameter expressions. Parse them once as lower then upper:
		       ```csharp
		       private static (int Lower, int Upper) ReadIntegerBetween(
		           EntitySchemaQueryFilter filter,
		           string expectedColumn) {
		           if (filter.ComparisonType != FilterComparisonType.Between ||
		               filter.LeftExpression?.ExpressionType !=
		                   EntitySchemaQueryExpressionType.SchemaColumn ||
		               filter.LeftExpression.Path != expectedColumn ||
		               filter.RightExpressions.Count != 2) {
		               throw new NotSupportedException("Unsupported Integer Between shape.");
		           }

		           int[] boundaries = filter.RightExpressions.Select(expression => {
		               if (expression.ExpressionType !=
		                       EntitySchemaQueryExpressionType.Parameter ||
		                   expression.ParameterValue is not int value) {
		                   throw new NotSupportedException(
		                       "Between boundaries must be Integer parameters.");
		               }
		               return value;
		           }).ToArray();
		           return (boundaries[0], boundaries[1]);
		       }
		       ```

		       Evaluate it inclusively: `record.SequenceNumber >= range.Lower &&
		       record.SequenceNumber <= range.Upper`. The alternative `GreaterOrEqual` plus `LessOrEqual` form
		       remains two leaves in the surrounding AND group. A general tree evaluator can support both, but must
		       dispatch the shapes independently; do not rewrite or silently accept an exclusive sibling pair.

		       Then require the CLR type appropriate to the schema column (`System.Int32` for the verified
		       Integer example and `System.String` for MediumText) and dispatch explicitly on
		       `ComparisonType`. Do not coerce an unexpected value or silently treat an unsupported
		       comparison as false. Compare `LeftExpression.Path` to the complete allowed path;
		       `SchemaColumnName` is only the terminal schema column and can collapse `Account.Name` to
		       `Name`. If the provider supports direct columns only, an exact path comparison rejects
		       forwarded/lookup filters instead of accidentally evaluating them as a root column.

		       `EntitySchemaQueryFilter` does not expose a leaf `IsNot`. Negated string predicates arrive
		       as `NotStartWith`, `NotContain`, or `NotEndWith`; evaluate those operators directly. Group
		       `IsNot` exists only on `EntitySchemaQueryFilterCollection` and negates the combined group.

		       ## Evaluation rules for the verified subset
		       1. Parse and validate the complete remotely supplied tree once. Reject unsupported nodes even
		          when an earlier sibling could determine the result; otherwise invalid hidden branches bypass
		          fail-closed validation.
		       2. Exclude disabled items from evaluation. Preserve/count native disabled nodes for structural
		          diagnostics; expect DataService to omit disabled children before this boundary.
		       3. Evaluate only the validated tree. Short-circuit AND at the first false child and OR at the
		          first true child so expensive provider predicates are not evaluated unnecessarily.
		       4. The normal empty root AND is true. Reject every other empty group, including an empty root OR,
		          until its semantics are independently validated. Apply group `IsNot` only after combining
		          enabled children.
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
		       Verified now: group envelope/nesting, disabled leaf/group behavior, group `IsNot`, and all
		       scalar Compare operators using representative Integer and MediumText values, plus text
		       `IsNull`/`IsNotNull` and Integer In cardinality boundaries. The frontend guide supplies the remaining
		       validation backlog: Boolean/Guid values,
		       lookups, dates/macros,
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
