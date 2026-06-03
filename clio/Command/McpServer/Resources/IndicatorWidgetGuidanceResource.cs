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

		       Runtime shape ownership
		       - The exact runtime field contract for `crt.IndicatorWidget` — valid filter-leaf forms, the aggregation expression shape, and the aggregation enum values — is owned by the component, not by this guide. Read it with `get-component-info` for `crt.IndicatorWidget` (or the widget's component-level authoring contract) before writing the payload, and treat that as the source of truth for shape.
		       - This guide's job is to map Copilot intent onto the correct fields and help you choose correct values. The fields you populate are:
		         - `values.type` = `crt.IndicatorWidget`
		         - `values.config.title` (use a resource binding for user-visible text)
		         - `values.config.data.providing.schemaName` (root aggregated entity)
		         - `values.config.data.providing.attribute` (platform-expected widget data attribute)
		         - `values.config.data.providing.aggregation.column.expression` (aggregate function + target column)
		         - `values.config.data.providing.filters.filter` (root ESQ filter group)

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

		       Worked translation example: count Todo records created by Supervisor
		       - This is an end-to-end translation example, not the shape contract; the leaf and aggregation shapes below are owned by the component contract (`get-component-info`). It is shown here only to illustrate how intent maps onto a finished payload.
		       - Requirement intent:
		         - source entity: `UsrTodo05211340`
		         - metric: `COUNT(Id)`
		         - filter intent: `CreatedBy = Supervisor`
		       - Because this guide cannot assume a stable user GUID across environments, the portable example uses the display-name path fallback `CreatedBy.Name = "Supervisor"`.
		       - Runtime widget payload fragment:

		       ```json
		       {
		         "type": "crt.IndicatorWidget",
		         "config": {
		           "title": "#ResourceString(IndicatorWidget_TodosBySupervisor_title)#",
		           "theme": "without-fill",
		           "data": {
		             "formatting": {
		               "type": "number",
		               "decimalPrecision": 0,
		               "decimalSeparator": ".",
		               "thousandSeparator": ","
		             },
		             "providing": {
		               "attribute": "IndicatorWidget_TodosBySupervisor_Data",
		               "schemaName": "UsrTodo05211340",
		               "filters": {
		                 "filter": {
		                   "items": {
		                     "CreatedByNameSupervisor": {
		                       "filterType": 4,
		                       "comparisonType": 3,
		                       "isEnabled": true,
		                       "trimDateTimeParameterToDate": false,
		                       "leftExpression": {
		                         "expressionType": 0,
		                         "columnPath": "CreatedBy"
		                       },
		                       "isAggregative": false,
		                       "dataValueType": 10,
		                       "referenceSchemaName": "Contact",
		                       "rightExpressions": [
		                         {
		                           "expressionType": 2,
		                           "parameter": {
		                             "dataValueType": 10,
		                             "value": {
		                               "Name": "Supervisor",
		                               "Email": "",
		                               "Account": {
		                                 "value": "e308b781-3c5b-4ecb-89ef-5c1ed4da488e",
		                                 "displayValue": "Our company",
		                                 "primaryImageValue": "",
		                                 "primaryColorValue": ""
		                               },
		                               "Id": "410006e1-ca4e-4502-a9ec-e54d922d2c00",
		                               "Photo": "",
		                               "value": "410006e1-ca4e-4502-a9ec-e54d922d2c00",
		                               "displayValue": "Supervisor"
		                             }
		                           }
		                         }
		                       ]
		                     }
		                   },
		                   "logicalOperation": 0,
		                   "isEnabled": true,
		                   "filterType": 6,
		                   "rootSchemaName": "UsrTodo05211340"
		                 }
		               },
		               "aggregation": {
		                 "column": {
		                   "expression": {
		                     "expressionType": 1,
		                     "functionArgument": {
		                       "expressionType": 0,
		                       "columnPath": "Id"
		                     },
		                     "functionType": 2,
		                     "aggregationType": 1,
		                     "aggregationEvalType": 2
		                   }
		                 }
		               },
		               "dependencies": []
		             }
		           },
		           "text": {
		             "template": "{0}",
		             "metricMacros": "{0}",
		             "labelPosition": "above-under",
		             "fontSizeMode": "medium"
		           },
		           "layout": {
		             "color": "green"
		           }
		         }
		       }
		       ```

		       Practical conversion checklist
		       - Confirm the aggregated root schema name, not just the page entity label.
		       - Confirm the aggregated column path and whether the requirement implies `COUNT(Id)` or another aggregation.
		       - Confirm whether the filter value is scalar, lookup, date/time, or derived from a child relation.
		       - Preserve existing widget-specific config such as `theme`, `layout`, `text`, `hint`, and `comparison` unless the task explicitly changes them.
		       - Preserve existing page-body indentation and use append-mode semantics when adding a widget to an already-customized page.

		       Common mistakes to avoid
		       - Do NOT copy Copilot's conceptual `from` / `select` / `filters` object directly into the page body; translate it into `config.data.providing.*`.
		       - Do NOT put raw business labels like `"Supervisor"` into a lookup-id slot when the runtime expects a GUID.
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
