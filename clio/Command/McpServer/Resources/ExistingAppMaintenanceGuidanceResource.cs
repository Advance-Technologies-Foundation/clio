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

	internal static readonly TextResourceContents Guide = new() {
			Uri = ResourceUri,
			MimeType = "text/plain",
			Text = """
			       clio MCP existing-app maintenance guide

			       Canonical flow
			       - Prefer `discover -> inspect -> mutate -> verify` for minimal edits to an existing app.
			       - To discover the target app, prefer `find-app` — it searches application name/code/description and section captions and returns the matching app(s) WITH their sections in a single call, replacing a `list-apps` + per-app `list-app-sections` scan. Fall back to `list-apps` only for a full unfiltered enumeration.
			       - For section creation in an existing app, prefer `list-apps -> get-app-info -> create-app-section -> get-app-info`.
			       - For section metadata updates in an existing app, prefer `list-apps -> get-app-info -> update-app-section`.
			       - For listing sections of an existing app, prefer `list-apps -> get-app-info -> list-app-sections`.
			       - For deleting a section from an existing app, prefer `list-apps -> get-app-info -> list-app-sections -> delete-app-section`.
			       - Prefer `list-pages -> get-page -> sync-pages -> get-page` as the canonical page workflow, including single-page saves when the caller wants the clio-advertised path.
			       - Read before write, and read back after mutations when the tool or workflow allows it.
			       - The application tools (`create-app-section`, `update-app-section`, `delete-app-section`, `list-app-sections`, `get-app-info`) are long-running backend calls that stream `notifications/progress`. Await completion; a progress notification is not a stall, so do not cancel/retry or fall back to raw SQL or manual UI on a perceived client timeout.
			       - For canonical data-binding workflow selection, call `get-guidance` with `name` set to `data-bindings`.
			       - For seeding, listing, or updating Creatio system settings (sys-settings), call `get-guidance` with `name` set to `sys-settings`.
			       - For the full DataForge orchestration protocol (layers 0–4, failure rules, stale index recovery), call `get-guidance` with `name` set to `dataforge-orchestration`.

			       Discover the target app
			       - Prefer `find-app` with `search-pattern` to map an imprecise or partial app name (e.g. "Customer Request Management") to its real code (e.g. `CrtCaseManagementApp`) in a single call; it matches the application name, code, description, and section captions, and returns each match together with its sections. Omit the pattern to list all apps with their sections.
			       - Use `list-apps` when you do not yet know the installed application code or need to confirm candidates.
			       - Wrap MCP tool arguments under the top-level `args` JSON object exactly as advertised by the tool schema (for example `{"args": {"environment-name": "...", "code": "..."}}`); do not flatten or rename canonical fields.
			       - Use `get-app-info` after `list-apps` to confirm the primary package and entity context for the target app.
			       - The `get-app-info` response includes `schema-name-prefix` — the active SchemaNamePrefix for the environment. Capture this value and use it as the prefix for ALL new schema codes in the session (entity codes, column codes, page schema names, lookup codes, binding names). An empty `schema-name-prefix` means no prefix. Do not assume `Usr`.
			       - If no `get-app-info` has been called yet and you need to create a named schema artifact (entity, lookup, page, column), call `get-schema-name-prefix` first to obtain the active prefix before naming anything.
			       - Use `create-app-section` when the requested mutation is "add a new section to this existing app".
			       - Use `update-app-section` when the requested mutation is "change metadata of this existing section", including fixing a broken JSON-style heading by supplying a new plain-text caption.
			       - If `create-app` fails because the app or configuration already exists, switch to the existing-app discovery flow: call `list-apps` to find the existing app, then `get-app-info` with the matched identifier, and continue with the inspect → mutate → verify flow.
			       - `create-app-section` accepts `application-code` as the target-app selector.
			       - `create-app-section` is scalar-only. Pass `caption`, `description`, and `entity-schema-name` as top-level strings, and pass `with-mobile-pages` as a top-level boolean.
			       - When entity-schema-name is omitted, Creatio derives both the section code and the new entity code from the caption (e.g. caption "Customer Profile" → section code and entity code "{prefix}CustomerProfile"). Choose the caption that produces the desired {prefix}<PascalCase> entity name — where {prefix} is the active SchemaNamePrefix; read it from the `schema-name-prefix` field returned by `get-app-info`, or call `get-schema-name-prefix` before `get-app-info` when the prefix is needed earlier.
			       - Do not send `title-localizations`, `description-localizations`, `caption-localizations`, or `name-localizations` to `create-app-section`.
			       - When reusing an existing entity schema, provide `entity-schema-name`. Otherwise omit that field and let Creatio create a new object for the section.
			       - When `create-app-section` fails, read `error-class` before deciding what to do next: `transport` means the request never reached Creatio (retry is safe); `creatio-timeout` means Creatio produced no response within the budget and the section may still be created server-side — wait, run `list-app-sections`, and retry only if the section is still absent; `server-error` means Creatio rejected the operation (fix inputs or server state first). The `section-created` field reports the verified side-effect state (`true`/`false`/`unknown`). Follow the returned `retry-guidance` and never blind-retry the same call.
			       - `update-app-section` accepts `application-code` and `section-code` as the existing-section selector pair.
			       - `update-app-section` is a partial scalar update. Pass only the top-level fields that should change: `caption`, `description`, `icon-id`, and `icon-background`.
			       - Do not send `title-localizations`, `description-localizations`, `caption-localizations`, or `name-localizations` to `update-app-section`.
			        - If the target package already contains a supporting or link schema that models the required relation pair, reuse that schema. Do not create a synonym schema just because the requirement uses a different business caption.
			        - Treat requests like "add a tab/detail/grid that shows linked records" as page-only/object-model reuse tasks by default. Create a new schema only when the inspect phase fails to find a suitable existing backing schema.

			       Inspect pages before editing
			       - Use `list-pages` to discover candidate Freedom UI page schemas in the target package or by installed `code`.
			       - `list-pages` page items identify each page with `schema-name`, together with `uId`, `packageName`, and `parentSchemaName`.
			       - Use `get-page` to inspect the merged page bundle and retrieve the raw editable page body.
			       - When you need to inspect the WHOLE replacing-schema chain of a page — every ancestor's own body, not just the effective one (e.g. Classic->Freedom migration discovery) — call `get-page-hierarchy` (via `clio-run`) ONCE instead of calling `get-page` / `get-client-unit-schema` per schema. It returns the full chain ordered root-first (by hierarchy level) with each schema's raw body in a single round-trip; pass `metadata-only` for a lightweight listing or `offset`/`limit` to page a very large chain.
			       - For writes, send the full `raw.body` string back to `sync-pages` or `update-page`; do not send `bundle` or `bundle.viewConfig` as the body payload.
			       - Use `get-component-info` when `get-page` shows unfamiliar `crt.*` component types before editing nested config or child collections.
			       - For standard data-bound form fields, bind `control` or `value` directly to `$Name` or `$PDS_*` attributes and prefer datasource captions keyed by the view-model attribute name: `$Resources.Strings.<bindingAttribute>` — the SAME attribute the control binds to, which must have a DS-bound `modelConfig.path` (e.g. `$Resources.Strings.PDS_UsrStatus` for a `$PDS_UsrStatus` control, or `$Resources.Strings.Name` for `$Name`). The platform auto-provides the caption from the bound column. The bare entity column code is NOT auto-provided unless it equals the attribute name; on `operation:"insert"` a label that is neither the binding-attribute key nor registered is rejected.
			       - Do not route standard field bindings through proxy attributes (e.g. binding a control to `$UsrStatus` while declaring a separate proxy attribute whose underlying view model path is `PDS.UsrStatus`). A plain attribute name like `$UsrName` is itself valid when it is directly bound to a DS column; the anti-pattern is duplicating an already-DS-bound attribute under a different name.
			       - Reserve `Usr*_label` and `Usr*_caption` resource keys for custom standalone UI with explicit `resources`; do not use those shortcuts as datasource field captions.
			       - When a page tool needs `resources`, pass a JSON object string rather than a nested object payload.
			       - Absence of a tab, detail, or grid on the page does not prove the backing entity is missing. Resolve the backing schema from runtime app context before planning new schema work.

			       Inspect schema before editing
			       - Use `get-entity-schema-properties` for machine-readable schema inspection before planning a schema mutation. Call it WITHOUT `package-name` to read the merged/effective schema (columns from ALL packages). Custom columns are frequently added in a package other than the one that defines the schema, so the merged view is the reliable way to discover them.
			       - Do NOT treat an empty column list (or `own-column-count: 0`) from a single-package read as proof a column is missing. If you queried with a `package-name`, re-read without it, or use `find-entity-schema` to find the package that customizes the schema, before concluding a field must be created.
			       - Use `get-entity-schema-column-properties` when the change is scoped to one existing column and you need current metadata first.

			       Preferred minimal mutations
			       - Use `sync-pages` as the canonical page write path after editing the raw body returned by `get-page`.
			       - Keep `update-page` only for single-page dry-run or legacy save workflows.
			       - Use `modify-entity-schema-column` for a single-column schema change when the target schema and column are already known.
			       - Use `sync-schemas` when the work spans multiple ordered schema steps, mixes create/update/seed operations, or must stay batched.
			       - `sync-schemas` requests use `operations[*].type`. Responses also use `type`; do not send `operations[*].operation` in requests.
			       - `sync-schemas` retries transient network faults (DNS/reset/timeout/gateway) per operation and returns a `resume-plan` when a batch aborts mid-way. On `success: false` with a `resume-plan`, resubmit ONLY `resume-plan.operations` (failed op + not-run ops, already in re-submittable shape); never resend completed operations.
			       - The column shape returned by `get-app-info` round-trips into `sync-schemas update-entity` without field translation: a column read as `{name, type (or data-value-type), reference-schema-name (or reference-schema), required (or is-required), caption}` can be sent back inside an `update-operations` entry — just add the `action` verb (`modify`/`remove`). To add columns, drop the read/create-shape objects into the operation's `columns` array (no `action` needed) — the scalar `caption` is promoted to `title-localizations` automatically — and they are treated as an implicit add-batch.
			       - Treat `create-lookup`, `create-entity-schema`, `update-entity-schema`, `create-data-binding-db`, and `update-page` as fallback-oriented tools when the preferred batched workflow is not the right fit for the requested scope.
			       - For standalone lookup seeding or local binding artifacts in an MCP workflow, follow `get-guidance` with `name` set to `data-bindings`.
			       - When that guide resolves to a DB-first binding path, prefer `create-data-binding-db` or `upsert-data-binding-row-db` over direct SQL commands.

			       Verification
			       - `sync-pages` keeps client-side validation enabled by default.
			       - Enable `sync-pages` `verify` only when the workflow needs explicit read-back within the same tool call. Otherwise keep the default `false` and follow with `get-page` when read-back evidence is still required.
			       - After `update-page` or `sync-pages`, read the page again with `get-page` when you need explicit read-back verification.
			       - After `modify-entity-schema-column`, re-read the column with `get-entity-schema-column-properties` or the full schema with `get-entity-schema-properties`.
			       - After `create-app-section`, refresh app context with `get-app-info` when you need updated primary-package entities and pages after the section is created.
			       - `update-app-section` already returns the previous and updated section metadata, so a separate verification read is optional unless the workflow also needs broader app context.
			       - After `sync-schemas`, refresh app or schema context with `get-app-info`, `get-entity-schema-properties`, or both, depending on the workflow.
			       - The refresh after schema mutations is essential: it verifies that changes were materialized, updates the canonical main-entity selector, and detects incomplete states such as `Database update required`. Treat the schema batch as successful only when refreshed metadata is available and no schema is left in an incomplete state.
			       - Example: if `get-app-info` or refreshed schema metadata already exposes `Support Case Knowledge Link` / `UsrSupportCaseKbLink`, a request to add a Related Knowledge detail on the Support Case form is a page mutation that reuses that schema. Do not create `UsrSupportCaseKnowledgeBase`.
			       """
	};

	/// <summary>
	/// Returns the canonical guidance article for discovering, inspecting, and minimally mutating an existing app.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "existing-app-maintenance-guidance")]
	[Description("Returns canonical MCP guidance for existing-app discovery, inspection, and minimal mutation workflows.")]
	public ResourceContents GetGuide() => Guide;
}

