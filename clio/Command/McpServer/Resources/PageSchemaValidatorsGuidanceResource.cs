using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for Freedom UI page schema validation through clio MCP.
/// </summary>
[McpServerResourceType]
public sealed class PageSchemaValidatorsGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/page-schema-validators";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Canonical guidance article accessible by name through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP page schema validators guide

		       Overview
		       - clio MCP validates Freedom UI page bodies client-side before saving.
		       - Validation runs automatically in `update-page` and `sync-pages` unless disabled.
		       - Use `dry-run: true` in `update-page` to validate without saving.

		       Marker integrity validation
		       - Every Freedom UI page body must contain exactly matching pairs of BEGIN/END markers.
		       - Marker pairs: `/* SCHEMA_MARKER_BEGIN */` / `/* SCHEMA_MARKER_END */`, and
		         `/* VIEW_CONFIG_DIFF_MARKER_BEGIN */` / `/* VIEW_CONFIG_DIFF_MARKER_END */`.
		       - Missing, duplicated, or unmatched markers fail the save with an explicit error.
		       - When using `raw.body` from `get-page`, markers are always present and balanced.
		       - Never strip or reorder marker pairs.

		       JavaScript syntax validation
		       - The page body must be syntactically valid JavaScript.
		       - Acorn-based syntax check runs before the save request reaches Creatio.
		       - Syntax errors include the line/column location of the first issue.

		       Form field binding validation
		       - Standard data-bound form fields must bind through recognized datasource paths.
		       - Valid paths: `$Name`, `$PDS_*`, or explicit `$DS.column` patterns.
		       - Route standard field bindings directly to datasource paths, not through proxy view-model attributes.
		       - The validator flags incorrect binding patterns to prevent fields from displaying stale or empty values.

		       Resource string validation
		       - `#ResourceString(key)#` macros must be backed by a matching entry in `localizableStrings`.
		       - Entries are auto-registered when `resources` contains a matching key-caption pair.
		       - Unresolvable macros are auto-registered with captions derived from the key name.

		       Optional-properties validation
		       - When `optional-properties` is provided to `update-page` or `sync-pages`, it must be a valid JSON array.
		       - Each element must contain a `key` field used as the merge key.
		       - Invalid JSON in `optional-properties` fails validation before the save attempt.
		       """
	};

	/// <summary>
	/// Returns the canonical guidance article for Freedom UI page schema validation.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "page-schema-validators-guidance")]
	[Description("Returns canonical MCP guidance for Freedom UI page schema validation rules enforced by clio.")]
	public ResourceContents GetGuide() => Guide;
}
