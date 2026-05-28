using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for Freedom UI page modification through clio MCP.
/// </summary>
[McpServerResourceType]
public sealed class PageModificationGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/page-modification";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Canonical guidance article accessible by name through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP page modification guide

		       PRE-EDIT GUIDANCE CHECKLIST — MANDATORY before writing any body
		       MOBILE CHECK: If `get-page` returned `schema-type: "mobile"`, STOP — call `get-guidance` with name `mobile-page-modification` instead. Mobile pages use plain JSON (NOT AMD), have a completely different component registry, and must NOT contain handlers, validators, or converters. The rules below apply ONLY to web pages (schema-type: "web"). NOTE: even on mobile, a subset of the checklist below still applies (notably `page-schema-resources` and entity-level `business-rules`) — the mobile guide lists which items carry over.

		       GATE: if ANY row in the table below matches your change, you MUST call the listed guide before producing the body. Skipping a matching row is treated as a defect, not a shortcut.

		       | Requirement pattern | Call get-guidance with name | Why |
		       | --- | --- | --- |
		       | conditional visibility, editability, or required state based on field values (e.g. "when Status=Closed hide field X") | `business-rules` | Business rules are SEPARATE artifacts — do NOT implement them as JavaScript handlers or validators. Use `create-page-business-rule` or `create-entity-business-rule`. |
		       | email as mailto link, phone as tel link, text uppercase/lowercase, boolean inversion, number/currency formatting, any display-only transformation | `page-schema-converters` | Determines whether a converter is the right tool BEFORE you choose a component type. Skipping this causes AI to use crt.EmailInput instead of crt.ToEmailLink, or crt.Label instead of a converter binding. |
		       | business logic, cross-field orchestration, async data loading, side effects | `page-schema-handlers` | Handlers are NOT the same as converters. |
		       | required field, max length, format enforcement with error message | `page-schema-validators` | Validators write to viewModelConfigDiff, not viewConfigDiff. |
		       | SDK service calls (SysSettingsService, HttpClientService, etc.) | `page-schema-creatio-devkit-common` | Correct import syntax and async patterns. |
		       | body contains `$Resources.Strings.*` or `#ResourceString(...)#`, or you plan to pass the `resources` parameter, OR your change adds/edits ANY user-visible string-like property (label, caption, title, tooltip, placeholder, description, button captions, tab/group titles, validator/dialog messages — examples, not exhaustive) | `page-schema-resources` | Every user-visible string must be a localizable-string binding, not an inline literal. Most resource registrations for DS-bound captions (e.g. `PDS_UsrStatus`) are unnecessary because the platform auto-provides them; guidance specifies which keys must vs must-not be registered, and `$Resources.Strings.*` is rejected in validator params. |
		       | body contains `operation:"insert"` in `viewConfigDiff` for a standard field component (crt.Input, crt.NumberInput, crt.Checkbox, crt.ComboBox, crt.PhoneInput, crt.EmailInput, crt.DateTimePicker, crt.WebInput, crt.RichTextEditor, crt.ColorPicker, crt.ImageInput, crt.FileInput, crt.EncryptedInput, crt.Slider) | `page-modification` (this guide — see the "Inserted-field contract" section below) | A field control insert is a 3-part edit; missing the viewModelConfigDiff attribute or the label resource is silently catastrophic (no data source / blank caption) and now hard-rejected by `update-page` validation. |

		       STOP. Do NOT call get-component-info and pick a component type to solve a display transformation requirement until you have read `page-schema-converters` and confirmed the OOTB decision table does not cover your case. A common mistake is treating a display transformation as a component selection problem.

		       STOP. Touching resources at all? Read `page-schema-resources` first. This covers any body that contains `$Resources.Strings.*` or `#ResourceString(...)#`, any call that passes the `resources` parameter, AND any change that adds or modifies a user-visible string-like property — no exceptions, no "simple cases".

		       Replacing-schema concept
		       - When a Freedom UI designer saves changes to a page, Creatio creates a replacing schema in a "design package".
		       - The replacing schema inherits from the original and contains only the diff applied by the designer.
		       - The design package is NOT the same as the package that owns the original page schema.
		       - `get-page` and `update-page` automatically resolve the replacing schema through the design package.
		       - The editable target is always `hierarchy[0]` — the most-derived schema in the hierarchy.
		       - If no replacing schema exists, `hierarchy[0]` is the original schema and the design package is the original package.

		       Canonical page modification flow
		       1. `list-pages` — discover the page schema name.
		       2. `get-page` — read `raw.body` (the editable replacing schema body) and `page` metadata.
		       3. Edit `raw.body` as needed.
		       4. `update-page` or `sync-pages` — save the modified body back.
		       5. Optionally use `verify: true` in `update-page` to read back the page metadata after saving.

		       get-page response structure
		       - `page` — metadata of the editable replacing schema: `schemaName`, `schemaUId`, `packageName`, `packageUId`, `parentSchemaName`.
		       - `raw.body` — full JavaScript body of the replacing schema (with markers). Read-only reference; see the CRITICAL warning below before reusing it as the write payload.
		       - `bundle` — read-only merged view across the full hierarchy. Do not send `bundle` or `bundle.viewConfig` as the body payload.

		       bundle.json shape (top-level keys)
		       - `name` — string, the page schema name.
		       - `viewConfig` — ARRAY container that wraps the merged root tree as its single element. By design: in `body.js`, `viewConfigDiff` is an array of operations; in `bundle.json`, `viewConfig` holds the result of applying those diffs, wrapped as `[ rootObject ]`. Walk it as `.viewConfig[0]` for the merged root, or `.viewConfig | .. | objects | ...` for recursive search.
		       - `containers` — ARRAY of objects `{ name, type, childCount, path }`, NOT a keyed object. Use `.containers[]`, never `.containers | keys[]` or `.containers | to_entries[]`.
		       - `resources` — nested object (localizable strings); not flat — `to_entries` plus `@tsv` will fail on nested values.
		       - `handlers`, `converters`, `validators` — plain JavaScript SOURCE STRINGS, not parsed JSON. Read with `jq -r '.handlers'`.
		       - `viewModelConfig`, `modelConfig`, `optionalProperties`, `parameters` — structured (object/array) merged metadata.

		       jq recipes for bundle.json
		       - Find a tab or container by name pattern (use `containers`, the index):
		         `jq '.containers[] | select(.name | test("Sales"; "i"))' .clio-pages/<schema>/bundle.json`
		       - List all tab containers:
		         `jq '.containers[] | select(.type == "crt.TabContainer") | {name, path, childCount}' .clio-pages/<schema>/bundle.json`
		       - Read the merged root object:
		         `jq '.viewConfig[0]' .clio-pages/<schema>/bundle.json`
		       - Find a node deep in viewConfig by name (recursive):
		         `jq '.viewConfig | .. | objects | select(.name? == "SalesTab")' .clio-pages/<schema>/bundle.json`
		       - Resolve a parent path for a known node:
		         `jq '.containers[] | select(.name == "SalesTab") | .path' .clio-pages/<schema>/bundle.json`
		       - Inspect handler source code:
		         `jq -r '.handlers' .clio-pages/<schema>/bundle.json`
		       Always use `.containers[]` (array iteration) and `.viewConfig[0]` for the merged root. Casting through `keys`, `to_entries`, or `@csv`/`@tsv` on these structures produces the errors `"object is not valid in a csv row"` or `"number cannot be matched, as it is not a string"`.

		       Design-package resolution (trust the backend)
		       - `get-page` returns `page.designPackageUId` — the package where `update-page` will save. `page.willCreateReplacingInDesignPackage: true` means a NEW replacing schema will be materialized on save (the package itself is virtual until then; the backend creates it from the cached `AppVirtualPackageInfo` at SaveSchema time).
		       - The backend resolves the design package deterministically from the locked schema's owning app via `SysPackageInInstalledApp` — there is no per-user "active app" to manage and no ambiguity between installed apps.
		       - Do NOT pass `target-package-uid` in normal flows. The override exists for niche scenarios where you have already discovered a specific replacing schema (for example via `list-pages` filtered by name) and want to bypass hierarchy resolution; otherwise, omit it and let the backend pick.

		       update-page write modes
		       - `mode: "replace"` (default): the body you pass replaces the schema body verbatim. Use only when composing the full schema body from scratch. All six marker pairs must be present.
		       - `mode: "append"`: clio loads the current schema body from the server, merges your incoming body fragment with it, and saves the merged result. Use this mode when ADDING a component/handler/config to an existing customized page — it is the safe choice when `ownBodySummary.viewConfigDiffOperations > 0`.
		       - Merge rules (append mode):
		         * `viewConfigDiff` — concat + dedupe by `name` (incoming wins)
		         * `handlers` — concat + dedupe by `request` string (incoming wins)
		         * `converters` — merge object by key (incoming wins)
		         * `viewModelConfigDiff` / `modelConfigDiff` — plain concat (no dedupe)
		       - Append mode is permissive about the incoming body: pass only the sections you want to merge (for example, just `SCHEMA_VIEW_CONFIG_DIFF` + `SCHEMA_HANDLERS`). Missing sections are skipped; the current body's values stay intact for those sections. No need to pad with empty `[]` / `{}` markers.
		       - Never invent custom markers (for example `SCHEMA_WRAPPERS` is not a valid marker). Stick to: `SCHEMA_DEPS`, `SCHEMA_ARGS`, `SCHEMA_VIEW_CONFIG_DIFF`, `SCHEMA_VIEW_MODEL_CONFIG_DIFF`, `SCHEMA_MODEL_CONFIG_DIFF`, `SCHEMA_HANDLERS`, `SCHEMA_CONVERTERS`, `SCHEMA_VALIDATORS`.

		       CRITICAL — do NOT resend the full raw.body as the update-page body payload
		       - `raw.body` contains the schema's own existing viewConfigDiff operations (existing merges/inserts). Re-sending it makes the backend re-apply those merges against the current parent hierarchy, and one of them typically fails with
		         "The requested operation requires an element of type 'Object', but the target element has type 'Array'".
		       - Correct pattern: compose a MINIMAL body that contains only the NEW operations you are adding (for example, one `insert` for the new button and one handler entry), wrapped in the six required marker pairs.
		       - The backend treats the saved body as the complete schema body, but for an existing replacing schema the incremental-save approach (minimal new ops only) is the only reliable way to add a single component without breaking existing inherited merges.
		       - Sanity check before sending: inspect `page.ownBodySummary.viewConfigDiffOperations` from the `get-page` response. If it is greater than 1, `raw.body` already holds existing operations — DO NOT resend it. Compose a minimal body with only the new ops.
		       - Fast rule: if `ownBodySummary.bodyLength` > 1000 characters, send only the delta, never the whole body.

		       update-page optional-properties
		       - Pass `optional-properties` as a JSON array of `{key, value}` objects to merge into `schema.optionalProperties`.
		       - Example: `[{"key":"entitySchemaName","value":"UsrMyEntity"}]`
		       - The merge is keyed on `key` (case-insensitive). Existing entries with the same key are replaced; others are preserved.
		       - `optional-properties` is validated as a JSON array before the save attempt. Invalid JSON fails the call.

		       update-page verify flag
		       - When `verify: true`, `update-page` reads the page back after saving and returns `page` metadata in the response.
		       - Verify is best-effort: if the read-back fails, the update response still reports `success: true`.
		       - Use `verify: true` when you need page metadata (schema name, package, UId) in the same call as the save.

		       Body formatting
		       - clio does NOT normalize or re-indent page bodies — the string you pass is saved verbatim.
		       - When adding new content, match the indentation style already present in the page body (tabs, 2 spaces, 4 spaces, etc.).
		       - Do NOT reformat existing code returned by `get-page`. Preserve original whitespace and only modify targeted sections.
		       - Single-line or dense JSON/JS in newly authored content is unacceptable: it blocks human review and produces unreadable diffs.
		       - If the page body is empty or brand-new (no existing style to match), default to tab indentation.

		       sync-pages optional-properties
		       - Each page entry in `sync-pages` also accepts `optional-properties` with the same JSON array semantics.
		       - Applies per-page; different pages in the same sync call may carry different optional-properties.

		       Virtual package materialization (first-time replacing creation)
		       - When the target page has no replacing schema in the design package yet, `update-page` automatically creates one.
		       - Behind the scenes: clio builds a fresh DTO (new Guid uId, design-package, parent=original, extendParent=true) and SaveSchema materializes the virtual package in DB on first save.
		       - No extra input from the caller — just call `update-page schema-name=<platform page>` with a replacing-style body and the backend creates the replacement.

		       Finding a container for a new component (parentName)
		       - Never guess a container name. Use `bundle.containers` from `get-page` — a flat list of all containers discovered in `viewConfig`.
		       - Each entry exposes: `name` (value to use as `parentName`), `type` (e.g. `crt.FlexContainer`, `crt.Grid`), `childCount` (existing siblings), `path` (ancestor chain, useful for disambiguation when the same `name` appears in multiple branches).
		       - Pick a container whose `path` matches the visual region you want to modify and whose `childCount` > 0 for consistency (existing sibling confirms the container is usable).
		       - Fallback: walk `bundle.viewConfig` tree manually when `bundle.containers` is empty (possible for pages built entirely via diffs without a root viewConfig node).
		       - Common Freedom UI container types: `crt.FlexContainer` (filter rows, action bars), `crt.Grid` (column layouts), `crt.TabContainer`, `crt.Expansion`.

		       Inserted-field contract — 3-part payload for a new data-bound field control
		       """
		       + "\n\n" + SchemaValidationService.InsertedFieldContractSummary + "\n\n"
		       + """
		       Three required edits for a single new field:

		       1. `viewConfigDiff` — insert the visual control with its `control` binding and `label` expression.
		       2. `viewModelConfigDiff` — merge a matching attribute entry that declares `modelConfig.path` to the entity column the control reads/writes.
		       3. Label resource — either pass an explicit entry in the `resources` parameter, OR rebind the label to `$Resources.Strings.<columnCode>` (the LAST segment of the binding attribute's `modelConfig.path`, e.g. `UsrCompleted` for `PDS.UsrCompleted`) so the platform auto-provides the caption from the entity column. The auto-provided form requires the binding attribute itself to have a `modelConfig.path` (step 2 above); auto-provide is keyed by column code, not by view-model attribute name.

		       Canonical payload — adding a `crt.NumberInput` "Estimated minutes" field bound to `UsrEstimatedMinutes`:

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
		                           "values": {
		                               "PDS_UsrEstimatedMinutes": {
		                                   "modelConfig": { "path": "PDS.UsrEstimatedMinutes" }
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
		       ` resources='{"PDS_UsrEstimatedMinutes": "Estimated minutes"}'
		       ```

		       Note: passing `resources` is one of two valid options. The shorter alternative is to rebind the label to the entity column code (the LAST segment of `modelConfig.path`), which the platform auto-provides from the column caption. Auto-provide is keyed by COLUMN CODE, not by view-model attribute name — so `PDS_UsrEstimatedMinutes` as the resource key is NOT auto-provided, but `UsrEstimatedMinutes` IS.

		       Apply this single-line change to the canonical payload above (everything else stays the same — the attribute name `PDS_UsrEstimatedMinutes` keeps its `modelConfig.path: "PDS.UsrEstimatedMinutes"` declaration in viewModelConfigDiff):

		       ```
		       "label": "$Resources.Strings.PDS_UsrEstimatedMinutes"   // ← before: requires `resources` parameter
		       "label": "$Resources.Strings.UsrEstimatedMinutes"      // ← after:  auto-provided, omit `resources`
		       ```

		       The control binding (`$PDS_UsrEstimatedMinutes`) does NOT change — it still references the view-model attribute. Only the label resource key changes to the column code form so the platform can resolve it from the entity column caption automatically.

		       Common validation diagnostics

		       - "Inserted field 'X' (type 'Y') binds to '$Z' but the body does not declare attribute 'Z' in viewModelConfigDiff." — Step 2 missing. Add the `viewModelConfigDiff` merge for attribute `Z` with the correct `modelConfig.path`. If `Z` is supposed to come from a parent schema, change `operation:"insert"` to `operation:"merge"` on the `viewConfigDiff` entry instead.
		       - "Inserted field 'X' has label '$Resources.Strings.K' but resource 'K' is neither registered in the 'resources' parameter nor auto-provided by a DS-bound attribute." — Step 3 missing. Either add `{"K":"<Caption>"}` to the `resources` parameter, or rebind the label to `$Resources.Strings.<columnCode>` where `<columnCode>` is the LAST segment of the binding attribute's `modelConfig.path` (auto-provided from the entity column caption). The binding attribute name itself (e.g. `PDS_<columnCode>`) is NOT a valid auto-provide key — only the column code is.

		       Adding a button with a click handler
		       Body structure for `update-page` (preserve all marker pairs — do not remove or reorder them):

		       ```
		       define("<PageName>", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {
		           return {
		               viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[
		                   {
		                       "operation": "insert",
		                       "name": "UsrMyButton",
		                       "values": {
		                           "type": "crt.Button",
		                           "visible": true,
		                           "caption": "$Resources.Strings.UsrMyButton_caption",
		                           "clicked": { "request": "usr.MyClickRequest" }
		                       },
		                       "parentName": "FilterGridContainer",
		                       "propertyName": "items",
		                       "index": 0
		                   }
		               ]/**SCHEMA_VIEW_CONFIG_DIFF*/,
		               viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
		               modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
		               handlers: /**SCHEMA_HANDLERS*/[
		                   {
		                       request: "usr.MyClickRequest",
		                       handler: async (request) => {
		                           alert("My button clicked");
		                           return request.next?.handle(request);
		                       }
		                   }
		               ]/**SCHEMA_HANDLERS*/,
		               converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
		               validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
		           };
		       });
		       ```

		       Rules for the handlers block
		       - Contents between `/**SCHEMA_HANDLERS*/` markers is raw JavaScript, NOT JSON. Use unquoted keys (`request`, `handler`) and arrow functions or `function` expressions.
		       - Every `viewConfigDiff` entry whose `values.clicked.request` is `usr.*` (or any custom namespace) MUST have a matching `handler` entry with the same `request` string.
		       - Always end custom handlers with `return request.next?.handle(request);` to propagate to the default pipeline. Omitting it breaks page lifecycle events.
		       - Built-in requests (`crt.*`) already have default handlers — don't duplicate them unless you intend to override.

		       Rules for viewConfigDiff
		       - `operation` must be one of: `insert`, `remove`, `merge`, `move`.
		       - `name` is the unique component id inside the hierarchy. Prefix custom components with `Usr` or project-specific prefix to avoid collisions. For entity-bound FormPage fields, the `control` binding uses the view-model attribute key — commonly `$PDS_<Column>` for designer-generated attributes against the primary data source, but may be `$Usr<Column>`, `$PageParameters_<Name>`, or another prefix depending on how the attribute was defined. Copy the attribute key from the existing binding rather than constructing one from the column name; use `get-component-info` for ready-to-use examples.
		       - `parentName` must match an existing container name from `bundle.viewConfig`.
		       - `propertyName` is usually `items` for containers.
		       - `index` is the insertion position within `parentName.items[]`.
		       - `visible` is a view-engine property, not a component-specific one. It can appear in the `values` object of ANY view element alongside `type` and element-specific properties. Accepts `true`, `false`, or a binding expression (e.g. `"$SomeAttr | crt.InvertBooleanValue"`). Applies equally to web and mobile.
		       - User-visible string values inside `values` (`label`, `caption`, `title`, `tooltip`, `placeholder`, `description`, button captions, tab/group titles — examples, not an exhaustive list; the rule covers ANY string-like property the runtime renders to the user) MUST be authored as `$Resources.Strings.<Key>` bindings, not inline string literals. Read `page-schema-resources` first to decide whether the key requires explicit registration via the `resources` parameter (DS-bound attributes auto-provide the caption; custom keys must be registered).
		       - For entity-bound FormPage data-entry fields, match the column DataValueType to the control: `ShortText`/`MediumText`/`LongText` → `crt.Input`; `Lookup` → `crt.ComboBox`; `Boolean` → `crt.Checkbox`; `DateTime`/`Date`/`Time` → `crt.DateTimePicker`; `Integer`/`Float`/`Money` → `crt.NumberInput`; `Email` → `crt.EmailInput`; `PhoneNumber` → `crt.PhoneInput`; `WebLink` → `crt.WebInput`. Use `get-component-info` for full insert examples. For display-only transformations (email as mailto link, phone as tel link) read `page-schema-converters` first — do not select a component type for display tasks.

		       Canonical flow to add a Test button to Accounts_ListPage
		       1. `list-pages filter=Accounts_List` → resolve schema name.
		       2. `get-page schema-name=Accounts_ListPage` → response contains `bundle.containers` (flat list of valid parentName values) and `raw.body` (empty replacing template if no replacement exists yet).
		       3. Pick a container from `bundle.containers`: filter by `type == "crt.FlexContainer"` and non-zero `childCount`; use its `name` as `parentName`.
		       4. Compose body: start from `raw.body` (or the template above), add button entry to `viewConfigDiff` with the chosen `parentName`, add matching handler to `handlers`.
		       5. `update-page schema-name=Accounts_ListPage body=<composed body> verify:true`.
		       6. Response includes `page.schemaUId` — the newly-materialized replacing schema in the design package.

		       Known limitations
		       - `update-page` fail-closed on design-package resolution: if `GetDesignPackageUId` fails for a write, the call returns an error instead of silently falling back to the original package.
		       - `get-page` uses a best-effort fallback to the original package if design-package resolution fails, because reads are non-destructive.
		       - Replacing schemas outside the design package (for example, manually created overrides in other packages) are not visible through `GetDesignPackageUId`. Use `list-pages` to find the correct schema name.
		       - `update-page` does NOT support handler JSON — handlers must be written as raw JavaScript inside `SCHEMA_HANDLERS` markers.
		       - The handler block is not JS-syntax-validated beyond Acorn parsing; semantic errors (wrong argument names, missing `await`) surface only when the page is loaded in the browser.
		       - ListPage DataGrid sorting: use `viewModelConfigDiff` via `Items.modelConfig.sortingConfig.attributeName` pointing to a sibling attribute (e.g. `ItemsSorting`) with sort options that use entity column names and `direction: asc/desc/none`. Do not insert `viewConfig.sorting` or `viewConfig.sortingChange` manually — the frontend preprocessor auto-injects them from `sortingConfig`.
		       """
	};

	/// <summary>
	/// Returns the canonical guidance article for Freedom UI page modification.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "page-modification-guidance")]
	[Description("Returns canonical MCP guidance for Freedom UI page modification, replacing-schema concepts, optional-properties, and verify round-trip.")]
	public ResourceContents GetGuide() => Guide;
}
