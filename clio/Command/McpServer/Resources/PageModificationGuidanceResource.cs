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
		       STEP 0 — WEB, MOBILE, OR BOTH (before list-pages/get-page): web and mobile are SEPARATE page schemas (e.g., `<Entity>_FormPage`/`_ListPage` vs `<Entity>_MobileFormPage`/`_MobileListPage`) — editing one never affects its counterpart. Decide whether the requirement targets web, mobile, or both; if it does not say, default to web (mobile is an explicit opt-in). Then for every page it touches edit each targeted variant (mobile pass: read `mobile-page-modification` first).

		       MOBILE CHECK: If `get-page` returned `schema-type: "mobile"`, STOP — call `get-guidance` with name `mobile-page-modification` instead. Mobile pages use plain JSON (NOT AMD), draw from a separate component catalog (different list of components, same wrapped registry envelope and same `get-component-info` response shape — pass `schema-type: "mobile"` to query it), and must NOT contain handlers, validators, or converters. The rules below apply ONLY to web pages (schema-type: "web"). NOTE: even on mobile, a subset of the checklist below still applies (notably `page-schema-resources` and entity-level `business-rules`) — the mobile guide lists which items carry over.

		       GATE: if ANY row in the table below matches your change, you MUST call the listed guide before producing the body. Skipping a matching row is treated as a defect, not a shortcut.

		       | Requirement pattern | Call get-guidance with name | Why |
		       | --- | --- | --- |
		       | conditional visibility, editability, required state based on field values or conditional set and clear value. Also filtering of lookups, based on condition or valur from other field. If task contains setting a lookup, based on condition or based on value in other lookup field, use business rules. (e.g. "when Status=Closed hide field X"), OR clearing a field / setting a field value based on another field (e.g. "when Type=Personal clear Company", "when Country=USA set Currency=USD") | `business-rules` | Business rules are SEPARATE artifacts — do NOT implement them as JavaScript handlers or validators. Use `create-page-business-rule` or `create-entity-business-rule`. Clearing and setting field values is done with the page business rule `set-values` action (set an empty value to clear) — NO handler needed. |
		       | restricting / filtering which records a lookup or ComboBox field offers (e.g. "limit a lookup to records whose <field> equals <value>", "show only records created within a relative period", "only <records> that have at least one related <child>", "show the <Field> only for <records> where <condition>") | `business-rules` | Lookup record-set restriction is an ENTITY business rule (`apply-static-filter`), NOT a page edit. Do NOT add `filterConfig` / `staticFilters` / `dataSourceFilters` to a datasource list attribute for this — call `create-entity-business-rule`. The entity rule applies everywhere the lookup is used and is validated; hand-edited page filters are page-scoped and brittle. This holds for ANY constraint mechanism — attribute value, now-relative period (date macro), fixed calendar/clock part such as a time of day (datePart), existence/count of related child records, or gating by another field's value — all are apply-static-filter, never a handler/crt.InitRequest; classify the mechanism, not the wording. A gated constraint puts the gate (X = Y) in the rule's condition group with the apply-static-filter action on the target lookup. |
		       | email as mailto link, phone as tel link, text uppercase/lowercase, boolean inversion, number/currency formatting, any display-only transformation | `page-schema-converters` | Determines whether a converter is the right tool BEFORE you choose a component type. Skipping this causes AI to use crt.EmailInput instead of crt.ToEmailLink, or crt.Label instead of a converter binding. |
		       | business logic, cross-field orchestration, async data loading, side effects | `page-schema-handlers` | Handlers are NOT the same as converters. NOTE: restricting which records a lookup/ComboBox offers is NEVER handler "business logic" — regardless of the constraint mechanism (attribute value, relative period, fixed time-of-day, child existence/count, or gating by another field); it is an entity business rule (apply-static-filter). See the lookup-restriction row above. |
		       | add a button/menu item that runs a business process (`clicked` -> `crt.RunBusinessProcessRequest`) | `run-process-button` | Resolve the process with `get-process-signature` FIRST; every parameter key (in `processParameters` / `parameterMappings` / `recordIdProcessParameterName`) must be the process parameter CODE, not the caption — the platform silently drops values keyed by an unknown code. `processRunType` is required. No custom handler needed. |
		       | required field, max length, format enforcement with error message | `page-schema-validators` | Validators write to viewModelConfigDiff, not viewConfigDiff. |
		       | SDK service calls (SysSettingsService, HttpClientService, etc.) | `page-schema-creatio-devkit-common` | Correct import syntax and async patterns. |
		       | any static/generated filter where path normalization, lookup GUID resolution, date-relative wording, or child-record conditions are involved | `esq-filters` | Filter generation frequently fails on `...Id` path usage, lookup value shape, relative-date semantics, and missing EXISTS/backward-reference modeling. Read the dedicated filter guide before writing the payload. |
		       | adding a related/child list (a "detail") to a record page, OR a list/grid that must show only the records belonging to the current/open record (e.g. "add a Contacts detail", "list the Orders for this account", "filter the list by the page record / by page data") | `related-list` | A detail is a composite (`crt.ExpansionPanel` + `crt.DataGrid`), not one component, and "filter by page data" is declarative master-detail wiring: a child `crt.EntityDataSource`, an `isCollection` attribute, and a `modelConfig.dependencies` entry (`attributePath`/`relationPath`) — NOT an inline `filter`, NOT an init handler, NOT a seeded filter attribute. Read the dedicated guide before writing the body or the list shows all records / none. |
		       | working with a dashboard page (a page inherited from `BaseDashboardTemplate`) | `dashboards` | Mandatory read before composing the body — owns the canonical dashboard layout, widget sizing, and styling. |
		       | body contains `$Resources.Strings.*` or `#ResourceString(...)#`, or you plan to pass the `resources` parameter, OR your change adds/edits ANY user-visible string-like property (label, caption, title, tooltip, placeholder, description, button captions, tab/group titles, validator/dialog messages — examples, not exhaustive) | `page-schema-resources` | Every user-visible string must be a localizable-string binding, not an inline literal. Most resource registrations for DS-bound captions (e.g. `PDS_UsrStatus`) are unnecessary because the platform auto-provides them; guidance specifies which keys must vs must-not be registered, and `$Resources.Strings.*` is rejected in validator params. update-page / sync-pages / validate-page HARD-REJECT an inline literal for label, caption, title, tooltip, or placeholder — the save fails until you bind it and register the key's default-language value. |
		       | body contains `operation:"insert"` in `viewConfigDiff` for a standard field component (crt.Input, crt.NumberInput, crt.Checkbox, crt.ComboBox, crt.PhoneInput, crt.EmailInput, crt.DateTimePicker, crt.WebInput, crt.RichTextEditor, crt.ColorPicker, crt.ImageInput, crt.FileInput, crt.EncryptedInput, crt.Slider) | `page-modification` (this guide — see the "Inserted-field contract" section below) | A field control insert is more than a viewConfigDiff entry — missing the viewModelConfigDiff binding attribute or a resolvable label is silently catastrophic (no data source / blank caption) and now hard-rejected by `update-page` validation. |

		       STOP. Do NOT call get-component-info and pick a component type to solve a display transformation requirement until you have read `page-schema-converters` and confirmed the OOTB decision table does not cover your case. A common mistake is treating a display transformation as a component selection problem.

		       STOP. Touching resources at all? Read `page-schema-resources` first. This covers any body that contains `$Resources.Strings.*` or `#ResourceString(...)#`, any call that passes the `resources` parameter, AND any change that adds or modifies a user-visible string-like property — no exceptions, no "simple cases". This is enforced, not advisory: update-page / sync-pages / validate-page HARD-REJECT a body that sets `label`, `caption`, `title`, `tooltip`, or `placeholder` to an inline literal anywhere in `viewConfigDiff`. When you author a new placeholder/title/caption, also register its default-language value via `resources` (e.g. `placeholder: "$Resources.Strings.EmailField_placeholder"` + `resources: '{"EmailField_placeholder": "name@firm.com"}'`).

		       STOP. Want to limit / filter which records a lookup or ComboBox field shows? That is NOT a page edit. Do NOT add `filterConfig`, `staticFilters`, or `dataSourceFilters` to a datasource list attribute (e.g. `PDS_UsrAssignee_*_List`) to restrict the offered records — call `get-guidance` with name `business-rules` and create the restriction with `create-entity-business-rule` (`apply-static-filter`). A common mistake is treating "show only contacts who…" / "limit the Assignee field to…" as a page filterConfig change instead of an entity business rule.
		       STOP. "show the <LookupField> only for <records> where <condition>" (e.g. "show the Assignee field only for contacts where Age = 30") is NOT a field-visibility request. It means RESTRICT which records the lookup offers — use `create-entity-business-rule` (`apply-static-filter`). Do NOT set the field `visible:false`, do NOT use hide-element/show-element, and do NOT add a page attribute or filterConfig. hide-element/show-element are only for the field itself appearing/disappearing, never for which records a lookup lists.
		       STOP. Finding an EXISTING `filterConfig` / `staticFilters` already on the page does NOT mean a lookup-restriction request is already satisfied. That stray page filter is the wrong artifact (often left by a previous incorrect attempt). Do not short-circuit to "already done": create the entity business rule with `create-entity-business-rule` (`apply-static-filter`), and remove the stray page `filterConfig` from the datasource list attribute so it does not mask or duplicate the rule.

		       STOP — COMPONENT-TYPE VERIFICATION IS MANDATORY before you put ANY component `type` into a `viewConfigDiff` `insert`. NEVER author a `crt.*` type from memory, by analogy to another UI framework, or because the name "sounds right". `update-page`/`sync-pages` do NOT validate that a component type exists — they accept an unknown type and report `success: true`, but Freedom UI cannot resolve it and renders the unknown-component fallback: a grey placeholder box that literally prints the type string with a loading spinner (no control, no data, no buttons). A reported success is therefore NOT proof the component works.
		       For EVERY component type you intend to insert:
		       1. Call `get-component-info` with `component-type` set to that exact type and `environment-name` set to the same environment you are editing the page on. Confirm the response is `mode: "detail"` with `success: true`. A `success: false` not-found response (it returns the closest known types as suggestions) means the type does NOT exist on that environment — do NOT use it.
		       2. If you cannot find an OOTB component that actually satisfies the requirement — the catalog has no match, or the closest match does not do what the user asked — do NOT silently invent a type, do NOT substitute a similarly-named one, and do NOT create a custom component on your own initiative to "make it work". STOP and ASK THE USER how to proceed, presenting the two options explicitly: (a) use one of the existing OOTB components you found (list the closest candidates from `get-component-info`), or (b) build a new custom component. Wait for the user's choice before writing the body.

		       STOP. Adding a related/child list (a "detail"), or making a list show only the records that belong to the current/open record ("filter by page data"), is NOT a single-component insert and NOT an inline `filter` on the list attribute. It is a master-detail composite (`crt.ExpansionPanel` + `crt.DataGrid`) plus a child `crt.EntityDataSource`, an `isCollection` attribute, and a declarative `modelConfig.dependencies` entry (`attributePath` = child foreign-key column, `relationPath` = master id path such as `PDS.Id`). Read `get-guidance` with name `related-list` before writing the body. Do NOT scope the list with a `crt.HandleViewModelInitRequest` handler, a seeded empty-Guid filter, or a `filterAttributes` entry — the platform applies the dependency filter for you and waits for the master to load.

		       Replacing-schema concept
		       - When a Freedom UI designer saves changes to a page, Creatio creates a replacing schema in a "design package".
		       - The replacing schema inherits from the original and contains only the diff applied by the designer.
		       - The design package is NOT the same as the package that owns the original page schema.
		       - `get-page` and `update-page` automatically resolve the replacing schema through the design package.
		       - The editable target is always `hierarchy[0]` — the most-derived schema in the hierarchy.
		       - If no replacing schema exists, `hierarchy[0]` is the original schema and the design package is the original package.

		       Canonical page modification flow
		       0. WEB, MOBILE, OR BOTH (see Step 0): decide whether the requirement targets web, mobile, or both (default to web if unspecified); edit each affected page's web and/or mobile variant accordingly.
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
		       - Append does NOT support a body in the full `viewModelConfig`/`modelConfig` form (instead of the `*Diff` form); use replace mode for such bodies.
		       - Modifying an existing component: edit the operation that introduces it. Changing an own-body `insert` to a same-`name` `merge`/`move`/`remove` discards the insert, so unless a parent schema inserts that name the component is orphaned (disappears at runtime). Identify own-body inserts via `ownBodySummary.viewConfigDiffOps` (`operation: insert`) and edit their `values`; reserve `merge`/`move`/`remove` for parent-introduced elements. `update-page`/`sync-pages` warn (advisory, never block) when they detect this against the prior body.
		       - Append mode is permissive about the incoming body: pass only the sections you want to merge (for example, just `SCHEMA_VIEW_CONFIG_DIFF` + `SCHEMA_HANDLERS`). Missing sections are skipped; the current body's values stay intact for those sections. No need to pad with empty `[]` / `{}` markers.
		       - Never invent custom markers (for example `SCHEMA_WRAPPERS` is not a valid marker). Stick to: `SCHEMA_DEPS`, `SCHEMA_ARGS`, `SCHEMA_VIEW_CONFIG_DIFF`, `SCHEMA_VIEW_MODEL_CONFIG_DIFF`, `SCHEMA_MODEL_CONFIG_DIFF`, `SCHEMA_HANDLERS`, `SCHEMA_CONVERTERS`, `SCHEMA_VALIDATORS`. Static-form FormPage bodies (see "Static vs diff body forms" below) instead carry `SCHEMA_VIEW_MODEL_CONFIG` and `SCHEMA_MODEL_CONFIG` (no `_DIFF`) in place of the two `_DIFF` markers — those are equally valid; preserve whichever pair the body you read from `get-page` already uses, and do not convert one form to the other.

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

		       External-modification conflicts (checksum baseline)
		       - `get-page` stores a checksum baseline of the editable schema in `.clio-pages/{schema}/meta.json`. `update-page` and `sync-pages` automatically compare that baseline against the server BEFORE saving (same environment only).
		       - If the page was modified outside your session (the user edited it in the Creatio designer, deleted a component you added, another tool saved it), the write FAILS with `conflict: true` and `conflictDetails.reason`: `checksum-mismatch`, `schema-created-externally`, `schema-deleted-externally`, or `schema-uid-mismatch`. In `sync-pages` the conflict is per page (`conflict` / `conflict-details` on the page result) and the rest of the batch continues.
		       - RECOVERY — follow exactly this order:
		         1. Do NOT retry with the same body. Your local view of the page is stale.
		         2. Re-run `get-page` for the schema — this reloads body.js/bundle.json AND refreshes the baseline.
		         3. Inspect what changed externally, re-apply your intended change on top of the FRESH body (respect the user's external edits — do not restore components the user deleted).
		         4. Retry `update-page` / `sync-pages`.
		       - `force: true` (per page in `sync-pages`) skips the check and deliberately overwrites the external changes. Use it ONLY after you have informed the user about the external modifications and they explicitly confirmed overwriting them. Never set force pre-emptively.
		       - After a successful save the baseline refreshes automatically — consecutive updates in the same session do not false-conflict.
		       - No baseline (no prior `get-page`, legacy meta.json, different environment) → the check is skipped; flows behave exactly as before.
		       - A small race window between the check and the save remains (last write wins) — the check is a guard, not a transaction.

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

		       - "Inserted field 'X' (type 'Y') binds to '$Z' but the body does not declare attribute 'Z' in viewModelConfigDiff." — Step 2 missing entirely. Add the `viewModelConfigDiff` entry: `{"operation":"merge","path":[],"values":{"attributes":{"Z":{"modelConfig":{"path":"<DS>.<Column>"}}}}}`. If `Z` is supposed to come from a parent schema, change `operation:"insert"` to `operation:"merge"` on the `viewConfigDiff` entry instead.
		       - "Inserted field 'X' (type 'Y') binds to '$Z' which is declared in viewModelConfigDiff without the required nesting ... the control will render but read and write no data." — Step 2 present but FLAT. The attribute is declared directly under `values` (or under a `path:[]` entry with no `attributes` wrapper) instead of under `values.attributes`, so it lands at the `viewModelConfig` root and the runtime ignores it. Move it to the properly-nested form: `{"operation":"merge","path":[],"values":{"attributes":{"Z":{"modelConfig":{"path":"<DS>.<Column>"}}}}}` (or the legacy `path:["attributes"]` form).
		       - "Inserted field 'X' has label '$Resources.Strings.K' but resource 'K' is neither registered in the 'resources' parameter nor auto-provided by a DS-bound attribute." — Step 3: the label key `K` does not match the binding attribute. Set the label to `$Resources.Strings.<bindingAttribute>` (the SAME name as the control) so the platform auto-provides the caption from the DS-bound column — auto-provide is keyed by the attribute name, not the column code. Or add `{"K":"<Caption>"}` to the `resources` parameter to register `K` explicitly.

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
		       - `type` (the component type inside `values`) MUST be a type you confirmed exists via `get-component-info` for the target environment — see the mandatory COMPONENT-TYPE VERIFICATION STOP above. Never invent or guess a `crt.*` type; an unknown type saves successfully but renders as a broken placeholder. If no catalog component matches the requirement, stop and ask the user (use an existing component, or build a custom one).
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

		       Interpreting get-component-info response metadata
		       - Every response carries `resolvedTargetVersion` (the catalog version actually loaded) and `resolvedFrom` (the resolver tier that selected it).
		       - Resolve the platform version BEFORE you generate an implementation plan: pass `environment-name` so clio probes the stand and reports the real `resolvedFrom`. Do not plan a page change against an unverified component set.
		       - `resolvedFrom: "environment"` means clio resolved the platform version from the active environment AND the exact per-version catalog was loaded. Treat the catalog as authoritative for `update-page` and proceed — no confirmation needed.
		       - `resolvedFrom: "environment-superset"` means the platform version was known (probe-success or explicit `--version`) but the exact catalog was not published on the CDN, so `latest` was served as the closest available. The version is not a mystery, but the `latest` catalog may include components not yet present in an older GA target environment. Flag this to the user and verify critical component types against the actual environment before committing to an implementation plan.
		       - `resolvedFrom: "latest-fallback"` means no usable platform version was resolved (no active environment, cliogate < `2.0.0.32`, probe failed, or `CoreVersion` did not parse). The response carries the most recent platform catalog clio knows of, which may be a superset of the target environment. STOP: do NOT silently assume this component set. Tell the user the target platform version could not be determined and request explicit confirmation before proceeding against `latest`. You may still use the catalog for discovery, but a component or property it lists may not exist on the target stand — `update-page` rejecting it is a legitimate signal the catalog was wider than the platform.
		       - Do not paper over a `latest-fallback` by pinning a target version yourself. Fix the upstream signal (active environment, cliogate version) so the next call resolves to `"environment"` or `"environment-superset"`.
		       - Discover proactively: at the start of page work call `get-component-info` in list mode (omit `component-type`) to enumerate the full component set for the resolved version. Non-obvious components (e.g. `crt.Gallery`) are in the catalog — consider and suggest them when relevant instead of waiting for the user to ask you to search. On a **detail** response for a collection/visual type (`crt.DataGrid`, `crt.List`, `crt.FileList`, `crt.MultiList`, `crt.ImageInput`), a `relatedComponents` array points you to an overlooked better fit such as `crt.Gallery`; every detail response also carries a `discoveryTip` reminding you to list the full catalog. Heed both rather than authoring a component choice from memory. When the producer has populated it, a detail response also carries selection metadata — `synonyms`, `useCases`, `whenToUse`/`whenNotToUse`, a taxonomy `category`, and an `appliesToCustomEntities` flag with an `entityCouplingNote` (e.g. `crt.CommunicationOptions` cannot be built on a custom entity) — use it to match the request to the right component and to avoid an entity-incompatible type before composing the `update-page` body.

		       Detail-response payload shape (read once before composing `update-page` bodies; same on web and mobile flavors — pass `schema-type: "mobile"` to query the mobile catalog through the same pipeline)
		       - `inputs` — the curated input bindings for the component (e.g. `caption`, `disabled`, `color` on `crt.Button`). Each value carries `type` and may carry `default`, `description`, `values` (enum constraints), `items` (array element type), `keyType`/`valueType` (record shape). Map these directly onto the `values` object of a `viewConfigDiff` insert.
		       - `outputs` — the curated output bindings (events) for the component (e.g. `clicked`, `blurred`, `focused`). Output bindings are bound through `request` descriptors in the body — match each `outputs.<name>` to a `viewConfigDiff` entry's `values.<name>.request` and add a matching `handlers` entry with the same `request` string.
		       - `references.typeDefinitions` — the producer's named-type schemas referenced by `inputs`/`outputs` `type` strings (e.g. `"string | ButtonIcon | ButtonAnimatedIcon"`). When a `type` token is not a primitive (`string`/`number`/`boolean`/`array`/`object`/`Record`), look it up here to learn the allowed values (enum) or the nested `fields` shape. Without this, you cannot construct a valid `icon`, `columns`, `bulkActions`, etc.
		       - `properties` — present only for legacy catalog entries that did not migrate to the `inputs`/`outputs` split. Today the mobile catalog still ships in the legacy shape, so mobile detail responses carry `properties` and omit `inputs`/`outputs`/`references.typeDefinitions`; web responses carry the wrapped fields. Treat whichever is present as authoritative for that component — both describe the same surface, just different schema generations.
		       - `documentation` — opt-in long-form markdown for complex components (e.g. `crt.DataGrid`); concatenated from every file listed in the producer's `references.docs[]`. Use it as the source of truth for non-trivial composition rules (e.g. data-grid features matrix). Absent on simple components and on mobile entries today — do not interpret its absence as missing data.
		       - `resolvedTargetVersion` / `resolvedFrom` — present on every detail response regardless of `schema-type`. Mobile and web share the same async catalog pipeline, so both carry the resolver markers.

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
