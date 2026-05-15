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

		       ─────────────────────────────────────────────────────
		       UNIFIED WORKFLOW — same tools as web pages
		       ─────────────────────────────────────────────────────
		       The standard get-page → update-page workflow applies to both web AND mobile pages.

		       1. get-page  — reads body.js (editable own body) and bundle.json (merged view).
		       2. Edit the body.js content (plain JSON).
		       3. update-page — saves the modified body.
		       4. Optionally use verify: true to confirm the save.

		       bundle.json IS reliable for mobile pages after this fix.
		       Read bundle.json to understand what is already inherited from the parent template
		       before composing new diff operations.

		       DO NOT use update-client-unit-schema for mobile pages — use update-page.
		       DO NOT use sync-pages with validate: false to bypass validation — mobile detection
		       is automatic; disallowed constructs are rejected actively with clear error messages.

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
		       ONE DATA SOURCE PER PAGE
		       ─────────────────────────────────────────────────────────────
		       Mobile pages support only one data source. Do not define multiple data sources
		       in modelConfigDiff.

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
		       MOBILE-SAFE REQUESTS
		       ─────────────────────────────────────────────────────────────
		       Use these built-in requests in button clicked attributes or viewModelConfigDiff.
		       Do NOT invent request types not on this list:

		         crt.OpenPageRequest, crt.ClosePageRequest
		         crt.SaveRecordRequest, crt.CancelRecordChangesRequest
		         crt.CreateRecordRequest, crt.UpdateRecordRequest, crt.DeleteRecordRequest
		         crt.LoadDataRequest, crt.RunBusinessProcessRequest, crt.UploadFileRequest

		       Specialized requests (barcode, NFC, communication options, quick-filter updates):
		         Reference only when explicitly requested by the user.

		       ─────────────────────────────────────────────────────────────
		       RESOURCE STRINGS
		       ─────────────────────────────────────────────────────────────
		       Use "$Resources.Strings.ElementName_label" or "#ResourceString(Key)#" for
		       user-visible text, consistent with what the templates use.

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
		       """
	};

	/// <summary>
	/// Returns the canonical guidance article for mobile page creation and modification.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "mobile-page-modification-guidance")]
	[Description("Returns canonical MCP guidance for mobile Freedom UI page editing: plain JSON body format, unified get-page/update-page workflow, validator and converter constraints, component registry differences, and Scaffold merge patterns.")]
	public ResourceContents GetGuide() => Guide;
}
