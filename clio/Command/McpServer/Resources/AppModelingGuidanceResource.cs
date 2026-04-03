using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for Creatio app modeling through clio MCP.
/// </summary>
[McpServerResourceType]
public sealed class AppModelingGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/app-modeling";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Returns the canonical guidance article for DB-first app creation, schema modeling, and page workflows.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "app-modeling-guidance")]
	[Description("Returns canonical MCP guidance for Creatio application modeling, schema design, and page-editing workflows.")]
	public ResourceContents GetGuide() =>
		new TextResourceContents {
			Uri = ResourceUri,
			MimeType = "text/plain",
			Text = """
			       clio MCP app modeling guide

			       Core contract
			       - clio MCP is a stdio MCP server, not an HTTP or browser API.
			       - Use discovered tool names exactly as advertised.
			       - Newer design tools use kebab-case JSON argument names such as `environment-name`, `package-name`, and `schema-name`.
			       - For existing-app minimal edits, read `docs://mcp/guides/existing-app-maintenance`.

			       Preferred workflow
			       - Use `application-create` when the workflow is modeling a new app shell rather than editing an existing installed app.
			       - Prefer `schema-sync` for multi-step schema work and `page-sync` for multi-page saves.
			       - Canonical new-app entity flow: `application-create` -> `schema-sync` -> `application-get-info`.
			       - Canonical page flow after planning a page change: `page-list` -> `page-get` -> `component-info` when needed -> `page-sync` or `page-update` -> `page-get` when explicit read-back verification is required.
			       - Entity-schema mutations are DB-first. After a successful schema tool call, treat the schema as immediately usable without a compile step.
			       - Treat single-tool entity or page mutations as compatibility fallbacks. Keep the preferred workflow in the current MCP contract unless the task is truly limited to one column, one lookup, or one page save.

			       Application modeling guardrails
			       - For a new app with one primary record type, `application-create` usually returns the canonical main entity. Extend that entity instead of creating a synonym entity for the same records.
			       - `application-create` is scalar-only for app shell fields. Keep `name`, `description`, and `optional-template-data-json.appSectionDescription` as plain strings.
			       - Do not send localization-map fields such as `title-localizations`, `description-localizations`, or `name-localizations` to `application-create`.
			       - If the app needs localized entity or column captions, create the app first and then apply those captions through `schema-sync`, `create-entity-schema`, `update-entity-schema`, or related entity-schema MCP tools.
			       - Use `create-lookup` or `schema-sync` `create-lookup` for managed enum-like values such as status or type catalogs.
			       - `create-lookup` always uses `BaseLookup`. `Name` and `Description` are inherited, and `Name` remains the display field. Do not add duplicate title-like columns just to mirror the lookup caption.
			       - Entity-schema MCP write tools use explicit localization maps. Send schema and column captions through `title-localizations`, and column descriptions through `description-localizations`. Every provided localization map must include `en-US`.
			       - Do not send legacy scalar `title`, `caption`, or `description` fields to entity-schema MCP write tools.
			       - Seed rows create data only. A requirement like "defaults to New" still needs an explicit `schema default` or `ui default`.

			       Page editing guardrails
			       - `page-list` identifies page candidates by `schema-name`.
			       - Use the raw page body returned by `page-get`, specifically `raw.body`, as the editable source of truth.
			       - Use `page-sync` for multi-page or plan-driven page writes. Use `page-update` only for a single-page save or dry-run workflow.
			       - Pass `resources` when edited bodies introduce `#ResourceString(key)#` macros.
			       """
		};
}
