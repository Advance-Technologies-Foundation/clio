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
		         PARTIAL:  `page-schema-converters` — read for concept; only the inline OOTB binding form is valid on mobile (see OOTB list below).
		         DOES NOT APPLY: `page-schema-handlers`, `page-schema-validators`, `page-schema-creatio-devkit-common` — the corresponding mobile sections do not exist.

		       If a web guide tells you to add a section this mobile guide forbids (handlers / validators / custom converters / AMD deps), the mobile rule wins.

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

		       Validators — do not generate:
		         Mobile validators require AMD remote modules that cannot be AI-generated.
		         Do not write a "validators" section. If the user explicitly names a custom validator
		         from an existing remote module, it may be referenced, but do not add it to the body.

		       Converters — OOTB only, no custom section:
		         Do not add a "converters" section. Reference OOTB converters only as inline binding
		         expression strings in viewConfigDiff values, e.g.:
		           "visible": "$HasUnsavedData | crt.InvertBooleanValue"
		           "visible": "$CardState | crt.IsEqual : 'edit'"
		         Allowed OOTB converters:
		           crt.ToObjectProp, crt.InvertBooleanValue, crt.IsEqual, crt.AndBooleanValue,
		           crt.IsInArray, crt.Concat, crt.ToCollectionFilters

		       Handlers — not supported:
		         Do not add a "handlers" section to mobile pages.

		       ────────────────────────────────────────────────────────
		       crt.Scaffold — do NOT re-insert
		       ────────────────────────────────────────────────────────
		       All four mobile templates already insert one crt.Scaffold.
		       Your page inherits it. DO NOT add another Scaffold insert — it will create
		       a duplicate root element.

		       To add content inside the Scaffold:
		       - Patch it:  { "operation": "merge", "name": "Scaffold", "values": { ... } }
		       - Add child: { "operation": "insert", ..., "parentName": "Scaffold", "propertyName": "items" }

		       ─────────────────────────────────────────────────────────────
		       ONE DATA SOURCE PER PAGE (designer constraint)
		       ─────────────────────────────────────────────────────────────
		       The mobile designer disables multi-data-source (disableMultiDataSource: true).
		       Define only one data source in modelConfigDiff.

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
		       Use these built-in requests in button clicked attributes or viewModelConfigDiff.
		       Do NOT invent request types not on this list.

		       Common requests (also available on web):
		         crt.OpenPageRequest, crt.ClosePageRequest
		         crt.SaveRecordRequest, crt.CancelRecordChangesRequest
		         crt.CreateRecordRequest, crt.UpdateRecordRequest, crt.DeleteRecordRequest
		         crt.CopyRecordRequest
		         crt.LoadDataRequest, crt.RunBusinessProcessRequest
		         crt.UploadFileRequest, crt.DeleteFileRequest
		         crt.ShowDialogRequest
		         crt.OpenLookupPageRequest
		         crt.AddCommunicationOptionsRequest, crt.CreateCommunicationOptionRequest,
		         crt.RemoveCommunicationOptionRequest

		       Mobile-only requests (native device capabilities):
		         crt.SetAttributeFromBarcodeRequest  — trigger barcode/QR scanner, write result to an attribute
		         crt.OpenAddressOnMapRequest          — open native maps app with an address
		         crt.OpenCustomWebViewPageRequest     — open an in-app web view

		       ─────────────────────────────────────────────────────────────
		       MOBILE PAGE NAMING CONVENTIONS (Creatio standard)
		       ─────────────────────────────────────────────────────────────
		         Mobile form page:  Usr<Entity>_MobileFormPage
		         Mobile list page:  Usr<Entity>_MobileListPage

		       ─────────────────────────────────────────────────────────────
		       TEMPLATE HIERARCHY (for reference)
		       ─────────────────────────────────────────────────────────────
		       BlankMobilePageTemplate  — bare Scaffold, no content
		       BaseMobilePageTemplate   — record/form pages (adds CancelButton, SaveButton, FAB)
		         └── MobilePageWithTabsFreedomTemplate — tabbed record pages (GeneralInfoTab, FeedTab, AttachmentsTab)
		       BaseMobileListTemplate   — list/section pages (adds search, FAB, HeaderContainer, crt.List)

		       All templates already insert crt.Scaffold — do NOT insert another one.

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
