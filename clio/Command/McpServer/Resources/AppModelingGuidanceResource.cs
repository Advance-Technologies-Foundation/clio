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
			       - For the full DataForge orchestration protocol (layers 0–4, failure rules, stale index recovery), read `docs://mcp/guides/dataforge-orchestration`.

			       Discovery before invocation
			       - Always read the executable contract through `get-tool-contract` before the first invocation of any MCP tool in a workflow. The contract specifies exact parameter names, aliases, required fields, defaults, and response shapes.
			       - Send tool arguments at the top level of the MCP request. Do not wrap canonical fields inside a synthetic `args` object.
			       - Tool-specific identifiers follow their own naming conventions and must not be guessed. For example, `get-app-info` and `list-pages` use `code`, `get-app-info` uses `id`, `create-app` accepts `icon-background`, `create-app-section` accepts `application-code`, and `update-app-section` accepts `application-code` plus `section-code`.

			       Preferred workflow
			       - Use `create-app` when the workflow is modeling a new app shell rather than editing an existing installed app.
			       - Use `create-app-section` when the workflow must add a section to an existing installed app instead of creating a new app shell.
			       - Use `update-app-section` when the workflow must change metadata of an existing section instead of creating a new one.
			       - Prefer `sync-schemas` for multi-step schema work and `sync-pages` for multi-page saves.
			       - Canonical new-app entity flow: `create-app` -> `sync-schemas` -> `get-app-info`.
			       - Canonical existing-app section flow: `list-apps` -> `get-app-info` -> `create-app-section` -> `get-app-info`.
			       - Canonical existing-section metadata update flow: `list-apps` -> `get-app-info` -> `update-app-section`.
			       - Canonical section discovery flow: `list-apps` -> `get-app-info` -> `list-app-sections`.
			       - Canonical section delete flow: `list-apps` -> `get-app-info` -> `list-app-sections` -> `delete-app-section`.
			       - `create-app` already performs internal Data Forge enrichment and returns optional `dataforge` diagnostics. Do not require a separate external Data Forge preflight for the standard create flow.
			       - If Data Forge is unavailable or partially degraded, `create-app` still creates the app shell and reports degraded enrichment through warnings and coverage flags instead of failing the whole create path.
			       - `sync-schemas` requests use `operations[*].type`. Responses also identify each result by `type`; do not invent or send `operations[*].operation`.
			       - Canonical page flow after planning a page change: `list-pages` -> `get-page` -> `get-component-info` when needed -> `sync-pages` or `update-page` -> `get-page` when explicit read-back verification is required.
			       - Entity-schema mutations are DB-first. After a successful schema tool call, treat the schema as immediately usable without a compile step.
			       - Treat single-tool entity or page mutations as compatibility fallbacks. Keep the preferred workflow in the current MCP contract unless the task is truly limited to one column, one lookup, or one page save.

			       Application modeling guardrails
			       - For a new app with one primary record type, `create-app` usually returns the canonical main entity. Extend that entity instead of creating a synonym entity for the same records.
			       - Apply the same anti-duplication rule to supporting entities: when refreshed app context already exposes a supporting or link schema with the same business purpose and relation pair, reuse it instead of creating a synonym schema.
			       - Business captions are not naming authority for new schema codes. If refreshed runtime context already maps a caption or title to an existing technical schema code, reuse that code instead of synthesizing a new one.
			       - Creating a synonym supporting or link schema inside the target package when an existing schema already models the same relationship is a blocker-level planning error.
			       - `create-app` is scalar-only for app shell fields. Keep `name`, `description`, and `optional-template-data-json.appSectionDescription` as plain strings.
			       - The minimal `create-app` shell still requires `template-code` and `icon-background`. `template-code` must be the technical template name such as `AppFreedomUI`, not a display label.
			       - Do not send localization-map fields such as `title-localizations`, `description-localizations`, or `name-localizations` to `create-app`.
			       - `create-app-section` is scalar-only for section shell fields. Keep `caption`, `description`, and `entity-schema-name` as plain strings and pass `with-mobile-pages` as a top-level boolean.
			       - `create-app-section` requires `application-code` as the target-app selector.
			       - Do not send localization-map fields such as `title-localizations`, `description-localizations`, `caption-localizations`, or `name-localizations` to `create-app-section`.
			       - When `create-app-section` receives `entity-schema-name`, it reuses that existing entity. Otherwise omit that field and let Creatio create a new object for the section.
			       - `update-app-section` is scalar-only for section metadata fields. Keep `caption`, `description`, `icon-id`, and `icon-background` as plain top-level scalar values and omit any field that should remain unchanged.
			       - Use `update-app-section` with `application-code` plus `section-code` to target one existing section inside the app.
			       - Do not send localization-map fields such as `title-localizations`, `description-localizations`, `caption-localizations`, or `name-localizations` to `update-app-section`.
			       - If the app needs localized entity or column captions, create the app first and then apply those captions through `sync-schemas`, `create-entity-schema`, `update-entity-schema`, or related entity-schema MCP tools.
			       - Use `create-lookup` or `sync-schemas` `create-lookup` for managed enum-like values such as status or type catalogs.
			       - `create-lookup` always uses `BaseLookup`. `Name` and `Description` are inherited, and `Name` remains the display field. Do not add duplicate title-like columns just to mirror the lookup caption.
			       - When the workflow cannot stay inside `sync-schemas`, seed lookup rows through `create-data-binding-db` or `upsert-data-binding-row-db` instead of direct SQL helpers so the agent stays on the supported MCP contract.
			       - When a data binding row contains lookup (reference) columns and the correct lookup GUID is not already known, call `dataforge-find-lookups` with `schema-name` set to the reference schema and a descriptive query term before calling `create-data-binding`, `create-data-binding-db`, or `upsert-data-binding-row-db`. Use the `lookup-id` from the best-matching result as the column value.
			       - When adding a reference (Lookup) column via `update-entity-schema` and the correct `reference-schema-name` is not certain, call `dataforge-find-tables` first to confirm a matching schema exists.
			       - Entity-schema MCP write tools use explicit localization maps. Send schema and column captions through `title-localizations`, and column descriptions through `description-localizations`. Every provided localization map must include `en-US`.
			       - Do not send legacy scalar `title`, `caption`, or `description` fields to entity-schema MCP write tools.
			       - Seed rows create data only. A requirement like "defaults to New" still needs an explicit `schema default` or `ui default`.
			       - Preserve semantic text field types: use `Email`, `PhoneNumber`, and `WebLink` for email, phone, and URL fields instead of collapsing them to generic `ShortText`. These types affect both data validation and Freedom UI component selection.

			       Page editing guardrails
			       - `list-pages` identifies page candidates by `schema-name`.
			       - For existing-app page/detail requests backed by data, resolve the backing schema from refreshed app context before planning schema creation: inspect `get-app-info`, then `list-pages` and `get-page`, and fall back to `get-entity-schema-properties` when the relation is still unclear.
			       - If runtime context already exposes a backing supporting or link schema, treat the request as page-only/object-model reuse work. Do not create a new schema just because the current page body does not yet show the detail.
			       - Use the raw page body returned by `get-page`, specifically `raw.body`, as the editable source of truth.
			       - When writing pages, send the full `raw.body` string back to `sync-pages` or `update-page`. Do not send `bundle` or `bundle.viewConfig` fragments as the body payload.
			       - Use `sync-pages` for multi-page or plan-driven page writes. Use `update-page` only for a single-page save or dry-run workflow.
			       - Pass `resources` as a JSON object string when edited bodies introduce `#ResourceString(key)#` macros.
			       - For new apps or extended main entities, perform page edits after `sync-schemas` and `get-app-info` refresh so that page bindings reference materialized columns.
			       - Example: if the app context already contains `Support Case Knowledge Link` / `UsrSupportCaseKbLink`, add the Related Knowledge detail by wiring the page to that existing schema. Do not create `UsrSupportCaseKnowledgeBase`.
			       """
		};
}
