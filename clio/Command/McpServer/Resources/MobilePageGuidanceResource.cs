using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for mobile Freedom UI page creation and modification through clio MCP.
/// </summary>
[McpServerResourceType]
public sealed class MobilePageGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/mobile-page-modification";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Canonical guidance article accessible by name through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP mobile page modification guide

		       CALL THIS GUIDE BEFORE EDITING ANY MOBILE PAGE BODY
		       Mobile pages (schemaType=10) have fundamentally different rules from web pages.
		       Applying web page patterns to mobile pages will cause broken schemas or silent no-ops.
		       In the worst case the page becomes unrenderable and will not open at all.

		       ─────────────────────────────────────────────────────────────
		       WHAT CARRIES OVER FROM THE WEB `page-modification` GUIDE
		       ─────────────────────────────────────────────────────────────
		       PRECEDENCE: If anything in the web guide conflicts with this mobile guide,
		       the mobile guide wins — unconditionally. Treat the web guide as a mechanics
		       reference only; never copy a web page body, AMD wrapper, section name, or
		       component `type` value into a mobile page.

		       Read the web guide for the mechanics; this table tells you which sections to apply, skip, or treat with a mobile-specific twist. Anything not listed here is web-only.

		       | Web-guide section | On mobile |
		       | --- | --- |
		       | PRE-EDIT GUIDANCE CHECKLIST: `page-schema-resources`, entity-level `business-rules` (`create-entity-business-rules`), page-level `business-rules` (`create-page-business-rules`) | Applies with mobile-specific limits |
		       | PRE-EDIT: `page-schema-converters` | Concept only — page body must not declare a `converters` section; reference OOTB converters inline (see below) or an already-existing custom converter in a remote module |
		       | PRE-EDIT: `page-schema-handlers` | Concept only — page body must not declare a `handlers` section; only reference an already-existing remote handler when the user explicitly asks |
		       | PRE-EDIT: `page-schema-validators`, `page-schema-creatio-devkit-common` | Does NOT apply — no validator support on mobile (even OOTB), no devkit-common AMD dependency |
		       | Replacing-schema concept, Design-package resolution, Virtual package materialization | Applies in full — same backend; `page.designPackageUId` / `page.willCreateReplacingInDesignPackage` are returned for mobile pages too |
		       | Canonical page modification flow (list-pages → get-page → edit → update-page → verify) | Applies in full |
		       | `update-page optional-properties`, `update-page verify` flag, `sync-pages optional-properties` | Applies in full — identical semantics |
		       | Body formatting (match existing indentation, do not reformat existing code) | Applies in full — mobile JSON bodies are also saved verbatim; match the indentation style already present in the page |
		       | Known limitations (fail-closed design-package resolution on writes, best-effort fallback on reads) | Applies in full |
		       | `bundle.json` shape and `jq` recipes | Applies, with one caveat: `handlers` / `converters` / `validators` source strings are always empty (`'[]'` / `'{}'`) because mobile bodies do not author these sections |
		       | Web `Rules for viewConfigDiff` section — every bullet EXCEPT the FormPage `DataValueType → component` mapping in the next row (i.e. `operation` / `name` / `parentName` / `propertyName` / `index`, the view-engine `visible` property, and the user-visible string → `$Resources.Strings.*` rule) | Applies in full |
		       | Rules for viewConfigDiff: FormPage `DataValueType → component` mapping (web component registry) | Does NOT apply — pick the control from the MOBILE registry via `get-component-info schema-type: "mobile"`; the COMPONENT REGISTRY section below is the authoritative type list (note the canonical gotcha: Boolean → `crt.Toggle`, not `crt.Checkbox`) |
		       | Finding a container for a new component (`parentName`) | Applies in full — same `bundle.containers` lookup; common mobile container types: `crt.Scaffold` (root), `crt.GridContainer`, `crt.FlexContainer`, `crt.TabPanel`, `crt.TabContainer`, `crt.ExpansionPanel` |
		       | update-page write modes (`replace` / `append`) — including the "do NOT resend `raw.body`" CRITICAL warning and the `ownBodySummary.viewConfigDiffOperations > 0 → use append` rule of thumb, and the own-body `insert`-to-`merge`/`move`/`remove` downgrade warning | Applies, but mobile bodies have no handlers/converters/validators sections (those merge rules do not apply) and the append fragment is plain JSON, not AMD (see BODY FORMAT below) |

		       If a web guide tells you to add a section this mobile guide forbids (validators / inline handlers or converters declared in the page body / AMD deps), the mobile rule wins.

		       ─────────────────────────────────────────────────────────────
		       BUSINESS RULES — mobile support boundary
		       ─────────────────────────────────────────────────────────────
		       Mobile Freedom UI pages do support business rules. Business rule generation works
		       identically to web — mobile pages do not affect how rules are created or stored.
		       The same `create-page-business-rules` / `create-entity-business-rules` tools produce
		       valid rules for both web and mobile pages without any mobile-specific parameters.

		       Read `business-rules` for rule semantics; this guide adds only the mobile-specific boundary:
		         - Use `create-page-business-rules` for UI state changes on one mobile page.
		         - Use `create-entity-business-rules` when the rule should apply everywhere the entity is used,
		           or when the user needs validation-like enforcement that mobile page bodies cannot provide.
		         - Business rules are separate artifacts. Do not implement business-rule logic in the mobile page
		           body itself. Use `get-page` first when you need page attribute or element names for a page-level rule.

		       OFFLINE LIMITATION:
		         Mobile business rules support a limited set of conditions and actions compared
		         to the web runtime. In offline mode, not all business rule conditions and actions
		         are guaranteed to work.
		         When creating or modifying business rules for a mobile page, always ask the user
		         whether the page is used in offline mode. If yes, warn the user that some rules
		         may not function correctly offline and recommend verifying each rule on a real
		         device in offline mode after deployment.

		       ─────────────────────────────────────────────────────────────
		       WHEN A REQUEST CANNOT BE IMPLEMENTED ON MOBILE — NO INVENTION RULE
		       ─────────────────────────────────────────────────────────────
		       If the user asks for behavior that this guide forbids or that the mobile
		       runtime does not support, STOP and tell the user. Do NOT silently substitute,
		       approximate, or fabricate to make the request "work on paper".

		       Specifically, NEVER:
		         - Invent a `crt.*` component, request, or converter name that is not listed
		           in this guide or returned by `get-component-info schema-type: "mobile"`.
		         - Use a `crt.*` value that exists on web "because it sounds reasonable" —
		           if it is not in the mobile component/request/converter catalogue, treat
		           it as not implemented.
		         - Author a `handlers` / `converters` / `validators` section in the body to
		           emulate a missing feature.
		         - Add a second data source in `modelConfigDiff` to work around the
		           one-data-source-per-page constraint.
		         - Add a `crt.Scaffold` insert to "make the layout match" a web template.

		       Instead, report the limitation explicitly to the user. Use this shape:
		         - What was requested.
		         - Why mobile cannot do it (cite the rule: e.g. "validators are not supported
		           on mobile", "crt.DataGrid is web-only", "page body must not declare
		           handlers").
		         - The closest supported alternative, if any (e.g. entity-level business rule
		           via `create-entity-business-rules`, an OOTB converter from the allowed list,
		           a remote handler the user must implement separately and reference by name).
		         - Stop and ask the user how to proceed before writing the body.

		       ─────────────────────────────────
		       BODY FORMAT — plain JSON, not AMD
		       ─────────────────────────────────
		       Mobile page bodies are plain JSON objects. They are NOT AMD define(...) modules.
		       The top-level object has exactly three keys:

		           {
		             "viewConfigDiff": [],
		             "viewModelConfigDiff": [],
		             "modelConfigDiff": []
		           }

		       DO NOT add: "handlers", "converters", "validators" — these are AMD/web-only constructs.
		       DO NOT wrap the body in define(...).
		       DO NOT add any AMD marker pairs (SCHEMA_DEPS, SCHEMA_VIEW_CONFIG_DIFF, etc.).
		       For append mode, send a fragment containing only the `*Diff` arrays you want to merge; missing keys are skipped.

		       ─────────────────────────────────────────────────────────────
		       VALIDATORS, CONVERTERS, HANDLERS — mobile constraints
		       ─────────────────────────────────────────────────────────────

		       Validators — not supported on mobile:
		         Mobile pages do not support validators at all, including OOTB validators.
		         Do not add a "validators" section. Do not reference any validator (custom or OOTB)
		         from a mobile page body. If the user asks for validation, implement it via entity-level
		         business rules (`create-entity-business-rules`) instead.

		       Converters — OOTB inline, or reference an existing custom converter:
		         Do not add a "converters" section to the page body.
		         Reference OOTB converters as inline binding expression strings in viewConfigDiff values, e.g.:
		           "visible": "$HasUnsavedData | crt.InvertBooleanValue"
		           "visible": "$CardState | crt.IsEqual : 'edit'"
		         Converters can be chained: ""$Attr | crt.ConverterA : arg | crt.ConverterB""
		         All converters receive the piped value as the first argument.

		         Allowed OOTB converters:
		           crt.ToObjectProp      — extracts a property from an object
		             Arg: propertyName (string)
		             ""$FileAttr | crt.ToObjectProp : 'displayValue'""
		           crt.InvertBooleanValue — negates a boolean (true → false)
		             No args.
		             ""$HasUnsavedData | crt.InvertBooleanValue""
		           crt.IsEqual           — compares piped value for equality, returns boolean
		             Arg: compareValue (string | attribute ref)
		             ""$CardState | crt.IsEqual : 'edit'""
		           crt.AndBooleanValue   — logical AND of piped boolean with another boolean
		             Arg: otherValue (boolean attribute ref)
		             ""$SomeFlag | crt.AndBooleanValue : $AnotherFlag""
		           crt.IsInArray         — checks if piped value is in an array, returns boolean
		             Arg: array (attribute ref)
		             ""$Name | crt.IsInArray : $AllowedNames""
		           crt.Concat            — concatenates piped string with another value
		             Arg: appendValue (string | attribute ref)
		             ""$Prefix | crt.Concat : $Suffix""
		           crt.ToCollectionFilters — converts selected collection items into filters
		             Args: collectionName (string), selectionState (attribute ref)
		             ""$Items | crt.ToCollectionFilters : 'Items' : $DataTable_SelectionState""

		         Custom converters: only reference one if the user explicitly asks for it AND it already
		         exists in a remote module (the page body still must not declare a `converters` section).

		       Handlers — reference-only, from an existing remote module:
		         Do not add a "handlers" section to the page body.
		         Custom request handlers can be referenced (e.g. via `clicked` binding to a custom request
		         type) only if the user explicitly asks for them AND the handler is already implemented in
		         a remote module. Do not author handler code inside the mobile page body.

		       ────────────────────────────────────────────────────────
		       crt.Scaffold — do NOT re-insert
		       ────────────────────────────────────────────────────────
		       All five mobile templates already insert one crt.Scaffold.
		       Your page inherits it. DO NOT add another Scaffold insert — it will create
		       a duplicate root element.

		       To add content inside the Scaffold:
		       - Patch it:  { "operation": "merge", "name": "Scaffold", "values": { ... } }
		       - Add child: { "operation": "insert", ..., "parentName": "Scaffold", "propertyName": "items" }

		       viewConfigDiff INSERTS ADDRESS THE SLOT BY propertyName ONLY — never use "path" in a
		       viewConfigDiff insert (e.g. NOT "path": ["tools"]; use "propertyName": "tools"). "path" is
		       the addressing mechanism for viewModelConfigDiff / modelConfigDiff only; a viewConfigDiff
		       insert that uses "path" is silently dropped by the differ.

		       ─────────────────────────────────────────────────────────────

		       ─────────────────────────────────────────────────────────────
		       COMPONENT REGISTRY — MOBILE COMPONENTS ONLY (CRITICAL)
		       ─────────────────────────────────────────────────────────────
		       ONLY components registered in the MOBILE registry can be used on a mobile page.
		       Web components WILL NOT WORK on mobile — they are not loaded by the mobile runtime
		       and will result in a broken page (blank slot, runtime error, or silent no-op).
		       This is a hard platform boundary, not a styling difference:
		         - Mobile and web have separate component registries and separate runtimes.
		         - A component name that exists on web (e.g. `crt.Checkbox`, `crt.DataGrid`)
		           is NOT automatically available on mobile even if it sounds generic.
		         - Do NOT copy `type` values from a web page body into a mobile page body.

		       MANDATORY before inserting any component into a mobile page:
		         get-component-info schema-type: "mobile"

		       Key mobile-specific types:
		         crt.Toggle          — Use for Boolean fields (NOT crt.Checkbox — mobile designer maps Boolean → Toggle)
		         crt.BarcodeScanner  — Barcode/QR scanner; mobile-only
		         crt.FloatingActionButton — Floating action button (set on Scaffold.floatAction)
		         crt.Sort            — Sort control for list pages; mobile-only
		         crt.QuickFilterGroup — Group of quick filter chips; mobile-only

		       NOT available in mobile (web-only):
		         crt.DataGrid, crt.HtmlEditor, crt.PasswordInput, crt.EncryptedInput,
		         crt.ColorPicker, crt.TagSelect, crt.MultiSelect, crt.IFrame,
		         crt.Chat, crt.Dashboards

		       VERIFY, DON'T DOWNGRADE. Neither list above is exhaustive — `get-component-info
		       schema-type: "mobile"` is the authoritative source for what runs on mobile. A type
		       existing on web does NOT make it web-only: if it IS in the mobile catalog it IS
		       implemented, so use it (the inverse of the NO INVENTION rule above). When a column
		       has a specific data type (phone, email, number, date, etc.), do NOT default it to a
		       generic input out of caution — check the catalog you already retrieved and PREFER
		       the matching specialized input. Fall back to a generic input only when the catalog
		       has no specialized match.

		       ─────────────────────────────────────────────────────────────
		       ADAPTIVE BREAKPOINTS
		       ─────────────────────────────────────────────────────────────
		       The web→mobile conversion PROPOSES an adaptive layout for containers that group 2+ fields
		       (stack on phone, 2 columns on tablet) — present it to the user, who can adjust or decline.
		       Apply this section whenever you want per-screen placement; otherwise rely on the static
		       `columns` / `layoutConfig` values.

		       Breakpoints: "small" (phone portrait), "medium" (landscape / tablet portrait), "large" (tablet landscape).
		       The mobile designer "Tablet portrait" / "Tablet landscape" preview switcher maps to `medium` / `large`.

		       Adaptive layout has TWO sides — you must edit both for a real responsive layout:

		       (1) CONTAINER side — `adaptive` on crt.GridContainer (sibling of `columns`).
		           Defines how many grid columns exist at each breakpoint.
		           Both string ("1fr 1fr") and array (["1fr","1fr"]) forms are accepted by the runtime.

		           {
		             "operation": "merge",
		             "name": "AreaProfileContainer",
		             "values": {
		               "adaptive": {
		                 "small":  { "columns": ["1fr"] },
		                 "medium": { "columns": ["1fr", "1fr"] },
		                 "large":  { "columns": ["1fr", "1fr", "1fr"] }
		               }
		             }
		           }

		       (2) CHILD side — `layoutConfig.adaptive` on each child component (button, field, label, ...).
		           Defines the explicit grid cell the child occupies at each breakpoint.
		           Per breakpoint keys: `row` (1-based), `column` (1-based), `colSpan`, `rowSpan`.

		           Without per-breakpoint `layoutConfig.adaptive`, children fall back to the static
		           `layoutConfig.{row,column,colSpan,rowSpan}` values and the container's adaptive
		           `columns` change alone will NOT reflow them — the result is a wider container with
		           items still pinned to their original cells.

		       Worked example — three buttons that stack on phone, wrap 2+1 on tablet portrait, and
		       line up 1×3 on tablet landscape (see UsrMobilePage_07zg9nr for a live reference):

		           // Container: 1 / 2 / 3 columns by breakpoint
		           { "operation": "merge", "name": "AreaProfileContainer", "values": {
		               "adaptive": {
		                 "small":  { "columns": ["1fr"] },
		                 "medium": { "columns": ["1fr","1fr"] },
		                 "large":  { "columns": ["1fr","1fr","1fr"] }
		               }
		           }}

		           // Button 1 — always top-left
		           { "operation": "insert", "name": "Button1", "parentName": "AreaProfileContainer",
		             "propertyName": "items", "index": 0,
		             "values": { "type": "crt.Button", "caption": "...",
		               "layoutConfig": { "adaptive": {
		                 "small":  { "row": 1, "column": 1, "colSpan": 1, "rowSpan": 1 },
		                 "medium": { "row": 1, "column": 1, "colSpan": 1, "rowSpan": 1 },
		                 "large":  { "row": 1, "column": 1, "colSpan": 1, "rowSpan": 1 }
		               }}
		             }}

		           // Button 2 — below on phone, right on tablet portrait, middle on tablet landscape
		           { "operation": "insert", "name": "Button2", "parentName": "AreaProfileContainer",
		             "propertyName": "items", "index": 1,
		             "values": { "type": "crt.Button", "caption": "...",
		               "layoutConfig": { "adaptive": {
		                 "small":  { "row": 2, "column": 1, "colSpan": 1, "rowSpan": 1 },
		                 "medium": { "row": 1, "column": 2, "colSpan": 1, "rowSpan": 1 },
		                 "large":  { "row": 1, "column": 2, "colSpan": 1, "rowSpan": 1 }
		               }}
		             }}

		           // Button 3 — third row on phone, second row on tablet portrait, third column on landscape
		           { "operation": "insert", "name": "Button3", "parentName": "AreaProfileContainer",
		             "propertyName": "items", "index": 2,
		             "values": { "type": "crt.Button", "caption": "...",
		               "layoutConfig": { "adaptive": {
		                 "small":  { "row": 3, "column": 1, "colSpan": 1, "rowSpan": 1 },
		                 "medium": { "row": 2, "column": 1, "colSpan": 1, "rowSpan": 1 },
		                 "large":  { "row": 1, "column": 3, "colSpan": 1, "rowSpan": 1 }
		               }}
		             }}

		       Rules of thumb:
		         - Define `adaptive` on the GridContainer AND `layoutConfig.adaptive` on every child
		           that needs to move between breakpoints. Skipping either side breaks the layout.
		         - Use 1-based `row` and `column` — the runtime reflows children by these per breakpoint
		           (one item per cell). `colSpan` / `rowSpan` are serialized as 1 to match the mobile
		           designer's format, but are NOT honored per-item by the runtime; do not rely on them to
		           span cells. To make an item wider, give the container fewer columns at that breakpoint.
		         - Keep all three breakpoints (`small`, `medium`, `large`) populated even when two
		           share the same cell — the designer always serialises the full set and partial maps
		           may render as empty cells on the missing breakpoint.
		         - Adaptive child placement is supported inside crt.GridContainer. For crt.FlexContainer,
		           reflow happens through the container's own `direction` / `wrap` properties, not
		           through child `layoutConfig.adaptive`.

		       ─────────────────────────────────────────────────────────────
		       FIELD GROUPING IN CONTAINERS (mobile layout convention)
		       ─────────────────────────────────────────────────────────────
		       On mobile pages, fields MUST be grouped inside crt.GridContainer instances
		       with the "primary" color. This gives each field group a visually distinct
		       card-like surface that matches the native mobile design language.

		       Default field container pattern:
		         {
		           "operation": "insert",
		           "name": "<GroupName>Container",
		           "values": {
		             "type": "crt.GridContainer",
		             "columns": "1fr",
		             "color": "primary",
		             "borderRadius": "medium",
		             "padding": { "top": "medium", "right": "medium", "bottom": "medium", "left": "medium" },
		             "items": []
		           },
		           "parentName": "GeneralTabContainer",
		           "propertyName": "items",
		           "index": 0
		         }

		       Rules:
		         - Always set "color": "primary" on field-group containers (not "default").
		         - If the page bundle already provides containers with "color": "primary"
		           (visible in the existing viewConfigDiff from `get-page`), reuse them as
		           field parents instead of inserting new ones.
		         - Group logically related fields together in one container.
		         - Use separate containers for distinct field groups (e.g. general info,
		           communication details, address fields).
		         - Insert individual field components as children of these containers, NOT
		           directly into the Scaffold items.

		       ─────────────────────────────────────────────────────────────
		       REQUESTS AVAILABLE ON MOBILE
		       ─────────────────────────────────────────────────────────────
		       Use these built-in requests in viewConfigDiff event bindings (e.g. `clicked`,
		       `valueChange`, `updated`). Do NOT invent request types not on this list.
		       Params listed below go into the `params` object of the binding:
		         "clicked": { "request": "crt.<Name>", "params": { ... } }

		       ── Navigation ──────────────────────────────────────────────

		       crt.OpenPageRequest
		         Opens a Freedom UI page by schema name.
		         params: schemaName (string, required)

		       crt.ClosePageRequest
		         Closes the current page. Triggers discard-changes confirmation when
		         there are unsaved changes.
		         params: (none)

		       ── Record operations ───────────────────────────────────────

		       crt.CreateRecordRequest
		         Creates a new record and opens the edit page.
		         params: entityName? (string), defaultValues? ([{attributeName, value}])

		       crt.UpdateRecordRequest
		         Opens an existing record for editing.
		         params: entityName? (string), recordId? (string)

		       crt.DeleteRecordRequest
		         Deletes a record.
		         params: recordId? (string)

		       crt.CopyRecordRequest
		         Creates a copy of an existing record.
		         params: entityName? (string), recordId (string, required)

		       crt.SaveRecordRequest
		         Saves all pending changes on the current page.
		         params: preventCardClose? (boolean — keep the page open after save)

		       crt.CancelRecordChangesRequest
		         Reverts unsaved changes on the current record; page stays open.
		         params: (none)

		       ── Data ────────────────────────────────────────────────────

		       crt.LoadDataRequest
		         Reloads data from a data source.
		         params: dataSourceName? (string), updateCache? (boolean)

		       crt.QuickFilterRequest
		         Applies a quick filter to a list/value attribute.
		         params: filterValue? (depends on the filter attribute)

		       ── List items ──────────────────────────────────────────────

		       crt.CreateListItemRequest
		         Creates an item in a list/collection.
		         params: defaultValues? ([{attributeName, value}])

		       crt.UpdateListItemRequest
		         Updates an item in a list/collection.
		         params: recordId? (string)

		       crt.DeleteListItemRequest
		         Deletes an item from a list/collection.
		         params: recordId? (string)

		       ── Advanced ────────────────────────────────────────────────

		       crt.ExecuteExpressionRequest
		         Evaluates an expression via the mobile expression engine.
		         params: expression (string, required)

		       ── Business process ────────────────────────────────────────

		       crt.RunBusinessProcessRequest
		         Starts a business process. processName AND processRunType are both REQUIRED
		         (e.g. 'ForTheSelectedPage' for the current record; 'RegardlessOfThePage' for none).
		         "For the selected page" maps to processRunType: 'ForTheSelectedPage' — setting
		         recordIdProcessParameterName alone does NOT select the run type.
		         FULL parameter contract is the run-process-button guide (single source of truth):
		         call get-guidance run-process-button and resolve the process with get-process-signature
		         FIRST. Keys in processParameters / parameterMappings / recordIdProcessParameterName are
		         process parameter CODES, NOT captions — a wrong code is silently dropped.

		       ── Files ───────────────────────────────────────────────────

		       crt.UploadFileRequest
		         Uploads a file. On mobile supports camera, gallery, and file picker.
		         params: viewElementName? (string),
		                 itemsAttributeName? (string),
		                 maximumAllowedFileSize? (number, MB),
		                 allowedFileTypes? (string, comma-separated extensions),
		                 fileEntitySchemaName? (string),
		                 recordEntitySchemaName? (string),
		                 recordColumnName? (string),
		                 recordId? (string)

		       crt.DeleteFileRequest
		         Deletes an uploaded file.
		         params: fileName? (string),
		                 entityName? (string, defaults to 'SysFile'),
		                 recordId? (string),
		                 itemsAttributeName? (string)

		       ── Dialogs and lookups ─────────────────────────────────────

		       crt.ShowDialogRequest
		         Shows a confirmation or informational dialog.
		         params: title? (string),
		                 message (string, required),
		                 actions (DialogAction[], required — each has caption, key)

		       crt.OpenLookupPageRequest
		         Opens a lookup selection page.
		         params: entitySchemaName? (string),
		                 filtersConfig? ({filterAttributes: [{name, value?}]}),
		                 features? ({create?: {enabled}, select?: {multiple?}}),
		                 selectionState? ({type: 'specific', selected: []}),
		                 caption? (string)

		       ── Communication options ───────────────────────────────────

		       crt.AddCommunicationOptionsRequest
		         Opens add-communication-option UI (phone, email, etc.).
		         params: viewElementName (string, required)

		       crt.CreateCommunicationOptionRequest
		         Creates a communication option record.
		         params: masterRecordColumnName (string, required),
		                 componentName (string, required),
		                 values (Record<string, unknown>, required),
		                 indexToInsert? (number)

		       crt.RemoveCommunicationOptionRequest
		         Removes a communication option.
		         params: recordId (string, required),
		                 collectionAttributeName (string, required)

		       ── Mobile-only requests (native device capabilities) ──────

		       crt.SetAttributeFromBarcodeRequest
		         Triggers the barcode/QR scanner and writes the result to an attribute.
		         params: attributeName (string, required),
		                 lookupFilterColumn? (string — filter lookup values by scanned code)

		       crt.OpenAddressOnMapRequest
		         Opens the native maps app with the specified address.
		         params: query (string[], required — address parts)

		       crt.OpenCustomWebViewPageRequest
		         Opens an in-app web view.
		         params: controllerName? (string),
		                 viewXClass? (string),
		                 extensionsModel? (string),
		                 parameters? (Record<string, JsonData>)

		       ─────────────────────────────────────────────────────────────
		       MOBILE PAGE NAMING CONVENTIONS (Creatio standard)
		       ─────────────────────────────────────────────────────────────
		         Mobile form page:  Usr<Entity>_MobileFormPage
		         Mobile list page:  Usr<Entity>_MobileListPage

		       ─────────────────────────────────────────────────────────────
		       TEMPLATE HIERARCHY (for reference)
		       ─────────────────────────────────────────────────────────────
		       BaseMobileTemplate       — root template shared by page and list branches (inserts Scaffold + MainContainer)
		       ├── BaseMobilePageTemplate   — record/form pages (adds CancelButton, SaveButton, FAB)
		       │     └── MobilePageWithTabsFreedomTemplate — tabbed record pages (GeneralInfoTab, FeedTab, AttachmentsTab)
		       └── BaseMobileListTemplate   — list/section pages (adds search, FAB, HeaderContainer, crt.List)
		       BlankMobilePageTemplate      — standalone bare Scaffold, no parent, no content

		       `list-page-templates schema-type=mobile` returns 4 selectable templates (the abstract `BaseMobileTemplate` shown above is a parent only, not user-selectable). All of them already provide `crt.Scaffold` — do NOT insert another one.
		       """
	};

	/// <summary>
	/// Returns the canonical guidance article for mobile page creation and modification.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "mobile-page-modification-guidance")]
	[Description("Returns canonical MCP guidance for mobile Freedom UI page editing: plain JSON body format, validator and converter constraints, Scaffold merge patterns, component registry differences, and requests available on mobile.")]
	public ResourceContents GetGuide() => Guide;
}
