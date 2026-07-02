using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Focused sub-guide of the <c>page-modification</c> family: the inserted-field contract for a new
/// data-bound field control — viewModelConfigDiff attribute binding, the label/resource rule, the
/// static-vs-diff body forms, the canonical payload, the data-source declaration, and the validation
/// diagnostics emitted when the contract is violated.
/// </summary>
[McpServerResourceType]
public sealed class PageModificationFieldContractGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/page-modification-field-contract";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Canonical guidance article accessible by name through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP page modification field-contract guide

		       This is a focused sub-guide of `page-modification`. Read `page-modification` FIRST and follow its
		       pre-edit GATE checklist; read `page-modification-overview` for the body save lifecycle (write modes,
		       static vs diff body, do-not-resend). This guide owns the INSERTED-FIELD CONTRACT: what a body must
		       declare when it inserts a new data-bound field control so the control actually binds and renders a
		       caption. For the column-DataValueType-to-control-type mapping (which `crt.*` control fits each column
		       type) read `page-modification-components` or call `get-component-info`.

		       Inserted-field contract for a new data-bound field control
		       """
		       + "\n\n" + SchemaValidationService.InsertedFieldContractSummary + "\n\n"
		       + """
		       Validation is fragment-scoped and runs BEFORE the append merge: in append mode too, the payload edits below must be present in the SAME payload you send, regardless of what the current server body already contains. (If the binding attribute is genuinely supplied by a parent schema or already declared in the current body, use `operation:"merge"` for the viewConfigDiff entry instead of `insert` — the contract above does not apply to merge.)

		       Three required edits for a single new field:

		       1. `viewConfigDiff` — insert the visual control with its `control` binding and `label` expression.
		       2. `viewModelConfigDiff` — a single merge entry with `"path": []` (root) and a `values.attributes` object declaring the attribute with its `modelConfig.path` to the entity column. The attribute name is conventionally `{DataSourceName}_{ColumnName}` (e.g. `PDS_UsrEstimatedMinutes` when the data source is `PDS`). Do NOT put the attribute directly in `values` — it must be nested under `values.attributes`.
		       3. Label resource — set the label to `$Resources.Strings.<bindingAttribute>` where `<bindingAttribute>` is the binding attribute name itself (the SAME name as the control, e.g. `$Resources.Strings.PDS_UsrEstimatedMinutes` for control `$PDS_UsrEstimatedMinutes`). For a DS-bound attribute the platform auto-provides the caption from the entity column under that attribute-name key — no `resources` entry needed. Auto-provide is keyed by the view-model ATTRIBUTE NAME, not by the column code (verified against shipped FormPage schemas: the Designer always emits the label key equal to the control attribute name, e.g. `$Resources.Strings.PartnerIdentityName` for an attribute bound to `SsoSamlProviderDS.EntityID`). If you want a caption different from the column's, pass an explicit entry in the `resources` parameter under the same attribute-name key (or use the `#ResourceString(<key>)#` macro form with a registered resource — this is what the Designer emits when a custom "Title on page" is set).

		       Static vs diff body forms — read `raw.body` before editing
		       FormPages created by `create-app` or `create-app-section` use the STATIC form: `viewModelConfig` (not `viewModelConfigDiff`) and `modelConfig` (not `modelConfigDiff`). The editable body marker is `SCHEMA_VIEW_MODEL_CONFIG`, not `SCHEMA_VIEW_MODEL_CONFIG_DIFF`.
		       - Static form (`SCHEMA_VIEW_MODEL_CONFIG`): `update-page append` is **blocked** — use `replace` mode only. Add the new attribute directly into `viewModelConfig.attributes.{attrName}`. The `modelConfig.dataSources.PDS` is already in the body — keep it as-is.
		       - Diff form (`SCHEMA_VIEW_MODEL_CONFIG_DIFF`): use `append` mode. Add attribute via `viewModelConfigDiff` with `path:[]` + `values.attributes` nesting (see below). The `PDS` data source is inherited from the original schema in the hierarchy — leave `modelConfigDiff: []`.
		       To determine which form is present: scan `raw.body` from `get-page` for the marker name. `SCHEMA_VIEW_MODEL_CONFIG` (no `_DIFF`) = static form. `SCHEMA_VIEW_MODEL_CONFIG_DIFF` = diff form. Also check `page.ownBodySummary.viewModelConfigDiffOperations > 0` as a quick signal that the diff form is already in use.

		       viewModelConfigDiff structure — CRITICAL: the attribute must reach `viewModelConfig.attributes`
		       Two equivalent properly-nested forms reach it: the preferred `"path": []` + `values.attributes` (what the Designer emits), or the older `"path": ["attributes"]` where `values` itself is the attribute map. Both are accepted.
		       The WRONG flat form (no `path`, attribute directly in `values`) lands the attribute at the `viewModelConfig` root instead of under `.attributes`, where the Freedom UI runtime ignores it — the platform accepts the save but the control renders with no data. `update-page` (and `sync-pages`) now reject this form when an inserted field binds to it:
		       ```
		       // WRONG — flat form. Attribute lands at viewModelConfig root, not under .attributes — control binds no data:
		       { "operation": "merge", "values": { "PDS_UsrEstimatedMinutes": { "modelConfig": { "path": "PDS.UsrEstimatedMinutes" } } } }

		       // CORRECT (preferred) — attribute nested under values.attributes with path:[]:
		       { "operation": "merge", "path": [], "values": { "attributes": { "PDS_UsrEstimatedMinutes": { "modelConfig": { "path": "PDS.UsrEstimatedMinutes" } } } } }

		       // ALSO CORRECT (legacy) — path:["attributes"], values is the attribute map:
		       { "operation": "merge", "path": ["attributes"], "values": { "PDS_UsrEstimatedMinutes": { "modelConfig": { "path": "PDS.UsrEstimatedMinutes" } } } }
		       ```

		       Canonical payload — adding a `crt.NumberInput` "Estimated minutes" field in DIFF form (`append` mode, for a page whose replacing schema uses viewModelConfigDiff):

		       ```
		       update-page schema-name="UsrTodo_FormPage" mode="append" body=`
		           define("UsrTodo_FormPage", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {
		               return {
		                   viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[
		                       {
		                           "operation": "insert",
		                           "name": "UsrEstimatedMinutes",
		                           "values": {
		                               "type": "crt.NumberInput",
		                               "label": "$Resources.Strings.PDS_UsrEstimatedMinutes",
		                               "control": "$PDS_UsrEstimatedMinutes",
		                               "labelPosition": "auto"
		                           },
		                           "parentName": "SideAreaProfileContainer",
		                           "propertyName": "items",
		                           "index": 0
		                       }
		                   ]/**SCHEMA_VIEW_CONFIG_DIFF*/,
		                   viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[
		                       {
		                           "operation": "merge",
		                           "path": [],
		                           "values": {
		                               "attributes": {
		                                   "PDS_UsrEstimatedMinutes": {
		                                       "modelConfig": { "path": "PDS.UsrEstimatedMinutes" }
		                                   }
		                               }
		                           }
		                       }
		                   ]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
		                   modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
		                   handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,
		                   converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
		                   validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
		               };
		           });
		       `
		       ```

		       The label `$Resources.Strings.PDS_UsrEstimatedMinutes` uses the binding attribute name (same as the control `$PDS_UsrEstimatedMinutes`). For a DS-bound attribute the platform auto-provides the caption from the entity column under that attribute-name key — no explicit `resources` parameter needed. To override the caption with custom text, pass it explicitly under the same key — `resources='{"PDS_UsrEstimatedMinutes": "Estimated minutes"}'` — or emit the `#ResourceString(<componentName>_label)#` macro label with a registered resource (the form the Designer writes for a custom "Title on page").

		       Auto-provide is keyed by the view-model ATTRIBUTE NAME, not the entity column code — only the binding-attribute key resolves:

		       ```
		       "label": "$Resources.Strings.PDS_UsrEstimatedMinutes"   // auto-provided — key equals the DS-bound binding attribute (no resources needed)
		       "label": "$Resources.Strings.UsrEstimatedMinutes"      // renders BLANK — bare column code is not the attribute name
		       ```

		       modelConfigDiff — declaring a data source for new pages
		       For app FormPages created via `create-app-section`, the data source (PDS) is already declared in the parent schema — you only need `viewModelConfigDiff` entries (leave `modelConfigDiff: []`).
		       For a standalone page with no inherited data source, declare it explicitly:
		       ```
		       modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[
		           {
		               "operation": "merge",
		               "path": [],
		               "values": {
		                   "dataSources": {
		                       "PDS": {
		                           "type": "crt.EntityDataSource",
		                           "scope": "page",
		                           "config": {
		                               "entitySchemaName": "UsrMyEntity",
		                               "loadParameters": {
		                                   "options": {
		                                       "pagingConfig": { "rowCount": 1, "rowsOffset": -1 },
		                                       "sortingConfig": { "columns": [] }
		                                   }
		                               },
		                               "allowCopyingRecords": false
		                           }
		                       }
		                   },
		                   "primaryDataSourceName": "PDS"
		               }
		           }
		       ]/**SCHEMA_MODEL_CONFIG_DIFF*/
		       ```
		       Check `bundle.modelConfig` from `get-page` — if `dataSources` is already populated, the data source is inherited and you should leave `modelConfigDiff: []`.

		       Common validation diagnostics

		       - "inserted field controls: field 'X' (type 'Y') has an undeclared attribute binding — the body does not declare attribute 'Z' in viewModelConfigDiff." — Step 2 missing entirely. Add the `viewModelConfigDiff` entry: `{"operation":"merge","path":[],"values":{"attributes":{"Z":{"modelConfig":{"path":"<DS>.<Column>"}}}}}`. If `Z` is supposed to come from a parent schema, change `operation:"insert"` to `operation:"merge"` on the `viewConfigDiff` entry instead.
		       - "inserted field controls: field 'X' (type 'Y') binds to '$Z' which is declared in viewModelConfigDiff without the required nesting ... the control will render but read and write no data." — Step 2 present but FLAT. The attribute is declared directly under `values` (or under a `path:[]` entry with no `attributes` wrapper) instead of under `values.attributes`, so it lands at the `viewModelConfig` root and the runtime ignores it. Move it to the properly-nested form: `{"operation":"merge","path":[],"values":{"attributes":{"Z":{"modelConfig":{"path":"<DS>.<Column>"}}}}}` (or the legacy `path:["attributes"]` form).
		       - "Inserted field 'X' has label '$Resources.Strings.K' but resource 'K' is neither registered in the 'resources' parameter nor auto-provided by a DS-bound attribute." — Step 3: the label key `K` does not match the binding attribute. Set the label to `$Resources.Strings.<bindingAttribute>` (the SAME name as the control) so the platform auto-provides the caption from the DS-bound column — auto-provide is keyed by the attribute name, not the column code. Or add `{"K":"<Caption>"}` to the `resources` parameter to register `K` explicitly.
		       """
	};

	/// <summary>
	/// Returns the canonical inserted-field-contract sub-guide of the page-modification family.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "page-modification-field-contract-guidance")]
	[Description("Returns the inserted-field contract sub-guide of the page-modification family: viewModelConfigDiff attribute binding, the label/resource rule, static-vs-diff body forms, the canonical NumberInput payload, data-source declaration, and the validation diagnostics emitted on contract violation.")]
	public ResourceContents GetGuide() => Guide;
}
