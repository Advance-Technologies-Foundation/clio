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
	[Description("Returns canonical MCP guidance for Creatio application modeling, schema design, and page modification workflows.")]
	public ResourceContents GetGuide() => Guide;

	internal static readonly TextResourceContents Guide = new() {
			Uri = ResourceUri,
			MimeType = "text/plain",
			Text = """
			       clio MCP app modeling guide

			       Profile language (detect once, reuse, ask on failure)
			       - Before creating ANY entity (application, object, page, section, lookup, column), call `get-user-culture` ONCE per session to detect the connected user's profile language.
			       - Reuse that detected culture for all generated names, labels, and captions for the rest of the session; do not re-detect per entity (the server caches it per environment). Re-detect only when the active environment changes.
			       - The detected profile culture is the LANGUAGE OF THE CAPTION TEXT, not just the localization key. Write every name, label, and caption IN that language. Do NOT keep captions in the conversation/task language when it differs from the profile language — the profile language wins (or ask the user). Example: an `en-US` profile means English captions ("Number", "Equipment"), even if the request was written in Ukrainian.
			       - The mandatory `en-US` localization-map entry MUST contain ENGLISH text. NEVER place non-English text (e.g. Cyrillic) under the `en-US` key. When the profile language is not English, add a second entry under the profile culture key (e.g. `uk-UA`) with the localized text AND keep `en-US` as the English baseline.
			       - clio ENFORCES this on the write path: a caption whose script does not match a Latin-script culture key (e.g. Cyrillic text under `en-US`) is REJECTED with an error. Author the value in the right language, or move the localized text under the matching culture key.
			       - If `get-user-culture` returns `success:false`, ASK the user which language to use before creating anything. Do NOT silently fall back to the host machine locale or to `en-US`.
			       - To force a specific language for one creation, pass `caption-culture` (precedence: `caption-culture` > detected profile culture > `en-US`).

			       Core contract
			       - clio MCP is a stdio MCP server, not an HTTP or browser API.
			       - Use discovered tool names exactly as advertised.
			       - Newer design tools use kebab-case JSON argument names such as `environment-name`, `package-name`, and `schema-name`.
			       - For existing-app minimal edits, call `get-guidance` with `name` set to `existing-app-maintenance`.
			       - For canonical data-binding workflow selection, call `get-guidance` with `name` set to `data-bindings`.
			       - For seeding or reading Creatio system settings (sys-settings), call `get-guidance` with `name` set to `sys-settings`.
			       - For the full DataForge orchestration protocol (layers 0–4, failure rules, stale index recovery), call `get-guidance` with `name` set to `dataforge-orchestration`.

			       Discovery before invocation
			       - Always read the executable contract through `get-tool-contract` before the first invocation of any MCP tool in a workflow. The contract specifies exact parameter names, aliases, required fields, defaults, and response shapes.
			       - Wrap tool arguments under the top-level `args` JSON object exactly as advertised by the tool schema (for example `{"args": {"code": "..."}}`). Do not flatten or rename canonical fields.
			       - Tool-specific identifiers follow their own naming conventions and must not be guessed. For example, `get-app-info` and `list-pages` use `code`, `get-app-info` uses `id`, `create-app` accepts `icon-background`, `create-app-section` accepts `application-code`, and `update-app-section` accepts `application-code` plus `section-code`.
			       - Use `get-schema-name-prefix` to discover the active SchemaNamePrefix before naming schemas when you need the prefix before calling `create-app` (e.g. working with an existing app or planning schema names upfront). When `create-app` is the first call, its response already includes `schema-name-prefix`.

			       Preferred workflow
			       - The application tools (`create-app`, `create-app-section`, `update-app-section`, `delete-app-section`, `list-app-sections`, `get-app-info`) are long-running backend calls that stream `notifications/progress` while working. Await completion — a progress notification means the server is still working, not a stall, so do not cancel/retry or fall back to raw SQL or manual UI on a perceived client timeout.
			       - Use `create-app` when the workflow is modeling a new app shell rather than editing an existing installed app.
			       - Use `create-app-section` when the workflow must add a section to an existing installed app instead of creating a new app shell.
			       - Use `update-app-section` when the workflow must change metadata of an existing section instead of creating a new one.
			       - Prefer `sync-schemas` for multi-step schema work and `sync-pages` for multi-page saves.
			       - Canonical new-app entity flow: `create-app` -> `sync-schemas` -> `get-app-info`.
			       - Prefer `find-app` as the discovery front door for the existing-app flows below: it searches app name/code/description and section captions and returns matching apps WITH their sections in one call, so you can usually skip the separate `list-apps` and `list-app-sections` steps when mapping an imprecise name to a code.
			       - Canonical existing-app section flow: `list-apps` -> `get-app-info` -> `create-app-section` -> `get-app-info`.
			       - Canonical existing-section metadata update flow: `list-apps` -> `get-app-info` -> `update-app-section`.
			       - Canonical section discovery flow: `list-apps` -> `get-app-info` -> `list-app-sections`.
			       - Canonical section delete flow: `list-apps` -> `get-app-info` -> `list-app-sections` -> `delete-app-section`.
			       - `create-app` already performs internal Data Forge enrichment and returns optional `dataforge` diagnostics. Do not require a separate external Data Forge preflight for the standard create flow.
			       - If Data Forge is unavailable or partially degraded, `create-app` still creates the app shell and reports degraded enrichment through warnings and coverage flags instead of failing the whole create path.
			       - `sync-schemas` requests use `operations[*].type`. Responses also identify each result by `type`; do not invent or send `operations[*].operation`.
			       - `sync-schemas` `create-lookup`, `create-entity`, and `update-entity` are convergent supersets: they create-if-absent and otherwise reconcile only the missing delta, so a schema that already exists is extended (never recreated) and re-running the same batch is safe. This reinforces the anti-duplication rule — when a schema already exists, model an extend/reconcile step against it rather than a synonym create.
			       - `sync-schemas` retries transient network faults (DNS/reset/timeout/gateway) per operation and, on a mid-batch abort, returns a `resume-plan`. Because the schema ops are convergent, re-submitting the whole batch verbatim is safe for the SCHEMA operations only (they replay as `already-satisfied`/`reconciled`); when the batch includes `seed-data` or a `resume-plan` was returned, resubmit `resume-plan.operations` instead, because `seed-data` is NOT replay-safe for rows without a `Name` and the plan converts a post-create seed failure to a standalone op.
			       - For a virtual entity with no physical database table, read `virtual-entities`. The virtual object MUST be created and read back before its `IEntityQueryExecutor` is implemented. Set `is-virtual: true` on a `sync-schemas` `create-entity` operation or on `create-entity-schema`; it defaults to false. Verify the saved flag through `get-app-info` or `get-entity-schema-properties`.
			       - Canonical page flow after planning a page change: `list-pages` -> `get-page` -> `get-component-info` when needed -> `sync-pages` or `update-page` -> `get-page` when explicit read-back verification is required.
			       - Entity-schema mutations are DB-first. After a successful schema tool call, treat the schema as immediately usable without a compile step.
			       - Treat single-tool entity or page mutations as compatibility fallbacks. Keep the preferred workflow in the current MCP contract unless the task is truly limited to one column, one lookup, or one page save.

			       Application modeling guardrails
			       - Use the `schema-name-prefix` value from `create-app` (or from `get-schema-name-prefix`) as the prefix for ALL custom schema codes in the session (lookups, entity columns, supporting entities, page names). An empty `schema-name-prefix` means no prefix — do not add one. Default Creatio environments return `Usr`.
			       - For a new app with one primary record type, `create-app` usually returns the canonical main entity. Extend that entity instead of creating a synonym entity for the same records.
			       - Apply the same anti-duplication rule to supporting entities: when refreshed app context already exposes a supporting or link schema with the same business purpose and relation pair, reuse it instead of creating a synonym schema.
			       - Business captions are not naming authority for new schema codes. If refreshed runtime context already maps a caption or title to an existing technical schema code, reuse that code instead of synthesizing a new one.
			       - Creating a synonym supporting or link schema inside the target package when an existing schema already models the same relationship is a blocker-level planning error.
			       - `create-app` is scalar-only for app shell fields. Keep `name`, `description`, and `optional-template-data-json.appSectionDescription` as plain strings.
			       - `create-app` accepts `with-mobile-pages` as a top-level boolean (default `true`). Leave it `true` to generate the main entity `_MobileFormPage` and `_MobileListPage` alongside the web pages. When the user's plan is web-only (no mobile app target), proactively pass `with-mobile-pages: false` so those mobile pages are not created and do not need manual cleanup. An explicit `client-type-id` takes precedence over `with-mobile-pages`.
			       - The minimal `create-app` shell requires `template-code`. `template-code` must be the technical template name such as `AppFreedomUI`, not a display label. Omit `icon-background` unless the request explicitly specifies an app color; when provided, it must be one of the Freedom UI palette colors: #A6DE00, #20A959, #22AC14, #FFAC07, #FF8800, #F9307F, #FF602E, #FF4013, #B87CCF, #7848EE, #247EE5, #0058EF, #009DE3, #4F43C2, #08857E, #00BFA5.
			       - Do not send localization-map fields such as `title-localizations`, `description-localizations`, or `name-localizations` to `create-app`.
			       - `optional-template-data-json.entitySchemaName` is only valid together with `useExistingEntitySchema=true`, and the entity MUST already exist in Creatio before `create-app` is called. Passing `entitySchemaName` for a non-existent entity, or without `useExistingEntitySchema=true`, will cause a server-side error. When you need the app to use an existing entity, first verify the entity exists (e.g. via `dataforge-find-tables`), then call `create-app` with both `entitySchemaName` and `useExistingEntitySchema=true`. To create a new app with a freshly generated entity, omit `optional-template-data-json` entirely and let Creatio generate the entity automatically.
			       - `useAIContentGeneration` inside `optional-template-data-json` is not supported and will be rejected by clio.
			       - `create-app-section` is scalar-only for section shell fields. Keep `caption`, `description`, and `entity-schema-name` as plain strings and pass `with-mobile-pages` as a top-level boolean.
			       - `create-app-section` requires `application-code` as the target-app selector.
			       - `create-app` automatically creates: (1) a **package** named after `code`; (2) the **app** record; (3) one default **section** record in `ApplicationSection` that wires the canonical main entity to Creatio's navigation bar; (4) the **canonical main entity** (schema code = `code`) with one default column named `{prefix}Name` (MediumText), where `{prefix}` is the active SchemaNamePrefix returned in the response field `schema-name-prefix` (when SchemaNamePrefix is empty the column is named `Name`); and (5) **five Freedom UI pages** — `{code}_FormPage`, `{code}_ListPage`, `{code}_Detail`, `{code}_MobileFormPage`, and `{code}_MobileListPage`. Passing `with-mobile-pages: false` suppresses the `{code}_MobileFormPage` and `{code}_MobileListPage`, leaving three web pages for a web-only app. The response returns the entity manifest under `entities` and the page manifest under `pages`; the section record itself is not in the response (use `canonical-main-entity-name` to identify the main entity). Read that manifest instead of re-discovering artifacts via additional tool calls.
			       - MOBILE PAGE EDITING — STOP. When `get-page` returns `schemaType: 10` (or `list-pages` shows `schema-type: "mobile"`), the page is a mobile page. Before editing its body, call `get-guidance` with name `mobile-page-modification`. Mobile pages use plain JSON (NOT AMD define(...)), have a different component registry, and must NOT contain handlers, validators, or converters sections. Applying web page patterns to mobile pages produces broken schemas.
			       - The auto-generated `{code}_FormPage` starts with `{prefix}Name` pre-inserted in `SideAreaProfileContainer` and Feed + AttachmentList tabs (see `schema-name-prefix` in the response). The auto-generated `{code}_ListPage` starts with `{prefix}Name`, `CreatedOn`, `CreatedBy` as default DataTable columns. Replace or extend these defaults when configuring the section; always call `get-page` to read the current body before writing.
			       - `create-app` already creates the default section for the canonical main entity — no separate `create-app-section` call is needed for it. Use `create-app-section` only when the app needs an additional section backed by a new, separately named entity; Creatio derives the new entity code from the `caption` you provide (e.g. caption `Customer Profile` → entity `{prefix}CustomerProfile`, for example `UsrCustomerProfile` when prefix is `Usr` or `AbcCustomerProfile` when prefix is `Abc`). For the primary record type, extend the canonical main entity returned by `create-app` — do not create a synonym section for it.
			       - Do not send localization-map fields such as `title-localizations`, `description-localizations`, `caption-localizations`, or `name-localizations` to `create-app-section`.
			       - When `create-app-section` receives `entity-schema-name`, it reuses that existing entity. Otherwise omit that field and let Creatio create a new object for the section.
			       - Create sections in one application SEQUENTIALLY, not in parallel: each `create-app-section` is a long (~90–100 s) backend insert, so overlapping inserts against the same app contend server-side and abort with a detail-less `InsertQuery failed`. clio serializes creations per app in-process and auto-retries a detail-less rejection once with verification, but do not fan out parallel `create-app-section` calls against the same app — create one, await it, then create the next.
			       - When `create-app-section` fails, read `error-class` before deciding what to do next: `transport` means the request never reached Creatio (retry is safe); `creatio-timeout` means Creatio produced no response within the budget and the section may still be created server-side — wait, run `list-app-sections`, and retry only if the section is still absent; `contention` means the insert was aborted without a detailed reason — the server returned no detail, so this may be parallel creation in the same app OR a server-side rejection unrelated to concurrency; no section was created (verified), so run `list-app-sections` to confirm it is absent, create sections one at a time if you were creating them concurrently (clio already serializes and auto-retries once), and if a single sequential create still fails treat it as a server-side issue (check `clio healthcheck` and the Creatio server logs) rather than just retrying; `server-error` means Creatio rejected the operation with a real, detailed reason (fix inputs or server state first). The `section-created` field reports the side-effect state (`true`/`false`/`unknown`/`in-progress`). Follow the returned `retry-guidance` and never blind-retry the same call.
			       - `section-created: in-progress` (with `error-class: creatio-timeout`) is NOT a failure: the section creation exceeded the response deadline but is still running server-side on the long-lived clio MCP server. Do NOT retry `create-app-section` (a retry would create a duplicate section) and do NOT fall back to `create-page` / `sync-pages` to build the section's pages by hand (those pages would not be registered as the section's List/Form pages). Instead wait briefly, then poll `list-app-sections` and `get-app-info` until the section and its generated `<Code>_ListPage` / `<Code>_FormPage` appear, then continue.
			       - `update-app-section` is scalar-only for section metadata fields. Keep `caption`, `description`, `icon-id`, and `icon-background` as plain top-level scalar values and omit any field that should remain unchanged.
			       - Use `update-app-section` with `application-code` plus `section-code` to target one existing section inside the app.
			       - Do not send localization-map fields such as `title-localizations`, `description-localizations`, `caption-localizations`, or `name-localizations` to `update-app-section`.
			       - If the app needs localized entity or column captions, create the app first and then apply those captions through `sync-schemas`, `create-entity-schema`, `update-entity-schema`, or related entity-schema MCP tools.
			       - Use `create-lookup` or `sync-schemas` `create-lookup` for managed enum-like values such as status or type catalogs.
			       - `create-lookup` always uses `BaseLookup`. `Name` and `Description` are inherited, and `Name` remains the display field. Do not add duplicate title-like columns just to mirror the lookup caption.
			       - For standalone lookup seeding or local binding artifacts, follow `get-guidance` with `name` set to `data-bindings` instead of relying on consumer-repo binding notes.
			       - When the workflow cannot stay inside `sync-schemas`, seed lookup rows through `create-data-binding-db` or `upsert-data-binding-row-db` instead of direct SQL helpers so the agent stays on the supported MCP contract.
			       - When a data binding row contains lookup (reference) columns and the correct lookup GUID is not already known, call `dataforge-find-lookups` with `schema-name` set to the reference schema and a descriptive query term before calling `create-data-binding`, `create-data-binding-db`, or `upsert-data-binding-row-db`. Use the `lookup-id` from the best-matching result as the column value.
			       - When adding a reference (Lookup) column via `update-entity-schema` and the correct `reference-schema-name` is not certain, call `dataforge-find-tables` first to confirm a matching schema exists.
			       - Entity-schema MCP write tools use explicit localization maps. Send schema and column captions through `title-localizations`, and column descriptions through `description-localizations`. `title-localizations` is OPTIONAL for a column add; when omitted, `en-US` is auto-derived from a scalar title/caption or the column name. When you DO provide a localization map it must include `en-US`, and the `en-US` value MUST be ENGLISH text — non-English text (e.g. Cyrillic) under `en-US` is rejected. Put localized text under its own culture key (e.g. `uk-UA`).
			       - Prefer `title-localizations` over the legacy scalar `title`/`caption` (they act only as an en-US fallback for an add). Do not send a scalar `description`; use `description-localizations`.
			       - Seed rows create data only. A requirement like "defaults to New" still needs an explicit `schema default` or `ui default`.
			       - To set a lookup column default to a seeded value, the workflow requires two separate calls because the row GUID is only known after creation: (1) create and seed the lookup via sync-schemas; (2) resolve the seeded row GUID via dataforge-find-lookups with schema-name set to the lookup entity and a descriptive query term; (3) apply the default via modify-entity-schema-column or a follow-up sync-schemas update-entity operation with default-value-config source=Const and the resolved GUID as value. Do not skip the GUID resolution step — the default value for a Lookup column must be the row's GUID, not its display name.
			       - Preserve semantic text field types: use `Email`, `PhoneNumber`, and `WebLink` for email, phone, and URL fields instead of collapsing them to generic `ShortText`. These types affect both data validation and Freedom UI component selection.
			       - For image/photo fields rendered with the `crt.ImageInput` component, model the backing column as `ImageLookup` ("Image link"), NOT the binary `Image` type. `ImageLookup` references the `SysImage` schema automatically — do not pass `reference-schema-name` for it.
			       - For a color field (a swatch/hex value such as `#RRGGBB`), use the `Color` column type on `create-entity-schema` / `modify-entity-schema-column` / `sync-schemas`. `Color` is not a text column: the text-only options (multiline / accent-insensitive / format-validated / masked) do not apply to it, and `get-entity-schema-properties` reads it back as the named `Color` type.
			       - To set which column is a schema's primary-display column (the value shown in lookups and links), call `set-entity-schema-properties` with `primary-display-column` (an own or inherited column, resolved by name). Read it back via `get-entity-schema-properties` (`primary-display-column-name`). This is the only supported way to change the display column of an existing schema.
			       - To rebrand a base/inherited object without redefining columns (e.g. relabel a replacing schema's inherited fields), override just the caption/description of the inherited column through `modify-entity-schema-column` / `update-entity-schema` `title-localizations` / `description-localizations`. The override lives on the child schema and leaves the parent unchanged; an inherited column's name, type, and flags stay read-only and it cannot be removed.

			       Page editing guardrails
			       - `list-pages` identifies page candidates by `schema-name`.
			       - For existing-app page/detail requests backed by data, resolve the backing schema from refreshed app context before planning schema creation: inspect `get-app-info`, then `list-pages` and `get-page`, and fall back to `get-entity-schema-properties` when the relation is still unclear. Call `get-entity-schema-properties` WITHOUT `package-name` so the merged/effective schema (columns from all packages) is returned; an empty single-package read is NOT proof a column or field is missing — re-read without `package-name` or use `find-entity-schema` before deciding to create new schema work.
			       - If runtime context already exposes a backing supporting or link schema, treat the request as page-only/object-model reuse work. Do not create a new schema just because the current page body does not yet show the detail.
			       - Use the raw page body returned by `get-page`, specifically `raw.body`, as the editable source of truth.
			       - When writing pages, send the full `raw.body` string back to `sync-pages` or `update-page`. Do not send `bundle` or `bundle.viewConfig` fragments as the body payload.
			       - Use `sync-pages` for multi-page or plan-driven page writes. Use `update-page` only for a single-page save or dry-run workflow.
			       - Pass `resources` as a JSON object string when edited bodies introduce `#ResourceString(key)#` macros.
			       - For new apps or extended main entities, perform page edits after `sync-schemas` and `get-app-info` refresh so that page bindings reference materialized columns.
			       - Example: if the app context already contains `Support Case Knowledge Link` / `UsrSupportCaseKbLink`, add the Related Knowledge detail by wiring the page to that existing schema. Do not create `UsrSupportCaseKnowledgeBase`.
			       - When a page needs a specific Freedom UI component or composite, fetch its structure (and, for a composite, its assembly recipe) from `get-component-info` instead of hand-assembling or inventing it; see `page-modification` for the editing mechanics and `get-guidance` for any dedicated authoring article.
			       """
		};
}

