using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for editing Freedom UI page converters through clio MCP.
/// </summary>
[McpServerResourceType]
public sealed class PageSchemaConvertersGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/page-schema-converters";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	internal static readonly TextResourceContents Guide = new() {
			Uri = ResourceUri,
			MimeType = "text/plain",
			Text = """
			       clio MCP page-schema converters guide

			       Scope
			       - Use this guide when the task transforms or reformats a display value in a Freedom UI page body.
			       - Converters are functions that modify the VALUE of a ViewModel attribute for display purposes only — they do NOT change the underlying model data.
			       - Resolve exact MCP tool contracts through `get-tool-contract` before any write workflow.
			       - Keep converter work inside clio-owned page-body guidance. This guide covers SCHEMA placement (not remote module placement).

			       Canonical runtime flow
			       - Prefer `list-pages -> get-page -> sync-pages -> get-page` for deployed page-schema converter edits.
			       - Use the `raw.body` field from the `get-page` response as the editable source of truth and preserve the outer AMD module structure.
			       - `SCHEMA_CONVERTERS` must contain an object section, not an array section.
			       - Treat `SCHEMA_CONVERTERS` as a JavaScript object section. Function-based entries are valid.

			       Decision tree
			       - If the requirement is a DISPLAY-ONLY value transformation (format, prefix, suffix, inversion, case change, link wrapping), use a CONVERTER — continue here.
			       - If the requirement is field-value VALIDATION (required, length, format enforcement with error message), stop and read `page-schema-validators`.
			       - If the requirement is BUSINESS LOGIC (cross-field orchestration, data loading, side effects), stop and read `page-schema-handlers`.
			       - If a built-in OOTB converter already covers the requirement, prefer it. Do NOT write a custom converter when an OOTB one is sufficient.
			       - If the converter will be reused across multiple pages, add it as a remote module instead of to the page schema.

			       OOTB converter decision table
			       - Use this table as the first-match decision rule before creating any `usr.*` custom converter.
			         | Requirement pattern | Use | Parameters | Custom converter needed |
			         | --- | --- | --- | --- |
			         | convert value to boolean | `crt.ToBoolean` | none | no |
			         | invert boolean (true→false, false→true) | `crt.InvertBooleanValue` | none | no |
			         | wrap email in mailto: link | `crt.ToEmailLink` | none | no |
			         | wrap phone number in tel: link | `crt.ToPhoneLink` | none | no |
			         | read a specific property from an object attribute | `crt.ToObjectProp` | `prop` (required), `defaultValue` (optional) | no |
			         | requirement is not covered by rows above | custom `usr.*` converter | custom | yes |

			       Converter binding syntax
			       - Converters are bound directly in `viewConfigDiff` element property values, NOT in `viewModelConfigDiff`.
			       - Binding format: `"$AttributeName | converterName"`.
			       - Example (OOTB, no params): `"caption": "$UsrName | crt.ToBoolean"`.
			       - Example (custom, no params): `"caption": "$UsrName | usr.ToUpperCase"`.
			       - Example (OOTB with params): `"caption": "$UsrObject | crt.ToObjectProp:propName"` — params are passed as colon-separated values after the converter name.
			       - A binding expression may chain multiple converters: `"caption": "$UsrValue | usr.ToUpperCase | crt.ToBoolean"`.
			       - Only bind converters to read-only display properties (caption, label, icon, visible). Do NOT bind a converter to `control` or `value` on an editable input field — the user cannot type into a converter-transformed value.

			       SCHEMA_CONVERTERS object structure
			       - OOTB converters (`crt.*`) are built into Creatio — do NOT declare them in `SCHEMA_CONVERTERS`. Only reference them in `viewConfigDiff` bindings.
			       - Custom converters (`usr.*`) MUST be declared in `SCHEMA_CONVERTERS`.
			       - Each custom converter entry is a function keyed by its full type name:
			           converters: /**SCHEMA_CONVERTERS*/{
			             "usr.ToUpperCase": function(value) {
			               return value?.toUpperCase() ?? '';
			             }
			           }/**SCHEMA_CONVERTERS*/
			       - The function receives `value` (the current attribute value) and returns the transformed display value.
			       - Custom converter names MUST use the `usr.` prefix followed by PascalCase (for example `usr.ToUpperCase`, `usr.FormatCurrency`).
			       - Do NOT name custom converters with the `crt.` prefix — that namespace is reserved for Creatio built-ins.

			       Async converters
			       - Converters MAY be async. The runtime detects a returned Promise via `instanceof Promise` and awaits it automatically.
			       - Use `async` arrow function or `async function` syntax — both are valid.
			       - SDK services from `@creatio-devkit/common` are accessible inside a converter via the SCHEMA_ARGS closure (the same `sdk` argument injected by SCHEMA_DEPS).
			       - `SysSettingsService` is safe to call in a converter: settings are pre-loaded into a two-layer cache (in-memory Map + IndexedDB) at application startup. Repeated `getByCode` calls return from the cache with no HTTP request.
			       - Do NOT call non-cached HTTP endpoints inside a converter — those fire on every render.
			       - When to use async converter vs handler: prefer async converter when the async call is cheap/cached and the result directly determines how a single attribute is displayed. Use a handler when the async call is expensive, uncached, or drives multiple attributes.
			       - Async converter template with SysSettings:
			           define("<PageName>", /**SCHEMA_DEPS*/["@creatio-devkit/common"]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/(sdk)/**SCHEMA_ARGS*/ {
			             return {
			               viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[
			                 {
			                   "operation": "merge",
			                   "name": "UsrPhone",
			                   "values": { "type": "crt.Label", "caption": "$UsrPhone | usr.FormatPhoneNumber" }
			                 }
			               ]/**SCHEMA_VIEW_CONFIG_DIFF*/,
			               viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
			               modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
			               handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,
			               converters: /**SCHEMA_CONVERTERS*/{
			                 "usr.FormatPhoneNumber": async (value) => {
			                   if (!value) return "";
			                   const sysSettingsService = new sdk.SysSettingsService();
			                   const setting = await sysSettingsService.getByCode("UsrEnablePhoneFormatting");
			                   if (!Boolean(setting?.value)) return value;
			                   const digits = String(value).replace(/\D/g, "");
			                   if (digits.length !== 11) return value;
			                   return `+${digits.slice(0, 1)} (${digits.slice(1, 4)}) ${digits.slice(4, 7)}-${digits.slice(7, 9)}-${digits.slice(9, 11)}`;
			                 }
			               }/**SCHEMA_CONVERTERS*/,
			               validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
			             };
			           });

			       NON-NEGOTIABLES
			       - `SCHEMA_CONVERTERS` is an OBJECT, not an array.
			       - Converters affect DISPLAY only. They do not write back to the model. Never put side effects inside a converter function.
			       - Only declare custom (`usr.*`) converters in `SCHEMA_CONVERTERS`. OOTB (`crt.*`) converters are built-in and need no declaration.
			       - Custom converter names must use `usr.` prefix and PascalCase body.
			       - Bindings live in `viewConfigDiff`, NOT in `viewModelConfigDiff`.
			       - The converter binding syntax uses a pipe: `"$Attr | converterName"`.
			       - Async converters are allowed. SDK services from SCHEMA_DEPS closure are allowed. Do NOT call non-cached HTTP endpoints — they fire on every render. `SysSettingsService` is safe (cached at app startup).

			       Minimal canonical template (custom converter)
			       - Replace `<AttrName>` and `<ConverterName>` with live schema names.
			         viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[
			           {
			             "operation": "insert",
			             "name": "UsrLabel",
			             "values": {
			               "type": "crt.Label",
			               "caption": "$<AttrName> | usr.<ConverterName>",
			               "labelType": "body",
			               "visible": true
			             },
			             "parentName": "SideAreaProfileContainer",
			             "propertyName": "items",
			             "index": 0
			           }
			         ]/**SCHEMA_VIEW_CONFIG_DIFF*/,
			         converters: /**SCHEMA_CONVERTERS*/{
			           "usr.<ConverterName>": function(value) {
			             if (!value) return '';
			             return <transformation>;
			           }
			         }/**SCHEMA_CONVERTERS*/

			       Complete example (uppercase converter on a Label)
			         viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[
			           {
			             "operation": "insert",
			             "name": "UsrNameLabel",
			             "values": {
			               "type": "crt.Label",
			               "caption": "$UsrName | usr.ToUpperCase",
			               "labelType": "body",
			               "labelThickness": "default",
			               "labelEllipsis": false,
			               "labelColor": "auto",
			               "labelBackgroundColor": "transparent",
			               "labelTextAlign": "start",
			               "visible": true
			             },
			             "parentName": "SideAreaProfileContainer",
			             "propertyName": "items",
			             "index": 0
			           }
			         ]/**SCHEMA_VIEW_CONFIG_DIFF*/,
			         converters: /**SCHEMA_CONVERTERS*/{
			           "usr.ToUpperCase": function(value) {
			             return value?.toUpperCase() ?? '';
			           }
			         }/**SCHEMA_CONVERTERS*/

			       OOTB converter binding examples
			       - Invert boolean (AllowSendingEmails → show as "Don't send emails" state):
			           "checked": "$AllowSendingEmails | crt.InvertBooleanValue"
			       - Email as clickable link (bind href to mailto):
			           "href": "$UsrEmail | crt.ToEmailLink"
			       - Phone as clickable link (bind href to tel):
			           "href": "$UsrPhone | crt.ToPhoneLink"
			       - Read a property from an object attribute:
			           "caption": "$UsrContact | crt.ToObjectProp:displayValue"

			       Common custom converter patterns
			       - Add currency symbol prefix:
			           "usr.ToCurrencyDisplay": function(value) {
			             if (value == null) return '';
			             return '$ ' + Number(value).toFixed(2);
			           }
			       - Append difference from another column (note: converters receive only the bound attribute's value — for multi-attribute transforms, use a handler that sets a computed attribute, then bind without a converter):
			           This pattern requires a handler. Do NOT use a converter.
			       - Truncate long text:
			           "usr.TruncateTo50": function(value) {
			             if (!value) return '';
			             return value.length > 50 ? value.substring(0, 47) + '...' : value;
			           }

			       BEFORE SAVE CHECKLIST
			       - The binding expression uses `$Attr | converterName` format, not `$Attr.converterName`.
			       - Only `usr.*` converters are declared in `SCHEMA_CONVERTERS`. `crt.*` converters are NOT declared — only referenced in bindings.
			       - The converter is bound to a display-only property (caption, label, href, checked on read-only). Not to `control` or `value` on an editable input.
			       - If the converter is async, ensure the SDK service used is cached (e.g. `SysSettingsService`). Do NOT call non-cached HTTP endpoints inside a converter.
			       - `SCHEMA_CONVERTERS` is an object literal, not an array.
			       - Custom converter name uses `usr.` prefix and PascalCase (e.g., `usr.ToUpperCase`).
			       - The `viewModelConfigDiff` section is NOT changed by the converter addition — only `viewConfigDiff` and `SCHEMA_CONVERTERS` are touched.
			       - The edited body is syntactically valid JavaScript before calling `sync-pages`.
			       """
		};

	/// <summary>
	/// Returns the canonical guidance article for editing converter sections in Freedom UI page bodies.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "page-schema-converters-guidance")]
	[Description("Returns canonical MCP guidance for creating and editing Freedom UI page converters inside raw page schema bodies.")]
	public ResourceContents GetGuide() => Guide;
}
