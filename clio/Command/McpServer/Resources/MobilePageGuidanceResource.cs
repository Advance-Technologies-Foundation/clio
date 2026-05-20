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

		       ─────────────────────────────────────────────────────────────
		       PRE-EDIT CHECKLIST APPLICABILITY (web guide → mobile)
		       ─────────────────────────────────────────────────────────────
		       The web `page-modification` PRE-EDIT GUIDANCE CHECKLIST partially applies on mobile:
		         APPLIES IN FULL: `page-schema-resources`, entity-level `business-rules` (`create-entity-business-rule`).
		         PARTIAL:  `page-schema-converters` — read for concept; on mobile only the inline OOTB binding form (see OOTB list below) or a reference to a custom converter that already exists in a remote module is valid. The page body itself must not declare a `converters` section.
		         PARTIAL:  `page-schema-handlers` — read for concept; on mobile, handlers can only be referenced if they already exist in a remote module and the user explicitly asks for them. The page body itself must not declare a `handlers` section.
		         DOES NOT APPLY: `page-schema-validators`, `page-schema-creatio-devkit-common` — validators are not supported on mobile at all (not even OOTB), and the devkit-common AMD dependency does not exist on mobile.

		       If a web guide tells you to add a section this mobile guide forbids (validators / inline handlers or converters declared in the page body / AMD deps), the mobile rule wins.

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

		       ─────────────────────────────────────────────────────────────
		       VALIDATORS, CONVERTERS, HANDLERS — mobile constraints
		       ─────────────────────────────────────────────────────────────

		       Validators — not supported on mobile:
		         Mobile pages do not support validators at all, including OOTB validators.
		         Do not add a "validators" section. Do not reference any validator (custom or OOTB)
		         from a mobile page body. If the user asks for validation, implement it via entity-level
		         business rules (`create-entity-business-rule`) instead.

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

		       ─────────────────────────────────────────────────────────────
		       ONE DATA SOURCE PER PAGE (designer constraint)
		       ─────────────────────────────────────────────────────────────
		       The mobile designer disables multi-data-source. Define only one data source in modelConfigDiff.

		       ─────────────────────────────────────────────────────────────
		       COMPONENT REGISTRY — mobile components are separate
		       ─────────────────────────────────────────────────────────────
		       Before inserting a component, confirm it exists in the mobile registry:
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

		       ─────────────────────────────────────────────────────────────
		       ADAPTIVE BREAKPOINTS
		       ─────────────────────────────────────────────────────────────
		       crt.GridContainer supports adaptive per-breakpoint column overrides:
		         {
		           "type": "crt.GridContainer",
		           "columns": "1fr",
		           "adaptive": {
		             "small":  { "columns": "1fr" },
		             "medium": { "columns": "1fr 1fr" },
		             "large":  { "columns": "1fr 1fr 1fr" }
		           }
		         }
		       Breakpoints: "small" (phone portrait), "medium" (landscape/tablet portrait), "large" (tablet landscape).

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

		       ── Business process ────────────────────────────────────────

		       crt.RunBusinessProcessRequest
		         Starts a business process.
		         params: processName (string, required),
		                 processParameters? (Record<string, unknown>),
		                 recordIdProcessParameterName? (string),
		                 showNotification? (boolean),
		                 notificationText? (string),
		                 saveAtProcessStart? (boolean)

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

		       All five templates already provide crt.Scaffold — do NOT insert another one.

		       ─────────────────────────────────────────────────────────────
		       SAVE WORKFLOW
		       ─────────────────────────────────────────────────────────────
		       Identical to web pages. Use update-page and sync-pages — they auto-detect mobile JSON bodies.
		       """
	};

	/// <summary>
	/// Returns the canonical guidance article for mobile page creation and modification.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "mobile-page-modification-guidance")]
	[Description("Returns canonical MCP guidance for mobile Freedom UI page editing: plain JSON body format, validator and converter constraints, Scaffold merge patterns, component registry differences, and requests available on mobile.")]
	public ResourceContents GetGuide() => Guide;
}
