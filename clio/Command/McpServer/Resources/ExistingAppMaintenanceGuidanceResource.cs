using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for existing-app maintenance through clio MCP.
/// </summary>
[McpServerResourceType]
public sealed class ExistingAppMaintenanceGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/existing-app-maintenance";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Returns the canonical guidance article for discovering, inspecting, and minimally mutating an existing app.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "existing-app-maintenance-guidance")]
	[Description("Returns canonical MCP guidance for existing-app discovery, inspection, and minimal mutation workflows.")]
	public ResourceContents GetGuide() =>
		new TextResourceContents {
			Uri = ResourceUri,
			MimeType = "text/plain",
			Text = """
			       clio MCP existing-app maintenance guide

			       Canonical flow
			       - Prefer `discover -> inspect -> mutate -> verify` for minimal edits to an existing app.
			       - Prefer `page-list -> page-get -> page-sync -> page-get` as the canonical page workflow, including single-page saves when the caller wants the clio-advertised path.
			       - Read before write, and read back after mutations when the tool or workflow allows it.

			       Discover the target app
			       - Use `application-get-list` when you do not yet know the installed application code or need to confirm candidates.
			       - Pass MCP tool arguments at the top level; do not wrap MCP arguments inside `args`.
			       - Use `application-get-info` after `application-get-list` to confirm the primary package and entity context for the target app.
			       - If `application-create` fails because the app or configuration already exists, switch to the existing-app discovery flow: call `application-get-list` to find the existing app, then `application-get-info` with the matched identifier, and continue with the inspect → mutate → verify flow.

			       Inspect pages before editing
			       - Use `page-list` to discover candidate Freedom UI page schemas in the target package or by installed `app-code`.
			       - `page-list` page items identify each page with `schema-name`, together with `uId`, `packageName`, and `parentSchemaName`.
			       - Use `page-get` to inspect the merged page bundle and retrieve the raw editable page body.
			       - For writes, send the full `raw.body` string back to `page-sync` or `page-update`; do not send `bundle` or `bundle.viewConfig` as the body payload.
			       - Use `component-info` when `page-get` shows unfamiliar `crt.*` component types before editing nested config or child collections.
			       - For standard data-bound form fields, bind `control` or `value` directly to `$Name` or `$PDS_*` attributes and prefer datasource captions such as `$Resources.Strings.PDS_UsrStatus`.
			       - Do not route standard field bindings through proxy attributes like `$UsrStatus` when the view model path is `PDS.UsrStatus`.
			       - Reserve `Usr*_label` and `Usr*_caption` resource keys for custom standalone UI with explicit `resources`; do not use those shortcuts as datasource field captions.
			       - When a page tool needs `resources`, pass a JSON object string rather than a nested object payload.

			       Inspect schema before editing
			       - Use `get-entity-schema-properties` for machine-readable schema inspection before planning a schema mutation.
			       - Use `get-entity-schema-column-properties` when the change is scoped to one existing column and you need current metadata first.

			       Preferred minimal mutations
			       - Use `page-sync` as the canonical page write path after editing the raw body returned by `page-get`.
			       - Keep `page-update` only for single-page dry-run or legacy save workflows.
			       - Use `modify-entity-schema-column` for a single-column schema change when the target schema and column are already known.
			       - Use `schema-sync` when the work spans multiple ordered schema steps, mixes create/update/seed operations, or must stay batched.
			       - Treat `create-lookup`, `create-entity-schema`, `update-entity-schema`, `create-data-binding-db`, and `page-update` as fallback-oriented tools when the preferred batched workflow is not the right fit for the requested scope.
			       - For standalone lookup seeding in an MCP workflow, prefer `create-data-binding-db` or `upsert-data-binding-row-db` over direct SQL commands.

			       Verification
			       - `page-sync` keeps client-side validation enabled by default.
			       - Enable `page-sync` `verify` only when the workflow needs explicit read-back within the same tool call. Otherwise keep the default `false` and follow with `page-get` when read-back evidence is still required.
			       - After `page-update` or `page-sync`, read the page again with `page-get` when you need explicit read-back verification.
			       - After `modify-entity-schema-column`, re-read the column with `get-entity-schema-column-properties` or the full schema with `get-entity-schema-properties`.
			       - After `schema-sync`, refresh app or schema context with `application-get-info`, `get-entity-schema-properties`, or both, depending on the workflow.
			       - The refresh after schema mutations is essential: it verifies that changes were materialized, updates the canonical main-entity selector, and detects incomplete states such as `Database update required`. Treat the schema batch as successful only when refreshed metadata is available and no schema is left in an incomplete state.
			       """
		};
}
