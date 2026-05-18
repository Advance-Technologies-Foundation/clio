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

		       Scope
		       - Use this guide when the task adds, changes, or references localizable strings (resources) in a Freedom UI page body.
		       - Resources are the mechanism for localizing captions, labels, titles, and error messages displayed on Freedom UI pages.
		       - Deciding whether to use resources — and which pattern to use — is a common source of inconsistency when editing pages.

		       ─────────────────────────────────────────────────────────────
		       RECOMMENDED APPROACH — binding-based resources
		       ─────────────────────────────────────────────────────────────

		       The recommended approach for referencing localizable strings in Freedom UI pages is the binding syntax:
		         $Resources.Strings.<ResourceKey>

		       This is a reactive binding expression resolved by the Freedom UI engine. It works for ALL localizable strings registered in the schema — not only for data-source-bound captions.

		       Resource key naming for data-bound controls
		       - The resource key always matches the view model attribute name that the control binds to — NOT the column name on its own.
		       - The view model attribute name is what appears in the binding expression (e.g. `$UsrName`, `$PDS_UsrColumn2_r2s859x`, `$PageParameters_UsrLookupParameter1_z257v57`). The resource key is the same identifier without the leading `$`.
		       - IMPORTANT: the platform auto-provides `$Resources.Strings.<AttributeName>` ONLY for view model attributes that are themselves bound to a data source column. The prefix in the attribute name (or the absence of one) does NOT decide this — what matters is the underlying DS binding.
		         * `$UsrName` resolves automatically only if the `UsrName` view model attribute is bound to a data source column (e.g. via `modelConfig.attributes.UsrName.modelConfig.path` pointing at a DS column). A plain attribute name with no DS binding will NOT produce an auto-provided resource string.
		         * `$PDS_UsrStatus` resolves automatically because the designer-generated `PDS_UsrStatus` attribute is bound to the `UsrStatus` column of the `PDS` data source.
		         * An attribute defined only in `viewModelConfigDiff` without a DS binding (e.g. a free-form local attribute) gets no auto-provided caption — you must register its resource key explicitly via `resources`.
		       - Real-world examples observed in pages (all bound to a DS column):
		         * Binding `$UsrName`                                  → `$Resources.Strings.UsrName` (plain attribute, no prefix)
		         * Binding `$PDS_UsrColumn2_r2s859x`                   → `$Resources.Strings.PDS_UsrColumn2_r2s859x` (designer-generated PDS_ prefix with hash suffix)
		         * Binding `$PageParameters_UsrLookupParameter1_z257v57` → `$Resources.Strings.PageParameters_UsrLookupParameter1_z257v57` (page-parameter attribute)
		       - The `PDS_` prefix is NOT mandatory. It appears when the page designer auto-generates the attribute against the primary data source, and the designer often appends a short hash suffix (e.g. `_r2s859x`). Manually authored attributes commonly use plain names without any prefix.
		       - Other data source names produce their own prefixes (e.g. `MyDs_Account`, `AttachmentListDS_Name`).
		       - You can also reference any custom localizable string registered in the schema's `localizableStrings` array using the same syntax: `$Resources.Strings.UsrMyCustomCaption`.

		       Rule: when adding `$Resources.Strings.<Key>` for a data-bound control, copy the attribute identifier from the existing binding (`bindTo` / `$<attr>`); do not invent a key from the column name. If the attribute is not bound to a DS, you must register the resource key yourself via `resources`.

		       ─────────────────────────────────────────────────────────────
		       #ResourceString(KeyName)# MACRO
		       ─────────────────────────────────────────────────────────────

		       `#ResourceString(KeyName)#` is a macro that gets replaced with the localizable string value during schema pre-processing.
		       - The key must be registered in the schema's `localizableStrings` array.
		       - Registration happens automatically when you pass the `resources` parameter on `update-page` / `sync-pages`.
		       - Inside `viewConfigDiff` string values (e.g. `label`, `caption`, `tooltip`, `placeholder`) both `$Resources.Strings.<Key>` and `#ResourceString(Key)#` are valid and interchangeable. Real pages mix both styles; prefer the binding form when in doubt because it is the reactive option.
		       - Data grid column captions (in list pages and embedded grids such as AttachmentList) are commonly authored with the macro form: `"caption": "#ResourceString(PDS_UsrName)#"`, `"caption": "#ResourceString(AttachmentListDS_Name)#"`. Either form works, but the macro form matches existing conventions in those areas.
		       - Use this macro (and only this macro) in contexts where the reactive binding engine does not process the value, notably validator params — see VALIDATOR PARAMS below.

		       ─────────────────────────────────────────────────────────────
		       DECISION TABLE — when to use each pattern
		       ─────────────────────────────────────────────────────────────

		       | Scenario | Pattern | Pass `resources` param? |
		       | --- | --- | --- |
		       | Data-bound form field label (default attribute caption) | `$Resources.Strings.<AttributeName>` (e.g. `$Resources.Strings.PDS_UsrStatus`) | NO — platform auto-provides for the bound attribute |
		       | Data-bound form field label (override caption) | `$Resources.Strings.<Key>` or `#ResourceString(<Key>)#` (interchangeable) | YES — register the override key with an explicit value |
		       | Data grid column caption (list page / embedded grid) | `#ResourceString(<DataSource>_<Column>)#` (convention) or `$Resources.Strings.<DataSource>_<Column>` | YES for custom columns; NO when the platform auto-provides the caption |
		       | Custom tab caption | `$Resources.Strings.UsrMyTab_caption` (or `#ResourceString(UsrMyTab_caption)#`) | YES — register the key with an explicit value |
		       | Expansion panel / group title | `$Resources.Strings.UsrMyGroup_title` (or `#ResourceString(UsrMyGroup_title)#`) | YES — register the key with an explicit value |
		       | Button caption (localizable) | `$Resources.Strings.UsrMyButton_caption` (or `#ResourceString(UsrMyButton_caption)#`) | YES — register the key with an explicit value |
		       | Button caption (simple, non-localizable) | Inline string: `"caption": "Click me"` | NO — no resource needed |
		       | Validator error message | `#ResourceString(UsrMyValidator_Message)#` (macro form required) | YES — always provide an explicit value |
		       | Inherited caption from parent schema | Already registered in parent | NO — do not re-register |
		       | Converter display values | None | NO — converters are pure value transforms, never use resources |

		       ─────────────────────────────────────────────────────────────
		       HOW TO PASS RESOURCES
		       ─────────────────────────────────────────────────────────────

		       Pass the `resources` parameter as a JSON object string mapping keys to their display values:
		         resources: '{"UsrDetailsTab_caption": "Details", "UsrSave_caption": "Save record"}'

		       Each key in the object corresponds to a localizable string that will be registered in the schema's `localizableStrings` array.
		       Values must be plain strings — no nesting, no arrays.
		       Always provide explicit values for all resource keys.

		       ─────────────────────────────────────────────────────────────
		       RESOURCE PRESERVATION
		       ─────────────────────────────────────────────────────────────

		       When `update-page` or `sync-pages` processes resources:
		       1. All existing `localizableStrings` entries are preserved (including platform entries like SaveButton, CancelButton, GeneralInfoTab_caption).
		       2. New entries from the `resources` parameter are added.
		       3. Existing entries are never deleted or overwritten — only new entries are added.

		       ─────────────────────────────────────────────────────────────
		       VALIDATOR PARAMS — special rule
		       ─────────────────────────────────────────────────────────────

		       Validator params (inside viewModelConfigDiff attribute validators) are not processed by the reactive binding engine.
		       The `$Resources.Strings.*` binding syntax does NOT work in validator params — it will be rejected by clio validation.
		       Use `#ResourceString(KeyName)#` for validator param values instead:

		       CORRECT:
		         "params": { "message": "#ResourceString(UsrMaxLength_Message)#" }

		       WRONG (rejected by clio validation):
		         "params": { "message": "$Resources.Strings.UsrMaxLength_Message" }

		       Read `page-schema-validators` for the full validator authoring guide.

		       ─────────────────────────────────────────────────────────────
		       COMMON MISTAKES
		       ─────────────────────────────────────────────────────────────

		       1. Using `$Resources.Strings.*` in validator params — validator params are not processed by the reactive binding engine; use `#ResourceString(KeyName)#` instead.
		       2. Forgetting to pass `resources` when adding custom localizable strings — keys without explicit values may not be registered, and the resource will not resolve at runtime.
		       3. Adding resources for inherited captions — parent schema resources are already registered; re-adding them is unnecessary (though harmless).
		       4. Re-registering keys the platform already provides — for view model attributes that are bound to a data source column, the platform auto-provides `$Resources.Strings.<AttributeName>` from the entity schema column caption. Do not pass these via `resources` unless you intend to override the caption. This auto-provision happens only when the attribute itself has a DS binding; attribute-name prefixes (`PDS_`, none, `PageParameters_`, etc.) do not by themselves trigger it.
		       5. Inventing data-source resource keys from column names — the key must match the view model attribute name from the binding (e.g. `$PDS_UsrColumn2_r2s859x` → `$Resources.Strings.PDS_UsrColumn2_r2s859x`), not the bare column name. The `PDS_` prefix is not guaranteed; some attributes use plain names like `UsrName` or other prefixes like `PageParameters_`.

		       ─────────────────────────────────────────────────────────────
		       WHEN NOT TO ADD RESOURCES
		       ─────────────────────────────────────────────────────────────

		       - Data-bound field labels that use the default attribute caption — the platform auto-provides them.
		       - Inherited captions from parent schemas — they are already registered.
		       - Simple non-localizable strings — inline them directly as string values.
		       - Converters — converters are pure value transforms and never use resources.
		       """
	};

	/// <summary>
	/// Returns the canonical guidance article for page localizable strings and resources.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "page-schema-resources-guidance")]
	[Description("Returns canonical MCP guidance for Freedom UI page localizable strings: when to use $Resources.Strings.* vs #ResourceString()#, resource key naming rules for data-bound attributes, resource parameter usage, and common mistakes.")]
	public ResourceContents GetGuide() => Guide;
}
