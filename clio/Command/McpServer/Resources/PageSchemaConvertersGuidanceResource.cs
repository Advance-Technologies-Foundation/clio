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

	/// <summary>
	/// Returns the canonical guidance article for editing converter sections in Freedom UI page bodies.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "page-schema-converters-guidance")]
	[Description("Returns canonical MCP guidance for creating and editing Freedom UI page converters inside raw page schema bodies.")]
	public ResourceContents GetGuide() => Guide;

	internal static readonly TextResourceContents Guide = new() {
			Uri = ResourceUri,
			MimeType = "text/plain",
			Text = """
			       clio MCP page-schema converters guide

			       Scope
			       - Use this guide when the task changes the `converters` section of a Freedom UI page body returned by `get-page`.
			       - Resolve exact MCP tool contracts through `get-tool-contract` before any write workflow.
			       - Keep converter work inside clio-owned page-body guidance instead of depending on an external repository.

			       Canonical runtime flow
			       - Prefer `list-pages -> get-page -> sync-pages -> get-page` for deployed page-schema converter edits.
			       - Use `raw.body` as the editable source of truth and preserve the outer AMD module structure.
			       - Preserve `converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/` as an object section.
			       - Treat `SCHEMA_CONVERTERS` as a JavaScript object section, not a strict JSON-only payload. Function-based converter entries are valid.

			       Converter intent
			       - Keep converters focused on value transformation between page data and UI presentation.
			       - Use converters only when the requirement explicitly calls for conversion logic or when the live page already contains converter infrastructure that must be extended.
			       - If the user asks to add a label, caption, or display-only field that shows an existing value in uppercase, lowercase, formatted text, or other derived presentation, implement a converter plus binding instead of a handler.
			       - When behavior is really page lifecycle, request-chain, navigation, save interception, HTTP access, or runtime state changes, call `get-guidance` with `name` set to `page-schema-handlers` instead.

			       Minimal page-body example
			       - Academy-style uppercase converter in a deployed page body:
			         converters: /**SCHEMA_CONVERTERS*/{
			         	"usr.ToUpperCase": function(value) {
			         		return value?.toUpperCase() ?? "";
			         	}
			         }/**SCHEMA_CONVERTERS*/
			       - Typical binding example in `viewConfigDiff`: `"caption": "$UsrName | usr.ToUpperCase"`.
			       - For a request like "add another label that shows Name in uppercase", create the label and bind its `caption` to `$Name | usr.ToUpperCase` or `$UsrName | usr.ToUpperCase` according to the live page attribute name.

			       Cookbook mini diff for "add uppercase label"
			       - When the live page already has an attribute like `$UsrName`, prefer a diff of this shape:
			         viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[
			         	{
			         		"operation": "insert",
			         		"name": "UsrUppercaseNameLabel",
			         		"values": {
			         			"type": "crt.Label",
			         			"caption": "$UsrName | usr.ToUpperCase"
			         		},
			         		"parentName": "SomeContainer",
			         		"propertyName": "items",
			         		"index": 1
			         	}
			         ]/**SCHEMA_VIEW_CONFIG_DIFF*/
			         converters: /**SCHEMA_CONVERTERS*/{
			         	"usr.ToUpperCase": function(value) {
			         		return value?.toUpperCase() ?? "";
			         	}
			         }/**SCHEMA_CONVERTERS*/
			       - Adjust `parentName`, `index`, and the bound attribute name to the live page structure returned by `get-page`.

			       Frontend-source pattern
			       - For frontend-source modules, the public SDK registration pattern is `@CrtConverter({ type: 'usr.MyConverter' })` from `@creatio-devkit/common`.
			       - The public converter contract is `Converter<V, R>` with `convert(value, context, ...args): R`.
			       - For runtime page-body editing, keep the implementation in `SCHEMA_CONVERTERS` instead of switching to decorator-based source modules unless the task explicitly targets frontend source.

			       Safe editing rules
			       - Edit converters conservatively and preserve all marker pairs.
			       - Replace only the converter marker content, not the whole page body.
			       - Re-parse the edited converter section before save.
			       - Avoid side effects inside converter logic.
			       - Do not move a runtime page-body task into standalone frontend source unless the task explicitly targets frontend source modules.

			       SDK and source guidance
			       - For page-body work, reuse the live SDK alias already present in the schema body when imports are required.
			       - For frontend-source work, keep converter imports on the public `@creatio-devkit/common` surface.
			       - Do not switch to internal SDK imports just because a broader package barrel exposes them.
			       - Use academy-style named converter keys such as `usr.ToUpperCase` when the task needs a reusable converter name that is referenced from a binding expression.

			       Academy and API references
			       - Freedom UI client schema overview: https://academy.creatio.com/docs/8.x/dev/development-on-creatio-platform/front-end-development/freedom-ui/client-schema-freedomui/overview
			       - Converter reference: https://academy.creatio.com/docs/8.x/dev/development-on-creatio-platform/front-end-development/freedom-ui/client-schema-freedomui/references/converters
			       - Field value conversion example: https://academy.creatio.com/docs/8.x/dev/development-on-creatio-platform/platform-customization/freedom-ui/customize-page-fields/examples/implement-the-field-value-conversion
			       - Use the public `@creatio-devkit/common` surface for `CrtConverter`, `ConverterConfig`, and `Converter<V, R>`.

			       Decision guardrails
			       - If the task validates a field value, call `get-guidance` with `name` set to `page-schema-validators` instead of using a converter.
			       - If the task toggles visibility, required state, readonly state, navigation, save flow, or data loading, use handlers or business rules instead of a converter.
			       - Do not introduce a handler that writes a second attribute only to display an uppercase or otherwise transformed copy of an existing field when converter binding can express the requirement directly.
			       - Do not use an external repository as the source of truth for converter authoring when clio MCP is available.
			       """
		};
}

