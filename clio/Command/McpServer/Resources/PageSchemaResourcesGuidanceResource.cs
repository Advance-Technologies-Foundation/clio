using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for Freedom UI page localizable strings (resources) through clio MCP.
/// </summary>
[McpServerResourceType]
public sealed class PageSchemaResourcesGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/page-schema-resources";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP page-schema resources guide

		       Scope: use when a Freedom UI page change adds, references, or modifies localizable strings (captions, labels, titles, validator messages).

		       ─────────────────────────────────────────────────────────────
		       WHEN TO USE A LOCALIZABLE STRING
		       ─────────────────────────────────────────────────────────────

		       Author user-visible string values as localizable-string bindings, not inline literals. The rule covers any string-like property the runtime renders to the user (e.g. `label`, `caption`, `title`, `tooltip`, `placeholder`, `description`, button/tab/group captions, validator and dialog messages — non-exhaustive). Inline literals are fine for non-displayed values: type/schema/attribute names, enum-like state values (`labelPosition`, `size`, `direction`, …), and binding/converter expressions. Applies equally to web and mobile.

		       ─────────────────────────────────────────────────────────────
		       ENFORCEMENT — HARD REJECT (not advisory)
		       ─────────────────────────────────────────────────────────────

		       `update-page`, `sync-pages`, and `validate-page` REJECT a body that sets `label`, `caption`, `title`, `tooltip`, or `placeholder` to an inline string literal anywhere in `viewConfigDiff` (including nested child components). The save fails with a diagnostic naming the node, the property, and the literal. To pass, author the value as `$Resources.Strings.<Key>` (or `#ResourceString(<Key>)#` for data-grid column captions and validator messages) and register the key where the platform does not auto-provide it. `description` is NOT hard-rejected (it also names non-display metadata), but localize it too when it is user-visible. A value that begins with `$` (any binding expression) or that is a non-string (e.g. `placeholder: false`) is never treated as a literal.

		       DANGLING BINDING — ALSO HARD REJECT (inserted widget/metric titles). A `title`/`caption`/`tooltip`/`placeholder` on a freshly INSERTED widget/container (`operation:"insert"`) that is bound as `$Resources.Strings.<Key>` or `#ResourceString(<Key>)#` is rejected when `<Key>` will NOT resolve — i.e. it is not passed in `resources`, is not a DS-bound attribute the platform auto-provides, and is not a `Usr`-prefixed key clio auto-derives. This closes the metric/chart-widget-title trap: the widget title is emitted as `config.title: "#ResourceString(IndicatorWidget_<slug>_title)#"`, whose key never starts with `Usr` and is not DS-bound, so it is registered ONLY if you pass it in `resources`. Omit it and the platform compiles the macro to `$Resources.Strings.IndicatorWidget_<slug>_title` and renders that raw string instead of the title. ALWAYS pair a widget title with `resources: '{"IndicatorWidget_<slug>_title": "<the title text>"}'`. (A `merge` is not checked — a parent schema may already provide the caption.)

		       CREATION RULE — POPULATE THE DEFAULT-LANGUAGE VALUE
		       When you introduce a NEW user-visible string (a placeholder, a custom title, a button caption, …) you must seed its default-language text yourself: choose a `<Key>`, point the property at `$Resources.Strings.<Key>`, and register `{"<Key>": "<the exact text you would have typed inline>"}` through the `resources` parameter. That registered value becomes the default-language entry in the page's `localizableStrings`; without it the binding resolves to an empty caption. Example: a placeholder you would have written as `"name@firm.com"` becomes `placeholder: "$Resources.Strings.EmailField_placeholder"` plus `resources: '{"EmailField_placeholder": "name@firm.com"}'`.

		       ─────────────────────────────────────────────────────────────
		       THE DECISION ALGORITHM
		       ─────────────────────────────────────────────────────────────

		       Two independent decisions per localizable string — do not conflate them:

		       1. REFERENCE SYNTAX in the page body
		          - Default: `$Resources.Strings.<ResourceKey>` (reactive binding; resolved by the Freedom UI engine for any key registered in the schema's `localizableStrings`).
		          - Exception: validator params — use `#ResourceString(<Key>)#` there (see VALIDATOR PARAMS).
		          - Inside `viewConfigDiff` string values (any user-visible string-like property — `label`, `caption`, `title`, `tooltip`, `placeholder`, `description`, etc. are examples, not an exhaustive list) both forms are interchangeable; prefer the binding form except where convention already established the macro form — notably data grid column captions in list pages and embedded grids like AttachmentList (`"#ResourceString(PDS_UsrName)#"`, `"#ResourceString(AttachmentListDS_Name)#"`).

		       2. REGISTER VIA `resources` PARAMETER? — depends on the target page, not the key name. Call `get-page` and inspect the merged `bundle.viewModelConfig.attributes.<Key>` (this is the effective runtime view, regardless of whether the attribute lives in the parent schema, inline `viewModelConfig`, or is added via `viewModelConfigDiff`). Then apply:
		          - Exists AND has a DS binding (`modelConfig.path` → data source column) → platform auto-provides the caption from the entity column → **DO NOT register** (unless overriding the caption with a custom value).
		          - Does NOT exist, OR exists without a DS binding → **MUST register** with an explicit value, or the binding will not resolve.

		       Same key name can require registration on one page and not on another. Prefixes (`PDS_`, `PageParameters_`, `MyDs_`, `AttachmentListDS_`, or none) are NOT signals — only the underlying DS binding matters.

		       KEY NAMING for data-bound controls — two distinct cases:
		       - AUTO-PROVIDED caption (DS-bound attribute, default caption, register nothing): the label key must be the VIEW-MODEL ATTRIBUTE NAME — the same attribute the control binds to — and that attribute must have a DS-bound `modelConfig.path`. The platform resolves the caption from the column the attribute points to. The attribute name is whatever the page declares (`PDS_UsrStatus`, `UsrName`, `Name123`, a designer hash-suffixed `PDS_UsrColumn2_r2s859x`) — all auto-provide as long as the label key equals the attribute name. Examples: attribute `$PDS_UsrStatus` (path `PDS.UsrStatus`) → label `$Resources.Strings.PDS_UsrStatus`; attribute `$Name123` (path `PDS.Name`) → label `$Resources.Strings.Name123`. The bare entity column code is NOT auto-provided unless it equals the attribute name — e.g. `$Resources.Strings.UsrStatus` renders blank for a `PDS_UsrStatus` attribute.
		       - EXPLICITLY REGISTERED caption (you pass a `resources` entry): the label key and the `resources` key must be identical; you control both. You may use the binding attribute name or any other key. Example: `resources: '{"PDS_UsrStatus": "Status"}'` paired with label `$Resources.Strings.PDS_UsrStatus`.
		       For `operation:"insert"`, update-page rejects an inserted field whose label is neither auto-provided (label key equal to the DS-bound binding attribute) nor explicitly registered — the bare column-code key form only works when it equals the attribute name or you register it.

		       PAIRED EXAMPLES — same field, opposite handling depending on the page
		       - Page A has `PDS_UsrStatus` bound to DS column `PDS.UsrStatus`, default caption:
		         ✅ Bind `$Resources.Strings.PDS_UsrStatus` (the DS-bound attribute name) and pass nothing — platform auto-provides the caption from the entity column.
		         ❌ Bind `$Resources.Strings.UsrStatus` (the bare column code) and pass nothing — NOT auto-provided (the key is not a DS-bound attribute name); on `operation:"insert"` this is rejected, and the label renders blank otherwise.
		         ✅ `resources: '{"PDS_UsrStatus": "Custom status caption"}'` paired with label `$Resources.Strings.PDS_UsrStatus` ONLY to deliberately override with a custom caption under that key.
		       - Page B has `UsrLocalFlag` declared in `viewModelConfigDiff` with no DS binding:
		         ✅ `resources: '{"UsrLocalFlag": "Local flag"}'` — required, or `$Resources.Strings.UsrLocalFlag` will not resolve.
		       - Page C does not declare `PDS_UsrStatus` at all:
		         ✅ `resources: '{"PDS_UsrStatus": "Status"}'` — required; platform has nothing to auto-provide.

		       ─────────────────────────────────────────────────────────────
		       DECISION TABLE
		       ─────────────────────────────────────────────────────────────

		       | Scenario | Reference syntax | Pass `resources` param? |
		       | --- | --- | --- |
		       | Key is the name of a DS-bound attribute on the page (the control's binding attribute), default caption acceptable | `$Resources.Strings.<attributeName>` (the binding attribute, any prefix/suffix — NOT the bare column code) | NO — platform auto-provides |
		       | DS-bound attribute, overriding the caption (any key form) | `$Resources.Strings.<Key>` | YES — register the same key with the override value |
		       | Key has NO matching DS-bound attribute (custom tab/group title, button caption, custom grid column, free-form `viewModelConfigDiff` attribute) | `$Resources.Strings.<Key>` (or `#ResourceString(<Key>)#` for grid column captions by convention) | YES — must register with an explicit value |
		       | Validator error message | `#ResourceString(<Key>)#` (macro form required) | YES — always register with an explicit value |
		       | Inherited caption from parent schema, simple non-localizable strings, converter display values | Inherited / inline string / N/A | NO |

		       ─────────────────────────────────────────────────────────────
		       HOW TO PASS, AND PRESERVATION
		       ─────────────────────────────────────────────────────────────

		       Pass the `resources` parameter as a JSON object string of `Key → display string` pairs; values must be plain strings (no nesting/arrays) and must be explicit:
		         `resources: '{"UsrDetailsTab_caption": "Details", "UsrSave_caption": "Save record"}'`

		       `update-page` / `sync-pages` preserve all existing `localizableStrings` entries (platform entries like `SaveButton`, `CancelButton`, `GeneralInfoTab_caption` included) and only ADD new entries — never delete or overwrite. Preservation is not permission to skip the registration check: confirm no DS-bound view model attribute with that name already provides the caption before adding it, otherwise you create unnecessary noise.

		       ─────────────────────────────────────────────────────────────
		       VALIDATOR PARAMS — special rule
		       ─────────────────────────────────────────────────────────────

		       Validator params (inside `viewModelConfigDiff` attribute validators) are not processed by the reactive binding engine. `$Resources.Strings.*` is rejected by clio validation here — use `#ResourceString(KeyName)#`.

		       ✅ `"params": { "message": "#ResourceString(UsrMaxLength_Message)#" }`
		       ❌ `"params": { "message": "$Resources.Strings.UsrMaxLength_Message" }`

		       See `page-schema-validators` for the full validator authoring guide.

		       ─────────────────────────────────────────────────────────────
		       COMMON MISTAKES
		       ─────────────────────────────────────────────────────────────

		       1. Passing `resources` for a key without checking the page first — if a DS-bound attribute with that exact name already exists, the platform auto-provides the caption and the entry is unnecessary. The key name alone cannot tell you; inspect `bundle.viewModelConfig.attributes.<Key>` from `get-page` (merged form — reflects parent, inline `viewModelConfig`, and `viewModelConfigDiff` additions in one view).
		       2. Treating a prefix (e.g. `PDS_`) as a signal of auto-provisioning — only the DS binding on the attribute matters. Manually authored attributes use plain names (`UsrName`); page-parameter attributes use `PageParameters_`; other data sources use their own prefixes (`MyDs_`, `AttachmentListDS_`).
		       3. Inventing data-source resource keys from column names — the key must match the view model attribute identifier from the binding, including any designer-generated hash suffix (e.g. `PDS_UsrColumn2_r2s859x`).
		       4. Using `$Resources.Strings.*` in validator params — rejected by clio validation; use `#ResourceString(KeyName)#`.
		       5. Re-registering inherited captions from a parent schema — already registered; the entry is unnecessary (though harmless).
		       6. Hardcoding a user-visible string as an inline literal — bind it via `$Resources.Strings.<Key>` (or `#ResourceString(<Key>)#` for validator params).
		       """
	};

	/// <summary>
	/// Returns the canonical guidance article for page localizable strings and resources.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "page-schema-resources-guidance")]
	[Description("Returns canonical MCP guidance for Freedom UI page localizable strings: when to use $Resources.Strings.* vs #ResourceString()#, resource key naming rules for data-bound attributes, resource parameter usage, and common mistakes.")]
	public ResourceContents GetGuide() => Guide;
}
