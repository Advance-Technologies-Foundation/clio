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
			       - Always read the executable contract through `tool-contract-get` before the first invocation of any MCP tool in a workflow. The contract specifies exact parameter names, aliases, required fields, defaults, and response shapes.
			       - Send tool arguments at the top level of the MCP request. Do not wrap canonical fields inside a synthetic `args` object.
			       - Tool-specific identifiers follow their own naming conventions and must not be guessed. For example, `application-get-info` and `page-list` use `code`, `application-get-info` uses `id`, `application-create` accepts `icon-background`, `application-section-create` accepts `application-code`, and `application-section-update` accepts `application-code` plus `section-code`.

			       Preferred workflow
			       - Use `application-create` when the workflow is modeling a new app shell rather than editing an existing installed app.
			       - Use `application-section-create` when the workflow must add a section to an existing installed app instead of creating a new app shell.
			       - Use `application-section-update` when the workflow must change metadata of an existing section instead of creating a new one.
			       - Prefer `schema-sync` for multi-step schema work and `page-sync` for multi-page saves.
			       - Canonical new-app entity flow: `application-create` -> `schema-sync` -> `application-get-info`.
			       - Canonical existing-app section flow: `application-get-list` -> `application-get-info` -> `application-section-create` -> `application-get-info`.
			       - Canonical existing-section metadata update flow: `application-get-list` -> `application-get-info` -> `application-section-update`.
			       - `application-create` already performs internal Data Forge enrichment and returns optional `dataforge` diagnostics. Do not require a separate external Data Forge preflight for the standard create flow.
			       - If Data Forge is unavailable or partially degraded, `application-create` still creates the app shell and reports degraded enrichment through warnings and coverage flags instead of failing the whole create path.
			       - `schema-sync` requests use `operations[*].type`. Responses also identify each result by `type`; do not invent or send `operations[*].operation`.
			       - Canonical page flow after planning a page change: `page-list` -> `page-get` -> `component-info` when needed -> `page-sync` or `page-update` -> `page-get` when explicit read-back verification is required.
			       - Entity-schema mutations are DB-first. After a successful schema tool call, treat the schema as immediately usable without a compile step.
			       - Treat single-tool entity or page mutations as compatibility fallbacks. Keep the preferred workflow in the current MCP contract unless the task is truly limited to one column, one lookup, or one page save.

			       Application modeling guardrails
			       - For a new app with one primary record type, `application-create` usually returns the canonical main entity. Extend that entity instead of creating a synonym entity for the same records.
			       - Apply the same anti-duplication rule to supporting entities: when refreshed app context already exposes a supporting or link schema with the same business purpose and relation pair, reuse it instead of creating a synonym schema.
			       - Business captions are not naming authority for new schema codes. If refreshed runtime context already maps a caption or title to an existing technical schema code, reuse that code instead of synthesizing a new one.
			       - Creating a synonym supporting or link schema inside the target package when an existing schema already models the same relationship is a blocker-level planning error.
			       - `application-create` is scalar-only for app shell fields. Keep `name`, `description`, and `optional-template-data-json.appSectionDescription` as plain strings.
			       - The minimal `application-create` shell still requires `template-code` and `icon-background`. `template-code` must be the technical template name such as `AppFreedomUI`, not a display label.
			       - Do not send localization-map fields such as `title-localizations`, `description-localizations`, or `name-localizations` to `application-create`.
			       - `application-section-create` is scalar-only for section shell fields. Keep `caption`, `description`, and `entity-schema-name` as plain strings and pass `with-mobile-pages` as a top-level boolean.
			       - `application-section-create` requires `application-code` as the target-app selector.
			       - Do not send localization-map fields such as `title-localizations`, `description-localizations`, `caption-localizations`, or `name-localizations` to `application-section-create`.
			       - When `application-section-create` receives `entity-schema-name`, it reuses that existing entity. Otherwise omit that field and let Creatio create a new object for the section.
			       - `application-section-update` is scalar-only for section metadata fields. Keep `caption`, `description`, `icon-id`, and `icon-background` as plain top-level scalar values and omit any field that should remain unchanged.
			       - Use `application-section-update` with `application-code` plus `section-code` to target one existing section inside the app.
			       - Do not send localization-map fields such as `title-localizations`, `description-localizations`, `caption-localizations`, or `name-localizations` to `application-section-update`.
			       - If the app needs localized entity or column captions, create the app first and then apply those captions through `schema-sync`, `create-entity-schema`, `update-entity-schema`, or related entity-schema MCP tools.
			       - Use `create-lookup` or `schema-sync` `create-lookup` for managed enum-like values such as status or type catalogs.
			       - `create-lookup` always uses `BaseLookup`. `Name` and `Description` are inherited, and `Name` remains the display field. Do not add duplicate title-like columns just to mirror the lookup caption.
			       - When the workflow cannot stay inside `schema-sync`, seed lookup rows through `create-data-binding-db` or `upsert-data-binding-row-db` instead of direct SQL helpers so the agent stays on the supported MCP contract.
			       - When a data binding row contains lookup (reference) columns and the correct lookup GUID is not already known, call `dataforge-find-lookups` with `schema-name` set to the reference schema and a descriptive query term before calling `create-data-binding`, `create-data-binding-db`, or `upsert-data-binding-row-db`. Use the `lookup-id` from the best-matching result as the column value.
			       - When adding a reference (Lookup) column via `update-entity-schema` and the correct `reference-schema-name` is not certain, call `dataforge-find-tables` first to confirm a matching schema exists.
			       - Entity-schema MCP write tools use explicit localization maps. Send schema and column captions through `title-localizations`, and column descriptions through `description-localizations`. Every provided localization map must include `en-US`.
			       - Do not send legacy scalar `title`, `caption`, or `description` fields to entity-schema MCP write tools.
			       - Seed rows create data only. A requirement like "defaults to New" still needs an explicit `schema default` or `ui default`.
			       - Preserve semantic text field types: use `Email`, `PhoneNumber`, and `WebLink` for email, phone, and URL fields instead of collapsing them to generic `ShortText`. These types affect both data validation and Freedom UI component selection.

			       Page editing guardrails
			       - `page-list` identifies page candidates by `schema-name`.
			       - For existing-app page/detail requests backed by data, resolve the backing schema from refreshed app context before planning schema creation: inspect `application-get-info`, then `page-list` and `page-get`, and fall back to `get-entity-schema-properties` when the relation is still unclear.
			       - If runtime context already exposes a backing supporting or link schema, treat the request as page-only/object-model reuse work. Do not create a new schema just because the current page body does not yet show the detail.
			       - Use the raw page body returned by `page-get`, specifically `raw.body`, as the editable source of truth.
			       - When writing pages, send the full `raw.body` string back to `page-sync` or `page-update`. Do not send `bundle` or `bundle.viewConfig` fragments as the body payload.
			       - Use `page-sync` for multi-page or plan-driven page writes. Use `page-update` only for a single-page save or dry-run workflow.
			       - Pass `resources` as a JSON object string when edited bodies introduce `#ResourceString(key)#` macros.
			       - For new apps or extended main entities, perform page edits after `schema-sync` and `application-get-info` refresh so that page bindings reference materialized columns.
			       - Example: if the app context already contains `Support Case Knowledge Link` / `UsrSupportCaseKbLink`, add the Related Knowledge detail by wiring the page to that existing schema. Do not create `UsrSupportCaseKnowledgeBase`.
			       """
		};
}
