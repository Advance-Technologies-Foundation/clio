using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for creating and editing Freedom UI indicator widgets through clio MCP.
/// </summary>
[McpServerResourceType]
public sealed class IndicatorWidgetGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/indicator-widget";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Canonical guidance article accessible by name through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP indicator widget guide

		       Scope
		       - Use this guide whenever the requirement is to create, edit, filter, or troubleshoot a `crt.IndicatorWidget` on a Freedom UI web page.
		       - This guide adapts the internal Copilot metric-widget authoring rules into the runtime page-body shape that `get-page` / `update-page` use in clio MCP.
		       - Read this guide before editing a page body when the task mentions indicator, metric, KPI, aggregate count, sum, average, min, max, or a static widget filter.

		       Core translation: Copilot intent shape vs clio runtime shape
		       - Copilot-style authoring intent is conceptual: `from`, `select`, `filters`.
		       - clio page editing writes the runtime widget payload inside `viewConfigDiff[*].values.config.data.providing`.
		       - Canonical mapping:
		         - `from` -> `config.data.providing.schemaName`
		         - `select` -> `config.data.providing.aggregation.column.expression`
		         - `filters` -> `config.data.providing.filters`
		         - result formatting -> `config.data.formatting`
		         - displayed metric text -> `config.text`

		       Canonical MCP workflow
		       1. Call `list-pages` and `get-page` to inspect the current page and choose the target container.
		       2. Call `get-component-info` for `crt.IndicatorWidget` if you need the exact insert shape or property defaults.
		       3. Resolve the data source entity and aggregation column from the requirement.
		       4. Build the widget filter logic using the rules below.
		       5. Write the widget into `viewConfigDiff` using `update-page` or `sync-pages`.
		       6. Verify the saved page body or read it back with `verify: true`.

		     
		       Aggregation selection (value choice, not shape)
		       - Decide which aggregation the requirement implies, then pick the target column: `COUNT` aggregates `Id`; `SUM`, `AVG`, `MIN`, `MAX` target the explicit business column from the requirement, never `Id`.
		       - The expression JSON and aggregation enum values that encode this are part of the component contract; do not hand-build them from memory — confirm them via `get-component-info`.
		       - Do not load records manually in handlers just to compute a metric. The widget queries and aggregates at runtime.
		       - Do not replace a static aggregate/filter requirement with business rules or JavaScript handlers.

		       Filter authoring (which filter, not its JSON)
		       - The runtime filter-leaf shapes (scalar compare, lookup, range, backward-reference / EXISTS) are part of the component contract — get them from `get-component-info` rather than reconstructing the JSON here.
		       - Before finalizing non-trivial filter paths, lookup constants, or relative-date wording, read `esq-filters`.
		       - Decide the value family first: scalar literal, lookup value, date / relative-date, or a condition on related child records.
		       - `BETWEEN` is modeled as two bounds joined by `AND`, not one leaf.
		       - For conditions on child records rather than direct columns on the aggregated root schema, use a backward-reference / EXISTS-style filter instead of inventing unsupported flat column paths.
		       - Resolve lookup GUIDs upstream; never fabricate lookup GUIDs or root schema names.

		       Common mistakes to avoid
		       - Do NOT guess lookup GUIDs or root schema names.
		       - Do NOT flatten child-schema conditions into unsupported `columnPath` strings when the requirement actually needs EXISTS semantics.
		       - Do NOT remove the root filter-group envelope (`filterType: 6`, `rootSchemaName`, `logicalOperation`) even for a single static condition.
		       - Do NOT manually query data in handlers to emulate a metric widget that the platform can express declaratively.

		       Related guidance
		       - Use `get-component-info` for `crt.IndicatorWidget` as the source of truth for the generation contract: runtime field shapes, filter-leaf forms, and aggregation enum values. This guide owns intent translation and value selection; the component owns shape.
		       - Read `esq-filters` for normalized path, lookup-value, and relative-date filter guidance.
		       - Read `page-modification` for replacing-schema and minimal-body write rules.
		       - Read `page-schema-resources` before adding new user-visible titles, labels, or hints.
		       """
	};

	/// <summary>
	/// Returns the canonical guidance article for Freedom UI indicator widgets.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "indicator-widget-guidance")]
	[Description("Returns canonical MCP guidance for Freedom UI indicator widgets, including Copilot-intent to runtime-config translation, aggregation rules, and static filter authoring patterns.")]
	public ResourceContents GetGuide() => Guide;
}
