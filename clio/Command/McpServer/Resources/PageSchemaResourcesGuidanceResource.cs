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
		       - AUTO-PROVIDED caption (DS-bound attribute, default caption, register nothing): the label key must be the ENTITY COLUMN CODE — the LAST segment of the binding attribute's `modelConfig.path` — NOT the view model attribute name. The platform resolves auto-provided captions by column code only. Examples: attribute `$PDS_UsrStatus` (path `PDS.UsrStatus`) → label `$Resources.Strings.UsrStatus`; attribute `$UsrName` (path `PDS.UsrName`) → label `$Resources.Strings.UsrName`. The path-with-underscores form `$Resources.Strings.PDS_UsrStatus` is NOT auto-provided.
		       - EXPLICITLY REGISTERED caption (you pass a `resources` entry): the label key and the `resources` key must be identical; you control both. You may use the binding attribute name or any other key. Example: `resources: '{"PDS_UsrStatus": "Status"}'` paired with label `$Resources.Strings.PDS_UsrStatus`.
		       For `operation:"insert"`, update-page rejects an inserted field whose label is neither auto-provided (column-code key) nor explicitly registered — the attribute-name key form (`PDS_<columnCode>`) only works when you register it.

		       PAIRED EXAMPLES — same field, opposite handling depending on the page
		       - Page A has `PDS_UsrStatus` bound to DS column `PDS.UsrStatus`, default caption:
		         ✅ Bind `$Resources.Strings.UsrStatus` (column-code form) and pass nothing — platform auto-provides the caption from the entity column.
		         ❌ Bind `$Resources.Strings.PDS_UsrStatus` and pass nothing — NOT auto-provided (key is not the column code); on `operation:"insert"` this is rejected, and the label renders blank otherwise.
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
		       | Key is the entity column code of a DS-bound attribute on the page, default caption acceptable | `$Resources.Strings.<columnCode>` (last path segment, NOT the `PDS_`-prefixed attribute name) | NO — platform auto-provides |
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
