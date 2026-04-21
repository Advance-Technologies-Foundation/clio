using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for DataForge orchestration through clio MCP.
/// </summary>
[McpServerResourceType]
public sealed class DataForgeOrchestrationGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/dataforge-orchestration";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Returns the canonical DataForge orchestration protocol for AI consumers of clio MCP.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "dataforge-orchestration-guidance")]
	[Description("Returns the canonical 4-layer DataForge orchestration protocol for AI agents using clio MCP.")]
	public ResourceContents GetGuide() => Guide;

	internal static readonly TextResourceContents Guide = new() {
			Uri = ResourceUri,
			MimeType = "text/plain",
			Text = """
			       clio MCP DataForge orchestration guide

			       Architecture split
			       - clio (passive): DataForge enrichment is built into selected write tools. It runs automatically before the mutation, returns a `dataforge` section alongside the result, and degrades gracefully when DataForge is unavailable. The mutation is never blocked.
			       - ADAC / AI consumer (active): explicit orchestration — the AI agent calls DataForge tools at the right points in a workflow to gather context it cannot derive from the requirement alone.
			       - Do not duplicate passive enrichment: tools that already enrich internally (`create-app`, `sync-schemas`, `create-entity-schema`, `create-lookup`, `update-entity-schema`) run DataForge themselves. Do not add an external pre-flight for these tools.

			       Layer 0 — Health preflight
			       When: once, at the start of a workflow that will span multiple write operations.
			       Call: `dataforge-health` or `dataforge-status`.
			       - `health.liveness = true` and `health.readiness = true` → full enrichment available, proceed normally.
			       - `health.data-structure-readiness = false` or `health.lookups-readiness = false` → proceed with caution; discovery may return partial context.
			       - `status.status != "Ready"` → DataForge not initialized or not ready; skip all DataForge calls for this session; optionally warn the user.
			       - Call throws → treat as `status.status != "Ready"`; proceed without DataForge.

			       Layer 1 — Planning discovery
			       When: once per planning phase, before calling any write tool, when the task involves creating new schemas or entities.
			       Not required for: existing-app maintenance without new schema creation; single-column modifications with an already-known schema name.
			       - DataForge is not the primary mechanism for exact package-local reuse checks in existing-app page/detail workflows. First inspect `get-app-info`, `get-page`, and `get-entity-schema-properties`; use DataForge only when runtime context still cannot identify the relevant schema or relation.
			       Call: `dataforge-context` with `requirement-summary`, `candidate-terms`, `lookup-hints`, and optional `relation-pairs`.
			       What to inspect:
			       - multiple close entries in `similar-tables` with matching names/captions/descriptions → treat as a strong duplicate candidate; surface to user before proceeding.
			       - `similar-lookups[].score >= 0.85` → existing lookup schema may already cover the concept; prefer reusing it.
			       - `relations` → if a Cypher path exists between two planned entities, model the FK along that path.
			       - `columns` → if a similar table exists, its column structure informs the new schema design.
			       Failure behavior: if `dataforge-context` throws or returns empty → skip Layer 1 entirely; write tools carry their own auto-enrichment (Layer 2). Do not block the user request.

			       Layer 2 — Read auto-enrichment from write tool responses
			       Tools that enrich internally: `create-app`, `sync-schemas`, `create-entity-schema`, `create-lookup`, `update-entity-schema`.
			       Rule: do not call DataForge separately before these tools. They already run it.
			       What to inspect from the `dataforge` section:
			       - `context-summary.similar-tables` — if multiple close matches exist for a just-created schema, the creation may have been redundant; surface as a warning.
			       - `coverage.tables = false` — enrichment did not run; treat conservatively but do not retry the DataForge call.
			       - `warnings` — degradation reasons; surface to the user if relevant.
			       Failure behavior: if the `dataforge` key is absent (null) → the tool ran in a degraded or test context; ignore and proceed.

			       Layer 3 — Explicit pre-flight for tools without internal enrichment
			       These tools require explicit DataForge calls because they do not enrich internally:
			       - `modify-entity-schema-column` (guidance-only; no built-in enrichment)
			       - `create-data-binding-db` and `upsert-data-binding-row-db` (lookup GUIDs must be resolved before calling)
			       Consistent failure rule:
			       - If the caller already supplied the value (explicit `reference-schema-name` or explicit lookup GUID) → proceed with the supplied value even if DataForge fails; surface a warning that validation was skipped.
			       - If the caller did NOT supply the value and DataForge was the only resolution path → do NOT guess; ask the user to supply the value explicitly.
			       Tool-specific guidance:
			       - Adding a Lookup column via `modify-entity-schema-column` with uncertain `reference-schema-name`: call `dataforge-find-tables(query: <concept>)`; use name/caption/description similarity as a manual confirmation step; if still ambiguous, confirm with the user.
			       - Writing rows with lookup columns via `create-data-binding-db` / `upsert-data-binding-row-db` with unknown GUID: call `dataforge-find-lookups(schema-name: <refSchema>, query: <value>)`; use top-scored `lookup-id` if score >= 0.70; ask user if no match with score >= 0.70.
			       - Cross-entity FK design before a multi-entity `sync-schemas`: call `dataforge-get-relations(source-table, target-table)`; model FK along the Cypher path if one exists; if call fails or returns empty, design FK independently (no user prompt needed).
			       - Inspecting runtime columns outside a local package: call `dataforge-get-table-columns(table-name)`; if call fails, fall back to `get-entity-schema-properties`.

			       Layer 4 — Index maintenance and stale index recovery
			       Normal maintenance: after a bulk schema creation batch (5+ new entities), call `dataforge-update` to trigger an incremental index refresh.
			       Staleness detection: if Layer 2 responses consistently show `coverage.tables = false` or `similar-tables = []` for schemas that were just created, the index is stale; trigger `dataforge-update` explicitly.
			       Recovery sequence when `dataforge-update` fails:
			       1. Retry `dataforge-update` once after 30 seconds.
			       2. Call `dataforge-status` to confirm DataForge is reachable.
			       3. If reachable but update still fails, call `dataforge-initialize` for a full reindex (slower but authoritative).
			       4. If `dataforge-initialize` also fails, surface the failure to the user and proceed without DataForge coverage for this session; the background scheduler will refresh the index.
			       Stale index in a read-only inspection workflow: if `dataforge-context` or `dataforge-find-tables` returns zero results for a schema known to exist and `dataforge-status` returns `status = "Ready"`, call `dataforge-update` and re-call the search tool once before treating the empty result as authoritative.
			       """
	};

	/// <summary>
	/// Returns the canonical DataForge orchestration protocol for AI consumers of clio MCP.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "dataforge-orchestration-guidance")]
	[Description("Returns the canonical 4-layer DataForge orchestration protocol for AI agents using clio MCP.")]
	public ResourceContents GetGuide() => Guide;
}

